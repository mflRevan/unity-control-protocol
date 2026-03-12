use crate::client::BridgeClient;
use crate::discovery;
use crate::output;

use super::Context;

pub async fn run(filter: Option<String>, depth: u32, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let mut params = serde_json::json!({ "depth": depth });
    if let Some(f) = &filter {
        params["filter"] = serde_json::json!(f);
    }

    let result = client.call("snapshot", params).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_json(&result);
    }

    Ok(())
}
