use crate::bridge_lifecycle::{self, WaitMode, WaitStatus};
use crate::client::BridgeClient;
use crate::discovery;
use crate::output;

use super::Context;

pub async fn run(no_wait: bool, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    if !ctx.json {
        output::print_info("Triggering recompilation...");
    }

    let result = client.call("compile", serde_json::json!({})).await?;
    client.close().await;

    if no_wait {
        if ctx.json {
            output::print_json(&output::success_json(result));
        } else {
            output::print_success("Compilation triggered (not waiting)");
        }
        return Ok(());
    }

    // Block until compilation + domain reload completes and bridge restarts
    if !ctx.json {
        output::print_info("Waiting for compilation...");
    }

    let wait_outcome = bridge_lifecycle::wait_for_bridge(
        &project,
        Some(&lock),
        ctx.timeout.max(60),
        WaitMode::RestartOptional,
    )
    .await?;

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "status": "ok",
            "message": "Compilation completed",
            "bridge": match wait_outcome.status {
                WaitStatus::Restarted => "restarted",
                WaitStatus::Stable => "stable",
                WaitStatus::Available => "available",
                WaitStatus::EditorNotRunning => "editor-not-running",
            }
        })));
    } else {
        output::print_success("Compilation completed");
    }

    Ok(())
}
