use crate::commands;
use crate::commands::install::{self, InstallOptions};
use crate::config::BridgeUpdatePolicy;
use crate::discovery;
use crate::output;
use semver::Version;
use serde::Serialize;
use std::fs;
use std::path::{Path, PathBuf};

const PACKAGE_NAME: &str = "com.ucp.bridge";
const PACKAGE_GIT_URL_BASE: &str =
    "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge";

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgePackageStatus {
    pub installed: bool,
    pub source_kind: String,
    pub dependency: Option<String>,
    pub installed_version: Option<String>,
    pub target_version: String,
    pub target_reference: String,
    pub outdated: bool,
    pub managed_git_dependency: bool,
    pub local_source: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgePolicyOutcome {
    pub action: String,
    pub status: BridgePackageStatus,
    pub previous_status: Option<BridgePackageStatus>,
}

pub fn target_reference() -> String {
    format!("{PACKAGE_GIT_URL_BASE}#v{}", env!("CARGO_PKG_VERSION"))
}

pub fn inspect(project: &Path) -> anyhow::Result<BridgePackageStatus> {
    let manifest_dependency = read_manifest_dependency(project)?;
    let embedded_package_path = project.join("Packages").join(PACKAGE_NAME);
    let embedded_exists = embedded_package_path.join("package.json").is_file();
    let target_version = env!("CARGO_PKG_VERSION").to_string();
    let target_reference = target_reference();

    let (
        installed,
        source_kind,
        dependency,
        installed_version,
        managed_git_dependency,
        local_source,
    ) = if embedded_exists {
        (
            true,
            "embedded-local".to_string(),
            Some(embedded_package_path.display().to_string()),
            read_package_json_version(&embedded_package_path.join("package.json")),
            false,
            true,
        )
    } else if let Some(reference) = manifest_dependency.clone() {
        let source_kind = if reference.starts_with(PACKAGE_GIT_URL_BASE) {
            "git"
        } else if reference.starts_with("file:") {
            "file"
        } else {
            "custom"
        };

        let version = if source_kind == "git" {
            parse_git_dependency_version(&reference)
        } else if source_kind == "file" {
            read_file_dependency_version(&reference)
        } else {
            parse_git_dependency_version(&reference)
        };

        (
            true,
            source_kind.to_string(),
            Some(reference.clone()),
            version,
            source_kind == "git",
            source_kind == "file",
        )
    } else {
        (false, "missing".to_string(), None, None, false, false)
    };

    let outdated = managed_git_dependency
        && installed_version
            .as_deref()
            .and_then(parse_version)
            .zip(parse_version(&target_version))
            .map(|(current, target)| current < target)
            .unwrap_or(false);

    Ok(BridgePackageStatus {
        installed,
        source_kind,
        dependency,
        installed_version,
        target_version,
        target_reference,
        outdated,
        managed_git_dependency,
        local_source,
    })
}

pub async fn apply_update_policy(
    project: &Path,
    ctx: &commands::Context,
    emit_output: bool,
) -> anyhow::Result<BridgePolicyOutcome> {
    let status = inspect(project)?;
    if !status.installed || !status.outdated {
        return Ok(BridgePolicyOutcome {
            action: "none".to_string(),
            status,
            previous_status: None,
        });
    }

    match ctx.bridge_update_policy {
        BridgeUpdatePolicy::Off => Ok(BridgePolicyOutcome {
            action: "skipped".to_string(),
            status,
            previous_status: None,
        }),
        BridgeUpdatePolicy::Warn => {
            if emit_output && !ctx.json {
                output::print_warn(&format!(
                    "Bridge package is outdated ({} -> {}). Run `ucp bridge update` or use --bridge-update-policy auto.",
                    status.installed_version.as_deref().unwrap_or("unknown"),
                    status.target_version
                ));
            }

            Ok(BridgePolicyOutcome {
                action: "warned".to_string(),
                status,
                previous_status: None,
            })
        }
        BridgeUpdatePolicy::Auto => {
            if emit_output && !ctx.json {
                output::print_info(&format!(
                    "Updating outdated bridge package reference ({} -> {})...",
                    status.installed_version.as_deref().unwrap_or("unknown"),
                    status.target_version
                ));
            }

            let mut install_ctx = ctx.clone();
            install_ctx.json = false;
            let no_wait = !discovery::is_unity_editor_running_for_project(project);
            install::run(
                Some(project.display().to_string()),
                InstallOptions {
                    manifest: true,
                    no_wait,
                    ..InstallOptions::default()
                },
                &install_ctx,
            )
            .await?;

            Ok(BridgePolicyOutcome {
                action: "updated".to_string(),
                status: inspect(project)?,
                previous_status: Some(status),
            })
        }
    }
}

fn read_manifest_dependency(project: &Path) -> anyhow::Result<Option<String>> {
    let manifest_path = project.join("Packages").join("manifest.json");
    if !manifest_path.is_file() {
        return Ok(None);
    }

    let content = fs::read_to_string(&manifest_path)?;
    let manifest: serde_json::Value = serde_json::from_str(&content)?;
    Ok(manifest
        .get("dependencies")
        .and_then(|value| value.as_object())
        .and_then(|deps| deps.get(PACKAGE_NAME))
        .and_then(|value| value.as_str())
        .map(ToOwned::to_owned))
}

fn parse_git_dependency_version(reference: &str) -> Option<String> {
    let fragment = reference.split('#').nth(1)?;
    Some(fragment.trim_start_matches('v').to_string())
}

fn read_file_dependency_version(reference: &str) -> Option<String> {
    let raw = reference.strip_prefix("file:")?;
    let path = PathBuf::from(raw);
    read_package_json_version(&path.join("package.json"))
}

fn read_package_json_version(path: &Path) -> Option<String> {
    let content = fs::read_to_string(path).ok()?;
    let value: serde_json::Value = serde_json::from_str(&content).ok()?;
    value
        .get("version")
        .and_then(|value| value.as_str())
        .map(ToOwned::to_owned)
}

fn parse_version(value: &str) -> Option<Version> {
    Version::parse(value.trim()).ok()
}

#[cfg(test)]
mod tests {
    use super::inspect;
    use std::fs;

    #[test]
    fn inspect_prefers_embedded_local_mount_over_manifest_dependency() {
        let temp_root =
            std::env::temp_dir().join(format!("ucp-bridge-inspect-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&temp_root);

        fs::create_dir_all(temp_root.join("Packages").join("com.ucp.bridge")).unwrap();
        fs::write(
            temp_root.join("Packages").join("manifest.json"),
            r#"{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v0.3.2"
  }
}"#,
        )
        .unwrap();
        fs::write(
            temp_root
                .join("Packages")
                .join("com.ucp.bridge")
                .join("package.json"),
            r#"{ "name": "com.ucp.bridge", "version": "0.3.3" }"#,
        )
        .unwrap();

        let status = inspect(&temp_root).expect("inspect status");
        assert_eq!(status.source_kind, "embedded-local");
        assert!(status.local_source);
        assert!(!status.managed_git_dependency);
        assert!(!status.outdated);

        let _ = fs::remove_dir_all(&temp_root);
    }
}
