use crate::output;
use clap::Subcommand;

use super::Context;

const MAX_PREFAB_OVERRIDES: usize = 20;
const MAX_PREFAB_COMPONENT_CHANGES: usize = 20;

#[derive(Subcommand)]
pub enum PrefabAction {
    /// Get prefab status of a GameObject
    Status {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
    },
    /// Apply prefab overrides back to the asset
    Apply {
        /// Instance ID of the prefab instance root
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
    },
    /// Revert prefab instance to match the asset
    Revert {
        /// Instance ID of the prefab instance root
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
    },
    /// Unpack a prefab instance
    Unpack {
        /// Instance ID of the prefab instance root
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Unpack completely (all nested prefabs)
        #[arg(long, action = clap::ArgAction::Set)]
        completely: bool,
    },
    /// Create a prefab asset from a scene object
    Create {
        /// Instance ID of the source GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Asset path to save the prefab
        #[arg(long)]
        path: String,
    },
    /// List property overrides on a prefab instance
    Overrides {
        /// Instance ID of the prefab instance root
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
    },
}

pub async fn run(action: PrefabAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let result = match &action {
        PrefabAction::Status { id } => {
            client
                .call("prefab/status", serde_json::json!({ "instanceId": id }))
                .await?
        }
        PrefabAction::Apply { id } => {
            client
                .call("prefab/apply", serde_json::json!({ "instanceId": id }))
                .await?
        }
        PrefabAction::Revert { id } => {
            client
                .call("prefab/revert", serde_json::json!({ "instanceId": id }))
                .await?
        }
        PrefabAction::Unpack { id, completely } => {
            client
                .call(
                    "prefab/unpack",
                    serde_json::json!({ "instanceId": id, "completely": completely }),
                )
                .await?
        }
        PrefabAction::Create { id, path } => {
            client
                .call(
                    "prefab/create",
                    serde_json::json!({ "instanceId": id, "path": path }),
                )
                .await?
        }
        PrefabAction::Overrides { id } => {
            client
                .call("prefab/overrides", serde_json::json!({ "instanceId": id }))
                .await?
        }
    };

    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            PrefabAction::Status { .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let is_instance = result
                    .get("isInstance")
                    .and_then(|v| v.as_bool())
                    .unwrap_or(false);
                let has_overrides = result
                    .get("hasOverrides")
                    .and_then(|v| v.as_bool())
                    .unwrap_or(false);
                output::print_success(&format!("{name}"));
                eprintln!("  Instance: {is_instance}");
                eprintln!("  Has overrides: {has_overrides}");
                if let Some(source) = result.get("sourcePath").and_then(|v| v.as_str()) {
                    eprintln!("  Source: {source}");
                }
            }
            PrefabAction::Apply { .. } => {
                output::print_success("Prefab overrides applied");
            }
            PrefabAction::Revert { .. } => {
                output::print_success("Prefab instance reverted");
            }
            PrefabAction::Unpack { .. } => {
                let mode = result.get("mode").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("Unpacked ({mode})"));
            }
            PrefabAction::Create { path, .. } => {
                output::print_success(&format!("Created prefab: {path}"));
            }
            PrefabAction::Overrides { .. } => {
                if let Some(mods) = result
                    .get("propertyModifications")
                    .and_then(|v| v.as_array())
                {
                    output::print_success(&format!("{} property modification(s)", mods.len()));
                    for m in mods.iter().take(MAX_PREFAB_OVERRIDES) {
                        let path = m
                            .get("propertyPath")
                            .and_then(|v| v.as_str())
                            .unwrap_or("?");
                        let val = m.get("value").and_then(|v| v.as_str()).unwrap_or("?");
                        eprintln!("  {path} = {val}");
                    }
                    if mods.len() > MAX_PREFAB_OVERRIDES {
                        eprintln!(
                            "  ... {} more modification(s) omitted; use --json for the full override list",
                            mods.len() - MAX_PREFAB_OVERRIDES
                        );
                    }
                }
                if let Some(added) = result.get("addedComponents").and_then(|v| v.as_array()) {
                    if !added.is_empty() {
                        eprintln!("  Added components:");
                        for a in added.iter().take(MAX_PREFAB_COMPONENT_CHANGES) {
                            let comp = a.get("component").and_then(|v| v.as_str()).unwrap_or("?");
                            eprintln!("    + {comp}");
                        }
                        if added.len() > MAX_PREFAB_COMPONENT_CHANGES {
                            eprintln!(
                                "    ... {} more added component(s) omitted",
                                added.len() - MAX_PREFAB_COMPONENT_CHANGES
                            );
                        }
                    }
                }
                if let Some(removed) = result.get("removedComponents").and_then(|v| v.as_array()) {
                    if !removed.is_empty() {
                        eprintln!("  Removed components:");
                        for r in removed.iter().take(MAX_PREFAB_COMPONENT_CHANGES) {
                            let comp = r.get("component").and_then(|v| v.as_str()).unwrap_or("?");
                            eprintln!("    - {comp}");
                        }
                        if removed.len() > MAX_PREFAB_COMPONENT_CHANGES {
                            eprintln!(
                                "    ... {} more removed component(s) omitted",
                                removed.len() - MAX_PREFAB_COMPONENT_CHANGES
                            );
                        }
                    }
                }
            }
        }
    }

    Ok(())
}
