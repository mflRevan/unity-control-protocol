use crate::output;

use super::Context;

pub async fn run(
    view: &str,
    width: u32,
    height: u32,
    out_path: Option<String>,
    base64: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    let (project, _, mut client) = super::connect_client(ctx).await?;

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

    let out_path = match (out_path, base64) {
        (Some(path), _) => Some(path),
        (None, true) => None,
        (None, false) => Some(super::default_capture_path(&project, "screenshot", "png")?),
    };
    if let Some(path) = out_path {
        if let Some(b64) = result.get("data").and_then(|v| v.as_str()) {
            use base64::Engine;
            let bytes = base64::engine::general_purpose::STANDARD
                .decode(b64)
                .map_err(|e| anyhow::anyhow!("Failed to decode base64: {e}"))?;
            std::fs::write(&path, &bytes)?;
            if ctx.json {
                output::print_json(&output::success_json(serde_json::json!({
                    "path": path,
                    "bytes": bytes.len(),
                    "width": result.get("width"),
                    "height": result.get("height"),
                    "view": view,
                })));
            } else {
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
