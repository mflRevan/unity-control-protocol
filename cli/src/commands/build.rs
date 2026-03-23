use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};

#[derive(Subcommand)]
pub enum BuildAction {
    /// List available build targets
    Targets,
    /// Get the active build target
    ActiveTarget,
    /// Switch active build target
    SetTarget {
        /// Target name (e.g. "StandaloneWindows64", "Android", "iOS")
        target: String,
    },
    /// List build scenes
    Scenes,
    /// Set build scenes
    SetScenes {
        /// Scene paths (comma-separated)
        scenes: String,
    },
    /// Start a build
    Start {
        /// Output path
        #[arg(long)]
        output: Option<String>,
        /// Development build
        #[arg(long)]
        development: bool,
    },
    /// Get scripting define symbols
    Defines,
    /// Set scripting define symbols
    SetDefines {
        /// Defines (semicolon-separated, e.g. "DEBUG;MY_DEFINE")
        defines: String,
    },
}

pub async fn run(action: BuildAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(&mut client, build_preflight_policy(&action)).await?;

    let mut result = match &action {
        BuildAction::Targets => client.call("build/targets", serde_json::json!({})).await?,
        BuildAction::ActiveTarget => {
            client
                .call("build/active-target", serde_json::json!({}))
                .await?
        }
        BuildAction::SetTarget { target } => {
            client
                .call("build/set-target", serde_json::json!({ "target": target }))
                .await?
        }
        BuildAction::Scenes => client.call("build/scenes", serde_json::json!({})).await?,
        BuildAction::SetScenes { scenes } => {
            let scene_list: Vec<&str> = scenes.split(',').map(|s| s.trim()).collect();
            client
                .call(
                    "build/set-scenes",
                    serde_json::json!({ "scenes": scene_list }),
                )
                .await?
        }
        BuildAction::Start {
            output,
            development,
        } => {
            let mut params = serde_json::json!({ "development": development });
            if let Some(out) = output {
                params["output"] = serde_json::json!(out);
            }
            client.call("build/start", params).await?
        }
        BuildAction::Defines => client.call("build/defines", serde_json::json!({})).await?,
        BuildAction::SetDefines { defines } => {
            client
                .call(
                    "build/set-defines",
                    serde_json::json!({ "defines": defines }),
                )
                .await?
        }
    };

    client.close().await;

    let lifecycle = super::await_unity_lifecycle(
        &project,
        Some(&lock),
        build_lifecycle_policy(&action, &result),
        ctx,
    )
    .await?;

    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            BuildAction::Targets => {
                let active = result.get("active").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("Active target: {active}"));
                if let Some(targets) = result.get("targets").and_then(|v| v.as_array()) {
                    let installed: Vec<_> = targets
                        .iter()
                        .filter(|t| {
                            t.get("installed")
                                .and_then(|v| v.as_bool())
                                .unwrap_or(false)
                        })
                        .collect();
                    eprintln!("  Installed targets ({}):", installed.len());
                    for t in installed {
                        let name = t.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let is_active =
                            t.get("isActive").and_then(|v| v.as_bool()).unwrap_or(false);
                        let marker = if is_active { " (active)" } else { "" };
                        eprintln!("    {name}{marker}");
                    }
                }
            }
            BuildAction::ActiveTarget => {
                let target = result.get("target").and_then(|v| v.as_str()).unwrap_or("?");
                let group = result.get("group").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("{target} ({group})"));
            }
            BuildAction::SetTarget { target } => {
                let status = result.get("status").and_then(|v| v.as_str()).unwrap_or("?");
                if status == "ok" {
                    output::print_success(&format!("Switched to: {target}"));
                } else {
                    output::print_success(&format!("Failed to switch to: {target}"));
                }
            }
            BuildAction::Scenes => {
                if let Some(scenes) = result.get("scenes").and_then(|v| v.as_array()) {
                    output::print_success(&format!("{} build scene(s)", scenes.len()));
                    for s in scenes {
                        let path = s.get("path").and_then(|v| v.as_str()).unwrap_or("?");
                        let enabled = s.get("enabled").and_then(|v| v.as_bool()).unwrap_or(false);
                        let marker = if enabled { "+" } else { "-" };
                        eprintln!("  [{marker}] {path}");
                    }
                }
            }
            BuildAction::SetScenes { .. } => {
                let count = result.get("count").and_then(|v| v.as_i64()).unwrap_or(0);
                output::print_success(&format!("Set {count} build scene(s)"));
            }
            BuildAction::Start { .. } => {
                let build_result = result.get("result").and_then(|v| v.as_str()).unwrap_or("?");
                let time = result
                    .get("totalTime")
                    .and_then(|v| v.as_f64())
                    .unwrap_or(0.0);
                let errors = result
                    .get("totalErrors")
                    .and_then(|v| v.as_i64())
                    .unwrap_or(0);
                output::print_success(&format!(
                    "Build {build_result} ({time:.1}s, {errors} errors)"
                ));
                if let Some(out) = result.get("outputPath").and_then(|v| v.as_str()) {
                    eprintln!("  Output: {out}");
                }
            }
            BuildAction::Defines => {
                if let Some(list) = result.get("list").and_then(|v| v.as_array()) {
                    output::print_success(&format!("{} define(s)", list.len()));
                    for d in list {
                        if let Some(s) = d.as_str() {
                            eprintln!("  {s}");
                        }
                    }
                }
            }
            BuildAction::SetDefines { defines } => {
                output::print_success(&format!("Set defines: {defines}"));
            }
        }
    }

    Ok(())
}

fn build_preflight_policy(action: &BuildAction) -> super::ActiveSceneGuardPolicy {
    match action {
        BuildAction::SetTarget { .. } => {
            super::ActiveSceneGuardPolicy::block_if_dirty("switch the active build target")
        }
        BuildAction::SetDefines { .. } => {
            super::ActiveSceneGuardPolicy::block_if_dirty("change scripting define symbols")
        }
        BuildAction::Targets
        | BuildAction::ActiveTarget
        | BuildAction::Scenes
        | BuildAction::SetScenes { .. }
        | BuildAction::Start { .. }
        | BuildAction::Defines => super::ActiveSceneGuardPolicy::None,
    }
}

fn build_lifecycle_policy(
    action: &BuildAction,
    result: &serde_json::Value,
) -> UnityLifecyclePolicy {
    match action {
        BuildAction::Targets
        | BuildAction::ActiveTarget
        | BuildAction::Scenes
        | BuildAction::Start { .. }
        | BuildAction::Defines => UnityLifecyclePolicy::None,
        BuildAction::SetScenes { .. } => UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish applying build settings...",
            "build settings processing",
        ),
        BuildAction::SetTarget { .. } => {
            let succeeded = result.get("status").and_then(|v| v.as_str()) == Some("ok");
            if succeeded {
                UnityLifecyclePolicy::restart_then_settle(
                    "Waiting for Unity to finish switching build target...",
                    "build target switch",
                    120,
                )
            } else {
                UnityLifecyclePolicy::None
            }
        }
        BuildAction::SetDefines { .. } => UnityLifecyclePolicy::restart_then_settle(
            "Waiting for Unity to finish applying scripting define changes...",
            "scripting define processing",
            120,
        ),
    }
}
