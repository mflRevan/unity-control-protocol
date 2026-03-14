use crate::output;

use super::Context;

pub async fn run(
    view: &str,
    width: u32,
    height: u32,
    out_path: Option<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let result = client
        .call(
            "screenshot",
            serde_json::json!({
                "view": view,
                "width": width,
                "height": height,
            }),
        )
        .await?;

    client.close().await;

    if let Some(path) = out_path {
        if let Some(b64) = result.get("data").and_then(|v| v.as_str()) {
            use base64::Engine;
            let bytes = base64::engine::general_purpose::STANDARD
                .decode(b64)
                .map_err(|e| anyhow::anyhow!("Failed to decode base64: {e}"))?;
            std::fs::write(&path, &bytes)?;
            if !ctx.json {
                output::print_success(&format!("Screenshot saved to {path}"));
            }
        }
    } else if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        // Print base64 to stdout for piping
        if let Some(b64) = result.get("data").and_then(|v| v.as_str()) {
            println!("{b64}");
        }
    }

    Ok(())
}
