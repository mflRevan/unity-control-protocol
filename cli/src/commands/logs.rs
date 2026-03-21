use crate::client::BridgeClient;
use crate::output;
use console::style;

use super::Context;

const MAX_MESSAGE_PREVIEW: usize = 160;

pub async fn run(
    level: Option<String>,
    count: Option<u32>,
    pattern: Option<String>,
    id: Option<u64>,
    before_id: Option<u64>,
    after_id: Option<u64>,
    follow: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    if id.is_some() && (pattern.is_some() || before_id.is_some() || after_id.is_some() || follow) {
        anyhow::bail!(
            "--id cannot be combined with --pattern, --before-id, --after-id, or --follow"
        );
    }

    let (_, _, mut client) = super::connect_client(ctx).await?;

    let wants_live_follow = follow
        || (id.is_none()
            && pattern.is_none()
            && count.is_none()
            && before_id.is_none()
            && after_id.is_none());

    if let Some(log_id) = id {
        let result = client
            .call("logs/get", serde_json::json!({ "id": log_id }))
            .await?;
        client.close().await;
        print_log_detail(&result, ctx);
        return Ok(());
    }

    if wants_live_follow {
        stream_live_logs(level, count, &mut client, ctx).await?;
        client.close().await;
        return Ok(());
    }

    let method = if pattern.is_some() {
        "logs/search"
    } else {
        "logs/tail"
    };
    let mut params = serde_json::json!({});
    if let Some(level) = level.as_deref() {
        params["level"] = serde_json::json!(level);
    }
    if let Some(count) = count {
        params["count"] = serde_json::json!(count);
    }
    if let Some(pattern) = pattern.as_deref() {
        params["pattern"] = serde_json::json!(pattern);
    }
    if let Some(before_id) = before_id {
        params["beforeId"] = serde_json::json!(before_id);
    }
    if let Some(after_id) = after_id {
        params["afterId"] = serde_json::json!(after_id);
    }

    let result = client.call(method, params).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        print_log_list(&result, pattern.is_some());
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
