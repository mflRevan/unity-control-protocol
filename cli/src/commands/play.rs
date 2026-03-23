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

    let mut result = client.call(method, payload).await?;

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
        let label = match method {
            "play" => "Entered play mode",
            "stop" => "Exited play mode",
            "pause" => "Toggled pause",
            _ => "Done",
        };
        output::print_success(label);
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
        let (_, _, mut client) = super::connect_client(ctx).await?;
        let status = client.call("play/status", serde_json::json!({})).await?;
        client.close().await;

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
        } else if observed_transition || started.elapsed() >= request_grace {
            anyhow::bail!("Failed to enter play mode: fix all errors before entering playmode");
        }

        if started.elapsed() >= timeout {
            anyhow::bail!("Timed out waiting for Unity to enter play mode");
        }

        sleep(Duration::from_millis(200)).await;
    }
}
