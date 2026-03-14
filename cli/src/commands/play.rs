use crate::output;
use serde_json::Value;

use super::Context;

pub async fn run(method: &str, payload: Value, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let result = client.call(method, payload).await?;
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
