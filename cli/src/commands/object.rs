use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};

const MAX_FIELD_LINES: usize = 40;

#[derive(Subcommand)]
pub enum ObjectAction {
    /// List all fields on a GameObject's component
    GetFields {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Component type name (e.g. "Transform", "MeshRenderer")
        #[arg(long)]
        component: String,
    },
    /// Get a specific property value
    GetProperty {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Property name
        #[arg(long)]
        property: String,
    },
    /// Set a property value
    SetProperty {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Property name
        #[arg(long)]
        property: String,
        /// Value as JSON
        #[arg(long)]
        value: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Set a GameObject's active state
    SetActive {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Active state (true or false)
        #[arg(long, action = clap::ArgAction::Set)]
        active: bool,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Rename a GameObject
    SetName {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// New name
        #[arg(long)]
        name: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Create a new empty GameObject
    Create {
        /// Name for the new object
        name: String,
        /// Parent instance ID
        #[arg(long, allow_hyphen_values = true)]
        parent: Option<i64>,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Delete a GameObject
    Delete {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Reparent a GameObject
    Reparent {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// New parent instance ID (omit for root)
        #[arg(long, allow_hyphen_values = true)]
        parent: Option<i64>,
        /// Sibling index
        #[arg(long)]
        sibling_index: Option<i64>,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Instantiate a prefab or clone a scene object
    Instantiate {
        /// Asset path to prefab, or instance ID to clone
        source: String,
        /// Optional name for the new instance
        #[arg(long)]
        name: Option<String>,
        /// Parent instance ID
        #[arg(long, allow_hyphen_values = true)]
        parent: Option<i64>,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Add a component to a GameObject
    AddComponent {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Remove a component from a GameObject
    RemoveComponent {
        /// Instance ID of the target GameObject
        #[arg(long, allow_hyphen_values = true)]
        id: i64,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
}

pub async fn run(action: ObjectAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    super::enforce_active_scene_guard(&mut client, object_preflight_policy(&action)).await?;

    let mut result = match &action {
        ObjectAction::GetFields { id, component } => {
            client
                .call(
                    "object/get-fields",
                    serde_json::json!({ "instanceId": id, "component": component }),
                )
                .await?
        }
        ObjectAction::GetProperty {
            id,
            component,
            property,
        } => {
            client
                .call(
                    "object/get-property",
                    serde_json::json!({
                        "instanceId": id,
                        "component": component,
                        "property": property
                    }),
                )
                .await?
        }
        ObjectAction::SetProperty {
            id,
            component,
            property,
            value,
            ..
        } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "object/set-property",
                    serde_json::json!({
                        "instanceId": id,
                        "component": component,
                        "property": property,
                        "value": parsed
                    }),
                )
                .await?
        }
        ObjectAction::SetActive { id, active, .. } => {
            client
                .call(
                    "object/set-active",
                    serde_json::json!({ "instanceId": id, "active": active }),
                )
                .await?
        }
        ObjectAction::SetName { id, name, .. } => {
            client
                .call(
                    "object/set-name",
                    serde_json::json!({ "instanceId": id, "name": name }),
                )
                .await?
        }
        ObjectAction::Create { name, parent, .. } => {
            let mut params = serde_json::json!({ "name": name });
            if let Some(p) = parent {
                params["parent"] = serde_json::json!(p);
            }
            client.call("object/create", params).await?
        }
        ObjectAction::Delete { id, .. } => {
            client
                .call("object/delete", serde_json::json!({ "instanceId": id }))
                .await?
        }
        ObjectAction::Reparent {
            id,
            parent,
            sibling_index,
            ..
        } => {
            let mut params = serde_json::json!({ "instanceId": id });
            if let Some(p) = parent {
                params["parent"] = serde_json::json!(p);
            }
            if let Some(s) = sibling_index {
                params["siblingIndex"] = serde_json::json!(s);
            }
            client.call("object/reparent", params).await?
        }
        ObjectAction::Instantiate {
            source,
            name,
            parent,
            ..
        } => {
            let mut params = serde_json::json!({});
            // If source looks like a path (contains / or .) treat as prefab path
            if source.contains('/') || source.contains('.') {
                params["prefab"] = serde_json::json!(source);
            } else if let Ok(id) = source.parse::<i64>() {
                params["sourceId"] = serde_json::json!(id);
            } else {
                params["prefab"] = serde_json::json!(source);
            }
            if let Some(n) = name {
                params["name"] = serde_json::json!(n);
            }
            if let Some(p) = parent {
                params["parent"] = serde_json::json!(p);
            }
            client.call("object/instantiate", params).await?
        }
        ObjectAction::AddComponent { id, component, .. } => {
            client
                .call(
                    "object/add-component",
                    serde_json::json!({ "instanceId": id, "type": component }),
                )
                .await?
        }
        ObjectAction::RemoveComponent { id, component, .. } => {
            client
                .call(
                    "object/remove-component",
                    serde_json::json!({ "instanceId": id, "type": component }),
                )
                .await?
        }
    };

    if object_should_save(&action) {
        super::save_active_scene(&mut client, ctx).await?;
    }

    client.close().await;

    let lifecycle =
        super::await_unity_lifecycle(&project, Some(&lock), object_lifecycle_policy(&action), ctx)
            .await?;

    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            ObjectAction::GetFields { .. } => {
                if let Some(fields) = result.get("fields").and_then(|v| v.as_array()) {
                    let obj_name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                    let comp = result
                        .get("component")
                        .and_then(|v| v.as_str())
                        .unwrap_or("?");
                    output::print_success(&format!("{obj_name}.{comp}: {} field(s)", fields.len()));
                    for f in fields.iter().take(MAX_FIELD_LINES) {
                        let name = f.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let ftype = f.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                        let val = f.get("value").map(|v| v.to_string()).unwrap_or_default();
                        eprintln!("  {name} ({ftype}): {val}");
                    }
                    if fields.len() > MAX_FIELD_LINES {
                        eprintln!(
                            "  ... {} more field(s) omitted; use --json or --property for a narrower read",
                            fields.len() - MAX_FIELD_LINES
                        );
                    }
                }
            }
            ObjectAction::GetProperty { .. } => {
                output::print_json(&result);
            }
            ObjectAction::SetProperty { property, .. } => {
                output::print_success(&format!("Set property: {property}"));
            }
            ObjectAction::SetActive { id, active, .. } => {
                output::print_success(&format!("Object {id}: active = {active}"));
            }
            ObjectAction::SetName { name, .. } => {
                output::print_success(&format!("Renamed to: {name}"));
            }
            ObjectAction::Create { name, .. } => {
                let id = result
                    .get("instanceId")
                    .and_then(|v| v.as_i64())
                    .unwrap_or(0);
                output::print_success(&format!("Created '{name}' (id: {id})"));
            }
            ObjectAction::Delete { id, .. } => {
                output::print_success(&format!("Deleted object {id}"));
            }
            ObjectAction::Reparent { id, parent, .. } => {
                if let Some(p) = parent {
                    output::print_success(&format!("Reparented {id} → {p}"));
                } else {
                    output::print_success(&format!("Moved {id} to root"));
                }
            }
            ObjectAction::Instantiate { source, .. } => {
                let id = result
                    .get("instanceId")
                    .and_then(|v| v.as_i64())
                    .unwrap_or(0);
                output::print_success(&format!("Instantiated '{source}' (id: {id})"));
            }
            ObjectAction::AddComponent { component, .. } => {
                output::print_success(&format!("Added component: {component}"));
            }
            ObjectAction::RemoveComponent { component, .. } => {
                output::print_success(&format!("Removed component: {component}"));
            }
        }
    }

    Ok(())
}

fn object_lifecycle_policy(action: &ObjectAction) -> UnityLifecyclePolicy {
    match action {
        ObjectAction::GetFields { .. } | ObjectAction::GetProperty { .. } => UnityLifecyclePolicy::None,
        ObjectAction::SetProperty { .. }
        | ObjectAction::SetActive { .. }
        | ObjectAction::SetName { .. }
        | ObjectAction::Create { .. }
        | ObjectAction::Delete { .. }
        | ObjectAction::Reparent { .. }
        | ObjectAction::Instantiate { .. }
        | ObjectAction::AddComponent { .. }
        | ObjectAction::RemoveComponent { .. } => UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish applying scene/object changes...",
            "scene/object processing",
        ),
    }
}

fn object_preflight_policy(action: &ObjectAction) -> super::ActiveSceneGuardPolicy {
    match action {
        ObjectAction::GetFields { .. } | ObjectAction::GetProperty { .. } => {
            super::ActiveSceneGuardPolicy::None
        }
        _ => super::ActiveSceneGuardPolicy::None,
    }
}

fn object_should_save(action: &ObjectAction) -> bool {
    match action {
        ObjectAction::SetProperty { save, .. }
        | ObjectAction::SetActive { save, .. }
        | ObjectAction::SetName { save, .. }
        | ObjectAction::Create { save, .. }
        | ObjectAction::Delete { save, .. }
        | ObjectAction::Reparent { save, .. }
        | ObjectAction::Instantiate { save, .. }
        | ObjectAction::AddComponent { save, .. }
        | ObjectAction::RemoveComponent { save, .. } => *save,
        ObjectAction::GetFields { .. } | ObjectAction::GetProperty { .. } => false,
    }
}
