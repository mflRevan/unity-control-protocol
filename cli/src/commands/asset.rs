use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use clap::Subcommand;

use super::Context;

#[derive(Subcommand)]
pub enum AssetAction {
    /// Search for assets by type and/or name
    Search {
        /// Asset type filter (e.g. "Texture2D", "Material", "Prefab")
        #[arg(long, short = 't')]
        r#type: Option<String>,
        /// Name filter
        #[arg(long, short = 'n')]
        name: Option<String>,
        /// Folder path filter (e.g. "Assets/Prefabs")
        #[arg(long, short = 'p')]
        path: Option<String>,
        /// Max results to return
        #[arg(long, default_value = "50")]
        max: i64,
    },
    /// Get detailed info about an asset
    Info {
        /// Asset path (e.g. "Assets/Materials/MyMat.mat")
        path: String,
    },
    /// Read fields from an asset (ScriptableObject, Material, etc.)
    Read {
        /// Asset path
        path: String,
        /// Specific field name (reads all if omitted)
        #[arg(long)]
        field: Option<String>,
    },
    /// Write a field on an asset
    Write {
        /// Asset path
        path: String,
        /// Field name
        #[arg(long)]
        field: String,
        /// Value as JSON
        #[arg(long)]
        value: String,
    },
    /// Create a new ScriptableObject asset
    CreateSo {
        /// ScriptableObject type name
        #[arg(long, short = 't')]
        r#type: String,
        /// Asset path to create at
        path: String,
    },
}

pub async fn run(action: AssetAction, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    let result = match &action {
        AssetAction::Search {
            r#type,
            name,
            path,
            max,
        } => {
            let mut params = serde_json::json!({ "maxResults": max });
            if let Some(t) = r#type {
                params["type"] = serde_json::json!(t);
            }
            if let Some(n) = name {
                params["name"] = serde_json::json!(n);
            }
            if let Some(p) = path {
                params["path"] = serde_json::json!(p);
            }
            client.call("asset/search", params).await?
        }
        AssetAction::Info { path } => {
            client
                .call("asset/info", serde_json::json!({ "path": path }))
                .await?
        }
        AssetAction::Read { path, field } => {
            let mut params = serde_json::json!({ "path": path });
            if let Some(f) = field {
                params["field"] = serde_json::json!(f);
            }
            client.call("asset/read", params).await?
        }
        AssetAction::Write { path, field, value } => {
            let parsed: serde_json::Value = serde_json::from_str(value)
                .unwrap_or_else(|_| serde_json::Value::String(value.clone()));
            client
                .call(
                    "asset/write",
                    serde_json::json!({ "path": path, "field": field, "value": parsed }),
                )
                .await?
        }
        AssetAction::CreateSo { r#type, path } => {
            client
                .call(
                    "asset/create-so",
                    serde_json::json!({ "type": r#type, "path": path }),
                )
                .await?
        }
    };

    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        match &action {
            AssetAction::Search { .. } => {
                let total = result.get("total").and_then(|v| v.as_i64()).unwrap_or(0);
                let returned = result.get("returned").and_then(|v| v.as_i64()).unwrap_or(0);
                output::print_success(&format!("Found {total} asset(s) (showing {returned})"));
                if let Some(results) = result.get("results").and_then(|v| v.as_array()) {
                    for r in results {
                        let path = r.get("path").and_then(|v| v.as_str()).unwrap_or("?");
                        let atype = r.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                        eprintln!("  [{atype}] {path}");
                    }
                }
            }
            AssetAction::Info { .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let atype = result
                    .get("type")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                let path = result
                    .get("path")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                output::print_success(&format!("{name} ({atype})"));
                eprintln!("  Path: {path}");
                if let Some(guid) = result.get("guid").and_then(|v| v.as_str()) {
                    eprintln!("  GUID: {guid}");
                }
            }
            AssetAction::Read { .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let atype = result
                    .get("type")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                output::print_success(&format!("{name} ({atype})"));
                if let Some(fields) = result.get("fields").and_then(|v| v.as_array()) {
                    for f in fields {
                        let fname = f.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let ftype = f.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                        let val = f.get("value").map(|v| v.to_string()).unwrap_or_default();
                        eprintln!("  {fname} ({ftype}): {val}");
                    }
                }
            }
            AssetAction::Write { path, field, .. } => {
                output::print_success(&format!("Updated {path} → {field}"));
            }
            AssetAction::CreateSo { r#type, path } => {
                output::print_success(&format!("Created {type} at {path}"));
            }
        }
    }

    Ok(())
}
