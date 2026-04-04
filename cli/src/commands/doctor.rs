use crate::bridge_package;
use crate::client::BridgeClient;
use crate::config;
use crate::discovery;
use crate::editor_runtime;
use crate::output;
use crate::release_check;
use console::style;

use super::Context;

pub async fn run(ctx: &Context) -> anyhow::Result<()> {
    let mut issues: Vec<String> = Vec::new();
    let mut warnings: Vec<String> = Vec::new();
    let mut checks: Vec<(&str, bool, String)> = Vec::new();

    // 1. CLI version
    checks.push((
        "CLI version",
        true,
        format!("v{}", env!("CARGO_PKG_VERSION")),
    ));

    match release_check::check_for_update().await {
        Ok(Some(status)) if status.update_available => {
            checks.push((
                "CLI release",
                true,
                format!(
                    "Update available: v{} (installed v{})",
                    status.latest_version, status.current_version
                ),
            ));
            warnings.extend(status.warning_lines());
        }
        Ok(Some(status)) => {
            checks.push((
                "CLI release",
                true,
                format!("Up to date with v{}", status.latest_version),
            ));
        }
        Ok(None) => {
            checks.push(("CLI release", true, "No release metadata available".into()));
        }
        Err(error) => {
            checks.push(("CLI release", true, format!("Not checked ({error})")));
        }
    }

    // 2. Project detection
    let project = match discovery::resolve_project(ctx.project.as_deref()) {
        Ok(p) => {
            checks.push(("Unity project", true, p.display().to_string()));
            Some(p)
        }
        Err(e) => {
            checks.push(("Unity project", false, format!("{e}")));
            issues.push("No Unity project found".into());
            None
        }
    };

    checks.push((
        "Bridge update policy",
        true,
        ctx.bridge_update_policy.to_string(),
    ));

    // 3. Bridge package + editor status
    if let Some(ref proj) = project {
        match bridge_package::apply_update_policy(proj, ctx, true).await {
            Ok(package_outcome) => {
                let package = package_outcome.status;
                if package.installed {
                    checks.push((
                        "Bridge package",
                        true,
                        format!(
                            "{} ({})",
                            package.installed_version.as_deref().unwrap_or("unknown"),
                            package.source_kind
                        ),
                    ));
                } else {
                    checks.push(("Bridge package", false, "Not installed".into()));
                    issues.push("Bridge package not installed -- run `ucp install`".into());
                }

                if package.outdated {
                    checks.push((
                        "Bridge package match",
                        false,
                        format!(
                            "Installed {} behind target {}",
                            package.installed_version.as_deref().unwrap_or("unknown"),
                            package.target_version
                        ),
                    ));
                    issues.push("Bridge package reference is behind the CLI version".into());
                } else if package.installed {
                    checks.push((
                        "Bridge package match",
                        true,
                        format!("Target {}", package.target_version),
                    ));
                }
            }
            Err(error) => {
                checks.push(("Bridge package", false, format!("{error:#}")));
                issues.push("Failed to inspect bridge package state".into());
            }
        }

        let editor = editor_runtime::status(proj, ctx);
        checks.push((
            "Editor runtime",
            editor.running,
            editor
                .pid
                .map(|pid| format!("Running (PID {pid})"))
                .unwrap_or_else(|| "Not running".into()),
        ));

        match editor.resolved_unity_path.as_deref() {
            Some(path) => checks.push(("Unity executable", true, path.to_string())),
            None => {
                checks.push((
                    "Unity executable",
                    false,
                    editor
                        .resolution_error
                        .clone()
                        .unwrap_or_else(|| "Not resolved".into()),
                ));
                issues.push("Unity executable could not be resolved for this project".into());
            }
        }

        if let Some(version) = editor.project_version.as_deref() {
            checks.push(("Project Unity version", true, version.to_string()));
        }

        match discovery::read_lock_file(proj) {
            Ok(lock) => {
                checks.push((
                    "Bridge",
                    true,
                    format!("Running on port {} (PID {})", lock.port, lock.pid),
                ));
                checks.push(("Protocol version", true, lock.protocol_version.clone()));
                checks.push(("Unity version", true, lock.unity_version.clone()));

                match BridgeClient::connect(&lock).await {
                    Ok(mut client) => {
                        if client.handshake().await.is_ok() {
                            checks.push(("Bridge handshake", true, "Responsive".into()));
                        } else {
                            checks.push(("Bridge handshake", false, "Handshake failed".into()));
                            issues.push("Bridge handshake failed".into());
                        }
                        client.close().await;
                    }
                    Err(error) => {
                        checks.push(("Bridge handshake", false, format!("{error}")));
                        issues.push("Bridge is not accepting connections".into());
                    }
                }

                // 4. Check protocol compatibility
                if lock.protocol_version != config::PROTOCOL_VERSION {
                    checks.push((
                        "Protocol match",
                        false,
                        format!(
                            "CLI expects {}, bridge reports {}",
                            config::PROTOCOL_VERSION,
                            lock.protocol_version
                        ),
                    ));
                    issues.push("Protocol version mismatch".into());
                } else {
                    checks.push(("Protocol match", true, "Compatible".into()));
                }
            }
            Err(e) => {
                checks.push(("Bridge", false, format!("{e}")));
                issues.push("Bridge not running".into());
            }
        }
    }

    // Serialization mode checks for native reference indexing
    if let Some(ref proj) = project {
        let ref_status = super::references::check_serialization(proj);
        checks.push((
            "Force Text serialization",
            ref_status.force_text,
            if ref_status.force_text {
                "Enabled".into()
            } else {
                "Not set (recommended for native reference indexing)".into()
            },
        ));
        checks.push((
            "Visible Meta Files",
            ref_status.visible_meta,
            if ref_status.visible_meta {
                "Enabled".into()
            } else {
                "Not set (recommended for native reference indexing)".into()
            },
        ));
        if !ref_status.native_capable {
            warnings.push("Native reference indexing is unavailable. Enable Force Text serialization and Visible Meta Files for full `ucp references` support.".into());
        }
    }

    if ctx.json {
        let data = serde_json::json!({
            "checks": checks.iter().map(|(name, ok, detail)| {
                serde_json::json!({"name": name, "pass": ok, "detail": detail})
            }).collect::<Vec<_>>(),
            "warnings": warnings,
            "healthy": issues.is_empty(),
        });
        output::print_json(&output::success_json(data));
    } else {
        eprintln!();
        for (name, ok, detail) in &checks {
            let icon = if *ok {
                let sym = if output::supports_unicode() {
                    "✔"
                } else {
                    "[OK]"
                };
                style(sym).green().bold().to_string()
            } else {
                let sym = if output::supports_unicode() {
                    "✖"
                } else {
                    "[ERR]"
                };
                style(sym).red().bold().to_string()
            };
            eprintln!("  {icon} {}: {}", style(name).bold(), detail);
        }
        eprintln!();

        if issues.is_empty() {
            output::print_success("All checks passed");
        } else {
            output::print_error(&format!("{} issue(s) found", issues.len()));
            for issue in &issues {
                let arrow = if output::supports_unicode() {
                    "→"
                } else {
                    "->"
                };
                eprintln!("    {arrow} {issue}");
            }
        }

        if !warnings.is_empty() {
            eprintln!();
            for warning in &warnings {
                output::print_warn(warning);
            }
        }
        eprintln!();
    }

    Ok(())
}
