use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use console::style;

use super::Context;

pub async fn run(ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;

    if !ctx.json {
        output::print_info(&format!(
            "Connecting to bridge on port {}...",
            lock.port
        ));
    }

    let mut client = BridgeClient::connect(&lock).await?;
    let info = client.handshake().await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(info));
    } else {
        output::print_success("Connected to Unity bridge");
        if let Some(obj) = info.as_object() {
            let bar = if output::supports_unicode() { "│" } else { "|" };
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
