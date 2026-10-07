use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};

/// Build request params from the target selector plus extra fields.
fn with_target(target: &super::TargetArgs, extra: serde_json::Value) -> anyhow::Result<serde_json::Value> {
    let mut obj = match extra {
        serde_json::Value::Object(map) => map,
        _ => serde_json::Map::new(),
    };
    target.apply(&mut obj)?;
    Ok(serde_json::Value::Object(obj))
}

const MAX_FIELD_LINES: usize = 40;

/// Inspect and edit GameObjects, their components, and properties. Address an object by
/// `--path Root/Child/Leaf` (stable across reloads), `--name` (first match), or the short-lived
/// `--id` from `ucp scene snapshot`. Reach for this to read/write component fields or restructure
/// the hierarchy; use `transform` for spatial moves.
#[derive(Subcommand)]
pub enum ObjectAction {
    /// List a GameObject's direct children or a deeper child hierarchy
    GetChildren {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Child hierarchy depth to include (1 = direct children only)
        #[arg(long, default_value_t = 1, value_parser = clap::value_parser!(u32).range(1..))]
        depth: u32,
    },
    /// List all fields on a GameObject's component
    GetFields {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Component type name (e.g. "Transform", "MeshRenderer")
        #[arg(long)]
        component: String,
    },
    /// Get a specific property value
    GetProperty {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Property name
        #[arg(long)]
        property: String,
    },
    /// Set a property value
    SetProperty {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Property name (e.g. "m_LocalPosition", "enabled")
        #[arg(long)]
        property: String,
        /// New value as JSON (e.g. true, 5, [1,2,3], "text"). Negative numbers and object
        /// reference instance ids like -55730 are accepted directly (no `--value=` needed).
        #[arg(long, allow_hyphen_values = true)]
        value: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Set a GameObject's active state
    SetActive {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Active state (true or false)
        #[arg(long, action = clap::ArgAction::Set)]
        active: bool,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Rename a GameObject
    SetName {
        #[command(flatten)]
        target: super::TargetArgs,
        /// New name
        #[arg(long)]
        name: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Create a GameObject. For anything you want to SEE in the scene (a cube, sphere, plane,
    /// etc.), pass `--primitive` — a plain create makes an EMPTY object with no mesh that does not
    /// render. There is no other supported way to add a built-in mesh from the CLI.
    Create {
        /// Name for the new object
        name: String,
        /// Build a visible primitive (mesh + collider) in one step. One of: Cube, Sphere, Capsule,
        /// Cylinder, Plane, Quad. This is THE way to make a renderable object; omit only when you
        /// deliberately want an empty container GameObject.
        #[arg(long)]
        primitive: Option<String>,
        /// Parent instance ID
        #[arg(long, allow_hyphen_values = true)]
        parent: Option<i64>,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Delete a GameObject
    Delete {
        #[command(flatten)]
        target: super::TargetArgs,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Reparent a GameObject
    Reparent {
        #[command(flatten)]
        target: super::TargetArgs,
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
        /// Source, auto-detected: anything containing "/" or "." is treated as a prefab asset path
        /// (e.g. "Assets/Enemy.prefab") and instantiated; a bare integer (e.g. -4231) is treated as
        /// an instance id and clones that scene object
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
        #[command(flatten)]
        target: super::TargetArgs,
        /// Component type name
        #[arg(long)]
        component: String,
        /// Save the active scene after applying the change
        #[arg(long)]
        save: bool,
    },
    /// Remove a component from a GameObject
    RemoveComponent {
        #[command(flatten)]
        target: super::TargetArgs,
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

    let result: anyhow::Result<serde_json::Value> = async {
        Ok(match &action {
        ObjectAction::GetChildren { target, depth } => {
            client
                .call("object/get-children", with_target(target, serde_json::json!({ "depth": depth }))?)
                .await?
        }
        ObjectAction::GetFields { target, component } => {
            client
                .call("object/get-fields", with_target(target, serde_json::json!({ "component": component }))?)
                .await?
        }
        ObjectAction::GetProperty {
            target,
            component,
            property,
        } => {
            client
                .call(
                    "object/get-property",
                    with_target(target, serde_json::json!({ "component": component, "property": property }))?,
                )
                .await?
        }
        ObjectAction::SetProperty {
            target,
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
                    with_target(
                        target,
                        serde_json::json!({ "component": component, "property": property, "value": parsed }),
                    )?,
                )
                .await?
        }
        ObjectAction::SetActive { target, active, .. } => {
            client
                .call("object/set-active", with_target(target, serde_json::json!({ "active": active }))?)
                .await?
        }
        ObjectAction::SetName { target, name, .. } => {
            client
                .call("object/set-name", with_target(target, serde_json::json!({ "name": name }))?)
                .await?
        }
        ObjectAction::Create {
            name,
            primitive,
            parent,
            ..
        } => {
            let mut params = serde_json::json!({ "name": name });
            if let Some(prim) = primitive {
                params["primitive"] = serde_json::json!(prim);
            }
            if let Some(p) = parent {
                params["parent"] = serde_json::json!(p);
            }
            client.call("object/create", params).await?
        }
        ObjectAction::Delete { target, .. } => {
            client
                .call("object/delete", with_target(target, serde_json::json!({}))?)
                .await?
        }
        ObjectAction::Reparent {
            target,
            parent,
            sibling_index,
            ..
        } => {
            let mut params = with_target(target, serde_json::json!({}))?;
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
        ObjectAction::AddComponent { target, component, .. } => {
            client
                .call("object/add-component", with_target(target, serde_json::json!({ "type": component }))?)
                .await?
        }
        ObjectAction::RemoveComponent { target, component, .. } => {
            client
                .call("object/remove-component", with_target(target, serde_json::json!({ "type": component }))?)
                .await?
        }
        })
    }
    .await;
    let mut result = result.map_err(|error: anyhow::Error| {
        let text = format!("{error:#}");
        if text.to_ascii_lowercase().contains("play mode") {
            anyhow::anyhow!("{text}\n  Hint: this edit is not allowed while playing; run `ucp stop` first.")
        } else {
            error
        }
    })?;

    // In Play Mode, scene/object edits apply only to the running instance and are discarded
    // on exit, and Unity refuses to save scenes during play. Rather than letting the --save
    // step fail opaquely, detect play mode, skip the save, and hand the agent a clear warning
    // (in both JSON and human output) that the change will not persist.
    let play_mode_warning =
        if is_object_mutation(&action) && super::editor_is_playing(&mut client).await {
            result["playMode"] = serde_json::json!(true);
            result["warning"] = serde_json::json!(super::PLAY_MODE_NONPERSISTENT_WARNING);
            Some(super::PLAY_MODE_NONPERSISTENT_WARNING)
        } else {
            None
        };

    if play_mode_warning.is_none() && object_should_save(&action) {
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
            ObjectAction::GetChildren { target, .. } => {
                let id = target;
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let child_count = result
                    .get("childCount")
                    .and_then(|v| v.as_u64())
                    .unwrap_or_default();
                let requested_depth = result
                    .get("requestedDepth")
                    .and_then(|v| v.as_u64())
                    .unwrap_or(1);

                output::print_success(&format!("{name} ({id}): {child_count} child(ren)"));
                if requested_depth > 1 {
                    eprintln!("  Showing hierarchy depth: {requested_depth}");
                }

                if let Some(children) = result.get("children").and_then(|v| v.as_array()) {
                    if children.is_empty() {
                        eprintln!("  (no children)");
                    } else {
                        for child in children {
                            print_child_node(child, 1);
                        }
                    }
                }
            }
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
                match result.get("value") {
                    Some(value) if !value.is_null() => {
                        output::print_success(&format!("Set property: {property} = {value}"))
                    }
                    _ => output::print_success(&format!("Set property: {property}")),
                }
            }
            ObjectAction::SetActive { target, active, .. } => {
                let in_hierarchy = result.get("activeInHierarchy").and_then(|v| v.as_bool());
                match in_hierarchy {
                    Some(effective) if effective != *active => output::print_success(&format!(
                        "Object {target}: active = {active} (activeInHierarchy = {effective}: a parent is inactive)"
                    )),
                    _ => output::print_success(&format!("Object {target}: active = {active}")),
                }
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
            ObjectAction::Delete { target, .. } => {
                output::print_success(&format!("Deleted object {target}"));
            }
            ObjectAction::Reparent { target, parent, .. } => {
                if let Some(p) = parent {
                    output::print_success(&format!("Reparented {target} → {p}"));
                } else {
                    output::print_success(&format!("Moved {target} to root"));
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

        if let Some(warning) = play_mode_warning {
            output::print_warn(warning);
        }
    }

    Ok(())
}

fn object_lifecycle_policy(action: &ObjectAction) -> UnityLifecyclePolicy {
    match action {
        ObjectAction::GetChildren { .. }
        | ObjectAction::GetFields { .. }
        | ObjectAction::GetProperty { .. } => UnityLifecyclePolicy::None,
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
        ObjectAction::GetChildren { .. }
        | ObjectAction::GetFields { .. }
        | ObjectAction::GetProperty { .. } => super::ActiveSceneGuardPolicy::None,
        _ => super::ActiveSceneGuardPolicy::None,
    }
}

/// Whether the action mutates the scene/object graph (as opposed to a read-only Get*).
/// Used to decide whether the Play Mode non-persistence warning applies.
fn is_object_mutation(action: &ObjectAction) -> bool {
    !matches!(
        action,
        ObjectAction::GetChildren { .. }
            | ObjectAction::GetFields { .. }
            | ObjectAction::GetProperty { .. }
    )
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
        ObjectAction::GetChildren { .. }
        | ObjectAction::GetFields { .. }
        | ObjectAction::GetProperty { .. } => false,
    }
}

fn print_child_node(node: &serde_json::Value, depth: usize) {
    let indent = "  ".repeat(depth);
    let name = node.get("name").and_then(|v| v.as_str()).unwrap_or("?");
    let id = node
        .get("instanceId")
        .and_then(|v| v.as_i64())
        .unwrap_or_default();
    let child_count = node
        .get("childCount")
        .and_then(|v| v.as_u64())
        .unwrap_or_default();
    let components = node
        .get("components")
        .and_then(|v| v.as_array())
        .map(|values| {
            values
                .iter()
                .filter_map(|value| value.as_str())
                .collect::<Vec<_>>()
                .join(", ")
        })
        .unwrap_or_default();

    if components.is_empty() {
        eprintln!("{indent}- {name} (id: {id}, children: {child_count})");
    } else {
        eprintln!("{indent}- {name} (id: {id}, children: {child_count}, components: {components})");
    }

    if let Some(children) = node.get("children").and_then(|v| v.as_array()) {
        for child in children {
            print_child_node(child, depth + 1);
        }
    }
}
