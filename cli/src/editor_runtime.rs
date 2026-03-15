use crate::client::BridgeClient;
use crate::commands;
use crate::config;
use crate::discovery;
use crate::output;
use chrono::Utc;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{Duration, Instant};

#[derive(Debug, Clone)]
struct UnityLaunchTarget {
    path: PathBuf,
    requested_version: Option<String>,
    warning: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorSession {
    pub pid: u32,
    pub project_path: String,
    pub executable_path: Option<String>,
    pub log_path: String,
    pub started_at: String,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorStartOutcome {
    pub started: bool,
    pub already_running: bool,
    pub pid: Option<u32>,
    pub executable_path: Option<String>,
    pub log_path: String,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorCloseOutcome {
    pub was_running: bool,
    pub pid: Option<u32>,
    pub graceful: bool,
    pub forced: bool,
    pub via_bridge: bool,
    pub exited: bool,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EditorStatus {
    pub running: bool,
    pub pid: Option<u32>,
    pub executable_path: Option<String>,
    pub resolved_unity_path: Option<String>,
    pub resolution_error: Option<String>,
    pub project_version: Option<String>,
    pub requested_version: Option<String>,
    pub installed_versions: Vec<String>,
    pub resolution_warning: Option<String>,
    pub log_path: String,
    pub session: Option<EditorSession>,
}

pub async fn ensure_editor_running(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<EditorStartOutcome> {
    if let Some(mut pid) = discovery::unity_editor_pid_for_project(project) {
        let wait_deadline = Instant::now() + Duration::from_secs(ctx.timeout.max(15).min(120));

        loop {
            if !discovery::is_process_running(pid) {
                clear_session(project)?;
                break;
            }

            if bridge_is_available(project).await {
                let outcome = already_running_outcome(project, pid);
                persist_session(project, &outcome)?;
                return Ok(outcome);
            }

            if Instant::now() >= wait_deadline {
                anyhow::bail!(
                    "Unity editor process {pid} for {} is still running without a live bridge. It is likely still closing or stuck. Wait a moment and retry, or run `ucp editor close --force`.",
                    project.display()
                );
            }

            tokio::time::sleep(Duration::from_secs(1)).await;

            if let Some(current_pid) = discovery::unity_editor_pid_for_project(project) {
                pid = current_pid;
            } else {
                clear_session(project)?;
                break;
            }
        }
    }

    start_editor(project, ctx).await
}

pub async fn start_editor(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<EditorStartOutcome> {
    let launch_target = resolve_unity_launch_target(project, ctx)?;
    let unity_path = launch_target.path.clone();
    let log_path = config::editor_log_path(project);
    ensure_log_dir(project)?;

    if let Some(warning) = launch_target.warning.as_deref() {
        if !ctx.json {
            output::print_warn(warning);
        }
    }

    if !ctx.json {
        output::print_info(&format!(
            "Launching Unity editor with {}...",
            unity_path.display()
        ));
    }

    let mut command = Command::new(&unity_path);
    command
        .arg("-projectPath")
        .arg(project)
        .arg("-logFile")
        .arg(&log_path);

    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;

        const DETACHED_PROCESS: u32 = 0x0000_0008;
        const CREATE_NEW_PROCESS_GROUP: u32 = 0x0000_0200;
        command.creation_flags(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP);
    }

    let child = command.spawn().map_err(|error| {
        anyhow::anyhow!(
            "Failed to launch Unity editor {}: {error}",
            unity_path.display()
        )
    })?;

    let mut pid = Some(child.id());
    let wait_deadline = Instant::now() + Duration::from_secs(ctx.timeout.max(15).min(45));
    while Instant::now() < wait_deadline {
        if let Some(actual_pid) = discovery::unity_editor_pid_for_project(project) {
            pid = Some(actual_pid);
            break;
        }

        tokio::time::sleep(Duration::from_secs(1)).await;
    }

    let outcome = EditorStartOutcome {
        started: true,
        already_running: false,
        pid,
        executable_path: Some(unity_path.display().to_string()),
        log_path: log_path.display().to_string(),
    };

    persist_session(project, &outcome)?;
    Ok(outcome)
}

pub async fn close_editor(
    project: &Path,
    ctx: &commands::Context,
    force: bool,
) -> anyhow::Result<EditorCloseOutcome> {
    let pid = discovery::unity_editor_pid_for_project(project);
    let Some(pid) = pid else {
        clear_session(project)?;
        return Ok(EditorCloseOutcome {
            was_running: false,
            pid: None,
            graceful: false,
            forced: false,
            via_bridge: false,
            exited: true,
        });
    };

    let mut via_bridge = false;
    let mut graceful = false;

    if let Ok(lock) = discovery::read_lock_file(project) {
        if let Ok(mut client) = BridgeClient::connect(&lock).await {
            if client.handshake().await.is_ok() && client.call("editor/quit", serde_json::json!({})).await.is_ok() {
                via_bridge = true;
                graceful = true;
            }
            client.close().await;
        }
    }

    if !graceful {
        graceful = discovery::request_unity_editor_close(project).unwrap_or(false);
    }

    let wait_deadline = Instant::now() + Duration::from_secs(ctx.timeout.max(10).min(30));
    while Instant::now() < wait_deadline {
        if !discovery::is_process_running(pid) {
            clear_session(project)?;
            return Ok(EditorCloseOutcome {
                was_running: true,
                pid: Some(pid),
                graceful,
                forced: false,
                via_bridge,
                exited: true,
            });
        }

        tokio::time::sleep(Duration::from_millis(500)).await;
    }

    let forced = if force || !graceful {
        discovery::terminate_process(pid).unwrap_or(false)
    } else {
        false
    };

    if forced {
        let forced_deadline = Instant::now() + Duration::from_secs(10);
        while Instant::now() < forced_deadline {
            if !discovery::is_process_running(pid) {
                clear_session(project)?;
                return Ok(EditorCloseOutcome {
                    was_running: true,
                    pid: Some(pid),
                    graceful,
                    forced: true,
                    via_bridge,
                    exited: true,
                });
            }
            tokio::time::sleep(Duration::from_millis(500)).await;
        }
    }

    Ok(EditorCloseOutcome {
        was_running: true,
        pid: Some(pid),
        graceful,
        forced,
        via_bridge,
        exited: false,
    })
}

pub fn status(project: &Path, ctx: &commands::Context) -> EditorStatus {
    let process = discovery::unity_editor_pid_for_project(project).and_then(|pid| {
        discovery::list_running_unity_editors()
            .into_iter()
            .find(|process| process.pid == pid)
    });

    let roots = unity_install_roots();
    let installed_versions = installed_unity_versions(&roots);

    let (resolved_unity_path, resolution_error, requested_version, resolution_warning) =
        match resolve_unity_launch_target(project, ctx) {
            Ok(target) => (
                Some(target.path.display().to_string()),
                None,
                target.requested_version,
                target.warning,
            ),
            Err(error) => (
                None,
                Some(format!("{error:#}")),
                resolve_requested_unity_version(project, ctx),
                None,
            ),
    };

    EditorStatus {
        running: process.is_some(),
        pid: process.as_ref().map(|process| process.pid),
        executable_path: process
            .as_ref()
            .and_then(|process| process.executable_path.as_ref())
            .map(|path| path.display().to_string()),
        resolved_unity_path,
        resolution_error,
        project_version: resolve_project_unity_version(project),
        requested_version,
        installed_versions,
        resolution_warning,
        log_path: config::editor_log_path(project).display().to_string(),
        session: read_session(project).ok().flatten(),
    }
}

fn resolve_unity_launch_target(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<UnityLaunchTarget> {
    let project_version = resolve_project_unity_version(project);
    let requested_version = ctx
        .force_unity_version
        .clone()
        .or(project_version.clone());
    let warning = forced_version_warning(project_version.as_deref(), ctx.force_unity_version.as_deref());
    let roots = unity_install_roots();
    let installed_versions = installed_unity_versions(&roots);

    if let Some(path) = ctx.unity.as_ref() {
        let candidate = PathBuf::from(path);
        if candidate.is_file() {
            return Ok(UnityLaunchTarget {
                path: candidate,
                requested_version,
                warning,
            });
        }

        anyhow::bail!("Configured Unity executable was not found at {}", candidate.display());
    }

    if let Some(version) = requested_version.as_deref() {
        let candidates = unity_candidate_paths(version, &roots);
        if let Some(path) = candidates.iter().find(|path| path.is_file()) {
            return Ok(UnityLaunchTarget {
                path: path.clone(),
                requested_version,
                warning,
            });
        }

        let installed_display = format_installed_versions(&installed_versions);
        if ctx.force_unity_version.is_some() {
            anyhow::bail!(
                "Forced Unity editor version {version} is not installed. Project expects {}. Installed versions: {}. Forcing a different editor version can upgrade project metadata or assets; create a backup or commit your work first.",
                project_version.as_deref().unwrap_or("unknown"),
                installed_display,
            );
        }

        anyhow::bail!(
            "Project expects Unity {version}, but that editor is not installed. Installed versions: {}. Use --force-unity-version <version> to override. Opening a project with a different Unity editor can upgrade project metadata or assets; create a backup or commit your work first.",
            installed_display,
        );
    }

    let candidates = unity_path_fallbacks();
    if let Some(path) = candidates.iter().find(|path| path.is_file()) {
        return Ok(UnityLaunchTarget {
            path: path.clone(),
            requested_version,
            warning,
        });
    }

    let checked = candidates
        .iter()
        .map(|path| path.display().to_string())
        .collect::<Vec<_>>()
        .join(", ");
    let installed_display = format_installed_versions(&installed_versions);

    anyhow::bail!(
        "Unable to locate a Unity editor executable for this project. Checked: {}. Installed versions: {}. Set --unity or --force-unity-version to override.",
        checked,
        installed_display,
    );
}

fn resolve_requested_unity_version(project: &Path, ctx: &commands::Context) -> Option<String> {
    ctx.force_unity_version
        .clone()
        .or_else(|| resolve_project_unity_version(project))
}

fn resolve_project_unity_version(project: &Path) -> Option<String> {
    read_project_version(project)
        .ok()
        .flatten()
        .or_else(|| read_hub_project_version(project))
}

pub fn read_project_version(project: &Path) -> anyhow::Result<Option<String>> {
    let path = project.join("ProjectSettings").join("ProjectVersion.txt");
    if !path.is_file() {
        return Ok(None);
    }

    let content = fs::read_to_string(path)?;
    Ok(content.lines().find_map(|line| {
        line.strip_prefix("m_EditorVersion:")
            .map(|value| value.trim().to_string())
    }))
}

fn unity_candidate_paths(version: &str, roots: &[PathBuf]) -> Vec<PathBuf> {
    dedupe_paths(
        roots
            .iter()
            .map(|root| root.join(version).join("Editor").join(unity_executable_name()))
            .collect(),
    )
}

fn unity_path_fallbacks() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if cfg!(windows) {
        if let Ok(output) = Command::new("where").arg("Unity.exe").output() {
            if output.status.success() {
                for line in String::from_utf8_lossy(&output.stdout).lines() {
                    let candidate = PathBuf::from(line.trim());
                    if !line.trim().is_empty() {
                        candidates.push(candidate);
                    }
                }
            }
        }
    }

    candidates.push(PathBuf::from(unity_executable_name()));
    dedupe_paths(candidates)
}

fn unity_install_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();

    #[cfg(windows)]
    {
        if let Some(app_data) = std::env::var_os("APPDATA") {
            let hub_dir = PathBuf::from(app_data).join("UnityHub");

            if let Some(path) = read_hub_path_string(&hub_dir.join("secondaryInstallPath.json")) {
                roots.push(path);
            }
        }

        for root in [std::env::var_os("ProgramFiles"), std::env::var_os("ProgramFiles(x86)")] {
            let Some(root) = root else {
                continue;
            };
            let root = PathBuf::from(root);
            roots.push(root.join("Unity").join("Hub").join("Editor"));
            roots.push(root.join("Unity Hub").join("Editor"));
            roots.push(root.join("Unity"));
        }
    }

    dedupe_paths(roots)
}

fn read_hub_path_string(path: &Path) -> Option<PathBuf> {
    let content = fs::read_to_string(path).ok()?;
    let value: String = serde_json::from_str(&content).ok()?;
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return None;
    }

    Some(PathBuf::from(trimmed))
}

fn read_hub_project_version(project: &Path) -> Option<String> {
    let app_data = std::env::var_os("APPDATA")?;
    let path = PathBuf::from(app_data)
        .join("UnityHub")
        .join("projects-v1.json");
    let content = fs::read_to_string(path).ok()?;
    let json_start = content.find('{')?;
    let value: serde_json::Value = serde_json::from_str(&content[json_start..]).ok()?;
    let project_key = project.display().to_string().replace('/', "\\");
    value
        .get("data")?
        .get(&project_key)?
        .get("version")?
        .as_str()
        .map(ToOwned::to_owned)
}

fn installed_unity_versions(roots: &[PathBuf]) -> Vec<String> {
    let mut versions = Vec::new();

    for root in roots {
        let Ok(entries) = fs::read_dir(root) else {
            continue;
        };

        for entry in entries.flatten() {
            let path = entry.path();
            if !path.is_dir() {
                continue;
            }

            let Some(name) = path.file_name().and_then(|value| value.to_str()) else {
                continue;
            };

            if path.join("Editor").join(unity_executable_name()).is_file()
                && !versions.iter().any(|existing| existing == name)
            {
                versions.push(name.to_string());
            }
        }
    }

    versions.sort();
    versions
}

fn format_installed_versions(versions: &[String]) -> String {
    if versions.is_empty() {
        "none detected".to_string()
    } else {
        versions.join(", ")
    }
}

fn forced_version_warning(
    project_version: Option<&str>,
    forced_version: Option<&str>,
) -> Option<String> {
    let forced_version = forced_version?;
    let project_version = project_version?;
    if forced_version == project_version {
        return None;
    }

    Some(format!(
        "Project is configured for Unity {project_version}, but UCP will launch Unity {forced_version} because --force-unity-version was set. This can upgrade project metadata or assets; create a backup or commit your work first."
    ))
}

fn dedupe_paths(paths: Vec<PathBuf>) -> Vec<PathBuf> {
    let mut unique = Vec::new();
    for path in paths {
        if unique.iter().any(|existing: &PathBuf| existing == &path) {
            continue;
        }
        unique.push(path);
    }
    unique
}

fn unity_executable_name() -> &'static str {
    #[cfg(windows)]
    {
        "Unity.exe"
    }

    #[cfg(not(windows))]
    {
        "Unity"
    }
}

fn ensure_log_dir(project: &Path) -> anyhow::Result<()> {
    fs::create_dir_all(config::editor_logs_dir(project))?;
    Ok(())
}

fn already_running_outcome(project: &Path, pid: u32) -> EditorStartOutcome {
    let process = discovery::list_running_unity_editors()
        .into_iter()
        .find(|process| process.pid == pid);

    EditorStartOutcome {
        started: false,
        already_running: true,
        pid: Some(pid),
        executable_path: process
            .and_then(|process| process.executable_path)
            .map(|path| path.display().to_string()),
        log_path: config::editor_log_path(project).display().to_string(),
    }
}

async fn bridge_is_available(project: &Path) -> bool {
    let Ok(lock) = discovery::read_lock_file(project) else {
        return false;
    };

    let Ok(mut client) = BridgeClient::connect(&lock).await else {
        return false;
    };

    let ready = client.handshake().await.is_ok();
    client.close().await;
    ready
}

fn persist_session(project: &Path, outcome: &EditorStartOutcome) -> anyhow::Result<()> {
    let Some(pid) = outcome.pid else {
        return Ok(());
    };

    fs::create_dir_all(config::ucp_dir(project))?;
    let session = EditorSession {
        pid,
        project_path: project.display().to_string(),
        executable_path: outcome.executable_path.clone(),
        log_path: outcome.log_path.clone(),
        started_at: Utc::now().to_rfc3339(),
    };
    fs::write(
        config::editor_session_path(project),
        format!("{}\n", serde_json::to_string_pretty(&session)?),
    )?;
    Ok(())
}

fn read_session(project: &Path) -> anyhow::Result<Option<EditorSession>> {
    let path = config::editor_session_path(project);
    if !path.is_file() {
        return Ok(None);
    }

    let content = fs::read_to_string(path)?;
    Ok(Some(serde_json::from_str(&content)?))
}

fn clear_session(project: &Path) -> anyhow::Result<()> {
    let path = config::editor_session_path(project);
    if path.exists() {
        fs::remove_file(path)?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{forced_version_warning, installed_unity_versions, read_hub_path_string};
    use std::fs;
    use std::path::PathBuf;

    #[test]
    fn reads_hub_path_json_string() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-hub-path-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(&temp_root).unwrap();
        let path = temp_root.join("secondaryInstallPath.json");
        fs::write(&path, "\"D:\\\\Unity\\\\Installs\"").unwrap();

        assert_eq!(
            read_hub_path_string(&path),
            Some(PathBuf::from("D:\\Unity\\Installs"))
        );

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn discovers_installed_versions_from_roots() {
        let temp_root = std::env::temp_dir().join(format!(
            "ucp-installed-versions-test-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&temp_root);
        let root = temp_root.join("Installs");
        fs::create_dir_all(root.join("6000.3.1f1").join("Editor")).unwrap();
        fs::create_dir_all(root.join("2023.1.7f1").join("Editor")).unwrap();
        fs::write(
            root.join("6000.3.1f1").join("Editor").join("Unity.exe"),
            b"",
        )
        .unwrap();
        fs::write(
            root.join("2023.1.7f1").join("Editor").join("Unity.exe"),
            b"",
        )
        .unwrap();

        let versions = installed_unity_versions(&[root]);
        assert_eq!(versions, vec!["2023.1.7f1", "6000.3.1f1"]);

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn warns_on_forced_version_mismatch() {
        let warning = forced_version_warning(Some("6000.3.1f1"), Some("2023.1.7f1"))
            .expect("warning expected");

        assert!(warning.contains("6000.3.1f1"));
        assert!(warning.contains("2023.1.7f1"));
        assert!(warning.contains("backup"));
    }
}