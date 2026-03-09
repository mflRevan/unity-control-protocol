use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use console::style;

use super::Context;

pub async fn run(level: Option<String>, count: Option<u32>, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    // Subscribe to logs
    let params = serde_json::json!({
        "level": level.as_deref().unwrap_or("info"),
    });
    client.call("logs/subscribe", params).await?;

    if !ctx.json {
        output::print_info("Streaming logs (Ctrl+C to stop)...");
    }

    let mut received: u32 = 0;

    // Read notifications directly from the WebSocket stream
    loop {
        if let Some(limit) = count {
            if received >= limit {
                break;
            }
        }

        match client.next_notification().await {
            Some(notif) if notif.method == "log" => {
                received += 1;
                if ctx.json {
                    output::print_json_compact(&notif.params);
                } else {
                    let lvl = notif.params.get("level").and_then(|v| v.as_str()).unwrap_or("info");
                    let msg = notif.params.get("message").and_then(|v| v.as_str()).unwrap_or("");
                    let styled_level = match lvl {
                        "error" | "exception" => style(format!("[{lvl:>5}]")).red(),
                        "warning" | "warn" => style(format!("[{lvl:>5}]")).yellow(),
                        _ => style(format!("[{lvl:>5}]")).dim(),
                    };
                    eprintln!("{styled_level} {msg}");
                }
            }
            Some(_) => continue,
            None => break,
        }
    }

    Ok(())
}
