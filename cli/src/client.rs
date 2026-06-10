use crate::config::LockFile;
use crate::error::UcpError;
use crate::protocol::{self, RpcNotification, RpcRequest, RpcResponse};
use futures_util::{SinkExt, StreamExt};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio_tungstenite::WebSocketStream;
use tokio_tungstenite::tungstenite::Message;

static REQUEST_ID: AtomicU64 = AtomicU64::new(1);

pub struct BridgeClient {
    write: futures_util::stream::SplitSink<WebSocketStream<tokio::net::TcpStream>, Message>,
    read: futures_util::stream::SplitStream<WebSocketStream<tokio::net::TcpStream>>,
    pub token: String,
    /// Per-request response deadline. `None` means wait indefinitely (used for
    /// operations that legitimately block the editor's main thread for a long time,
    /// such as builds). Set from the `--timeout` flag for everything else so a
    /// modal-blocked or wedged editor surfaces a clear error instead of hanging forever.
    request_timeout: Option<Duration>,
}

impl BridgeClient {
    pub async fn connect(lock: &LockFile) -> Result<Self, UcpError> {
        let addr = format!("127.0.0.1:{}", lock.port);
        tracing::debug!("Connecting to bridge at ws://{addr}");

        let mut stream = tokio::net::TcpStream::connect(&addr)
            .await
            .map_err(|e| UcpError::ConnectionFailed(e.to_string()))?;

        // Manual WebSocket upgrade (RFC 6455)
        let key_bytes: [u8; 16] = rand::random();
        let key = base64::Engine::encode(&base64::engine::general_purpose::STANDARD, key_bytes);

        let request = format!(
            "GET / HTTP/1.1\r\n\
             Host: {addr}\r\n\
             Connection: Upgrade\r\n\
             Upgrade: websocket\r\n\
             Sec-WebSocket-Version: 13\r\n\
             Sec-WebSocket-Key: {key}\r\n\
             \r\n"
        );
        stream
            .write_all(request.as_bytes())
            .await
            .map_err(|e| UcpError::ConnectionFailed(e.to_string()))?;

        // Read HTTP response
        let mut buf = vec![0u8; 4096];
        let mut total = 0;
        loop {
            let n = stream
                .read(&mut buf[total..])
                .await
                .map_err(|e| UcpError::ConnectionFailed(e.to_string()))?;
            if n == 0 {
                return Err(UcpError::ConnectionFailed(
                    "Connection closed during handshake".into(),
                ));
            }
            total += n;
            let response = std::str::from_utf8(&buf[..total]).unwrap_or("");
            if response.contains("\r\n\r\n") {
                break;
            }
        }

        let response = std::str::from_utf8(&buf[..total]).map_err(|_| {
            UcpError::ConnectionFailed("Invalid UTF-8 in handshake response".into())
        })?;

        // Validate status line
        if !response.starts_with("HTTP/1.1 101") {
            return Err(UcpError::ConnectionFailed(format!(
                "Unexpected handshake response: {}",
                response.lines().next().unwrap_or("empty")
            )));
        }

        // Validate Sec-WebSocket-Accept
        let expected_accept = {
            use sha1::{Digest, Sha1};
            let magic = "258EAFA5-E914-47DA-95CA-5AB5DC85B11B";
            let mut hasher = Sha1::new();
            hasher.update(key.as_bytes());
            hasher.update(magic.as_bytes());
            base64::Engine::encode(
                &base64::engine::general_purpose::STANDARD,
                hasher.finalize(),
            )
        };

        let accept_ok = response.lines().any(|line| {
            if let Some(val) = line.strip_prefix("Sec-WebSocket-Accept:") {
                val.trim() == expected_accept
            } else {
                false
            }
        });

        if !accept_ok {
            return Err(UcpError::ConnectionFailed(
                "Invalid Sec-WebSocket-Accept in handshake".into(),
            ));
        }

        tracing::debug!("WebSocket handshake completed");

        // Wrap in WebSocketStream (server=false since we're the client)
        let ws = WebSocketStream::from_raw_socket(
            stream,
            tokio_tungstenite::tungstenite::protocol::Role::Client,
            None,
        )
        .await;
        let (write, read) = ws.split();
        Ok(Self {
            write,
            read,
            token: lock.token.clone(),
            request_timeout: None,
        })
    }

    /// Bound every subsequent `call` to `timeout`. Pass `None` to wait indefinitely.
    pub fn set_request_timeout(&mut self, timeout: Option<Duration>) {
        self.request_timeout = timeout;
    }

    /// Issue an RPC bounded by the client's configured `request_timeout`.
    pub async fn call(
        &mut self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<serde_json::Value, UcpError> {
        self.call_with_timeout(method, params, self.request_timeout)
            .await
    }

    /// Issue an RPC with an explicit per-call deadline, overriding the client default.
    /// `None` waits indefinitely — use only for operations that intentionally block the
    /// editor's main thread for a long time (e.g. builds, synchronous compiles).
    pub async fn call_with_timeout(
        &mut self,
        method: &str,
        params: serde_json::Value,
        timeout: Option<Duration>,
    ) -> Result<serde_json::Value, UcpError> {
        match timeout {
            Some(dur) => match tokio::time::timeout(dur, self.call_inner(method, params)).await {
                Ok(result) => result,
                Err(_) => Err(UcpError::RequestTimeout {
                    method: method.to_string(),
                    secs: dur.as_secs(),
                }),
            },
            None => self.call_inner(method, params).await,
        }
    }

    async fn call_inner(
        &mut self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<serde_json::Value, UcpError> {
        let id = REQUEST_ID.fetch_add(1, Ordering::SeqCst);
        let req = RpcRequest::new(id, method, params);
        let payload = serde_json::to_string(&req)
            .map_err(|e| UcpError::Other(format!("Serialize error: {e}")))?;

        tracing::debug!("→ {payload}");

        self.write
            .send(Message::Text(payload.into()))
            .await
            .map_err(|e| UcpError::ConnectionFailed(e.to_string()))?;

        // Read messages until we get the matching response
        loop {
            let msg = self
                .read
                .next()
                .await
                .ok_or_else(|| UcpError::ConnectionFailed("Connection closed".into()))?
                .map_err(|e| UcpError::ConnectionFailed(e.to_string()))?;

            let text = match msg {
                Message::Text(t) => t.to_string(),
                Message::Close(_) => {
                    return Err(UcpError::ConnectionFailed("Connection closed".into()));
                }
                _ => continue,
            };

            tracing::debug!("← {text}");

            let value: serde_json::Value = serde_json::from_str(&text)
                .map_err(|e| UcpError::Other(format!("Invalid JSON from bridge: {e}")))?;

            // Skip notifications while waiting for response
            if protocol::is_notification(&value) {
                continue;
            }

            let resp: RpcResponse = serde_json::from_value(value)
                .map_err(|e| UcpError::Other(format!("Invalid RPC response: {e}")))?;

            if resp.id == Some(id) {
                if let Some(err) = resp.error {
                    return Err(UcpError::BridgeError {
                        code: err.code,
                        message: err.message,
                    });
                }
                return Ok(resp.result.unwrap_or(serde_json::Value::Null));
            }
        }
    }

    /// Read the next notification from the WebSocket stream.
    /// Blocks until a notification arrives or the connection closes.
    /// Non-notification messages (responses) are silently dropped.
    pub async fn next_notification(&mut self) -> Option<RpcNotification> {
        loop {
            let msg = self.read.next().await?.ok()?;
            let text = match msg {
                Message::Text(t) => t.to_string(),
                Message::Close(_) => return None,
                _ => continue,
            };

            tracing::debug!("← {text}");

            if let Ok(value) = serde_json::from_str::<serde_json::Value>(&text) {
                if protocol::is_notification(&value) {
                    if let Ok(notif) = serde_json::from_value::<RpcNotification>(value) {
                        return Some(notif);
                    }
                }
            }
        }
    }

    pub async fn handshake(&mut self) -> Result<serde_json::Value, UcpError> {
        self.call(
            "handshake",
            serde_json::json!({
                "clientVersion": env!("CARGO_PKG_VERSION"),
                "protocolVersion": crate::config::PROTOCOL_VERSION,
                "token": self.token,
            }),
        )
        .await
    }

    pub async fn close(mut self) {
        let _ = self.write.send(Message::Close(None)).await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::net::TcpListener;

    /// Completes the WebSocket upgrade but never sends a single frame, modelling a Unity
    /// editor whose main-thread pump is wedged behind a modal dialog.
    async fn upgrade_then_go_silent(listener: TcpListener) {
        let Ok((mut stream, _)) = listener.accept().await else {
            return;
        };

        let mut buf = vec![0u8; 4096];
        let mut total = 0;
        loop {
            let n = stream.read(&mut buf[total..]).await.unwrap_or(0);
            if n == 0 {
                return;
            }
            total += n;
            if std::str::from_utf8(&buf[..total])
                .unwrap_or("")
                .contains("\r\n\r\n")
            {
                break;
            }
        }

        let request = std::str::from_utf8(&buf[..total]).unwrap_or("");
        let key = request
            .lines()
            .find_map(|line| line.strip_prefix("Sec-WebSocket-Key:"))
            .map(str::trim)
            .unwrap_or_default();

        let accept = {
            use sha1::{Digest, Sha1};
            let mut hasher = Sha1::new();
            hasher.update(key.as_bytes());
            hasher.update(b"258EAFA5-E914-47DA-95CA-5AB5DC85B11B");
            base64::Engine::encode(
                &base64::engine::general_purpose::STANDARD,
                hasher.finalize(),
            )
        };

        let response = format!(
            "HTTP/1.1 101 Switching Protocols\r\n\
             Upgrade: websocket\r\n\
             Connection: Upgrade\r\n\
             Sec-WebSocket-Accept: {accept}\r\n\r\n"
        );
        let _ = stream.write_all(response.as_bytes()).await;

        // Hold the socket open without ever replying.
        tokio::time::sleep(Duration::from_secs(30)).await;
    }

    fn fake_lock(port: u16) -> LockFile {
        LockFile {
            pid: 0,
            port,
            protocol_version: "test".into(),
            unity_version: "test".into(),
            project_path: "test".into(),
            started_at: "test".into(),
            token: "test-token".into(),
        }
    }

    #[tokio::test]
    async fn call_times_out_when_bridge_never_responds() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        tokio::spawn(upgrade_then_go_silent(listener));

        let mut client = BridgeClient::connect(&fake_lock(port)).await.unwrap();
        client.set_request_timeout(Some(Duration::from_millis(200)));

        let err = client
            .call("editor/status", serde_json::json!({}))
            .await
            .expect_err("a silent bridge must surface a timeout, not hang");

        match err {
            UcpError::RequestTimeout { method, .. } => assert_eq!(method, "editor/status"),
            other => panic!("expected RequestTimeout, got {other:?}"),
        }
    }

    #[tokio::test]
    async fn call_with_none_timeout_is_not_bounded() {
        // A None deadline must not impose any timeout: the call stays pending until we
        // abandon it. We race it against a short timer and assert the timer wins.
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        tokio::spawn(upgrade_then_go_silent(listener));

        let mut client = BridgeClient::connect(&fake_lock(port)).await.unwrap();
        client.set_request_timeout(Some(Duration::from_millis(50)));

        let unbounded = client.call_with_timeout("build/start", serde_json::json!({}), None);
        let raced = tokio::time::timeout(Duration::from_millis(300), unbounded).await;
        assert!(
            raced.is_err(),
            "call_with_timeout(None) should never resolve on its own here"
        );
    }
}
