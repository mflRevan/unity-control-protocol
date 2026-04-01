use crate::output;
use tokio::time::{Duration, timeout};

use super::Context;

pub async fn run(mode: &str, filter: Option<String>, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(
        &mut client,
        super::ActiveSceneGuardPolicy::block_if_dirty("run Unity tests"),
    )
    .await?;

    if !ctx.json {
        output::print_info(&format!("Running {mode}-mode tests..."));
    }

    let mut params = serde_json::json!({ "mode": mode });
    if let Some(f) = &filter {
        params["filter"] = serde_json::json!(f);
    }

    let start_result = client.call("tests/run", params).await?;

    // Tests run asynchronously in Unity. Wait for the tests/result notification.
    if let Some(status) = start_result.get("status").and_then(|v| v.as_str()) {
        if status == "started" && !ctx.json {
            output::print_info("Tests started, waiting for results...");
        }
    }

    let wait_timeout = Duration::from_secs(ctx.timeout.max(1));

    // Wait for tests/result notification
    let result = loop {
        match timeout(wait_timeout, client.next_notification()).await {
            Ok(Some(notif)) if notif.method == "tests/result" => break notif.params,
            Ok(Some(_)) => continue, // skip other notifications (logs, etc.)
            Ok(None) => {
                anyhow::bail!("Connection closed before test results arrived");
            }
            Err(_) => {
                anyhow::bail!(
                    "Timed out after {}s waiting for Unity test results notification",
                    wait_timeout.as_secs()
                );
            }
        }
    };

    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        if let Some(summary) = result.get("summary") {
            let total = summary.get("total").and_then(|v| v.as_u64()).unwrap_or(0);
            let failed = summary.get("failed").and_then(|v| v.as_u64()).unwrap_or(0);
            let dur = summary
                .get("duration")
                .and_then(|v| v.as_f64())
                .unwrap_or(0.0);

            if failed == 0 {
                output::print_success(&format!("All {total} tests passed ({dur:.2}s)"));
            } else {
                output::print_error(&format!("{failed}/{total} tests failed ({dur:.2}s)"));
            }
        }

        if let Some(tests) = result.get("tests").and_then(|v| v.as_array()) {
            for t in tests {
                let st = t.get("status").and_then(|v| v.as_str()).unwrap_or("");
                if st == "failed" {
                    let name = t.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                    let msg = t.get("message").and_then(|v| v.as_str()).unwrap_or("");
                    let icon = if output::supports_unicode() {
                        "✖"
                    } else {
                        "x"
                    };
                    eprintln!("  {icon} {name}");
                    if !msg.is_empty() {
                        eprintln!("    {msg}");
                    }
                }
            }
        }
    }

    Ok(())
}
