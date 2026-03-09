use crate::client::BridgeClient;
use crate::discovery;
use crate::output;

use super::Context;

pub async fn run(method: &str, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = client.call(method, serde_json::json!({})).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let label = match method {
            "play" => "Entered play mode",
            "stop" => "Exited play mode",
            "pause" => "Toggled pause",
            _ => "Done",
        };
        output::print_success(label);
    }

    Ok(())
}
