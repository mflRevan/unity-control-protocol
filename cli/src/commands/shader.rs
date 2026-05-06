use crate::output;
use clap::Subcommand;

use super::Context;

#[derive(Subcommand)]
pub enum ShaderAction {
    /// List shader compile errors and warnings known to the editor
    Errors {
        /// Only return errors, not warnings
        #[arg(long)]
        errors_only: bool,
        /// Filter shader names or paths by substring
        #[arg(long)]
        filter: Option<String>,
    },
}

pub async fn run(action: ShaderAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = match action {
        ShaderAction::Errors {
            errors_only,
            filter,
        } => {
            let mut params = serde_json::json!({ "errorsOnly": errors_only });
            if let Some(filter) = filter {
                params["filter"] = serde_json::json!(filter);
            }
            client.call("shader/errors", params).await?
        }
    };
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result.get("count").and_then(|v| v.as_u64()).unwrap_or(0);
        output::print_success(&format!("Found {count} shader diagnostic(s)"));
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
