use crate::output;
use clap::Subcommand;

use super::Context;

#[derive(Subcommand)]
pub enum ScriptAction {
    /// Diagnose script project files and optionally regenerate stale .csproj files
    Doctor {
        /// Delete stale generated project files and ask Unity to regenerate them
        #[arg(long)]
        fix: bool,
    },
}

pub async fn run(action: ScriptAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = match action {
        ScriptAction::Doctor { fix } => {
            client
                .call("script/doctor", serde_json::json!({ "fix": fix }))
                .await?
        }
    };
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let stale = result
            .get("staleProjectCount")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        let missing = result
            .get("missingFileCount")
            .and_then(|v| v.as_u64())
            .unwrap_or(0);
        output::print_success(&format!(
            "Script project check complete: {stale} stale project(s), {missing} missing source reference(s)"
        ));
        if result
            .get("fixed")
            .and_then(|v| v.as_bool())
            .unwrap_or(false)
        {
            output::print_info("Requested Unity project-file regeneration");
        }
    }

    Ok(())
}
