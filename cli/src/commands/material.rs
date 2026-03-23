use crate::output;
use clap::Subcommand;

use super::{Context, UnityLifecyclePolicy};

const MAX_PROPERTIES: usize = 40;

#[derive(Subcommand)]
pub enum MaterialAction {
    /// Create a new material asset
    Create {
        /// Asset path to create at
        path: String,
        /// Optional shader name; defaults to a common lit shader if omitted
        #[arg(long)]
        shader: Option<String>,
    },
    /// List all properties on a material
    GetProperties {
        /// Asset path to the material
        #[arg(long)]
        path: String,
    },
    /// Get a specific property on a material
    GetProperty {
        /// Asset path to the material
        #[arg(long)]
        path: String,
        /// Property name (e.g. "_Color", "_MainTex")
        #[arg(long)]
        property: String,
    },
    /// Set a property on a material
    SetProperty {
        /// Asset path to the material
        #[arg(long)]
        path: String,
        /// Property name
        #[arg(long)]
        property: String,
        /// Value as JSON
        #[arg(long)]
        value: String,
    },
    /// List enabled keywords on a material
    Keywords {
        /// Asset path to the material
        #[arg(long)]
        path: String,
    },
    /// Enable or disable a shader keyword
    SetKeyword {
        /// Asset path to the material
        #[arg(long)]
        path: String,
        /// Keyword name
        #[arg(long)]
        keyword: String,
        /// Enable or disable
        #[arg(long, action = clap::ArgAction::Set)]
        enabled: bool,
    },
    /// Change the shader of a material
    SetShader {
        /// Asset path to the material
        #[arg(long)]
        path: String,
        /// Shader name (e.g. "Standard", "Universal Render Pipeline/Lit")
        #[arg(long)]
        shader: String,
    },
}

pub async fn run(action: MaterialAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    let mut result = match &action {
        MaterialAction::Create { path, shader } => {
            let mut params = serde_json::json!({ "path": path });
            if let Some(shader_name) = shader {
                params["shader"] = serde_json::json!(shader_name);
            }
            match client.call("material/create", params).await {
                Ok(value) => value,
                Err(err) => {
                    return Err(super::map_bridge_method_error(
                        err,
                        "material/create",
                        "material creation",
                    ));
                }
            }
        }
        MaterialAction::GetProperties { path } => {
            client
                .call(
                    "material/get-properties",
                    serde_json::json!({ "path": path }),
                )
                .await?
        }
        MaterialAction::GetProperty { path, property } => {
            client
                .call(
                    "material/get-property",
                    serde_json::json!({ "path": path, "property": property }),
                )
                .await?
        }
        MaterialAction::SetProperty {
            path,
            property,
            value,
        } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "material/set-property",
                    serde_json::json!({ "path": path, "property": property, "value": parsed }),
                )
                .await?
        }
        MaterialAction::Keywords { path } => {
            client
                .call("material/get-keywords", serde_json::json!({ "path": path }))
                .await?
        }
        MaterialAction::SetKeyword {
            path,
            keyword,
            enabled,
        } => {
            client
                .call(
                    "material/set-keyword",
                    serde_json::json!({ "path": path, "keyword": keyword, "enabled": enabled }),
                )
                .await?
        }
        MaterialAction::SetShader { path, shader } => {
            client
                .call(
                    "material/set-shader",
                    serde_json::json!({ "path": path, "shader": shader }),
                )
                .await?
        }
    };

    client.close().await;

    let lifecycle =
        super::await_unity_lifecycle(&project, Some(&lock), material_lifecycle_policy(&action), ctx)
            .await?;

    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            MaterialAction::Create { path, .. } => {
                let shader = result.get("shader").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("Created material: {path} ({shader})"));
            }
            MaterialAction::GetProperties { .. } => {
                let mat_name = result
                    .get("material")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                let shader = result.get("shader").and_then(|v| v.as_str()).unwrap_or("?");
                if let Some(props) = result.get("properties").and_then(|v| v.as_array()) {
                    output::print_success(&format!(
                        "{mat_name} ({shader}): {} properties",
                        props.len()
                    ));
                    for p in props.iter().take(MAX_PROPERTIES) {
                        let name = p.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let ptype = p.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                        let val = p.get("value").map(|v| v.to_string()).unwrap_or_default();
                        eprintln!("  {name} ({ptype}): {val}");
                    }
                    if props.len() > MAX_PROPERTIES {
                        eprintln!(
                            "  ... {} more propertie(s) omitted; use --json or get-property for a narrower read",
                            props.len() - MAX_PROPERTIES
                        );
                    }
                }
            }
            MaterialAction::GetProperty { property, .. } => {
                let val = result
                    .get("value")
                    .map(|v| v.to_string())
                    .unwrap_or_default();
                let ptype = result.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("{property} ({ptype}): {val}"));
            }
            MaterialAction::SetProperty { property, .. } => {
                output::print_success(&format!("Set material property: {property}"));
            }
            MaterialAction::Keywords { .. } => {
                if let Some(kws) = result.get("keywords").and_then(|v| v.as_array()) {
                    output::print_success(&format!("{} keyword(s) enabled", kws.len()));
                    for kw in kws {
                        if let Some(s) = kw.as_str() {
                            eprintln!("  {s}");
                        }
                    }
                }
            }
            MaterialAction::SetKeyword {
                keyword, enabled, ..
            } => {
                let state = if *enabled { "enabled" } else { "disabled" };
                output::print_success(&format!("Keyword {keyword}: {state}"));
            }
            MaterialAction::SetShader { shader, .. } => {
                output::print_success(&format!("Changed shader to: {shader}"));
            }
        }
    }

    Ok(())
}

fn material_lifecycle_policy(action: &MaterialAction) -> UnityLifecyclePolicy {
    match action {
        MaterialAction::Create { .. } => UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish creating the material asset...",
            "material processing",
        ),
        MaterialAction::GetProperties { .. }
        | MaterialAction::GetProperty { .. }
        | MaterialAction::Keywords { .. } => UnityLifecyclePolicy::None,
        MaterialAction::SetProperty { .. }
        | MaterialAction::SetKeyword { .. }
        | MaterialAction::SetShader { .. } => UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish applying material changes...",
            "material processing",
        ),
    }
}
