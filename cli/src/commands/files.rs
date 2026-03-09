use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use super::Context;
use super::compile;

pub async fn read(path: &str, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = client
        .call("file/read", serde_json::json!({ "path": path }))
        .await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else if let Some(content) = result.get("content").and_then(|v| v.as_str()) {
        print!("{content}");
    }

    Ok(())
}

pub async fn write(path: &str, content: Option<String>, do_compile: bool, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;

    let content = match content {
        Some(c) => c,
        None => {
            use std::io::Read;
            let mut buf = String::new();
            std::io::stdin().read_to_string(&mut buf)?;
            buf
        }
    };

    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = client
        .call(
            "file/write",
            serde_json::json!({ "path": path, "content": content }),
        )
        .await?;
    client.close().await;

    if ctx.json && !do_compile {
        output::print_json(&output::success_json(result));
    } else if !ctx.json {
        output::print_success(&format!("Written: {path}"));
    }

    if do_compile {
        compile::run(false, ctx).await?;
    }

    Ok(())
}

pub async fn patch(path: &str, find: Option<String>, replace: Option<String>, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;

    let find = find.ok_or_else(|| anyhow::anyhow!("--find is required for patch-file"))?;
    let replace = replace.unwrap_or_default();

    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = client
        .call(
            "file/patch",
            serde_json::json!({ "path": path, "patch": { "find": find, "replace": replace } }),
        )
        .await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_success(&format!("Patched: {path}"));
    }

    Ok(())
}
