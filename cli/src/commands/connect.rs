use crate::output;
use console::style;

use super::Context;

pub async fn run(ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    if !ctx.json {
        output::print_info("Connecting to bridge...");
    }

    let info = client.handshake().await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(info));
    } else {
        output::print_success("Connected to Unity bridge");
        if let Some(obj) = info.as_object() {
            let bar = if output::supports_unicode() {
                "│"
            } else {
                "|"
            };
            if let Some(v) = obj.get("unityVersion") {
                eprintln!("  {} Unity {}", style(bar).dim(), v.as_str().unwrap_or("?"));
            }
            if let Some(v) = obj.get("projectName") {
                eprintln!(
                    "  {} Project: {}",
                    style(bar).dim(),
                    v.as_str().unwrap_or("?")
                );
            }
            if let Some(v) = obj.get("protocolVersion") {
                eprintln!(
                    "  {} Protocol: {}",
                    style(bar).dim(),
                    v.as_str().unwrap_or("?")
                );
            }
        }
    }

    Ok(())
}
