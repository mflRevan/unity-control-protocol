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
    pub log_path: String,
    pub session: Option<EditorSession>,
}

pub async fn ensure_editor_running(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<EditorStartOutcome> {
    if let Some(pid) = discovery::unity_editor_pid_for_project(project) {
        let process = discovery::list_running_unity_editors()
            .into_iter()
            .find(|process| process.pid == pid);

        let outcome = EditorStartOutcome {
            started: false,
            already_running: true,
            pid: Some(pid),
            executable_path: process
                .and_then(|process| process.executable_path)
                .map(|path| path.display().to_string()),
            log_path: config::editor_log_path(project).display().to_string(),
        };

        persist_session(project, &outcome)?;
        return Ok(outcome);
    }

    start_editor(project, ctx).await
}

pub async fn start_editor(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<EditorStartOutcome> {
    let unity_path = resolve_unity_executable(project, ctx)?;
    let log_path = config::editor_log_path(project);
    ensure_log_dir(project)?;

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
    })
}

pub fn status(project: &Path, ctx: &commands::Context) -> EditorStatus {
    let process = discovery::unity_editor_pid_for_project(project).and_then(|pid| {
        discovery::list_running_unity_editors()
            .into_iter()
            .find(|process| process.pid == pid)
    });

    let (resolved_unity_path, resolution_error) = match resolve_unity_executable(project, ctx) {
        Ok(path) => (Some(path.display().to_string()), None),
        Err(error) => (None, Some(format!("{error:#}"))),
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
        project_version: read_project_version(project).ok().flatten(),
        log_path: config::editor_log_path(project).display().to_string(),
        session: read_session(project).ok().flatten(),
    }
}

pub fn resolve_unity_executable(
    project: &Path,
    ctx: &commands::Context,
) -> anyhow::Result<PathBuf> {
    if let Some(path) = ctx.unity.as_ref() {
        let candidate = PathBuf::from(path);
        if candidate.is_file() {
            return Ok(candidate);
        }

        anyhow::bail!("Configured Unity executable was not found at {}", candidate.display());
    }

    let project_version = read_project_version(project).ok().flatten();
    let candidates = unity_candidate_paths(project_version.as_deref());
    if let Some(path) = candidates.iter().find(|path| path.is_file()) {
        return Ok(path.clone());
    }

    let checked = candidates
        .iter()
        .map(|path| path.display().to_string())
        .collect::<Vec<_>>()
        .join(", ");

    anyhow::bail!(
        "Unable to locate a Unity editor executable for this project. Checked: {}. Set --unity or UCP_UNITY to override.",
        checked
    );
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

fn unity_candidate_paths(project_version: Option<&str>) -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if let Some(version) = project_version {
        for root in [std::env::var_os("ProgramFiles"), std::env::var_os("ProgramFiles(x86)")] {
            let Some(root) = root else {
                continue;
            };
            let root = PathBuf::from(root);
            candidates.push(root.join("Unity").join("Hub").join("Editor").join(version).join("Editor").join(unity_executable_name()));
            candidates.push(root.join("Unity Hub").join("Editor").join(version).join("Editor").join(unity_executable_name()));
            candidates.push(root.join("Unity").join(version).join("Editor").join(unity_executable_name()));
        }
    }

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