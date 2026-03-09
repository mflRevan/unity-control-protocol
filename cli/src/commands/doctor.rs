use crate::config;
use crate::discovery;
use crate::output;
use console::style;

use super::Context;

pub async fn run(ctx: &Context) -> anyhow::Result<()> {
    let mut issues: Vec<String> = Vec::new();
    let mut checks: Vec<(&str, bool, String)> = Vec::new();

    // 1. CLI version
    checks.push(("CLI version", true, format!("v{}", env!("CARGO_PKG_VERSION"))));

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

    // 3. Bridge status
    if let Some(ref proj) = project {
        match discovery::read_lock_file(proj) {
            Ok(lock) => {
                checks.push((
                    "Bridge",
                    true,
                    format!("Running on port {} (PID {})", lock.port, lock.pid),
                ));
                checks.push(("Protocol version", true, lock.protocol_version.clone()));
                checks.push(("Unity version", true, lock.unity_version.clone()));

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

        // 5. Package installed?
        let manifest = proj.join("Packages").join("manifest.json");
        if manifest.exists() {
            let content = std::fs::read_to_string(&manifest).unwrap_or_default();
            if content.contains("com.ucp.bridge") {
                checks.push(("Package installed", true, "com.ucp.bridge".into()));
            } else {
                checks.push(("Package installed", false, "Not found in manifest.json".into()));
                issues.push("Bridge package not installed -- run `ucp install`".into());
            }
        }
    }

    if ctx.json {
        let data = serde_json::json!({
            "checks": checks.iter().map(|(name, ok, detail)| {
                serde_json::json!({"name": name, "pass": ok, "detail": detail})
            }).collect::<Vec<_>>(),
            "healthy": issues.is_empty(),
        });
        output::print_json(&output::success_json(data));
    } else {
        eprintln!();
        for (name, ok, detail) in &checks {
            let icon = if *ok {
                let sym = if output::supports_unicode() { "✔" } else { "[OK]" };
                style(sym).green().bold().to_string()
            } else {
                let sym = if output::supports_unicode() { "✖" } else { "[ERR]" };
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
                let arrow = if output::supports_unicode() { "→" } else { "->" };
                eprintln!("    {arrow} {issue}");
            }
        }
        eprintln!();
    }

    Ok(())
}
