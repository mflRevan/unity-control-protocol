use crate::discovery;
use crate::output;
use console::style;
use dialoguer::{Confirm, theme::ColorfulTheme};
use indicatif::{ProgressBar, ProgressStyle};
use std::path::{Path, PathBuf};
use std::time::Duration;

use super::Context;

const PACKAGE_NAME: &str = "com.ucp.bridge";
const PACKAGE_GIT_URL: &str =
    "https://github.com/AimMakerOrg/unity-control-protocol.git?path=unity-package/com.ucp.bridge";

pub async fn run(path: Option<String>, ctx: &Context) -> anyhow::Result<()> {
    let project_path = if let Some(p) = path {
        let pb = PathBuf::from(&p);
        if !pb.join("ProjectSettings").is_dir() {
            anyhow::bail!("Not a Unity project: {p}");
        }
        pb
    } else {
        // Interactive discovery
        let cwd = std::env::current_dir()?;
        match discovery::find_unity_project(&cwd) {
            Ok(p) => p,
            Err(_) => {
                if !ctx.json {
                    output::print_warn(
                        "No Unity project found in current directory tree",
                    );
                }
                anyhow::bail!("No Unity project found. Use --project or run from a Unity project directory.");
            }
        }
    };

    let manifest_path = project_path.join("Packages").join("manifest.json");
    if !manifest_path.exists() {
        anyhow::bail!(
            "manifest.json not found at {}",
            manifest_path.display()
        );
    }

    // Check if already installed
    let manifest_content = std::fs::read_to_string(&manifest_path)?;
    if manifest_content.contains(PACKAGE_NAME) {
        if !ctx.json {
            output::print_info("UCP bridge is already installed in this project");
        }
        return Ok(());
    }

    if !ctx.json {
        eprintln!();
        let bolt = if output::supports_unicode() { "⚡" } else { "*" };
        let bar = if output::supports_unicode() { "│" } else { "|" };
        eprintln!(
            "  {} Install UCP bridge into:",
            style(bolt).cyan().bold()
        );
        eprintln!(
            "  {} {}",
            style(bar).dim(),
            project_path.display()
        );
        eprintln!();

        let confirm = Confirm::with_theme(&ColorfulTheme::default())
            .with_prompt("Proceed with installation?")
            .default(true)
            .interact()?;

        if !confirm {
            output::print_warn("Installation cancelled");
            return Ok(());
        }
    }

    let spinner = if !ctx.json {
        let pb = ProgressBar::new_spinner();
        pb.set_style(
            ProgressStyle::with_template("{spinner:.cyan} {msg}")
                .unwrap()
                .tick_strings(&["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"]),
        );
        pb.enable_steady_tick(Duration::from_millis(80));
        pb.set_message("Installing UCP bridge package...");
        Some(pb)
    } else {
        None
    };

    // Inject into manifest.json
    inject_package(&manifest_path, PACKAGE_NAME, PACKAGE_GIT_URL)?;

    // Create .ucp directory
    let ucp_dir = project_path.join(".ucp");
    if !ucp_dir.exists() {
        std::fs::create_dir_all(&ucp_dir)?;
    }

    // Add .ucp to project .gitignore if it exists
    let gitignore = project_path.join(".gitignore");
    if gitignore.exists() {
        let gi = std::fs::read_to_string(&gitignore)?;
        if !gi.contains(".ucp/") {
            std::fs::write(&gitignore, format!("{gi}\n# UCP bridge\n.ucp/\n"))?;
        }
    }

    if let Some(pb) = spinner {
        pb.finish_and_clear();
    }

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "installed": true,
            "project": project_path.display().to_string(),
        })));
    } else {
        output::print_success("UCP bridge installed successfully");
        eprintln!();
        eprintln!(
            "  {} Unity will import the package when it regains focus.",
            style("ℹ").cyan()
        );
        eprintln!(
            "  {} Run {} to verify connectivity.",
            style("ℹ").cyan(),
            style("ucp connect").bold()
        );
        eprintln!();
    }

    Ok(())
}

pub async fn uninstall(ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let manifest_path = project.join("Packages").join("manifest.json");

    let content = std::fs::read_to_string(&manifest_path)?;
    if !content.contains(PACKAGE_NAME) {
        if !ctx.json {
            output::print_info("UCP bridge is not installed in this project");
        }
        return Ok(());
    }

    let mut manifest: serde_json::Value = serde_json::from_str(&content)?;
    if let Some(deps) = manifest.get_mut("dependencies").and_then(|v| v.as_object_mut()) {
        deps.remove(PACKAGE_NAME);
    }

    let out = serde_json::to_string_pretty(&manifest)?;
    std::fs::write(&manifest_path, format!("{out}\n"))?;

    // Clean up lock file
    let lock_path = crate::config::lock_file_path(&project);
    let _ = std::fs::remove_file(lock_path);

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({"uninstalled": true})));
    } else {
        output::print_success("UCP bridge uninstalled");
    }

    Ok(())
}

fn inject_package(manifest_path: &Path, name: &str, url: &str) -> anyhow::Result<()> {
    let content = std::fs::read_to_string(manifest_path)?;
    let mut manifest: serde_json::Value = serde_json::from_str(&content)?;

    if let Some(deps) = manifest.get_mut("dependencies").and_then(|v| v.as_object_mut()) {
        deps.insert(name.to_string(), serde_json::json!(url));
    } else {
        anyhow::bail!("manifest.json has no 'dependencies' object");
    }

    let out = serde_json::to_string_pretty(&manifest)?;
    std::fs::write(manifest_path, format!("{out}\n"))?;

    Ok(())
}
