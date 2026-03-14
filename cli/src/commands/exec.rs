use crate::output;

use super::Context;

pub async fn list(ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let result = client.call("exec/list", serde_json::json!({})).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else if let Some(scripts) = result.get("scripts").and_then(|v| v.as_array()) {
        if scripts.is_empty() {
            output::print_warn("No scripts found. Implement IUCPScript in your Editor scripts.");
        } else {
            output::print_info(&format!("Found {} script(s):", scripts.len()));
            for s in scripts {
                let name = s.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let desc = s.get("description").and_then(|v| v.as_str()).unwrap_or("");
                eprintln!("  {name} -- {desc}");
            }
        }
    }

    Ok(())
}

pub async fn run(name: &str, params: Option<String>, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    if !ctx.json {
        output::print_info(&format!("Running script: {name}"));
    }

    let script_params = match &params {
        Some(p) => serde_json::from_str(p).unwrap_or(serde_json::json!({})),
        None => serde_json::json!({}),
    };

    let result = client
        .call(
            "exec/run",
            serde_json::json!({ "name": name, "params": script_params }),
        )
        .await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_success(&format!("Script '{name}' completed"));
        if let Some(script_result) = result.get("result") {
            output::print_json(script_result);
        }
    }

    Ok(())
}
