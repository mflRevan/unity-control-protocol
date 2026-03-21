use crate::output;
use anyhow::bail;
use clap::Subcommand;
use serde_json::{Value, json};
use tokio::time::{Duration, Instant, sleep};

use super::Context;

#[derive(Subcommand, Debug, Clone)]
pub enum ProfilerAction {
    /// Show profiler status, capabilities, and frame-buffer summary
    Status,
    /// Read or update profiler configuration
    Config {
        #[command(subcommand)]
        action: ProfilerConfigAction,
    },
    /// Start, stop, or clear a profiling session
    Session {
        #[command(subcommand)]
        action: ProfilerSessionAction,
    },
    /// Save or load profiler capture files
    Capture {
        #[command(subcommand)]
        action: ProfilerCaptureAction,
    },
    /// List or inspect captured frames
    Frames {
        #[command(subcommand)]
        action: ProfilerFramesAction,
    },
    /// Inspect hierarchy data for a frame/thread
    Hierarchy {
        /// Frame index (defaults to latest)
        #[arg(long)]
        frame: Option<i32>,
        /// Thread index (defaults to 0)
        #[arg(long)]
        thread: Option<i32>,
        /// Sort by total-time, self-time, calls, gc-memory, or name
        #[arg(long, default_value = "total-time")]
        sort: String,
        /// Maximum number of rows to return
        #[arg(long, default_value_t = 50)]
        limit: u32,
        /// Maximum depth to include
        #[arg(long)]
        max_depth: Option<u32>,
    },
    /// Inspect timeline samples for a frame/thread
    Timeline {
        /// Frame index (defaults to latest)
        #[arg(long)]
        frame: Option<i32>,
        /// Thread index (defaults to 0)
        #[arg(long)]
        thread: Option<i32>,
        /// Maximum number of samples to return
        #[arg(long, default_value_t = 200)]
        limit: u32,
        /// Maximum nesting depth to include
        #[arg(long)]
        max_depth: Option<u32>,
        /// Include sample metadata when available
        #[arg(long)]
        include_metadata: bool,
    },
    /// Resolve raw or hierarchy sample callstacks
    Callstacks {
        /// Frame index (defaults to latest)
        #[arg(long)]
        frame: Option<i32>,
        /// Thread index (defaults to 0)
        #[arg(long)]
        thread: Option<i32>,
        /// Callstack source kind: raw or hierarchy
        #[arg(long, default_value = "raw")]
        kind: String,
        /// Raw sample index for kind=raw
        #[arg(long)]
        sample: Option<i32>,
        /// Hierarchy item id for kind=hierarchy
        #[arg(long)]
        item: Option<i32>,
        /// Resolve instruction pointers to method/file info
        #[arg(long)]
        resolve_methods: bool,
    },
    /// Aggregate usage stats across a frame range
    Summary {
        /// First frame index (defaults to earliest buffered frame)
        #[arg(long)]
        first_frame: Option<i32>,
        /// Last frame index (defaults to latest buffered frame)
        #[arg(long)]
        last_frame: Option<i32>,
        /// Thread index for thread-local aggregation (defaults to 0)
        #[arg(long)]
        thread: Option<i32>,
        /// Number of top markers to include
        #[arg(long, default_value_t = 10)]
        limit: u32,
    },
}

#[derive(Subcommand, Debug, Clone)]
pub enum ProfilerConfigAction {
    /// Read current profiler settings
    Get,
    /// Update profiler settings
    Set {
        /// Target profiling mode: play or edit
        #[arg(long)]
        mode: Option<String>,
        /// Enable or disable deep profiling
        #[arg(long)]
        deep_profile: Option<bool>,
        /// Enable or disable allocation callstacks
        #[arg(long)]
        allocation_callstacks: Option<bool>,
        /// Enable or disable binary capture logging
        #[arg(long)]
        binary_log: Option<bool>,
        /// Capture file path for binary logging
        #[arg(long)]
        output: Option<String>,
        /// Max profiler buffer memory in bytes
        #[arg(long)]
        max_used_memory: Option<u64>,
        /// Enable one or more profiler categories
        #[arg(long = "enable-category")]
        enable_categories: Vec<String>,
        /// Disable one or more profiler categories
        #[arg(long = "disable-category")]
        disable_categories: Vec<String>,
    },
}

#[derive(Subcommand, Debug, Clone)]
pub enum ProfilerSessionAction {
    /// Start a profiling session
    Start {
        /// Target profiling mode: play or edit
        #[arg(long)]
        mode: Option<String>,
        /// Enable or disable deep profiling
        #[arg(long)]
        deep_profile: Option<bool>,
        /// Enable or disable allocation callstacks
        #[arg(long)]
        allocation_callstacks: Option<bool>,
        /// Enable or disable binary capture logging
        #[arg(long)]
        binary_log: Option<bool>,
        /// Capture file path for binary logging
        #[arg(long)]
        output: Option<String>,
        /// Max profiler buffer memory in bytes
        #[arg(long)]
        max_used_memory: Option<u64>,
        /// Enable one or more profiler categories
        #[arg(long = "enable-category")]
        enable_categories: Vec<String>,
        /// Disable one or more profiler categories
        #[arg(long = "disable-category")]
        disable_categories: Vec<String>,
        /// Clear buffered frames before starting
        #[arg(long)]
        clear_first: bool,
    },
    /// Stop the current profiling session
    Stop,
    /// Clear buffered profiler data
    Clear,
}

#[derive(Subcommand, Debug, Clone)]
pub enum ProfilerCaptureAction {
    /// Copy the current capture to a path
    Save {
        /// Destination path for the copied capture
        #[arg(long)]
        output: String,
    },
    /// Load an existing .raw or .data capture into the Profiler
    Load {
        /// Input capture path
        #[arg(long)]
        input: String,
    },
}

#[derive(Subcommand, Debug, Clone)]
pub enum ProfilerFramesAction {
    /// List buffered frames
    List {
        /// First frame index (defaults to earliest buffered frame)
        #[arg(long)]
        first_frame: Option<i32>,
        /// Last frame index (defaults to latest buffered frame)
        #[arg(long)]
        last_frame: Option<i32>,
        /// Max frames to return
        #[arg(long, default_value_t = 20)]
        limit: u32,
    },
    /// Show one buffered frame in detail
    Show {
        /// Frame index (defaults to latest)
        #[arg(long)]
        frame: Option<i32>,
        /// Include thread summaries and counters
        #[arg(long)]
        include_threads: bool,
    },
}

pub async fn run(action: ProfilerAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        ProfilerAction::Status => render_call(
            ctx,
            "profiler/status",
            json!({}),
            render_status,
            "Profiler status retrieved",
        )
        .await,
        ProfilerAction::Config { action } => match action {
            ProfilerConfigAction::Get => {
                render_call(
                    ctx,
                    "profiler/config/get",
                    json!({}),
                    render_config,
                    "Profiler config retrieved",
                )
                .await
            }
            ProfilerConfigAction::Set {
                mode,
                deep_profile,
                allocation_callstacks,
                binary_log,
                output,
                max_used_memory,
                enable_categories,
                disable_categories,
            } => {
                let payload = config_payload(
                    mode,
                    deep_profile,
                    allocation_callstacks,
                    binary_log,
                    output,
                    max_used_memory,
                    enable_categories,
                    disable_categories,
                );
                render_call(
                    ctx,
                    "profiler/config/set",
                    payload,
                    render_config,
                    "Profiler config updated",
                )
                .await
            }
        },
        ProfilerAction::Session { action } => match action {
            ProfilerSessionAction::Start {
                mode,
                deep_profile,
                allocation_callstacks,
                binary_log,
                output,
                max_used_memory,
                enable_categories,
                disable_categories,
                clear_first,
            } => {
                let payload = session_payload(
                    mode.clone(),
                    deep_profile,
                    allocation_callstacks,
                    binary_log,
                    output,
                    max_used_memory,
                    enable_categories,
                    disable_categories,
                    clear_first,
                );
                let result =
                    call_bridge(ctx, "profiler/session/start", payload, mode.as_deref()).await?;
                render_result(ctx, &result, render_session, "Profiler session started")
            }
            ProfilerSessionAction::Stop => {
                render_call(
                    ctx,
                    "profiler/session/stop",
                    json!({}),
                    render_session,
                    "Profiler session stopped",
                )
                .await
            }
            ProfilerSessionAction::Clear => {
                render_call(
                    ctx,
                    "profiler/session/clear",
                    json!({}),
                    render_simple,
                    "Profiler frames cleared",
                )
                .await
            }
        },
        ProfilerAction::Capture { action } => match action {
            ProfilerCaptureAction::Save { output } => {
                render_call(
                    ctx,
                    "profiler/capture/save",
                    json!({ "output": output }),
                    render_capture,
                    "Profiler capture saved",
                )
                .await
            }
            ProfilerCaptureAction::Load { input } => {
                render_call(
                    ctx,
                    "profiler/capture/load",
                    json!({ "input": input }),
                    render_capture,
                    "Profiler capture loaded",
                )
                .await
            }
        },
        ProfilerAction::Frames { action } => match action {
            ProfilerFramesAction::List {
                first_frame,
                last_frame,
                limit,
            } => {
                render_call(
                    ctx,
                    "profiler/frames/list",
                    range_payload(first_frame, last_frame, limit),
                    render_frames,
                    "Profiler frames listed",
                )
                .await
            }
            ProfilerFramesAction::Show {
                frame,
                include_threads,
            } => {
                let mut payload = json!({});
                if let Some(frame) = frame {
                    payload["frame"] = json!(frame);
                }
                payload["includeThreads"] = json!(include_threads);
                render_call(
                    ctx,
                    "profiler/frames/show",
                    payload,
                    render_frame_detail,
                    "Profiler frame loaded",
                )
                .await
            }
        },
        ProfilerAction::Hierarchy {
            frame,
            thread,
            sort,
            limit,
            max_depth,
        } => {
            let mut payload = json!({
                "sort": sort,
                "limit": limit,
            });
            if let Some(frame) = frame {
                payload["frame"] = json!(frame);
            }
            if let Some(thread) = thread {
                payload["thread"] = json!(thread);
            }
            if let Some(max_depth) = max_depth {
                payload["maxDepth"] = json!(max_depth);
            }
            render_call(
                ctx,
                "profiler/hierarchy",
                payload,
                render_hierarchy,
                "Profiler hierarchy loaded",
            )
            .await
        }
        ProfilerAction::Timeline {
            frame,
            thread,
            limit,
            max_depth,
            include_metadata,
        } => {
            let mut payload = json!({
                "limit": limit,
                "includeMetadata": include_metadata,
            });
            if let Some(frame) = frame {
                payload["frame"] = json!(frame);
            }
            if let Some(thread) = thread {
                payload["thread"] = json!(thread);
            }
            if let Some(max_depth) = max_depth {
                payload["maxDepth"] = json!(max_depth);
            }
            render_call(
                ctx,
                "profiler/timeline",
                payload,
                render_timeline,
                "Profiler timeline loaded",
            )
            .await
        }
        ProfilerAction::Callstacks {
            frame,
            thread,
            kind,
            sample,
            item,
            resolve_methods,
        } => {
            if kind == "raw" && sample.is_none() {
                bail!("--sample is required when --kind raw");
            }
            if kind == "hierarchy" && item.is_none() {
                bail!("--item is required when --kind hierarchy");
            }

            let mut payload = json!({
                "kind": kind,
                "resolveMethods": resolve_methods,
            });
            if let Some(frame) = frame {
                payload["frame"] = json!(frame);
            }
            if let Some(thread) = thread {
                payload["thread"] = json!(thread);
            }
            if let Some(sample) = sample {
                payload["sample"] = json!(sample);
            }
            if let Some(item) = item {
                payload["item"] = json!(item);
            }
            render_call(
                ctx,
                "profiler/callstacks",
                payload,
                render_callstacks,
                "Profiler callstack loaded",
            )
            .await
        }
        ProfilerAction::Summary {
            first_frame,
            last_frame,
            thread,
            limit,
        } => {
            let mut payload = range_payload(first_frame, last_frame, limit);
            if let Some(thread) = thread {
                payload["thread"] = json!(thread);
            }
            render_call(
                ctx,
                "profiler/summary",
                payload,
                render_summary,
                "Profiler summary generated",
            )
            .await
        }
    }
}

async fn render_call(
    ctx: &Context,
    method: &str,
    payload: Value,
    human_renderer: fn(&Value),
    success_message: &str,
) -> anyhow::Result<()> {
    let result = call_bridge(ctx, method, payload, None).await?;
    render_result(ctx, &result, human_renderer, success_message)
}

async fn call_bridge(
    ctx: &Context,
    method: &str,
    payload: Value,
    requested_mode: Option<&str>,
) -> anyhow::Result<Value> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = client.call(method, payload).await?;
    client.close().await;

    if method == "profiler/session/start" {
        return wait_for_target_mode(ctx, result, requested_mode).await;
    }

    Ok(result)
}

fn render_result(
    ctx: &Context,
    result: &Value,
    human_renderer: fn(&Value),
    success_message: &str,
) -> anyhow::Result<()> {
    if ctx.json {
        output::print_json(&output::success_json(result.clone()));
    } else {
        output::print_success(success_message);
        human_renderer(result);
    }
    Ok(())
}

async fn wait_for_target_mode(
    ctx: &Context,
    initial: Value,
    requested_mode: Option<&str>,
) -> anyhow::Result<Value> {
    let Some(requested_mode) = requested_mode.map(str::to_ascii_lowercase) else {
        return Ok(initial);
    };

    let started = Instant::now();
    let timeout = Duration::from_secs(ctx.timeout.max(1));
    loop {
        let (_, _, mut client) = super::connect_client(ctx).await?;
        let status = client.call("profiler/status", json!({})).await?;
        client.close().await;

        let editor_state = status
            .get("editorState")
            .and_then(Value::as_object)
            .cloned()
            .unwrap_or_default();

        let in_play = editor_state
            .get("playing")
            .and_then(Value::as_bool)
            .unwrap_or(false);
        let in_transition = editor_state
            .get("willChange")
            .and_then(Value::as_bool)
            .unwrap_or(false);

        let effective_mode = status
            .get("session")
            .and_then(|v| v.get("effectiveMode"))
            .and_then(Value::as_str)
            .unwrap_or_default();

        let target_met = match requested_mode.as_str() {
            "play" => in_play && effective_mode == "play",
            "edit" => !in_play && (effective_mode == "edit" || effective_mode.is_empty()),
            _ => true,
        };

        if target_met {
            return Ok(status);
        }

        if started.elapsed() >= timeout {
            bail!("Timed out waiting for profiler to settle into {requested_mode} mode");
        }

        if !in_transition && started.elapsed() > Duration::from_secs(1) {
            return Ok(status);
        }

        sleep(Duration::from_millis(250)).await;
    }
}

fn config_payload(
    mode: Option<String>,
    deep_profile: Option<bool>,
    allocation_callstacks: Option<bool>,
    binary_log: Option<bool>,
    output: Option<String>,
    max_used_memory: Option<u64>,
    enable_categories: Vec<String>,
    disable_categories: Vec<String>,
) -> Value {
    let mut payload = json!({});
    if let Some(mode) = mode {
        payload["mode"] = json!(mode);
    }
    if let Some(deep_profile) = deep_profile {
        payload["deepProfile"] = json!(deep_profile);
    }
    if let Some(allocation_callstacks) = allocation_callstacks {
        payload["allocationCallstacks"] = json!(allocation_callstacks);
    }
    if let Some(binary_log) = binary_log {
        payload["binaryLog"] = json!(binary_log);
    }
    if let Some(output) = output {
        payload["output"] = json!(output);
    }
    if let Some(max_used_memory) = max_used_memory {
        payload["maxUsedMemory"] = json!(max_used_memory);
    }
    if !enable_categories.is_empty() {
        payload["enableCategories"] = json!(enable_categories);
    }
    if !disable_categories.is_empty() {
        payload["disableCategories"] = json!(disable_categories);
    }
    payload
}

fn session_payload(
    mode: Option<String>,
    deep_profile: Option<bool>,
    allocation_callstacks: Option<bool>,
    binary_log: Option<bool>,
    output: Option<String>,
    max_used_memory: Option<u64>,
    enable_categories: Vec<String>,
    disable_categories: Vec<String>,
    clear_first: bool,
) -> Value {
    let mut payload = config_payload(
        mode,
        deep_profile,
        allocation_callstacks,
        binary_log,
        output,
        max_used_memory,
        enable_categories,
        disable_categories,
    );
    if clear_first {
        payload["clearFirst"] = json!(true);
    }
    payload
}

fn range_payload(first_frame: Option<i32>, last_frame: Option<i32>, limit: u32) -> Value {
    let mut payload = json!({ "limit": limit });
    if let Some(first_frame) = first_frame {
        payload["firstFrame"] = json!(first_frame);
    }
    if let Some(last_frame) = last_frame {
        payload["lastFrame"] = json!(last_frame);
    }
    payload
}

fn render_status(result: &Value) {
    let active = result
        .get("session")
        .and_then(|v| v.get("active"))
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let effective_mode = result
        .get("session")
        .and_then(|v| v.get("effectiveMode"))
        .and_then(Value::as_str)
        .unwrap_or("unknown");
    let frame_count = result
        .get("frames")
        .and_then(|v| v.get("count"))
        .and_then(Value::as_i64)
        .unwrap_or(0);

    eprintln!(
        "  active: {active}\n  mode: {effective_mode}\n  buffered frames: {frame_count}"
    );

    if let Some(output_path) = result
        .get("session")
        .and_then(|v| v.get("outputPath"))
        .and_then(Value::as_str)
        .filter(|path| !path.is_empty())
    {
        eprintln!("  output: {output_path}");
    }

    render_warnings(result);
}

fn render_config(result: &Value) {
    let config = result.get("config").unwrap_or(result);
    output::print_json(config);
    render_warnings(result);
}

fn render_session(result: &Value) {
    let session = result.get("session").unwrap_or(result);
    output::print_json(session);
    render_warnings(result);
}

fn render_capture(result: &Value) {
    let capture = result.get("capture").unwrap_or(result);
    output::print_json(capture);
    render_warnings(result);
}

fn render_frames(result: &Value) {
    let frames = result.get("frames").unwrap_or(result);
    output::print_json(frames);
    render_warnings(result);
}

fn render_frame_detail(result: &Value) {
    let frame = result.get("frame").unwrap_or(result);
    output::print_json(frame);
    render_warnings(result);
}

fn render_hierarchy(result: &Value) {
    let hierarchy = result.get("items").unwrap_or(result);
    output::print_json(hierarchy);
    render_warnings(result);
}

fn render_timeline(result: &Value) {
    let timeline = result.get("samples").unwrap_or(result);
    output::print_json(timeline);
    render_warnings(result);
}

fn render_callstacks(result: &Value) {
    let callstack = result.get("callstack").unwrap_or(result);
    output::print_json(callstack);
    render_warnings(result);
}

fn render_summary(result: &Value) {
    let summary = result.get("summary").unwrap_or(result);
    let stats = summary.get("stats").unwrap_or(summary);
    let frame_count = stats
        .get("frameCount")
        .and_then(Value::as_i64)
        .unwrap_or_default();
    let avg_cpu = stats
        .get("avgCpuMs")
        .and_then(Value::as_f64)
        .unwrap_or_default();
    let avg_fps = stats
        .get("avgFps")
        .and_then(Value::as_f64)
        .unwrap_or_default();
    eprintln!("  frames: {frame_count}\n  avg cpu ms: {avg_cpu:.3}\n  avg fps: {avg_fps:.2}");

    if let Some(markers) = summary.get("topMarkers").and_then(Value::as_array) {
        for marker in markers.iter().take(5) {
            let name = marker.get("name").and_then(Value::as_str).unwrap_or("?");
            let self_ms = marker
                .get("selfMs")
                .and_then(Value::as_f64)
                .unwrap_or_default();
            eprintln!("    - {name}: {self_ms:.3} ms self");
        }
    }

    render_warnings(result);
}

fn render_simple(result: &Value) {
    output::print_json(result);
    render_warnings(result);
}

fn render_warnings(result: &Value) {
    if let Some(warnings) = result.get("warnings").and_then(Value::as_array) {
        for warning in warnings {
            if let Some(message) = warning.as_str() {
                output::print_warn(message);
            }
        }
    }
}
