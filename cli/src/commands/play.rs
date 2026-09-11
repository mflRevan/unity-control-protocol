use crate::error::UcpError;
use crate::output;
use serde_json::Value;
use tokio::time::{Duration, Instant, sleep};

use super::Context;

pub async fn run(method: &str, payload: Value, ctx: &Context) -> anyhow::Result<()> {
    if method == "play" {
        // Boxed: the play flow inlines two bridge waits and the confirmation loop, and its
        // state machine would otherwise dominate the size of every command's future.
        return Box::pin(run_play(payload, ctx)).await;
    }

    let (_, _, mut client) = super::connect_client(ctx).await?;
    let mut result = client.call(method, payload).await?;
    if method == "stop" {
        let log_status = crate::commands::logs::fetch_status(&mut client).await?;
        result["logStatus"] = log_status;
    }
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
        return Ok(());
    }
    let status = result.get("status").and_then(Value::as_str).unwrap_or("ok");
    match (method, status) {
        ("stop", "already_stopped") => output::print_success("Already in edit mode"),
        ("stop", _) => output::print_success("Exited play mode"),
        ("pause", _) => output::print_success("Toggled pause"),
        _ => output::print_success("Done"),
    }
    if method == "stop" {
        if let Some(log_status) = result.get("logStatus") {
            if super::log_status_has_failures(log_status) {
                crate::commands::logs::print_status(log_status, ctx);
            }
        }
    }
    Ok(())
}

/// Entering play mode is not a single request: the editor reloads the domain, the bridge goes
/// down and comes back, and a recompile that was still pending when the request arrived can
/// discard the transition entirely (Unity 6000.6 simply stays in edit mode). So the request is
/// sent, confirmed against `play/status`, and re-issued a bounded number of times when the
/// bridge reports a pending compile or a reload swallowed it.
async fn run_play(payload: Value, ctx: &Context) -> anyhow::Result<()> {
    const MAX_ATTEMPTS: u32 = 3;
    let mut attempt = 0;
    let result = loop {
        attempt += 1;
        let (project, lock, mut client) = super::connect_client(ctx).await?;
        super::enforce_active_scene_guard(
            &mut client,
            super::ActiveSceneGuardPolicy::block_if_dirty("enter play mode"),
        )
        .await?;

        let requested = match client.call("play", payload.clone()).await {
            Ok(result) => result,
            // The reload that play mode triggers can win the race against the response to the
            // very request that started it. The request has taken effect; confirm the state.
            Err(UcpError::BridgeConnectionLost { .. }) => {
                serde_json::json!({ "status": "ok", "responseLost": true })
            }
            Err(err) => return Err(err.into()),
        };
        client.close().await;

        let status = requested.get("status").and_then(Value::as_str).unwrap_or("ok");
        if status == "compiling" {
            if attempt >= MAX_ATTEMPTS {
                anyhow::bail!(
                    "Unity kept a script recompile pending while entering play mode; run `ucp compile`, then `ucp play` again"
                );
            }
            if !ctx.json {
                output::print_info(
                    "Unity has a script recompile pending; waiting for it before entering play mode...",
                );
            }
            crate::bridge_lifecycle::wait_for_bridge(
                &project,
                Some(&lock),
                ctx.timeout,
                ctx.dialog_policy,
                crate::bridge_lifecycle::WaitMode::RestartOptional,
            )
            .await?;
            continue;
        }
        if status != "ok" {
            break requested;
        }

        match confirm_play_mode_entry(ctx, &project, &lock.token).await? {
            PlayConfirmation::Playing(confirmed) => break confirmed,
            PlayConfirmation::DiscardedByReload => {
                if attempt >= MAX_ATTEMPTS {
                    anyhow::bail!(
                        "A script reload discarded the play request {attempt} times; run `ucp compile`, then `ucp play` again"
                    );
                }
                if !ctx.json {
                    output::print_info("A script reload discarded the play request; asking again...");
                }
            }
        }
    };

    if ctx.json {
        output::print_json(&output::success_json(result));
        return Ok(());
    }
    match result.get("status").and_then(Value::as_str).unwrap_or("ok") {
        "already_playing" => anyhow::bail!("Already in play mode. Use `ucp stop` to exit play mode."),
        _ => output::print_success("Entered play mode"),
    }
    Ok(())
}

enum PlayConfirmation {
    Playing(Value),
    /// The bridge restarted (a domain reload ran) and the editor settled back in edit mode
    /// without ever reporting a pending transition: the reload ate the request.
    DiscardedByReload,
}

/// How many consecutive idle polls after a bridge restart count as "the reload discarded the
/// request". A single idle poll is not enough: right after a reload some editors answer the
/// first `play/status` with neither `playing` nor `willChange` set for a moment.
const IDLE_POLLS_AFTER_RESTART: u32 = 4;

async fn confirm_play_mode_entry(
    ctx: &Context,
    project: &std::path::Path,
    initial_token: &str,
) -> anyhow::Result<PlayConfirmation> {
    let started = Instant::now();
    let timeout = Duration::from_secs(ctx.timeout.max(1));
    let request_grace = Duration::from_secs(1);
    let mut observed_transition = false;
    let mut restarted = false;
    let mut idle_polls_after_restart = 0u32;

    loop {
        // The domain reload tears the bridge down in the middle of this poll; a dropped
        // connection is the expected path. Keep polling until the timeout, and only give up
        // early if the editor process itself is gone.
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
            return Ok(PlayConfirmation::Playing(confirmed));
        }

        let will_change = status
            .get("willChange")
            .and_then(Value::as_bool)
            .unwrap_or(false);
        if will_change {
            observed_transition = true;
        } else if (observed_transition || started.elapsed() >= request_grace)
            && compile_errors_reported()
        {
            // Unity refuses play mode only for script compilation failures; the summary on the
            // last response says so directly.
            anyhow::bail!(
                "Failed to enter play mode: Unity reports script compilation errors; fix them first (`ucp compile`)"
            );
        }

        if !restarted {
            restarted = crate::discovery::read_lock_file(project)
                .map(|lock| lock.token != initial_token)
                .unwrap_or(false);
        }
        if restarted && !will_change && !editor_compiling_reported() {
            idle_polls_after_restart += 1;
            if idle_polls_after_restart >= IDLE_POLLS_AFTER_RESTART {
                return Ok(PlayConfirmation::DiscardedByReload);
            }
        } else {
            idle_polls_after_restart = 0;
        }

        if started.elapsed() >= timeout {
            anyhow::bail!("Timed out waiting for Unity to enter play mode");
        }

        sleep(Duration::from_millis(200)).await;
    }
}

/// Whether the editor-state summary on the most recent bridge response says Unity is compiling.
fn editor_compiling_reported() -> bool {
    crate::editor_state::current()
        .and_then(|state| state.get("compiling").and_then(Value::as_bool))
        .unwrap_or(false)
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
