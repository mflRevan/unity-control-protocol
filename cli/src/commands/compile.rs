use crate::output;

use super::{Context, UnityLifecyclePolicy};

pub async fn run(no_wait: bool, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(
        &mut client,
        super::ActiveSceneGuardPolicy::block_if_dirty("trigger recompilation"),
    )
    .await?;

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

    let lifecycle = super::await_unity_lifecycle(
        &project,
        Some(&lock),
        UnityLifecyclePolicy::restart_then_settle(
            "Waiting for compilation...",
            "compilation",
            5,
        ),
        ctx,
    )
    .await?;

    if ctx.json {
        let result = super::attach_lifecycle_log_status(
            serde_json::json!({
            "status": "ok",
            "message": "Compilation completed",
            "bridge": match lifecycle.bridge_status.unwrap_or(crate::bridge_lifecycle::WaitStatus::Stable) {
                crate::bridge_lifecycle::WaitStatus::Restarted => "restarted",
                crate::bridge_lifecycle::WaitStatus::Stable => "stable",
                crate::bridge_lifecycle::WaitStatus::Available => "available",
                crate::bridge_lifecycle::WaitStatus::EditorNotRunning => "editor-not-running",
            }
        }),
            &lifecycle,
        );
        output::print_json(&output::success_json(result));
    } else {
        output::print_success("Compilation completed");
    }

    Ok(())
}
