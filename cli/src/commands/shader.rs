use crate::output;
use clap::Subcommand;

use super::Context;

/// Surface shader diagnostics the editor already knows about. Reports compile errors and warnings
/// across the project's shaders, no id needed, optionally narrowed by name/path substring. Reach for
/// this after importing or editing shaders to confirm they compiled.
#[derive(Subcommand)]
pub enum ShaderAction {
    /// List shader compile errors and warnings for the project's own shaders (`--all` adds package shaders)
    Errors {
        /// Only return errors, not warnings
        #[arg(long)]
        errors_only: bool,
        /// Filter shader names or paths by substring
        #[arg(long)]
        filter: Option<String>,
        /// Include diagnostics from package shaders (Packages/, Library/PackageCache)
        #[arg(long)]
        all: bool,
    },
}

pub async fn run(action: ShaderAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let (mut result, all) = match action {
        ShaderAction::Errors {
            errors_only,
            filter,
            all,
        } => {
            let mut params = serde_json::json!({ "errorsOnly": errors_only });
            if let Some(filter) = filter {
                params["filter"] = serde_json::json!(filter);
            }
            (client.call("shader/errors", params).await?, all)
        }
    };
    client.close().await;

    // Package shaders (HDRP alone ships dozens of warnings) are not something the agent can fix;
    // keep them to a count unless asked for.
    let mut hidden = 0;
    if !all {
        if let Some(items) = result.get_mut("diagnostics").and_then(|v| v.as_array_mut()) {
            let before = items.len();
            items.retain(|item| {
                item.get("path")
                    .and_then(|v| v.as_str())
                    .is_none_or(|p| p.starts_with("Assets/"))
            });
            hidden = before - items.len();
            let kept = items.len();
            result["count"] = serde_json::json!(kept);
            if hidden > 0 {
                result["packageDiagnosticsHidden"] = serde_json::json!(hidden);
            }
        }
    }

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result.get("count").and_then(|v| v.as_u64()).unwrap_or(0);
        let note = if hidden > 0 {
            format!(" ({hidden} in packages hidden; --all shows them)")
        } else {
            String::new()
        };
        output::print_success(&format!("{count} shader diagnostic(s) in project shaders{note}"));
        if let Some(items) = result.get("diagnostics").and_then(|v| v.as_array()) {
            for item in items.iter().take(40) {
                let severity = item.get("severity").and_then(|v| v.as_str()).unwrap_or("?");
                let shader = item.get("shader").and_then(|v| v.as_str()).unwrap_or("?");
                let line = item.get("line").and_then(|v| v.as_i64()).unwrap_or(0);
                let message = item.get("message").and_then(|v| v.as_str()).unwrap_or("");
                eprintln!("  [{severity}] {shader}:{line} {message}");
            }
        }
    }

    Ok(())
}
