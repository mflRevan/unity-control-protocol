use crate::output;
use clap::Subcommand;
use std::path::Path;

use super::Context;

const MAX_ASSET_FIELDS: usize = 40;

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
    /// Write multiple fields on an asset from a JSON object
    WriteBatch {
        /// Asset path
        path: String,
        /// JSON object of field/value pairs
        #[arg(long)]
        values: String,
    },
    /// Create a new ScriptableObject asset
    CreateSo {
        /// ScriptableObject type name
        #[arg(long, short = 't')]
        r#type: String,
        /// Asset path to create at
        path: String,
    },
    /// Reimport an asset or meta file through Unity
    Reimport {
        /// Asset path or .meta path
        path: String,
    },
    /// Inspect and modify Unity importer settings for an asset
    ImportSettings {
        #[command(subcommand)]
        action: ImportSettingsAction,
    },
}

#[derive(Subcommand)]
pub enum ImportSettingsAction {
    /// Read importer settings from an asset or .meta file
    Read {
        /// Asset path or .meta path
        path: String,
        /// Specific importer field/property path (reads all if omitted)
        #[arg(long)]
        field: Option<String>,
    },
    /// Write one importer setting
    Write {
        /// Asset path or .meta path
        path: String,
        /// Importer field/property path
        #[arg(long)]
        field: String,
        /// Value as JSON
        #[arg(long)]
        value: String,
        /// Update settings without immediately reimporting the asset
        #[arg(long)]
        no_reimport: bool,
    },
    /// Write multiple importer settings from a JSON object
    WriteBatch {
        /// Asset path or .meta path
        path: String,
        /// JSON object of field/value pairs
        #[arg(long)]
        values: String,
        /// Update settings without immediately reimporting the asset
        #[arg(long)]
        no_reimport: bool,
    },
}

pub async fn run(action: AssetAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

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
        AssetAction::WriteBatch { path, values } => {
            let parsed: serde_json::Value = serde_json::from_str(values)
                .unwrap_or_else(|_| serde_json::Value::String(values.clone()));
            let object = parsed
                .as_object()
                .ok_or_else(|| anyhow::anyhow!("--values must be a JSON object"))?;
            client
                .call(
                    "asset/write-batch",
                    serde_json::json!({ "path": path, "values": object }),
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
        AssetAction::Reimport { path } => {
            client
                .call("asset/reimport", serde_json::json!({ "path": path }))
                .await?
        }
        AssetAction::ImportSettings { action } => match action {
            ImportSettingsAction::Read { path, field } => {
                let mut params = serde_json::json!({ "path": path });
                if let Some(f) = field {
                    params["field"] = serde_json::json!(f);
                }
                client.call("asset/import-settings/read", params).await?
            }
            ImportSettingsAction::Write {
                path,
                field,
                value,
                no_reimport,
            } => {
                let parsed = parse_json_or_string(value);
                client
                    .call(
                        "asset/import-settings/write",
                        serde_json::json!({
                            "path": path,
                            "field": field,
                            "value": parsed,
                            "noReimport": no_reimport
                        }),
                    )
                    .await?
            }
            ImportSettingsAction::WriteBatch {
                path,
                values,
                no_reimport,
            } => {
                let parsed = parse_json_or_string(values);
                let object = parsed
                    .as_object()
                    .ok_or_else(|| anyhow::anyhow!("--values must be a JSON object"))?;
                client
                    .call(
                        "asset/import-settings/write-batch",
                        serde_json::json!({
                            "path": path,
                            "values": object,
                            "noReimport": no_reimport
                        }),
                    )
                    .await?
            }
        },
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
                        let name = r.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let path_name = Path::new(path)
                            .file_stem()
                            .and_then(|v| v.to_str())
                            .unwrap_or("");
                        if name != "?" && !name.is_empty() && name != path_name {
                            eprintln!("  [{atype}] {path} :: {name}");
                        } else {
                            eprintln!("  [{atype}] {path}");
                        }
                    }
                }
            }
            AssetAction::Info { .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let atype = result.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                let path = result.get("path").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("{name} ({atype})"));
                eprintln!("  Path: {path}");
                if let Some(guid) = result.get("guid").and_then(|v| v.as_str()) {
                    eprintln!("  GUID: {guid}");
                }
            }
            AssetAction::Read { .. } => {
                let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let atype = result.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                output::print_success(&format!("{name} ({atype})"));
                if let Some(fields) = result.get("fields").and_then(|v| v.as_array()) {
                    for f in fields.iter().take(MAX_ASSET_FIELDS) {
                        let fname = f.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                        let ftype = f.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                        let val = f.get("value").map(|v| v.to_string()).unwrap_or_default();
                        eprintln!("  {fname} ({ftype}): {val}");
                    }
                    if fields.len() > MAX_ASSET_FIELDS {
                        eprintln!(
                            "  ... {} more field(s) omitted; use --json or --field for a narrower read",
                            fields.len() - MAX_ASSET_FIELDS
                        );
                    }
                }
            }
            AssetAction::Write { path, field, .. } => {
                output::print_success(&format!("Updated {path} → {field}"));
            }
            AssetAction::WriteBatch { path, .. } => {
                let fields = result
                    .get("fields")
                    .and_then(|v| v.as_array())
                    .map(|items| items.len())
                    .unwrap_or(0);
                output::print_success(&format!("Updated {path} → {fields} field(s)"));
            }
            AssetAction::CreateSo { r#type, path } => {
                output::print_success(&format!("Created {} at {path}", r#type));
            }
            AssetAction::Reimport { .. } => {
                let asset_path = result
                    .get("assetPath")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                let importer_type = result
                    .get("importerType")
                    .and_then(|v| v.as_str())
                    .unwrap_or("asset");
                output::print_success(&format!("Reimported {asset_path} ({importer_type})"));
            }
            AssetAction::ImportSettings { action } => match action {
                ImportSettingsAction::Read { .. } => {
                    let importer_type = result
                        .get("importerType")
                        .and_then(|v| v.as_str())
                        .unwrap_or("Importer");
                    let asset_path = result
                        .get("assetPath")
                        .and_then(|v| v.as_str())
                        .unwrap_or("?");
                    output::print_success(&format!("{importer_type} settings for {asset_path}"));
                    if let Some(fields) = result.get("fields").and_then(|v| v.as_array()) {
                        for field in fields.iter().take(MAX_ASSET_FIELDS) {
                            let property_path = field
                                .get("propertyPath")
                                .and_then(|v| v.as_str())
                                .or_else(|| field.get("name").and_then(|v| v.as_str()))
                                .unwrap_or("?");
                            let field_type =
                                field.get("type").and_then(|v| v.as_str()).unwrap_or("?");
                            let value = field
                                .get("value")
                                .map(|v| v.to_string())
                                .unwrap_or_default();
                            eprintln!("  {property_path} ({field_type}): {value}");
                        }
                        if fields.len() > MAX_ASSET_FIELDS {
                            eprintln!(
                                "  ... {} more field(s) omitted; use --json or --field for a narrower read",
                                fields.len() - MAX_ASSET_FIELDS
                            );
                        }
                    }
                }
                ImportSettingsAction::Write {
                    path,
                    field,
                    no_reimport,
                    ..
                } => {
                    if *no_reimport {
                        output::print_success(&format!(
                            "Updated importer setting {path} → {field} (reimport skipped)"
                        ));
                    } else {
                        output::print_success(&format!(
                            "Updated importer setting {path} → {field} and reimported"
                        ));
                    }
                }
                ImportSettingsAction::WriteBatch {
                    path, no_reimport, ..
                } => {
                    let fields = result
                        .get("fields")
                        .and_then(|v| v.as_array())
                        .map(|items| items.len())
                        .unwrap_or(0);
                    if *no_reimport {
                        output::print_success(&format!(
                            "Updated importer settings {path} → {fields} field(s) (reimport skipped)"
                        ));
                    } else {
                        output::print_success(&format!(
                            "Updated importer settings {path} → {fields} field(s) and reimported"
                        ));
                    }
                }
            },
        }
    }

    Ok(())
}

fn parse_json_or_string(value: &str) -> serde_json::Value {
    serde_json::from_str(value).unwrap_or_else(|_| serde_json::Value::String(value.to_owned()))
}
