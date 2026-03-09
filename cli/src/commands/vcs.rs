use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use clap::Subcommand;
use console::style;

use super::Context;

#[derive(Subcommand)]
pub enum VcsAction {
    /// Show version control provider info and connection status
    Info,
    /// Show pending changes (defaults to all; use --path for specific files)
    Status {
        /// Filter to a specific path
        #[arg(long)]
        path: Option<String>,
    },
    /// Checkout files for editing
    Checkout {
        /// Asset path(s) to checkout
        paths: Vec<String>,
        /// Checkout all modified/added assets at once
        #[arg(long)]
        all: bool,
    },
    /// Revert files to repository version
    Revert {
        /// Asset path(s) to revert
        paths: Vec<String>,
        /// Revert all pending changes
        #[arg(long)]
        all: bool,
        /// Keep local modifications (undo checkout only)
        #[arg(long)]
        keep_local: bool,
    },
    /// Commit (checkin) pending changes
    Commit {
        /// Commit message (required)
        #[arg(short, long)]
        message: String,
        /// Specific paths to commit (defaults to all pending)
        paths: Vec<String>,
    },
    /// Show change summary or per-file status
    Diff {
        /// Specific files to diff
        paths: Vec<String>,
    },
    /// List incoming changes from remote
    Incoming,
    /// Pull and apply incoming changes (get latest)
    Update,
    /// List branches (requires cm CLI)
    Branches,
    /// Lock files to prevent others from editing
    Lock {
        /// Asset path(s) to lock
        paths: Vec<String>,
    },
    /// Unlock previously locked files
    Unlock {
        /// Asset path(s) to unlock
        paths: Vec<String>,
    },
    /// Show changeset history (requires cm CLI)
    History {
        /// Limit results
        #[arg(long, default_value = "20")]
        limit: u32,
    },
    /// Resolve merge conflicts
    Resolve {
        /// Asset path(s) to resolve
        paths: Vec<String>,
        /// Resolution method: merge (default), mine, or theirs
        #[arg(long, default_value = "merge")]
        method: String,
    },
}

pub async fn run(action: VcsAction, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    match action {
        VcsAction::Info => cmd_info(&mut client, ctx).await,
        VcsAction::Status { path } => cmd_status(&mut client, path, ctx).await,
        VcsAction::Checkout { paths, all } => cmd_checkout(&mut client, paths, all, ctx).await,
        VcsAction::Revert {
            paths,
            all,
            keep_local,
        } => cmd_revert(&mut client, paths, all, keep_local, ctx).await,
        VcsAction::Commit { message, paths } => {
            cmd_commit(&mut client, &message, paths, ctx).await
        }
        VcsAction::Diff { paths } => cmd_diff(&mut client, paths, ctx).await,
        VcsAction::Incoming => cmd_incoming(&mut client, ctx).await,
        VcsAction::Update => cmd_update(&mut client, ctx).await,
        VcsAction::Branches => cmd_branches(&mut client, ctx).await,
        VcsAction::Lock { paths } => cmd_lock(&mut client, paths, ctx).await,
        VcsAction::Unlock { paths } => cmd_unlock(&mut client, paths, ctx).await,
        VcsAction::History { limit: _ } => cmd_history(&mut client, ctx).await,
        VcsAction::Resolve { paths, method } => {
            cmd_resolve(&mut client, paths, &method, ctx).await
        }
    }?;

    client.close().await;
    Ok(())
}

// ── Individual command implementations ───────────────────────────────

async fn cmd_info(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client.call("vcs/info", serde_json::json!({})).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let bar = if output::supports_unicode() { "\u{2502}" } else { "|" };
        output::print_success("Version control info");
        if let Some(obj) = result.as_object() {
            for (k, v) in obj {
                let val = match v {
                    serde_json::Value::String(s) => s.clone(),
                    other => other.to_string(),
                };
                eprintln!("  {} {}: {}", style(bar).dim(), k, val);
            }
        }
    }
    Ok(())
}

async fn cmd_status(
    client: &mut BridgeClient,
    path: Option<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let params = match path {
        Some(ref p) => serde_json::json!({ "path": p }),
        None => serde_json::json!({}),
    };
    let result = client.call("vcs/status", params).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        if count == 0 {
            output::print_success("No pending changes");
        } else {
            output::print_info(&format!("{count} pending change(s)"));
            print_asset_list(&result);
        }
    }
    Ok(())
}

async fn cmd_checkout(
    client: &mut BridgeClient,
    paths: Vec<String>,
    all: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    let params = if all {
        serde_json::json!({ "all": true })
    } else {
        serde_json::json!({ "paths": paths })
    };
    let result = client.call("vcs/checkout", params).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        if count == 0 {
            output::print_info("Nothing to checkout");
        } else {
            output::print_success(&format!("Checked out {count} file(s)"));
        }
    }
    Ok(())
}

async fn cmd_revert(
    client: &mut BridgeClient,
    paths: Vec<String>,
    all: bool,
    keep_local: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    let mut params = if all {
        serde_json::json!({ "all": true })
    } else {
        serde_json::json!({ "paths": paths })
    };
    if keep_local {
        params["keepLocal"] = serde_json::json!(true);
    }
    let result = client.call("vcs/revert", params).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        output::print_success(&format!("Reverted {count} file(s)"));
    }
    Ok(())
}

async fn cmd_commit(
    client: &mut BridgeClient,
    message: &str,
    paths: Vec<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let params = if paths.is_empty() {
        serde_json::json!({ "message": message })
    } else {
        serde_json::json!({ "message": message, "paths": paths })
    };
    let result = client.call("vcs/commit", params).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let success = result
            .get("success")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        if success {
            output::print_success(&format!("Committed {count} file(s)"));
        } else {
            let msg = result
                .get("message")
                .and_then(|v| v.as_str())
                .unwrap_or("Commit failed");
            output::print_error(msg);
        }
    }
    Ok(())
}

async fn cmd_diff(
    client: &mut BridgeClient,
    paths: Vec<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let params = if paths.is_empty() {
        serde_json::json!({})
    } else {
        serde_json::json!({ "paths": paths })
    };
    let result = client.call("vcs/diff", params).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else if paths.is_empty() {
        // Summary mode
        if let Some(summary) = result.get("summary").and_then(|v| v.as_object()) {
            output::print_info("Change summary:");
            for (category, count) in summary {
                let n = count.as_u64().unwrap_or(0);
                if n > 0 {
                    eprintln!("  {category}: {n}");
                }
            }
        }
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        if count > 0 {
            eprintln!();
            print_asset_list(&result);
        }
    } else {
        // Per-file detail
        print_asset_list(&result);
    }
    Ok(())
}

async fn cmd_incoming(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client.call("vcs/incoming", serde_json::json!({})).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        if count == 0 {
            output::print_success("No incoming changes");
        } else {
            output::print_info(&format!("{count} incoming change(s)"));
            print_asset_list(&result);
        }
    }
    Ok(())
}

async fn cmd_update(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    if !ctx.json {
        output::print_info("Pulling latest changes...");
    }
    let result = client.call("vcs/update", serde_json::json!({})).await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        let msg = result
            .get("message")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        if count == 0 {
            output::print_success(if msg.is_empty() {
                "Already up to date"
            } else {
                msg
            });
        } else {
            output::print_success(&format!("Updated {count} file(s)"));
        }
    }
    Ok(())
}

async fn cmd_branches(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client
        .call("vcs/branches", serde_json::json!({}))
        .await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        if let Some(note) = result.get("note").and_then(|v| v.as_str()) {
            output::print_warn(note);
        }
        if let Some(info) = result.get("currentInfo").and_then(|v| v.as_str()) {
            if !info.is_empty() {
                output::print_info(&format!("Current: {info}"));
            }
        }
        if let Some(branches) = result.get("branches").and_then(|v| v.as_array()) {
            if !branches.is_empty() {
                output::print_info(&format!("{} branch(es):", branches.len()));
                for b in branches {
                    if let Some(name) = b.as_str() {
                        eprintln!("  {name}");
                    }
                }
            }
        }
    }
    Ok(())
}

async fn cmd_lock(
    client: &mut BridgeClient,
    paths: Vec<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client
        .call("vcs/lock", serde_json::json!({ "paths": paths }))
        .await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        output::print_success(&format!("Locked {count} file(s)"));
    }
    Ok(())
}

async fn cmd_unlock(
    client: &mut BridgeClient,
    paths: Vec<String>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client
        .call("vcs/unlock", serde_json::json!({ "paths": paths }))
        .await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        output::print_success(&format!("Unlocked {count} file(s)"));
    }
    Ok(())
}

async fn cmd_history(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client
        .call("vcs/history", serde_json::json!({}))
        .await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        if let Some(entries) = result.get("entries").and_then(|v| v.as_array()) {
            output::print_info(&format!("{} changeset(s):", entries.len()));
            for e in entries {
                let cs = e.get("changeset").and_then(|v| v.as_str()).unwrap_or("?");
                let date = e.get("date").and_then(|v| v.as_str()).unwrap_or("");
                let author = e.get("author").and_then(|v| v.as_str()).unwrap_or("");
                let comment = e.get("comment").and_then(|v| v.as_str()).unwrap_or("");
                eprintln!(
                    "  {} {} {} {}",
                    style(cs).cyan(),
                    style(date).dim(),
                    author,
                    comment
                );
            }
        }
    }
    Ok(())
}

async fn cmd_resolve(
    client: &mut BridgeClient,
    paths: Vec<String>,
    method: &str,
    ctx: &Context,
) -> anyhow::Result<()> {
    let result = client
        .call(
            "vcs/resolve",
            serde_json::json!({ "paths": paths, "method": method }),
        )
        .await?;
    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("count")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        output::print_success(&format!("Resolved {count} conflict(s)"));
    }
    Ok(())
}

// ── Shared display helpers ───────────────────────────────────────────

fn print_asset_list(result: &serde_json::Value) {
    if let Some(assets) = result.get("assets").and_then(|v| v.as_array()) {
        for a in assets {
            let path = a.get("path").and_then(|v| v.as_str()).unwrap_or("?");
            let state = a.get("state").and_then(|v| v.as_str()).unwrap_or("");
            let locked = a.get("lockedLocal").and_then(|v| v.as_bool()).unwrap_or(false);
            let lock_marker = if locked { " [locked]" } else { "" };
            eprintln!(
                "  {} {}{}",
                style(compact_state(state)).yellow(),
                path,
                lock_marker
            );
        }
    }
}

fn compact_state(state: &str) -> &str {
    // Map Unity's verbose state flags to compact labels
    if state.contains("AddedLocal") {
        return "A";
    }
    if state.contains("DeletedLocal") {
        return "D";
    }
    if state.contains("MovedLocal") {
        return "R";
    }
    if state.contains("CheckedOutLocal") || state.contains("ModifiedLocal") {
        return "M";
    }
    if state.contains("Conflicted") {
        return "C";
    }
    if state.contains("OutOfSync") {
        return "!";
    }
    if state.contains("LockedLocal") {
        return "L";
    }
    if state.contains("LockedRemote") {
        return "Lr";
    }
    "?"
}
