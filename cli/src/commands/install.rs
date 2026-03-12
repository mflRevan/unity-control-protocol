use crate::bridge_lifecycle::{self, WaitMode, WaitStatus};
use crate::client::BridgeClient;
use crate::discovery;
use crate::output;
use console::style;
use directories::ProjectDirs;
use dialoguer::{Confirm, theme::ColorfulTheme};
use indicatif::{ProgressBar, ProgressStyle};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use super::Context;

const PACKAGE_NAME: &str = "com.ucp.bridge";
const PACKAGE_GIT_URL_BASE: &str =
    "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge";
const INSTALL_STATE_FILE: &str = "install-state.json";

#[derive(Debug, Clone, Default)]
pub struct InstallOptions {
    pub dev: bool,
    pub embedded: bool,
    pub manifest: bool,
    pub bridge_path: Option<String>,
    pub bridge_ref: Option<String>,
    pub no_wait: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct InstallState {
    mode: String,
    source: Option<String>,
}

pub async fn run(path: Option<String>, options: InstallOptions, ctx: &Context) -> anyhow::Result<()> {
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

    if options.dev && options.bridge_path.is_some() {
        anyhow::bail!("Use either --dev or --bridge-path, not both");
    }

    if options.manifest && (options.dev || options.embedded || options.bridge_path.is_some()) {
        anyhow::bail!("Use --manifest by itself, or use --dev/--embedded/--bridge-path");
    }

    if options.bridge_ref.is_some() && (options.dev || options.bridge_path.is_some() || options.embedded) {
        anyhow::bail!("Use --bridge-ref by itself, or use --dev/--embedded/--bridge-path");
    }

    let manifest_path = project_path.join("Packages").join("manifest.json");
    if !manifest_path.exists() {
        anyhow::bail!(
            "manifest.json not found at {}",
            manifest_path.display()
        );
    }

    if let Some(source_path) = desired_embedded_package_source(&options)? {
        return run_embedded_local_install(&project_path, source_path, options.no_wait, ctx).await;
    }

    remove_owned_embedded_mount_if_present(&project_path)?;
    clear_install_state(&project_path)?;

    let desired_package_url = desired_package_reference(&options)?;
    let using_custom_source = options.bridge_ref.is_some();
    let previous_lock = discovery::read_lock_file(&project_path).ok();
    let already_running = previous_lock.is_some();

    // Check if already installed
    let manifest_content = std::fs::read_to_string(&manifest_path)?;
    let manifest_json: serde_json::Value = serde_json::from_str(&manifest_content)?;
    let current_dependency = manifest_json
        .get("dependencies")
        .and_then(|v| v.as_object())
        .and_then(|deps| deps.get(PACKAGE_NAME))
        .and_then(|v| v.as_str())
        .map(ToOwned::to_owned);

    let same_dependency = current_dependency
        .as_deref()
        .map(|current| current == desired_package_url)
        .unwrap_or(false);
    let refresh_existing_custom_source = should_refresh_existing_custom_source(same_dependency, using_custom_source);

    let updating_existing = match current_dependency.as_deref() {
        Some(current) if current == desired_package_url => false,
        Some(current) if current.starts_with(PACKAGE_GIT_URL_BASE) => true,
        Some(_) => {
            if !using_custom_source {
                if !ctx.json {
                    output::print_info("UCP bridge is already installed with a custom dependency reference");
                }
                return Ok(());
            }
            if !ctx.json {
                output::print_info("UCP bridge is already installed with a custom dependency reference");
            }
            true
        }
        None => false,
    };

    if same_dependency && already_running && !using_custom_source {
        if !ctx.json {
            output::print_info("UCP bridge is already installed in this project");
        }
        return Ok(());
    }

    if updating_existing && !ctx.json {
        output::print_info(&format!(
            "Updating UCP bridge reference to match CLI v{}",
            env!("CARGO_PKG_VERSION")
        ));
    }

    if current_dependency.is_some() && !updating_existing && !refresh_existing_custom_source {
        if !ctx.json {
            output::print_info("UCP bridge is already installed in this project");
        }
        return Ok(());
    }

    if !ctx.json && !refresh_existing_custom_source {
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

    let spinner = if !ctx.json && !same_dependency {
        let pb = ProgressBar::new_spinner();
        pb.set_style(
            ProgressStyle::with_template("{spinner:.cyan} {msg}")
                .unwrap()
                .tick_strings(&["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"]),
        );
        pb.enable_steady_tick(Duration::from_millis(80));
        pb.set_message(if updating_existing {
            "Updating UCP bridge package reference..."
        } else {
            "Installing UCP bridge package..."
        });
        Some(pb)
    } else {
        None
    };

    if refresh_existing_custom_source && !ctx.json {
        output::print_info("UCP bridge is already installed from a local/custom source; refreshing Unity package import");
    }

    if !same_dependency || !already_running {
        // Inject into manifest.json. Rewriting the same value is intentional when the bridge is down,
        // because Unity needs a concrete manifest change to re-evaluate the local package.
        inject_package(&manifest_path, PACKAGE_NAME, &desired_package_url)?;
    } else if !ctx.json {
        output::print_info("Desired bridge reference is already installed; waiting for Unity to bring it back up");
    }

    ensure_ucp_state_dir(&project_path)?;
    ensure_local_git_exclude(&project_path, ".ucp/")?;

    if let Some(pb) = spinner {
        pb.finish_and_clear();
    }

    if let Some(lock) = previous_lock.as_ref() {
        let _ = request_asset_refresh(lock).await;
    }

    if !ctx.json && !same_dependency {
        if updating_existing {
            output::print_success("UCP bridge reference updated successfully");
        } else {
            output::print_success("UCP bridge installed successfully");
        }
        eprintln!();
    }

    write_install_state(&project_path, InstallState {
        mode: "manifest".to_string(),
        source: Some(desired_package_url.clone()),
    })?;

    if options.no_wait {
        if ctx.json {
            output::print_json(&output::success_json(serde_json::json!({
                "installed": true,
                "updated": updating_existing,
                "bridge": desired_package_url,
                "project": project_path.display().to_string(),
                "bridgeStatus": "skipped",
                "nudgedEditor": false,
            })));
        } else {
            eprintln!(
                "  {} Skipped automatic bridge wait. Run {} when Unity finishes importing.",
                style("ℹ").cyan(),
                style("ucp connect").bold()
            );
            eprintln!();
        }
        return Ok(());
    }

    let wait_outcome = bridge_lifecycle::wait_for_bridge(
        &project_path,
        previous_lock.as_ref(),
        ctx.timeout.max(90),
        if previous_lock.is_some() {
            WaitMode::RestartOptional
        } else {
            WaitMode::FirstAvailable
        },
    )
    .await?;

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "installed": true,
            "updated": updating_existing,
            "bridge": desired_package_url,
            "project": project_path.display().to_string(),
            "bridgeStatus": match wait_outcome.status {
                WaitStatus::Available => "available",
                WaitStatus::Restarted => "restarted",
                WaitStatus::Stable => "stable",
                WaitStatus::EditorNotRunning => "editor-not-running",
            },
            "nudgedEditor": wait_outcome.nudged_editor,
        })));
    } else {
        match wait_outcome.status {
            WaitStatus::EditorNotRunning => {
                eprintln!(
                    "  {} Unity is not running for this project. Open it, then run {}.",
                    style("ℹ").cyan(),
                    style("ucp connect").bold()
                );
            }
            WaitStatus::Available | WaitStatus::Restarted | WaitStatus::Stable => {
                if wait_outcome.nudged_editor {
                    eprintln!(
                        "  {} Brought Unity to the foreground to trigger package import.",
                        style("ℹ").cyan()
                    );
                }
                eprintln!(
                    "  {} Bridge is ready. Verify with {}.",
                    style("ℹ").cyan(),
                    style("ucp connect").bold()
                );
            }
        }
        eprintln!(
            "  {} Installed bridge reference: {}",
            style("ℹ").cyan(),
            desired_package_url
        );
        eprintln!();
    }

    Ok(())
}

fn package_git_url() -> String {
    format!("{PACKAGE_GIT_URL_BASE}#v{}", env!("CARGO_PKG_VERSION"))
}

fn uses_embedded_local_package(options: &InstallOptions) -> bool {
    options.dev || options.embedded || options.bridge_path.is_some()
}

fn should_refresh_existing_custom_source(same_dependency: bool, using_custom_source: bool) -> bool {
    same_dependency && using_custom_source
}

fn desired_embedded_package_source(options: &InstallOptions) -> anyhow::Result<Option<PathBuf>> {
    if let Some(path) = &options.bridge_path {
        return Ok(Some(canonicalize_package_dir(PathBuf::from(path))?));
    }

    if uses_embedded_local_package(options) {
        return Ok(Some(require_local_bridge_package()?));
    }

    autodetect_local_bridge_package()
}

fn desired_package_reference(options: &InstallOptions) -> anyhow::Result<String> {
    if let Some(reference) = &options.bridge_ref {
        let trimmed = reference.trim();
        if trimmed.is_empty() {
            anyhow::bail!("--bridge-ref cannot be empty");
        }
        return Ok(trimmed.to_string());
    }

    if let Some(path) = &options.bridge_path {
        return file_package_reference(PathBuf::from(path));
    }

    if options.dev {
        return file_package_reference(default_dev_bridge_path()?);
    }

    Ok(package_git_url())
}

fn default_dev_bridge_path() -> anyhow::Result<PathBuf> {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("unity-package")
        .join(PACKAGE_NAME);

    if path.is_dir() {
        Ok(path)
    } else {
        anyhow::bail!(
            "Repository bridge package not found at {}. Use --bridge-path to point at a local package.",
            path.display()
        );
    }
}

fn require_local_bridge_package() -> anyhow::Result<PathBuf> {
    autodetect_local_bridge_package()?.ok_or_else(|| {
        anyhow::anyhow!(
            "No local bridge package payload is available. Use --manifest for a tracked project dependency, or provide --bridge-path."
        )
    })
}

fn autodetect_local_bridge_package() -> anyhow::Result<Option<PathBuf>> {
    for candidate in bridge_source_candidates() {
        if candidate.is_dir() {
            return Ok(Some(canonicalize_package_dir(candidate)?));
        }
    }

    Ok(None)
}

fn bridge_source_candidates() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if let Some(path) = repo_bridge_package_path() {
        candidates.push(path);
    }
    if let Some(path) = executable_bridge_package_path() {
        candidates.push(path);
    }
    if let Some(path) = cached_bridge_package_path() {
        candidates.push(path);
    }

    candidates
}

fn repo_bridge_package_path() -> Option<PathBuf> {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("unity-package")
        .join(PACKAGE_NAME);

    path.is_dir().then_some(path)
}

fn executable_bridge_package_path() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?;
    let exe_dir = exe.parent()?;
    let parent = exe_dir.parent();

    [
        exe_dir.join("bridge").join(PACKAGE_NAME),
        parent?.join("bridge").join(PACKAGE_NAME),
    ]
    .into_iter()
    .find(|candidate| candidate.is_dir())
}

fn cached_bridge_package_path() -> Option<PathBuf> {
    let dirs = ProjectDirs::from("io", "mflrevan", "ucp")?;
    let path = dirs
        .cache_dir()
        .join("bridge")
        .join(format!("v{}", env!("CARGO_PKG_VERSION")))
        .join(PACKAGE_NAME);

    path.is_dir().then_some(path)
}

fn canonicalize_package_dir(path: PathBuf) -> anyhow::Result<PathBuf> {
    let canonical = fs::canonicalize(&path)
        .map_err(|e| anyhow::anyhow!("Failed to resolve bridge path {}: {e}", path.display()))?;
    let canonical = normalize_windows_path(canonical);

    if !canonical.join("package.json").is_file() {
        anyhow::bail!("Bridge path {} does not look like a Unity package", canonical.display());
    }

    Ok(canonical)
}

fn normalize_windows_path(path: PathBuf) -> PathBuf {
    #[cfg(windows)]
    {
        let raw = path.to_string_lossy().replace('\\', "/");
        if let Some(stripped) = raw.strip_prefix("//?/") {
            return PathBuf::from(stripped.replace('/', "\\"));
        }
    }

    path
}

fn file_package_reference(path: PathBuf) -> anyhow::Result<String> {
    let canonical = canonicalize_package_dir(path)?;

    let mut normalized = canonical.to_string_lossy().replace('\\', "/");
    if let Some(stripped) = normalized.strip_prefix("//?/") {
        normalized = stripped.to_string();
    }

    Ok(format!(
        "file:{}",
        normalized
    ))
}

async fn run_embedded_local_install(
    project_path: &Path,
    source_path: PathBuf,
    no_wait: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    let mount_path = embedded_package_mount(project_path);
    let manifest_path = project_path.join("Packages").join("manifest.json");
    let previous_lock = discovery::read_lock_file(project_path).ok();

    if !ctx.json {
        eprintln!();
        let bolt = if output::supports_unicode() { "⚡" } else { "*" };
        let bar = if output::supports_unicode() { "│" } else { "|" };
        eprintln!(
            "  {} Mount local UCP bridge into:",
            style(bolt).cyan().bold()
        );
        eprintln!("  {} {}", style(bar).dim(), project_path.display());
        eprintln!("  {} {}", style(bar).dim(), source_path.display());
        eprintln!();

        let confirm = Confirm::with_theme(&ColorfulTheme::default())
            .with_prompt("Proceed with local bridge mount?")
            .default(true)
            .interact()?;

        if !confirm {
            output::print_warn("Installation cancelled");
            return Ok(());
        }
    }

    let same_source = is_same_package_source(&mount_path, &source_path)?;
    if !same_source {
        replace_embedded_mount(project_path, &mount_path, &source_path)?;
    }

    let removed_local_manifest_ref = remove_local_file_dependency(&manifest_path)?;

    ensure_ucp_state_dir(project_path)?;
    ensure_local_git_exclude(project_path, ".ucp/")?;
    ensure_local_git_exclude(project_path, "Packages/com.ucp.bridge/")?;
    write_install_state(project_path, InstallState {
        mode: "embedded-local".to_string(),
        source: Some(source_path.display().to_string()),
    })?;

    if let Some(lock) = previous_lock.as_ref() {
        let _ = request_asset_refresh(lock).await;
    }

    if no_wait {
        if ctx.json {
            output::print_json(&output::success_json(serde_json::json!({
                "installed": true,
                "updated": !same_source,
                "bridge": source_path.display().to_string(),
                "project": project_path.display().to_string(),
                "installationMode": "embedded-local",
                "bridgeStatus": "skipped",
                "nudgedEditor": false,
            })));
        } else {
            output::print_success(if same_source {
                "Local UCP bridge mount already present"
            } else {
                "Local UCP bridge mounted successfully"
            });
            if removed_local_manifest_ref {
                eprintln!(
                    "  {} Removed the existing tracked local file dependency from Packages/manifest.json.",
                    style("ℹ").cyan()
                );
            }
            eprintln!(
                "  {} Skipped automatic bridge wait. Run {} when Unity finishes importing.",
                style("ℹ").cyan(),
                style("ucp connect").bold()
            );
            eprintln!();
        }
        return Ok(());
    }

    let wait_outcome = bridge_lifecycle::wait_for_bridge(
        project_path,
        previous_lock.as_ref(),
        ctx.timeout.max(90),
        if previous_lock.is_some() {
            WaitMode::RestartOptional
        } else {
            WaitMode::FirstAvailable
        },
    )
    .await?;

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "installed": true,
            "updated": !same_source,
            "bridge": source_path.display().to_string(),
            "project": project_path.display().to_string(),
            "installationMode": "embedded-local",
            "bridgeStatus": match wait_outcome.status {
                WaitStatus::Available => "available",
                WaitStatus::Restarted => "restarted",
                WaitStatus::Stable => "stable",
                WaitStatus::EditorNotRunning => "editor-not-running",
            },
            "nudgedEditor": wait_outcome.nudged_editor,
        })));
    } else {
        output::print_success(if same_source {
            "Local UCP bridge mount already present"
        } else {
            "Local UCP bridge mounted successfully"
        });
        if wait_outcome.nudged_editor {
            eprintln!(
                "  {} Brought Unity to the foreground to trigger package import.",
                style("ℹ").cyan()
            );
        }
        if removed_local_manifest_ref {
            eprintln!(
                "  {} Removed the existing tracked local file dependency from Packages/manifest.json.",
                style("ℹ").cyan()
            );
        }
        eprintln!(
            "  {} Local-only mount: {}",
            style("ℹ").cyan(),
            mount_path.display()
        );
        eprintln!(
            "  {} Source package: {}",
            style("ℹ").cyan(),
            source_path.display()
        );
        eprintln!(
            "  {} This local mount is ignored via .git/info/exclude when available.",
            style("ℹ").cyan()
        );
        eprintln!();
    }

    Ok(())
}

fn embedded_package_mount(project_path: &Path) -> PathBuf {
    project_path.join("Packages").join(PACKAGE_NAME)
}

fn is_same_package_source(mount_path: &Path, source_path: &Path) -> anyhow::Result<bool> {
    if !mount_path.exists() {
        return Ok(false);
    }

    let mount = normalize_windows_path(
        fs::canonicalize(mount_path)
            .map_err(|e| anyhow::anyhow!("Failed to resolve embedded package mount {}: {e}", mount_path.display()))?,
    );

    Ok(mount == source_path)
}

fn replace_embedded_mount(project_path: &Path, mount_path: &Path, source_path: &Path) -> anyhow::Result<()> {
    if mount_path.exists() {
        if !mount_is_owned_by_ucp(project_path)? {
            anyhow::bail!(
                "Embedded package path {} already exists and is not a managed UCP local mount. Remove it manually or use --manifest/--bridge-ref.",
                mount_path.display()
            );
        }

        remove_mount_link(mount_path)?;
    }

    create_mount_link(mount_path, source_path)
}

fn remove_mount_link(mount_path: &Path) -> anyhow::Result<()> {
    fs::remove_dir(mount_path)
        .or_else(|_| fs::remove_file(mount_path))
        .map_err(|e| anyhow::anyhow!("Failed to remove existing embedded package mount {}: {e}", mount_path.display()))
}

fn create_mount_link(mount_path: &Path, source_path: &Path) -> anyhow::Result<()> {
    let parent = mount_path
        .parent()
        .ok_or_else(|| anyhow::anyhow!("Invalid mount path {}", mount_path.display()))?;
    fs::create_dir_all(parent)?;

    #[cfg(windows)]
    {
        let mount = mount_path.display().to_string().replace('\'', "''");
        let source = source_path.display().to_string().replace('\'', "''");
        let script = format!(
            "New-Item -ItemType Junction -Path '{mount}' -Target '{source}' | Out-Null"
        );
        let status = Command::new("powershell")
            .args(["-NoProfile", "-Command", &script])
            .status()
            .map_err(|e| anyhow::anyhow!("Failed to create embedded package junction: {e}"))?;

        if !status.success() {
            anyhow::bail!(
                "Failed to create embedded package junction at {}",
                mount_path.display()
            );
        }
    }

    #[cfg(unix)]
    {
        std::os::unix::fs::symlink(source_path, mount_path)
            .map_err(|e| anyhow::anyhow!("Failed to create embedded package symlink: {e}"))?;
    }

    Ok(())
}

fn ensure_ucp_state_dir(project_path: &Path) -> anyhow::Result<()> {
    let ucp_dir = project_path.join(".ucp");
    if !ucp_dir.exists() {
        fs::create_dir_all(&ucp_dir)?;
    }
    Ok(())
}

fn ensure_local_git_exclude(project_path: &Path, entry: &str) -> anyhow::Result<()> {
    let exclude_path = project_path.join(".git").join("info").join("exclude");
    let Some(parent) = exclude_path.parent() else {
        return Ok(());
    };

    if !parent.exists() {
        return Ok(());
    }

    let mut content = fs::read_to_string(&exclude_path).unwrap_or_default();
    if content.lines().any(|line| line.trim() == entry) {
        return Ok(());
    }

    if !content.is_empty() && !content.ends_with('\n') {
        content.push('\n');
    }
    content.push_str(entry);
    content.push('\n');
    fs::write(exclude_path, content)?;
    Ok(())
}

pub async fn uninstall(ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let mount_path = embedded_package_mount(&project);

    if mount_path.exists() && mount_is_owned_by_ucp(&project)? {
            remove_mount_link(&mount_path)?;
            clear_install_state(&project)?;

            if ctx.json {
                output::print_json(&output::success_json(serde_json::json!({
                    "uninstalled": true,
                    "installationMode": "embedded-local"
                })));
            } else {
                output::print_success("Local UCP bridge mount removed");
            }

            return Ok(());
    }

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

    clear_install_state(&project)?;

    Ok(())
}

fn install_state_path(project_path: &Path) -> PathBuf {
    project_path.join(".ucp").join(INSTALL_STATE_FILE)
}

fn read_install_state(project_path: &Path) -> anyhow::Result<Option<InstallState>> {
    let path = install_state_path(project_path);
    if !path.is_file() {
        return Ok(None);
    }

    let content = fs::read_to_string(&path)?;
    Ok(Some(serde_json::from_str(&content)?))
}

fn write_install_state(project_path: &Path, state: InstallState) -> anyhow::Result<()> {
    ensure_ucp_state_dir(project_path)?;
    let path = install_state_path(project_path);
    fs::write(path, format!("{}\n", serde_json::to_string_pretty(&state)?))?;
    Ok(())
}

fn clear_install_state(project_path: &Path) -> anyhow::Result<()> {
    let path = install_state_path(project_path);
    if path.exists() {
        fs::remove_file(path)?;
    }
    Ok(())
}

fn mount_is_owned_by_ucp(project_path: &Path) -> anyhow::Result<bool> {
    Ok(matches!(
        read_install_state(project_path)?,
        Some(state) if state.mode == "embedded-local"
    ))
}

fn remove_owned_embedded_mount_if_present(project_path: &Path) -> anyhow::Result<()> {
    let mount_path = embedded_package_mount(project_path);
    if mount_path.exists() && mount_is_owned_by_ucp(project_path)? {
        remove_mount_link(&mount_path)?;
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

fn remove_local_file_dependency(manifest_path: &Path) -> anyhow::Result<bool> {
    let content = fs::read_to_string(manifest_path)?;
    let mut manifest: serde_json::Value = serde_json::from_str(&content)?;

    let should_remove = manifest
        .get("dependencies")
        .and_then(|v| v.as_object())
        .and_then(|deps| deps.get(PACKAGE_NAME))
        .and_then(|v| v.as_str())
        .map(|value| value.starts_with("file:"))
        .unwrap_or(false);

    if !should_remove {
        return Ok(false);
    }

    if let Some(deps) = manifest.get_mut("dependencies").and_then(|v| v.as_object_mut()) {
        deps.remove(PACKAGE_NAME);
    }

    let out = serde_json::to_string_pretty(&manifest)?;
    fs::write(manifest_path, format!("{out}\n"))?;
    Ok(true)
}

async fn request_asset_refresh(lock: &crate::config::LockFile) -> anyhow::Result<()> {
    let mut client = BridgeClient::connect(lock).await?;
    client.handshake().await?;
    let _ = client
        .call("refresh-assets", serde_json::json!({}))
        .await?;
    client.close().await;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{
        InstallOptions, canonicalize_package_dir, desired_package_reference,
        file_package_reference, read_install_state, remove_local_file_dependency,
        should_refresh_existing_custom_source, uses_embedded_local_package, write_install_state,
        InstallState,
    };
    use std::fs;
    use std::path::PathBuf;

    #[test]
    fn explicit_bridge_ref_wins() {
        let reference = desired_package_reference(&InstallOptions {
            bridge_ref: Some("file:../bridge".to_string()),
            ..InstallOptions::default()
        })
        .unwrap();

        assert_eq!(reference, "file:../bridge");
    }

    #[test]
    fn file_reference_uses_forward_slashes() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-install-test-{}",
            std::process::id()
        ));
        let package_dir = temp_root.join("com.ucp.bridge");
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(&package_dir).unwrap();
        fs::write(package_dir.join("package.json"), "{}\n").unwrap();

        let reference = file_package_reference(PathBuf::from(&package_dir)).unwrap();
        assert!(reference.starts_with("file:"));
        assert!(!reference.contains('\\'));
        assert!(!reference.contains("//?/"));

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn refreshes_same_custom_source_even_when_bridge_is_down() {
        assert!(should_refresh_existing_custom_source(true, true));
        assert!(!should_refresh_existing_custom_source(true, false));
        assert!(!should_refresh_existing_custom_source(false, true));
    }

    #[test]
    fn dev_and_bridge_path_use_embedded_local_mount() {
        assert!(uses_embedded_local_package(&InstallOptions {
            dev: true,
            ..InstallOptions::default()
        }));
        assert!(uses_embedded_local_package(&InstallOptions {
            embedded: true,
            ..InstallOptions::default()
        }));
        assert!(uses_embedded_local_package(&InstallOptions {
            bridge_path: Some("../bridge".to_string()),
            ..InstallOptions::default()
        }));
        assert!(!uses_embedded_local_package(&InstallOptions {
            bridge_ref: Some("file:../bridge".to_string()),
            ..InstallOptions::default()
        }));
    }

    #[test]
    fn canonicalize_package_dir_requires_package_json() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-install-canonicalize-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(&temp_root).unwrap();

        assert!(canonicalize_package_dir(temp_root.clone()).is_err());

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn install_state_round_trip() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-install-state-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(temp_root.join(".ucp")).unwrap();

        write_install_state(&temp_root, InstallState {
            mode: "embedded-local".to_string(),
            source: Some("C:/bridge".to_string()),
        })
        .unwrap();

        let state = read_install_state(&temp_root).unwrap().unwrap();
        assert_eq!(state.mode, "embedded-local");
        assert_eq!(state.source.as_deref(), Some("C:/bridge"));

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn removes_local_file_dependency_from_manifest() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-install-manifest-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(&temp_root).unwrap();
        let manifest_path = temp_root.join("manifest.json");
        fs::write(
            &manifest_path,
            "{\n  \"dependencies\": {\n    \"com.ucp.bridge\": \"file:C:/temp/com.ucp.bridge\",\n    \"com.foo.bar\": \"1.0.0\"\n  }\n}\n",
        )
        .unwrap();

        assert!(remove_local_file_dependency(&manifest_path).unwrap());
        let manifest = fs::read_to_string(&manifest_path).unwrap();
        assert!(!manifest.contains("file:C:/temp/com.ucp.bridge"));
        assert!(manifest.contains("com.foo.bar"));

        let _ = fs::remove_dir_all(&temp_root);
    }
}
