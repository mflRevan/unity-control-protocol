use crate::output;

use super::Context;

pub async fn capture(out: String, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = client
        .call(
            "profiler/capture/save",
            serde_json::json!({ "output": out }),
        )
        .await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let path = result
            .get("capture")
            .and_then(|v| v.get("path"))
            .and_then(|v| v.as_str())
            .unwrap_or("frame.json");
        output::print_success(&format!("Frame capture written: {path}"));
    }

    Ok(())
}
