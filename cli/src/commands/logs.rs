use crate::client::BridgeClient;
use crate::output;
use clap::{Args, Subcommand};
use console::style;
use serde_json::Value;

use super::Context;

const MAX_MESSAGE_PREVIEW: usize = 160;

#[derive(Args, Clone, Debug, Default)]
pub struct LogsArgs {
    /// Filter by level: info, warn, error
    #[arg(long)]
    pub level: Option<String>,
    /// Read up to N buffered logs when searching or tailing, or stop after N live logs when following
    #[arg(long)]
    pub count: Option<u32>,
    /// Search buffered logs by regex pattern
    #[arg(long)]
    pub pattern: Option<String>,
    /// Read one buffered log entry by id
    #[arg(long)]
    pub id: Option<u64>,
    /// Restrict buffered reads to log ids lower than this value
    #[arg(long, value_name = "ID")]
    pub before_id: Option<u64>,
    /// Restrict buffered reads to log ids higher than this value
    #[arg(long, value_name = "ID")]
    pub after_id: Option<u64>,
    /// Follow live log notifications instead of reading buffered history
    #[arg(long)]
    pub follow: bool,
}

#[derive(Subcommand, Clone, Debug)]
pub enum LogsAction {
    /// Print a curated summary of the buffered debug log state
    Status,
}

pub async fn run(
    action: Option<LogsAction>,
    args: LogsArgs,
    ctx: &Context,
) -> anyhow::Result<()> {
    match action {
        Some(LogsAction::Status) => run_status(ctx).await,
        None => run_query(args, ctx).await,
    }
}

pub async fn fetch_status(client: &mut BridgeClient) -> anyhow::Result<Value> {
    Ok(client.call("logs/status", serde_json::json!({})).await?)
}

pub fn print_status(status: &Value, ctx: &Context) {
    if ctx.json {
        output::print_json(&output::success_json(status.clone()));
        return;
    }

    let total = status.get("total").and_then(Value::as_u64).unwrap_or(0);
    let unique = status.get("uniqueCount").and_then(Value::as_u64).unwrap_or(0);
    output::print_success(&format!("Buffered logs: {total} entries across {unique} categories"));

    if let Some(by_level) = status.get("byLevel").and_then(Value::as_object) {
        let info = by_level.get("info").and_then(Value::as_u64).unwrap_or(0);
        let warnings = by_level.get("warning").and_then(Value::as_u64).unwrap_or(0);
        let errors = by_level.get("error").and_then(Value::as_u64).unwrap_or(0);
        let exceptions = by_level.get("exception").and_then(Value::as_u64).unwrap_or(0);
        eprintln!("  Levels: info={info}, warning={warnings}, error={errors}, exception={exceptions}");
    }

    if let Some(window) = status.get("historyWindowSeconds").and_then(Value::as_f64) {
        let first = status
            .get("firstTimestamp")
            .and_then(Value::as_str)
            .unwrap_or("?");
        let last = status
            .get("lastTimestamp")
            .and_then(Value::as_str)
            .unwrap_or("?");
        eprintln!("  Window: {window:.2}s ({first} -> {last})");
    }

    if let Some(play) = status.get("play") {
        let playing = play.get("playing").and_then(Value::as_bool).unwrap_or(false);
        if playing {
            if let Some(duration) = play.get("currentPlayDurationSeconds").and_then(Value::as_f64) {
                eprintln!("  Play: active for {duration:.2}s");
            } else {
                eprintln!("  Play: active");
            }
        } else if let Some(duration) = play.get("lastPlayDurationSeconds").and_then(Value::as_f64) {
            eprintln!("  Last play: {duration:.2}s");
        }
    }

    if let Some(last_play) = status.get("lastPlayWindow").and_then(Value::as_object) {
        let total = last_play.get("total").and_then(Value::as_u64).unwrap_or(0);
        let warnings = last_play.get("warnings").and_then(Value::as_u64).unwrap_or(0);
        let errors = last_play.get("errors").and_then(Value::as_u64).unwrap_or(0);
        let duration = last_play
            .get("durationSeconds")
            .and_then(Value::as_f64)
            .unwrap_or(0.0);
        eprintln!("  Last play window: {total} log(s), {warnings} warning(s), {errors} error/exception(s) over {duration:.2}s");
    }

    if let Some(categories) = status.get("topCategories").and_then(Value::as_array) {
        if !categories.is_empty() {
            eprintln!("  Top categories:");
            for category in categories.iter().take(5) {
                let count = category.get("count").and_then(Value::as_u64).unwrap_or(0);
                let level = category
                    .get("level")
                    .and_then(Value::as_str)
                    .unwrap_or("info");
                let sample = category
                    .get("sampleMessage")
                    .and_then(Value::as_str)
                    .unwrap_or("?");
                eprintln!("    {count}x [{level}] {}", preview_text(sample, 120));
            }
        }
    }
}

async fn run_status(ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let status = fetch_status(&mut client).await?;
    client.close().await;
    print_status(&status, ctx);
    Ok(())
}

async fn run_query(args: LogsArgs, ctx: &Context) -> anyhow::Result<()> {
    if args.id.is_some()
        && (args.pattern.is_some() || args.before_id.is_some() || args.after_id.is_some() || args.follow)
    {
        anyhow::bail!("--id cannot be combined with --pattern, --before-id, --after-id, or --follow");
    }

    let (_, _, mut client) = super::connect_client(ctx).await?;

    let wants_live_follow = args.follow
        || (args.id.is_none()
            && args.pattern.is_none()
            && args.count.is_none()
            && args.before_id.is_none()
            && args.after_id.is_none());

    if let Some(log_id) = args.id {
        let result = client
            .call("logs/get", serde_json::json!({ "id": log_id }))
            .await?;
        client.close().await;
        print_log_detail(&result, ctx);
        return Ok(());
    }

    if wants_live_follow {
        stream_live_logs(args.level, args.count, &mut client, ctx).await?;
        client.close().await;
        return Ok(());
    }

    let method = if args.pattern.is_some() {
        "logs/search"
    } else {
        "logs/tail"
    };
    let mut params = serde_json::json!({});
    if let Some(level) = args.level.as_deref() {
        params["level"] = serde_json::json!(level);
    }
    if let Some(count) = args.count {
        params["count"] = serde_json::json!(count);
    }
    if let Some(pattern) = args.pattern.as_deref() {
        params["pattern"] = serde_json::json!(pattern);
    }
    if let Some(before_id) = args.before_id {
        params["beforeId"] = serde_json::json!(before_id);
    }
    if let Some(after_id) = args.after_id {
        params["afterId"] = serde_json::json!(after_id);
    }

    let result = client.call(method, params).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        print_log_list(&result, args.pattern.is_some());
    }

    Ok(())
}

async fn stream_live_logs(
    level: Option<String>,
    count: Option<u32>,
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    let params = serde_json::json!({
        "level": level.as_deref().unwrap_or("info"),
    });
    client.call("logs/subscribe", params).await?;

    if !ctx.json {
        output::print_info("Streaming live logs (Ctrl+C to stop)...");
    }

    let mut received = 0_u32;
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
                    eprintln!("{}", render_log_summary(&notif.params));
                }
            }
            Some(_) => continue,
            None => break,
        }
    }

    Ok(())
}

fn print_log_detail(result: &serde_json::Value, ctx: &Context) {
    if ctx.json {
        output::print_json(&output::success_json(result.clone()));
        return;
    }

    let id = result.get("id").and_then(|v| v.as_u64()).unwrap_or(0);
    let level = result
        .get("level")
        .and_then(|v| v.as_str())
        .unwrap_or("info");
    let timestamp = result
        .get("timestamp")
        .and_then(|v| v.as_str())
        .unwrap_or("?");
    let message = result.get("message").and_then(|v| v.as_str()).unwrap_or("");
    output::print_success(&format!("Log #{id} [{level}] {timestamp}"));
    eprintln!("  {message}");
    if let Some(stack_trace) = result.get("stackTrace").and_then(|v| v.as_str()) {
        if !stack_trace.trim().is_empty() {
            eprintln!();
            eprintln!("{stack_trace}");
        }
    }
}

fn print_log_list(result: &serde_json::Value, used_pattern: bool) {
    let total = result.get("total").and_then(|v| v.as_u64()).unwrap_or(0);
    let returned = result.get("returned").and_then(|v| v.as_u64()).unwrap_or(0);
    let truncated = result
        .get("truncated")
        .and_then(|v| v.as_bool())
        .unwrap_or(false);
    let logs = result
        .get("logs")
        .and_then(|v| v.as_array())
        .cloned()
        .unwrap_or_default();

    if total == 0 {
        output::print_success(if used_pattern {
            "No logs matched the current search"
        } else {
            "No buffered logs available"
        });
        return;
    }

    output::print_success(&format!("Found {total} log(s) (showing {returned})"));
    for log in logs {
        eprintln!("{}", render_log_summary(&log));
    }

    if truncated {
        output::print_info(
            "More buffered logs matched than were returned. Increase `--count`, narrow the filters, or inspect a specific entry with `ucp logs --id <id>`.",
        );
    }
}

fn render_log_summary(log: &serde_json::Value) -> String {
    let id = log.get("id").and_then(|v| v.as_u64()).unwrap_or(0);
    let level = log.get("level").and_then(|v| v.as_str()).unwrap_or("info");
    let timestamp = log.get("timestamp").and_then(|v| v.as_str()).unwrap_or("?");
    let message = log
        .get("messagePreview")
        .or_else(|| log.get("message"))
        .and_then(|v| v.as_str())
        .unwrap_or("");
    let message = preview_text(message, MAX_MESSAGE_PREVIEW);
    let styled_level = match level {
        "error" | "exception" => style(format!("[{level:>9}] ")).red(),
        "warning" | "warn" => style(format!("[{level:>9}] ")).yellow(),
        _ => style(format!("[{level:>9}] ")).dim(),
    };
    format!("  #{id:<5} {styled_level}{timestamp} {message}")
}

fn preview_text(value: &str, max_chars: usize) -> String {
    if value.chars().count() <= max_chars {
        value.to_string()
    } else {
        let truncated: String = value.chars().take(max_chars).collect();
        format!("{truncated}...")
    }
}

#[cfg(test)]
mod tests {
    use super::{preview_text, render_log_summary};

    #[test]
    fn preview_text_truncates_long_messages() {
        let preview = preview_text("abcdefghijklmnopqrstuvwxyz", 10);
        assert_eq!(preview, "abcdefghij...");
    }

    #[test]
    fn render_log_summary_uses_id_and_message_preview() {
        let rendered = render_log_summary(&serde_json::json!({
            "id": 42,
            "level": "warning",
            "timestamp": "2026-03-12T00:00:00Z",
            "messagePreview": "Something happened"
        }));

        assert!(rendered.contains("#42"));
        assert!(rendered.contains("Something happened"));
    }
}
