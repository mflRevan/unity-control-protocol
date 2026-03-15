use crate::output;
use clap::Subcommand;

use super::compile;
use super::Context;

#[derive(Subcommand)]
pub enum FilesAction {
    /// Read a project file
    Read {
        /// File path relative to project root
        path: String,
    },
    /// Write content to a project file
    Write {
        /// File path relative to project root
        path: String,
        /// File content (reads from stdin if omitted)
        #[arg(long)]
        content: Option<String>,
        /// Trigger recompilation after write and wait for it to finish
        #[arg(long)]
        compile: bool,
    },
    /// Apply a find/replace patch to a project file
    Patch {
        /// File path relative to project root
        path: String,
        /// Text to find
        #[arg(long)]
        find: Option<String>,
        /// Text to replace with
        #[arg(long)]
        replace: Option<String>,
    },
}

pub async fn run(action: FilesAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        FilesAction::Read { path } => read(&path, ctx).await,
        FilesAction::Write {
            path,
            content,
            compile,
        } => write(&path, content, compile, ctx).await,
        FilesAction::Patch {
            path,
            find,
            replace,
        } => patch(&path, find, replace, ctx).await,
    }
}

pub async fn read(path: &str, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

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
    let content = match content {
        Some(c) => c,
        None => {
            use std::io::Read;
            let mut buf = String::new();
            std::io::stdin().read_to_string(&mut buf)?;
            buf
        }
    };

    let (_, _, mut client) = super::connect_client(ctx).await?;

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
    let find = find.ok_or_else(|| anyhow::anyhow!("--find is required for files patch"))?;
    let replace = replace.unwrap_or_default();

    let (_, _, mut client) = super::connect_client(ctx).await?;

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
