use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};

const MAX_COLLECTION_ITEMS: usize = 20;
const MAX_STRING_LEN: usize = 120;

/// Read and write Unity project settings (player, quality, physics, lighting) plus tags and layers.
/// Each `set-*` command writes one key; run the matching getter first (e.g. `settings player`) to
/// list the valid keys and their current shapes. Keys are Unity setting names; values are JSON.
#[derive(Subcommand)]
pub enum SettingsAction {
    /// Get player settings (also lists the keys accepted by `settings set-player`)
    Player,
    /// Set one player setting. Run `settings player` to see valid keys
    SetPlayer {
        /// Player setting key, as listed by `settings player` (e.g. companyName, productName)
        #[arg(long)]
        key: String,
        /// New value as JSON (e.g. "Acme" for a string, 1920 for a number, true for a bool)
        #[arg(long)]
        value: String,
    },
    /// Get quality settings (also lists the keys accepted by `settings set-quality`)
    Quality,
    /// Set one quality setting. Run `settings quality` to see valid keys
    SetQuality {
        /// Quality setting key, as listed by `settings quality` (e.g. vSyncCount, antiAliasing)
        #[arg(long)]
        key: String,
        /// New value as JSON (e.g. 0 for vSyncCount, 2 for antiAliasing)
        #[arg(long)]
        value: String,
    },
    /// Get physics settings (also lists the keys accepted by `settings set-physics`)
    Physics,
    /// Set one physics setting. Run `settings physics` to see valid keys
    SetPhysics {
        /// Physics setting key, as listed by `settings physics` (e.g. gravity, bounceThreshold)
        #[arg(long)]
        key: String,
        /// New value as JSON (e.g. [0,-9.81,0] for gravity, 2.0 for bounceThreshold)
        #[arg(long)]
        value: String,
    },
    /// Get lighting/render settings (also lists the keys accepted by `settings set-lighting`)
    Lighting,
    /// Set one lighting/render setting. Run `settings lighting` to see valid keys
    SetLighting {
        /// Lighting setting key, as listed by `settings lighting` (e.g. ambientMode, fog)
        #[arg(long)]
        key: String,
        /// New value as JSON (e.g. true for fog, [0.2,0.2,0.2,1] for an ambient color)
        #[arg(long)]
        value: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// List tags and layers
    TagsLayers,
    /// Add a tag. No-op if the tag already exists
    AddTag {
        /// Tag name to add (e.g. Enemy)
        tag: String,
    },
    /// Add a layer at a free or specified index
    AddLayer {
        /// Layer name to add (e.g. Interactable)
        name: String,
        /// Layer index 8-31 (user range; auto-assigned to the first free slot if omitted)
        #[arg(long)]
        index: Option<i64>,
    },
}

pub async fn run(action: SettingsAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(&mut client, settings_preflight_policy(&action)).await?;

    let mut result = match &action {
        SettingsAction::Player => {
            client
                .call("settings/player", serde_json::json!({}))
                .await?
        }
        SettingsAction::SetPlayer { key, value } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "settings/player-set",
                    serde_json::json!({ "key": key, "value": parsed }),
                )
                .await?
        }
        SettingsAction::Quality => {
            client
                .call("settings/quality", serde_json::json!({}))
                .await?
        }
        SettingsAction::SetQuality { key, value } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "settings/quality-set",
                    serde_json::json!({ "key": key, "value": parsed }),
                )
                .await?
        }
        SettingsAction::Physics => {
            client
                .call("settings/physics", serde_json::json!({}))
                .await?
        }
        SettingsAction::SetPhysics { key, value } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "settings/physics-set",
                    serde_json::json!({ "key": key, "value": parsed }),
                )
                .await?
        }
        SettingsAction::Lighting => {
            client
                .call("settings/lighting", serde_json::json!({}))
                .await?
        }
        SettingsAction::SetLighting { key, value, .. } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "settings/lighting-set",
                    serde_json::json!({ "key": key, "value": parsed }),
                )
                .await?
        }
        SettingsAction::TagsLayers => {
            client
                .call("settings/tags-layers", serde_json::json!({}))
                .await?
        }
        SettingsAction::AddTag { tag } => {
            client
                .call("settings/add-tag", serde_json::json!({ "tag": tag }))
                .await?
        }
        SettingsAction::AddLayer { name, index } => {
            let mut params = serde_json::json!({ "name": name });
            if let Some(idx) = index {
                params["index"] = serde_json::json!(idx);
            }
            client.call("settings/add-layer", params).await?
        }
    };

    if settings_should_save(&action) {
        super::save_active_scene(&mut client, ctx).await?;
    }

    client.close().await;

    let lifecycle =
        super::await_unity_lifecycle(&project, Some(&lock), settings_lifecycle_policy(&action), ctx)
            .await?;

    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            SettingsAction::Player => {
                output::print_success("Player Settings");
                print_kv_object(&result);
            }
            SettingsAction::SetPlayer { key, .. } => {
                output::print_success(&format!("Set player setting: {key}"));
            }
            SettingsAction::Quality => {
                output::print_success("Quality Settings");
                print_kv_object(&result);
            }
            SettingsAction::SetQuality { key, .. } => {
                output::print_success(&format!("Set quality setting: {key}"));
            }
            SettingsAction::Physics => {
                output::print_success("Physics Settings");
                print_kv_object(&result);
            }
            SettingsAction::SetPhysics { key, .. } => {
                output::print_success(&format!("Set physics setting: {key}"));
            }
            SettingsAction::Lighting => {
                output::print_success("Lighting Settings");
                print_kv_object(&result);
            }
            SettingsAction::SetLighting { key, .. } => {
                output::print_success(&format!("Set lighting setting: {key}"));
            }
            SettingsAction::TagsLayers => {
                if let Some(tags) = result.get("tags").and_then(|v| v.as_array()) {
                    output::print_success(&format!("Tags ({})", tags.len()));
                    for t in tags {
                        if let Some(s) = t.as_str() {
                            eprintln!("  {s}");
                        }
                    }
                }
                if let Some(layers) = result.get("layers").and_then(|v| v.as_array()) {
                    eprintln!();
                    output::print_success(&format!("Layers ({})", layers.len()));
                    for l in layers {
                        let idx = l.get("index").and_then(|v| v.as_i64()).unwrap_or(0);
                        let name = l.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        eprintln!("  [{idx}] {name}");
                    }
                }
            }
            SettingsAction::AddTag { tag } => {
                let status = result
                    .get("status")
                    .and_then(|v| v.as_str())
                    .unwrap_or("ok");
                if status == "exists" {
                    output::print_success(&format!("Tag '{tag}' already exists"));
                } else {
                    output::print_success(&format!("Added tag: {tag}"));
                }
            }
            SettingsAction::AddLayer { name, .. } => {
                let idx = result.get("index").and_then(|v| v.as_i64()).unwrap_or(0);
                let status = result
                    .get("status")
                    .and_then(|v| v.as_str())
                    .unwrap_or("ok");
                if status == "exists" {
                    output::print_success(&format!("Layer '{name}' already exists at index {idx}"));
                } else {
                    output::print_success(&format!("Added layer: {name} at index {idx}"));
                }
            }
        }
    }

    Ok(())
}

fn print_kv_object(val: &serde_json::Value) {
    if let Some(obj) = val.as_object() {
        for (k, v) in obj {
            let display = summarize_value(v);
            eprintln!("  {k}: {display}");
        }
    }
}

fn summarize_value(value: &serde_json::Value) -> String {
    match value {
        serde_json::Value::String(s) => truncate_string(s),
        serde_json::Value::Array(arr) => {
            let mut parts: Vec<String> = arr
                .iter()
                .take(MAX_COLLECTION_ITEMS)
                .map(summarize_value)
                .collect();
            if arr.len() > MAX_COLLECTION_ITEMS {
                parts.push(format!("... {} more", arr.len() - MAX_COLLECTION_ITEMS));
            }
            format!("[{}]", parts.join(", "))
        }
        serde_json::Value::Object(obj) => {
            let mut parts: Vec<String> = obj
                .iter()
                .take(MAX_COLLECTION_ITEMS)
                .map(|(key, entry)| format!("{key}: {}", summarize_value(entry)))
                .collect();
            if obj.len() > MAX_COLLECTION_ITEMS {
                parts.push(format!("... {} more", obj.len() - MAX_COLLECTION_ITEMS));
            }
            format!("{{{}}}", parts.join(", "))
        }
        other => other.to_string(),
    }
}

fn truncate_string(value: &str) -> String {
    if value.chars().count() <= MAX_STRING_LEN {
        value.to_string()
    } else {
        let truncated: String = value.chars().take(MAX_STRING_LEN).collect();
        format!("{truncated}...")
    }
}

fn settings_lifecycle_policy(action: &SettingsAction) -> UnityLifecyclePolicy {
    match action {
        SettingsAction::Player
        | SettingsAction::Quality
        | SettingsAction::Physics
        | SettingsAction::Lighting
        | SettingsAction::TagsLayers => UnityLifecyclePolicy::None,
        SettingsAction::SetPlayer { .. }
        | SettingsAction::SetQuality { .. }
        | SettingsAction::SetPhysics { .. }
        | SettingsAction::SetLighting { .. }
        | SettingsAction::AddTag { .. }
        | SettingsAction::AddLayer { .. } => UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish applying project settings changes...",
            "project settings processing",
        ),
    }
}

fn settings_preflight_policy(action: &SettingsAction) -> super::ActiveSceneGuardPolicy {
    match action {
        SettingsAction::Player
        | SettingsAction::SetPlayer { .. }
        | SettingsAction::Quality
        | SettingsAction::SetQuality { .. }
        | SettingsAction::Physics
        | SettingsAction::SetPhysics { .. }
        | SettingsAction::TagsLayers
        | SettingsAction::AddTag { .. }
        | SettingsAction::AddLayer { .. }
        | SettingsAction::Lighting => super::ActiveSceneGuardPolicy::None,
        SettingsAction::SetLighting { .. } => super::ActiveSceneGuardPolicy::None,
    }
}

fn settings_should_save(action: &SettingsAction) -> bool {
    match action {
        SettingsAction::SetLighting { save, .. } => *save,
        _ => false,
    }
}
