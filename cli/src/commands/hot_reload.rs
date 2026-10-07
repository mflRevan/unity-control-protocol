use crate::output;
use clap::Subcommand;
use serde_json::Value;

use super::Context;

/// Patch edited C# method bodies into the running editor without a domain reload. The files are
/// compiled on their own with Unity's own compiler and their methods detoured onto the live types,
/// in edit mode or play mode, in about a second. Field, type, and signature changes still need
/// `ucp compile`; the result says which edits were applied and which need the real compile.
#[derive(Subcommand)]
pub enum HotReloadAction {
    /// Compile the given .cs files and patch their method bodies into the editor
    Apply {
        /// Script paths (relative to the project, or absolute inside it)
        #[arg(required = true)]
        files: Vec<String>,
    },
    /// Show which methods are currently hot-patched
    Status,
    /// Remove every hot patch and let Unity refresh normally again
    Revert,
}

pub async fn run(action: HotReloadAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = match &action {
        HotReloadAction::Apply { files } => {
            client
                .call("hot-reload/apply", serde_json::json!({ "files": files }))
                .await?
        }
        HotReloadAction::Status => client.call("hot-reload/status", serde_json::json!({})).await?,
        HotReloadAction::Revert => client.call("hot-reload/revert", serde_json::json!({})).await?,
    };
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result.clone()));
    } else {
        match &action {
            HotReloadAction::Apply { .. } => print_apply(&result),
            HotReloadAction::Status => {
                let active = result.get("activePatches").and_then(Value::as_u64).unwrap_or(0);
                output::print_success(&format!("{active} hot patch(es) active"));
                for patch in result.get("patches").and_then(Value::as_array).into_iter().flatten() {
                    eprintln!(
                        "  {}.{}  ({})",
                        patch.get("type").and_then(Value::as_str).unwrap_or("?"),
                        patch.get("method").and_then(Value::as_str).unwrap_or("?"),
                        patch.get("file").and_then(Value::as_str).unwrap_or("?")
                    );
                }
                if result.get("autoRefreshHeld").and_then(Value::as_bool).unwrap_or(false) {
                    eprintln!("  Auto refresh is held while patches are live; `ucp compile` makes the edits permanent.");
                }
            }
            HotReloadAction::Revert => {
                let reverted = result.get("reverted").and_then(Value::as_u64).unwrap_or(0);
                output::print_success(&format!("Reverted {reverted} hot patch(es)"));
            }
        }
    }

    let failed = result.get("status").and_then(Value::as_str) == Some("compile-failed");
    if failed {
        anyhow::bail!("hot reload compile failed; fix the errors above or run `ucp compile`");
    }
    Ok(())
}

fn print_apply(result: &Value) {
    let list = |key: &str| result.get(key).and_then(Value::as_array).cloned().unwrap_or_default();
    let patched = list("patched");
    let skipped = list("skipped");
    let needs = list("needsFullCompile");
    let errors = list("errors");
    let compile_ms = result.get("compileMs").and_then(Value::as_u64).unwrap_or(0);
    let total_ms = result.get("totalMs").and_then(Value::as_u64).unwrap_or(0);

    if !errors.is_empty() {
        output::print_error("Hot reload compile failed");
        for error in &errors {
            eprintln!("  {}", error.as_str().unwrap_or("?"));
        }
        return;
    }
    output::print_success(&format!(
        "Hot-patched {} method(s) in {total_ms} ms (compile {compile_ms} ms){}",
        patched.len(),
        if result.get("playMode").and_then(Value::as_bool).unwrap_or(false) { ", play mode kept running" } else { "" }
    ));
    for item in &patched {
        eprintln!(
            "  {}.{}",
            item.get("type").and_then(Value::as_str).unwrap_or("?"),
            item.get("method").and_then(Value::as_str).unwrap_or("?")
        );
    }
    if !needs.is_empty() {
        output::print_warn("Needs `ucp compile` (not hot-patchable):");
        for item in &needs {
            eprintln!("  {}{}: {}",
                item.get("type").and_then(Value::as_str).unwrap_or("?"),
                item.get("method").and_then(Value::as_str).map(|m| format!(".{m}")).unwrap_or_default(),
                item.get("reason").and_then(Value::as_str).unwrap_or("?"));
        }
    }
    if !skipped.is_empty() {
        eprintln!("  Skipped:");
        for item in &skipped {
            eprintln!("    {}{}: {}",
                item.get("type").and_then(Value::as_str).unwrap_or("?"),
                item.get("method").and_then(Value::as_str).map(|m| format!(".{m}")).unwrap_or_default(),
                item.get("reason").and_then(Value::as_str).unwrap_or("?"));
        }
    }
    if patched.is_empty() && needs.is_empty() {
        eprintln!("  Nothing to patch: no methods changed, or every candidate was skipped.");
    }
}
