use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use clap::Subcommand;

use super::Context;

#[derive(Subcommand)]
pub enum SceneAction {
    /// List all scenes in build settings
    List,
    /// Load a scene by path
    Load {
        path: String,
        /// Do not auto-save dirty scenes before loading
        #[arg(long)]
        no_save: bool,
        /// Keep dirty untitled scenes instead of discarding them when auto-save runs
        #[arg(long)]
        keep_untitled: bool,
    },
    /// Get active scene info
    Active,
}

pub async fn run(action: SceneAction, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = match action {
        SceneAction::List => client.call("scene/list", serde_json::json!({})).await?,
        SceneAction::Load {
            ref path,
            no_save,
            keep_untitled,
        } => {
            client
                .call(
                    "scene/load",
                    serde_json::json!({
                        "path": path,
                        "saveDirtyScenes": !no_save,
                        "discardUntitled": !keep_untitled,
                    }),
                )
                .await?
        }
        SceneAction::Active => client.call("scene/active", serde_json::json!({})).await?,
    };

    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match action {
            SceneAction::List => {
                if let Some(scenes) = result.get("scenes").and_then(|v| v.as_array()) {
                    output::print_success(&format!("Found {} scene(s)", scenes.len()));
                    for s in scenes {
                        if let Some(path) = s.get("path").and_then(|v| v.as_str()) {
                            let enabled = s
                                .get("enabled")
                                .and_then(|v| v.as_bool())
                                .unwrap_or(false);
                            let marker = if enabled {
                                if output::supports_unicode() { "●" } else { "*" }
                            } else {
                                if output::supports_unicode() { "○" } else { "o" }
                            };
                            eprintln!("  {marker} {path}");
                        }
                    }
                }
            }
            SceneAction::Load { path, .. } => {
                output::print_success(&format!("Loaded scene: {path}"));
            }
            SceneAction::Active => {
                if let Some(name) = result.get("name").and_then(|v| v.as_str()) {
                    output::print_success(&format!("Active scene: {name}"));
                    if let Some(path) = result.get("path").and_then(|v| v.as_str()) {
                        eprintln!("  Path: {path}");
                    }
                }
            }
        }
    }

    Ok(())
}
