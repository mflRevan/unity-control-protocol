use crate::output;
use serde_json::Value;
use tokio::time::{Duration, sleep};

use super::Context;

pub async fn run(seconds: u64, mode: Option<String>, ctx: &Context) -> anyhow::Result<()> {
    let seconds = seconds.max(1);
    let requested_mode = mode.unwrap_or_else(|| "play".to_string());

    let (_, _, mut client) = super::connect_client(ctx).await?;
    client
        .call(
            "profiler/session/start",
            serde_json::json!({
                "mode": requested_mode,
                "clearFirst": true,
                "enableCategories": ["Render", "Scripts", "Memory"]
            }),
        )
        .await?;
    client.close().await;

    if !ctx.json {
        output::print_info(&format!("Profiling for {seconds}s..."));
    }
    sleep(Duration::from_secs(seconds)).await;

    let (_, _, mut client) = super::connect_client(ctx).await?;
    let stop = client
        .call("profiler/session/stop", serde_json::json!({}))
        .await?;
    let summary = client
        .call("profiler/summary", serde_json::json!({ "limit": 12 }))
        .await?;
    client.close().await;

    let result = serde_json::json!({
        "seconds": seconds,
        "session": stop,
        "summary": summary.get("summary").cloned().unwrap_or(Value::Null),
        "warnings": summary.get("warnings").cloned().unwrap_or_else(|| serde_json::json!([]))
    });

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_success("Profile snapshot captured");
        if let Some(stats) = result
            .get("summary")
            .and_then(|v| v.get("stats"))
            .and_then(|v| v.as_object())
        {
            let frame_count = stats
                .get("frameCount")
                .and_then(|v| v.as_u64())
                .unwrap_or(0);
            let avg_cpu = stats
                .get("avgCpuMs")
                .and_then(|v| v.as_f64())
                .unwrap_or(0.0);
            let avg_gpu = stats
                .get("avgGpuMs")
                .and_then(|v| v.as_f64())
                .unwrap_or(0.0);
            let avg_fps = stats.get("avgFps").and_then(|v| v.as_f64()).unwrap_or(0.0);
            eprintln!("  Frames: {frame_count}");
            eprintln!("  Avg CPU: {avg_cpu:.2}ms");
            eprintln!("  Avg GPU: {avg_gpu:.2}ms");
            eprintln!("  Avg FPS: {avg_fps:.1}");
        }
    }

    Ok(())
}
