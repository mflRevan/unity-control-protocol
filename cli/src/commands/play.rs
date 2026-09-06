use crate::error::UcpError;
use crate::output;
use serde_json::Value;
use tokio::time::{Duration, Instant, sleep};

use super::Context;

pub async fn run(method: &str, payload: Value, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    if method == "play" {
        super::enforce_active_scene_guard(
            &mut client,
            super::ActiveSceneGuardPolicy::block_if_dirty("enter play mode"),
        )
        .await?;
    }

    let mut result = match client.call(method, payload).await {
        Ok(result) => result,
        // Entering play mode reloads the domain, and on some editor versions the teardown wins
        // the race against the response to the very request that triggered it. The request has
        // already taken effect; confirm the state instead of reporting a lost connection.
        Err(UcpError::BridgeConnectionLost { .. }) if method == "play" => {
            serde_json::json!({ "status": "ok", "responseLost": true })
        }
        Err(err) => return Err(err.into()),
    };

    if method == "play" {
        client.close().await;
        result = confirm_play_mode_entry(ctx, result).await?;
    } else if method == "stop" {
        let log_status = crate::commands::logs::fetch_status(&mut client).await?;
        result["logStatus"] = log_status;
        client.close().await;
    } else {
        client.close().await;
    }

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let status = result.get("status").and_then(Value::as_str).unwrap_or("ok");
        match (method, status) {
            ("play", "already_playing") => {
                anyhow::bail!("Already in play mode. Use `ucp stop` to exit play mode.");
            }
            ("stop", "already_stopped") => {
                output::print_success("Already in edit mode");
            }
            _ => {
                let label = match method {
                    "play" => "Entered play mode",
                    "stop" => "Exited play mode",
                    "pause" => "Toggled pause",
                    _ => "Done",
                };
                output::print_success(label);
            }
        }
        if method == "stop" {
            if let Some(log_status) = result.get("logStatus") {
                crate::commands::logs::print_status(log_status, ctx);
            }
        }
    }

    Ok(())
}

async fn confirm_play_mode_entry(ctx: &Context, request_result: Value) -> anyhow::Result<Value> {
    if request_result
        .get("status")
        .and_then(Value::as_str)
        .filter(|status| *status != "ok")
        .is_some()
    {
        return Ok(request_result);
    }

    let started = Instant::now();
    let timeout = Duration::from_secs(ctx.timeout.max(1));
    let request_grace = Duration::from_secs(1);
    let mut observed_transition = false;

    loop {
        // Entering play mode triggers a domain reload, which tears the bridge down in the middle
        // of this poll. A dropped connection here is the expected path, not a failure -- surfacing
        // it told the user to retry a command that had already taken effect. Keep polling until
        // the timeout, and only give up early if the editor process itself is gone.
        let status = match poll_play_status(ctx).await {
            Ok(status) => status,
            Err(err) => {
                if matches!(
                    err.downcast_ref::<UcpError>(),
                    Some(UcpError::EditorProcessDied { .. })
                ) {
                    return Err(err);
                }
                if started.elapsed() >= timeout {
                    anyhow::bail!("Timed out waiting for Unity to enter play mode");
                }
                sleep(Duration::from_millis(200)).await;
                continue;
            }
        };

        if status
            .get("playing")
            .and_then(Value::as_bool)
            .unwrap_or(false)
        {
            let mut confirmed = status;
            confirmed["status"] = serde_json::json!("ok");
            return Ok(confirmed);
        }

        if status
            .get("willChange")
            .and_then(Value::as_bool)
            .unwrap_or(false)
        {
            observed_transition = true;
        } else if (observed_transition || started.elapsed() >= request_grace)
            && compile_errors_reported()
        {
            // Unity refuses play mode only for script compilation failures; the summary on the
            // last response says so directly. Seeing neither flag set is not proof of refusal:
            // 6000.5 answers the first poll after the reload with both false for a moment.
            anyhow::bail!(
                "Failed to enter play mode: Unity reports script compilation errors; fix them first (`ucp compile`)"
            );
        }

        if started.elapsed() >= timeout {
            anyhow::bail!("Timed out waiting for Unity to enter play mode");
        }

        sleep(Duration::from_millis(200)).await;
    }
}

/// Whether the editor-state summary on the most recent bridge response reports
/// `EditorUtility.scriptCompilationFailed`.
fn compile_errors_reported() -> bool {
    crate::editor_state::current()
        .and_then(|state| state.get("compileErrors").and_then(Value::as_bool))
        .unwrap_or(false)
}

async fn poll_play_status(ctx: &Context) -> anyhow::Result<Value> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let status = client.call("play/status", serde_json::json!({})).await;
    client.close().await;
    Ok(status?)
}
