use std::fmt;
use std::time::{Duration, Instant};

use serde_json::Value;

use crate::output;

use super::{Context, UnityLifecyclePolicy};

/// How long to wait, after the editor settles, for the tracked compilation to report completion.
const DIAGNOSTICS_POLL_SECS: u64 = 15;
/// Cap on per-assembly compiler messages printed in human mode (full set is in --json).
const MAX_PRINTED_MESSAGES: usize = 50;

/// Carries compile-error details so `main` can render them (including in `--json` mode) and exit
/// nonzero. `ucp compile` used to always report success even when assemblies failed to compile;
/// this surfaces the real per-assembly compiler messages and fails the command on errors.
#[derive(Debug)]
pub struct CompileFailure {
    pub message: String,
    pub result: Value,
}

impl fmt::Display for CompileFailure {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.message)
    }
}

impl std::error::Error for CompileFailure {}

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

    // A synchronous AssetDatabase.Refresh can take well over the default --timeout on
    // large projects; don't bound this call. Domain-reload waiting is handled below.
    let trigger = client
        .call_with_timeout("compile", serde_json::json!({}), None)
        .await?;
    let request_id = trigger.get("requestId").and_then(|v| v.as_i64());
    client.close().await;

    if no_wait {
        if ctx.json {
            output::print_json(&output::success_json(trigger));
        } else {
            output::print_success("Compilation triggered (not waiting)");
        }
        return Ok(());
    }

    let lifecycle = super::await_unity_lifecycle(
        &project,
        Some(&lock),
        UnityLifecyclePolicy::restart_then_settle("Waiting for compilation...", "compilation", 5),
        ctx,
    )
    .await?;

    let bridge_status = match lifecycle
        .bridge_status
        .unwrap_or(crate::bridge_lifecycle::WaitStatus::Stable)
    {
        crate::bridge_lifecycle::WaitStatus::Restarted => "restarted",
        crate::bridge_lifecycle::WaitStatus::Stable => "stable",
        crate::bridge_lifecycle::WaitStatus::Available => "available",
        crate::bridge_lifecycle::WaitStatus::EditorNotRunning => "editor-not-running",
    };

    // Read back the per-assembly compiler diagnostics now that the editor has settled. Older
    // bridges without `compile/diagnostics` return None, in which case we keep the previous
    // behavior (report completion without an error breakdown) rather than failing.
    let diagnostics = collect_compile_diagnostics(ctx, request_id).await;
    let (error_count, warning_count) = match &diagnostics {
        Some(d) => (
            d.get("errorCount").and_then(|v| v.as_i64()).unwrap_or(0),
            d.get("warningCount").and_then(|v| v.as_i64()).unwrap_or(0),
        ),
        None => (0, 0),
    };

    let failed = error_count > 0;
    let mut result = serde_json::json!({
        "status": if failed { "failed" } else { "ok" },
        "message": if failed { "Compilation failed" } else { "Compilation completed" },
        "bridge": bridge_status,
        "errorCount": error_count,
        "warningCount": warning_count,
        "diagnosticsAvailable": diagnostics.is_some(),
    });
    if let Some(d) = &diagnostics {
        result["diagnostics"] = d.clone();
    }
    let result = super::attach_lifecycle_log_status(result, &lifecycle);

    if failed {
        if !ctx.json {
            output::print_error(&format!(
                "Compilation failed: {error_count} error(s){}",
                if warning_count > 0 {
                    format!(", {warning_count} warning(s)")
                } else {
                    String::new()
                }
            ));
            print_messages(diagnostics.as_ref());
        }
        return Err(CompileFailure {
            message: format!("Compilation failed with {error_count} error(s)"),
            result,
        }
        .into());
    }

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else if warning_count > 0 {
        output::print_success(&format!(
            "Compilation completed with {warning_count} warning(s)"
        ));
        print_messages(diagnostics.as_ref());
    } else {
        output::print_success("Compilation completed");
        if diagnostics.is_none() {
            output::print_info(
                "Compile diagnostics unavailable on this bridge; update the Unity bridge package to surface per-assembly errors.",
            );
        }
    }

    Ok(())
}

/// Poll `compile/diagnostics` until the tracked compilation reports completion (or we time out).
/// Returns None when the bridge does not expose the method (older package) so the caller can
/// degrade gracefully instead of failing.
async fn collect_compile_diagnostics(ctx: &Context, request_id: Option<i64>) -> Option<Value> {
    let deadline = Instant::now() + Duration::from_secs(DIAGNOSTICS_POLL_SECS);
    let (_, _, mut client) = super::connect_client(ctx).await.ok()?;

    let outcome = loop {
        match client
            .call("compile/diagnostics", serde_json::json!({}))
            .await
        {
            Ok(value) => {
                let status = value
                    .get("status")
                    .and_then(|v| v.as_str())
                    .unwrap_or("idle");
                let request_matches = match request_id {
                    Some(id) => value.get("requestId").and_then(|v| v.as_i64()) == Some(id),
                    None => true,
                };
                let done = status == "completed" && request_matches;
                // Break with the freshest snapshot once the compile completes or we time out.
                if done || Instant::now() >= deadline {
                    break Some(value);
                }
            }
            // Method missing on an older bridge, or a transient error: degrade to no breakdown.
            Err(_) => break None,
        }
        tokio::time::sleep(Duration::from_millis(400)).await;
    };

    client.close().await;
    outcome
}

/// Print compiler messages (errors first, then warnings) in human mode, capped to keep output
/// readable; the full list is always present in `--json`.
fn print_messages(diagnostics: Option<&Value>) {
    let Some(messages) = diagnostics
        .and_then(|d| d.get("messages"))
        .and_then(|m| m.as_array())
    else {
        return;
    };

    let mut ordered: Vec<&Value> = messages.iter().collect();
    ordered.sort_by_key(|m| match m.get("type").and_then(|v| v.as_str()) {
        Some("error") => 0,
        Some("warning") => 1,
        _ => 2,
    });

    for message in ordered.iter().take(MAX_PRINTED_MESSAGES) {
        let kind = message
            .get("type")
            .and_then(|v| v.as_str())
            .unwrap_or("info");
        let text = message
            .get("message")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        let file = message.get("file").and_then(|v| v.as_str()).unwrap_or("");
        let line = message.get("line").and_then(|v| v.as_i64()).unwrap_or(0);
        let assembly = message
            .get("assembly")
            .and_then(|v| v.as_str())
            .unwrap_or("");

        let location = if file.is_empty() {
            assembly.to_string()
        } else {
            format!("{file}:{line}")
        };
        eprintln!("  [{kind}] {location}: {text}");
    }

    if ordered.len() > MAX_PRINTED_MESSAGES {
        eprintln!(
            "  ... {} more message(s); use --json for the full list",
            ordered.len() - MAX_PRINTED_MESSAGES
        );
    }
}
