use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};
use super::snapshot;

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
    /// Save the active scene
    Save,
    /// Focus the Scene view camera on a GameObject
    Focus {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Optional scene camera alignment direction as X Y Z
        #[arg(long, num_args = 3, allow_hyphen_values = true, value_names = ["X", "Y", "Z"])]
        axis: Option<Vec<f32>>,
    },
    /// Capture a lean hierarchy snapshot of the active scene
    Snapshot {
        /// Filter objects by name pattern
        #[arg(long)]
        filter: Option<String>,
        /// Max hierarchy depth (default: 0, root objects only)
        #[arg(long, default_value_t = 0)]
        depth: u32,
    },
}

pub async fn run(action: SceneAction, ctx: &Context) -> anyhow::Result<()> {
    if let SceneAction::Snapshot { filter, depth } = action {
        return snapshot::run(filter, depth, ctx).await;
    }

    let (project, lock, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(&mut client, scene_preflight_policy(&action)).await?;

    let mut result = match &action {
        SceneAction::List => client.call("scene/list", serde_json::json!({})).await?,
        SceneAction::Load {
            path,
            no_save,
            keep_untitled,
        } => {
            client
                .call(
                    "scene/load",
                    serde_json::json!({
                        "path": path,
                        "saveDirtyScenes": !*no_save,
                        "discardUntitled": !*keep_untitled,
                    }),
                )
                .await?
        }
        SceneAction::Active => client.call("scene/active", serde_json::json!({})).await?,
        SceneAction::Save => super::save_active_scene(&mut client, ctx).await?,
        SceneAction::Focus { id, axis } => {
            let mut params = serde_json::json!({ "instanceId": id });
            if let Some(axis_values) = axis {
                params["axis"] = serde_json::json!(axis_values);
            }
            client.call("scene/focus", params).await?
        }
        SceneAction::Snapshot { .. } => unreachable!(),
    };

    client.close().await;

    let should_settle = matches!(action, SceneAction::Load { .. });

    if should_settle {
        let lifecycle = super::await_unity_lifecycle(
            &project,
            Some(&lock),
            UnityLifecyclePolicy::editor_settle(
                "Waiting for Unity to finish scene processing...",
                "scene processing",
            ),
            ctx,
        )
        .await?;
        result = super::attach_lifecycle_log_status(result, &lifecycle);
    }

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match action {
            SceneAction::List => {
                if let Some(scenes) = result.get("scenes").and_then(|v| v.as_array()) {
                    output::print_success(&format!("Found {} scene(s)", scenes.len()));
                    for s in scenes {
                        if let Some(path) = s.get("path").and_then(|v| v.as_str()) {
                            let enabled =
                                s.get("enabled").and_then(|v| v.as_bool()).unwrap_or(false);
                            let marker = if enabled {
                                if output::supports_unicode() {
                                    "●"
                                } else {
                                    "*"
                                }
                            } else {
                                if output::supports_unicode() {
                                    "○"
                                } else {
                                    "o"
                                }
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
            SceneAction::Save => {
                let path = result.get("path").and_then(|v| v.as_str()).unwrap_or("?");
                let saved = result.get("saved").and_then(|v| v.as_bool()).unwrap_or(true);
                if saved {
                    output::print_success(&format!("Saved active scene: {path}"));
                } else {
                    output::print_success(&format!("Active scene already saved: {path}"));
                }
            }
            SceneAction::Focus { id, .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("Focused Scene view on {name} ({id})"));
            }
            SceneAction::Snapshot { .. } => unreachable!(),
        }
    }

    Ok(())
}

fn scene_preflight_policy(action: &SceneAction) -> super::ActiveSceneGuardPolicy {
    match action {
        SceneAction::Load { .. } => super::ActiveSceneGuardPolicy::block_if_dirty("load another scene"),
        SceneAction::List
        | SceneAction::Active
        | SceneAction::Save
        | SceneAction::Focus { .. }
        | SceneAction::Snapshot { .. } => super::ActiveSceneGuardPolicy::None,
    }
}
