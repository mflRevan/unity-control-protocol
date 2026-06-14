use crate::bridge_package;
use crate::output;
use clap::Subcommand;

use super::{Context, resolve_project_path};

/// Inspect or update the UCP bridge package installed in the project. The bridge is the editor-side
/// package every command talks to; this group reports its source/version and pins it to the version
/// this CLI expects. Reach for this when commands fail because the bridge is missing or out of date.
#[derive(Subcommand)]
pub enum BridgeAction {
    /// Show the installed bridge package source and version state
    Status,
    /// Update the project to the tracked bridge git dependency for this CLI version
    Update {
        /// Skip waiting for Unity to re-import the package
        #[arg(long)]
        no_wait: bool,
    },
}

pub async fn run(action: BridgeAction, ctx: &Context) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;

    match action {
        BridgeAction::Status => {
            let status = bridge_package::inspect(&project)?;
            if ctx.json {
                output::print_json(&output::success_json(serde_json::to_value(status)?));
            } else {
                output::print_success("Bridge package status");
                eprintln!("  Source: {}", status.source_kind);
                if let Some(version) = status.installed_version.as_deref() {
                    eprintln!("  Installed: {version}");
                }
                eprintln!("  Target: {}", status.target_version);
                if let Some(reference) = status.dependency.as_deref() {
                    eprintln!("  Reference: {reference}");
                }
                if status.outdated {
                    output::print_warn("Bridge package is behind the CLI version");
                }
            }
            Ok(())
        }
        BridgeAction::Update { no_wait } => {
            let options = super::install::InstallOptions {
                manifest: true,
                no_wait,
                ..super::install::InstallOptions::default()
            };
            super::install::run(Some(project.display().to_string()), options, ctx).await
        }
    }
}
