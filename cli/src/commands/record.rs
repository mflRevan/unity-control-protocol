use std::time::{Duration, Instant};

use clap::{Args, Subcommand};
use serde_json::{Value, json};
use tokio::time::sleep;

use crate::output;

use super::Context;

#[derive(Args, Clone, Debug)]
pub struct RecordSettings {
    /// View to record: game or scene
    #[arg(long, default_value = "game", value_parser = ["game", "scene"])]
    pub view: String,
    /// Longest output edge in pixels; preserves the source aspect ratio
    #[arg(long, default_value_t = 960, value_parser = clap::value_parser!(u32).range(64..=4096))]
    pub max_edge: u32,
    /// Exact output width; derive height from the source aspect when height is omitted
    #[arg(long, value_parser = clap::value_parser!(u32).range(64..=4096))]
    pub width: Option<u32>,
    /// Exact output height; derive width from the source aspect when width is omitted
    #[arg(long, value_parser = clap::value_parser!(u32).range(64..=4096))]
    pub height: Option<u32>,
    /// Capture rate in frames per second
    #[arg(long, default_value_t = 15, value_parser = clap::value_parser!(u32).range(1..=60))]
    pub fps: u32,
    /// Output container/codec selection
    #[arg(long, default_value = "auto", value_parser = ["auto", "mp4", "webm"])]
    pub format: String,
    /// Target video bitrate in kilobits per second
    #[arg(long, default_value_t = 2_000, value_parser = clap::value_parser!(u32).range(128..=50_000))]
    pub bitrate_kbps: u32,
    /// Stretch playback by this factor for multimodal analysis, without dropping or duplicating a
    /// single captured frame.
    ///
    /// This exists for one specific reason. Video-understanding models do not watch a file, they
    /// sample it -- typically at about one frame per second regardless of the file's own frame
    /// rate. A six-second clip therefore reaches the model as roughly six frames, and anything that
    /// happens between those samples is invisible to it: foot sliding, a camera settling, a
    /// one-frame animation pop, a physics jitter. Raising `--fps` does not help, because the
    /// sampler ignores it.
    ///
    /// `--slowdown` raises effective temporal resolution instead. Frames are still captured at
    /// `--fps` in real time; only the container's declared playback rate is divided by this factor,
    /// so the same frames are spaced further apart in playback time. At `--slowdown 6`, one second
    /// of gameplay becomes six seconds of file, and a one-frame-per-second sampler receives about
    /// six samples of that second rather than one. Nothing is re-encoded and no frames are
    /// interpolated, so what the model sees is exactly what was rendered.
    ///
    /// Use it when an agent must judge motion -- contact, timing, smoothness, settling. Leave it at
    /// 1 for clips a human will watch, which are wrong at any other value.
    #[arg(long, default_value_t = 1.0, value_parser = slowdown_factor)]
    pub slowdown: f64,
    /// Replace an existing output file
    #[arg(long)]
    pub overwrite: bool,
}

#[derive(Subcommand, Debug)]
pub enum RecordAction {
    /// Record a bounded clip and wait until the finalized video is available
    Capture {
        #[command(flatten)]
        settings: RecordSettings,
        /// Recording length in seconds
        #[arg(long, default_value_t = 5.0, value_parser = positive_seconds)]
        duration: f64,
        /// Output path (defaults to .ucp/recordings/<timestamp>.<format>)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Start a detached recording that survives this CLI invocation
    Start {
        #[command(flatten)]
        settings: RecordSettings,
        /// Stop automatically after this many seconds
        #[arg(long, value_parser = positive_seconds)]
        duration: Option<f64>,
        /// Safety limit for detached recordings; use 0 for no limit
        #[arg(long, default_value_t = 60.0, value_parser = non_negative_seconds)]
        max_duration: f64,
        /// Output path (defaults to .ucp/recordings/<timestamp>.<format>)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Stop and finalize an active recording, or cancel an armed trigger
    Stop,
    /// Show active, armed, or most recently completed recording state
    Status,
    /// Arm a bounded recording for a play, log, or named signal event
    Arm {
        #[command(flatten)]
        settings: RecordSettings,
        /// Trigger: play-enter, play-exit, log:<regex>, or signal:<name>
        #[arg(long = "on", value_parser = validate_trigger)]
        trigger: String,
        /// Recording length after the trigger, in seconds
        #[arg(long, default_value_t = 5.0, value_parser = positive_seconds)]
        duration: f64,
        /// Stop waiting for the event after this many seconds; use 0 for no limit
        #[arg(long, default_value_t = 60.0, value_parser = non_negative_seconds)]
        wait_timeout: f64,
        /// Output path (defaults to .ucp/recordings/<timestamp>.<format>)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Emit a named event for a matching armed recording
    Signal {
        /// Signal name
        name: String,
    },
}

pub async fn run(action: RecordAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        RecordAction::Capture {
            settings,
            duration,
            output,
        } => capture(settings, duration, output, ctx).await,
        RecordAction::Start {
            settings,
            duration,
            max_duration,
            output,
        } => {
            let result = start(&settings, duration, max_duration, output, ctx).await?;
            render_result("Recording started", result, ctx);
            Ok(())
        }
        RecordAction::Stop => {
            let result = stop(ctx).await?;
            render_result("Recording finalized", result, ctx);
            Ok(())
        }
        RecordAction::Status => {
            let result = call("record/status", json!({}), ctx).await?;
            if ctx.json {
                output::print_json(&output::success_json(result));
            } else {
                output::print_json(&result);
            }
            Ok(())
        }
        RecordAction::Arm {
            settings,
            trigger,
            duration,
            wait_timeout,
            output,
        } => {
            let mut params = settings_json(&settings, output);
            params["trigger"] = json!(trigger);
            params["duration"] = json!(duration);
            params["maxDuration"] = json!(duration);
            params["timeout"] = json!(wait_timeout);
            let result = call("record/arm", params, ctx).await?;
            render_result("Recording armed", result, ctx);
            Ok(())
        }
        RecordAction::Signal { name } => {
            let result = call("record/signal", json!({ "name": name }), ctx).await?;
            render_result("Recording signal emitted", result, ctx);
            Ok(())
        }
    }
}

pub(crate) async fn start(
    settings: &RecordSettings,
    duration: Option<f64>,
    max_duration: f64,
    output_path: Option<String>,
    ctx: &Context,
) -> anyhow::Result<Value> {
    let mut params = settings_json(settings, output_path);
    params["duration"] = json!(duration);
    params["maxDuration"] = json!(max_duration);
    call("record/start", params, ctx).await
}

pub(crate) async fn stop(ctx: &Context) -> anyhow::Result<Value> {
    call("record/stop", json!({}), ctx).await
}

async fn capture(
    settings: RecordSettings,
    duration: f64,
    output_path: Option<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let mut params = settings_json(&settings, output_path);
    params["duration"] = json!(duration);
    params["maxDuration"] = json!(duration);
    client.call("record/start", params).await?;
    let deadline = Instant::now()
        + Duration::from_secs_f64(duration)
        + Duration::from_secs(ctx.timeout.max(10));
    loop {
        sleep(Duration::from_millis(100)).await;
        let status = client.call("record/status", json!({})).await?;
        let state = status
            .get("state")
            .and_then(Value::as_str)
            .unwrap_or("unknown");
        if matches!(state, "completed" | "failed" | "idle") {
            if state == "failed" {
                anyhow::bail!(
                    "Recording failed: {}",
                    status
                        .get("error")
                        .and_then(Value::as_str)
                        .unwrap_or("unknown encoder error")
                );
            }
            client.close().await;
            render_result("Recording finalized", status, ctx);
            return Ok(());
        }
        if Instant::now() >= deadline {
            let _ = client.call("record/stop", json!({})).await;
            client.close().await;
            anyhow::bail!("Timed out waiting for recording finalization");
        }
    }
}

fn settings_json(settings: &RecordSettings, output_path: Option<String>) -> Value {
    json!({
        "view": settings.view, "maxEdge": settings.max_edge,
        "width": settings.width, "height": settings.height, "fps": settings.fps,
        "format": settings.format, "bitrateKbps": settings.bitrate_kbps,
        "overwrite": settings.overwrite, "path": output_path,
        "slowdown": settings.slowdown,
    })
}

async fn call(method: &str, params: Value, ctx: &Context) -> anyhow::Result<Value> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = client.call(method, params).await;
    client.close().await;
    Ok(result?)
}

fn render_result(label: &str, result: Value, ctx: &Context) {
    if ctx.json {
        output::print_json(&output::success_json(result));
        return;
    }
    let path = result.get("path").and_then(Value::as_str).unwrap_or("");
    if path.is_empty() {
        output::print_success(label);
    } else {
        output::print_success(&format!("{label}: {path}"));
    }
}

pub(crate) fn positive_seconds(value: &str) -> Result<f64, String> {
    let parsed = value
        .parse::<f64>()
        .map_err(|_| "expected a number of seconds".to_string())?;
    if parsed.is_finite() && parsed > 0.0 {
        Ok(parsed)
    } else {
        Err("seconds must be greater than zero".to_string())
    }
}

pub(crate) fn non_negative_seconds(value: &str) -> Result<f64, String> {
    let parsed = value
        .parse::<f64>()
        .map_err(|_| "expected a number of seconds".to_string())?;
    if parsed.is_finite() && parsed >= 0.0 {
        Ok(parsed)
    } else {
        Err("seconds must be zero or greater".to_string())
    }
}

fn validate_trigger(value: &str) -> Result<String, String> {
    if matches!(value, "play-enter" | "play-exit") {
        return Ok(value.to_string());
    }
    for prefix in ["log:", "signal:"] {
        if value
            .strip_prefix(prefix)
            .is_some_and(|rest| !rest.trim().is_empty())
        {
            return Ok(value.to_string());
        }
    }
    Err("expected play-enter, play-exit, log:<regex>, or signal:<name>".to_string())
}

pub(crate) fn slowdown_factor(value: &str) -> Result<f64, String> {
    let parsed = value
        .parse::<f64>()
        .map_err(|_| "expected a playback slowdown factor, e.g. 6".to_string())?;
    if parsed.is_finite() && (1.0..=20.0).contains(&parsed) {
        Ok(parsed)
    } else {
        Err("slowdown must be between 1 and 20".to_string())
    }
}
