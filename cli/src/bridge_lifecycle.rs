use std::fs;
use std::path::Path;
use std::time::{Duration, Instant};

use crate::client::BridgeClient;
use crate::config::LockFile;
use crate::config::StartupDialogPolicy;
use crate::discovery;
use crate::output;
use serde::Deserialize;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WaitMode {
    FirstAvailable,
    RestartOptional,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WaitStatus {
    Available,
    Restarted,
    Stable,
    EditorNotRunning,
}

#[derive(Debug, Clone)]
pub struct BridgeWaitOutcome {
    pub status: WaitStatus,
    pub nudged_editor: bool,
    pub handled_dialogs: Vec<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EditorSettleStatus {
    Settled,
    BridgeRestarted,
    EditorNotRunning,
}

#[derive(Debug, Clone)]
pub struct EditorSettleOutcome {
    pub status: EditorSettleStatus,
}

#[derive(Debug, Deserialize)]
struct EditorStatusResponse {
    compiling: bool,
    updating: bool,
}

pub async fn wait_for_bridge(
    project: &Path,
    previous_lock: Option<&LockFile>,
    timeout_secs: u64,
    dialog_policy: StartupDialogPolicy,
    mode: WaitMode,
) -> anyhow::Result<BridgeWaitOutcome> {
    if !discovery::is_unity_editor_running_for_project(project) {
        return Ok(BridgeWaitOutcome {
            status: WaitStatus::EditorNotRunning,
            nudged_editor: false,
            handled_dialogs: Vec::new(),
        });
    }

    let max_wait = timeout_secs.max(5);
    let stable_after = Duration::from_secs(8);
    let start = Instant::now();
    let previous_token = previous_lock.map(|lock| lock.token.as_str());
    let mut bridge_went_down = false;
    let mut nudged_editor = discovery::focus_unity_editor(project).unwrap_or(false);
    let mut last_nudge = start;
    let mut handled_dialogs = Vec::new();

    tokio::time::sleep(Duration::from_secs(2)).await;

    loop {
        if start.elapsed().as_secs() > max_wait {
            maybe_report_editor_log_failure(project);
            let expectation = match mode {
                WaitMode::FirstAvailable => "Bridge did not become available",
                WaitMode::RestartOptional => "Bridge did not stabilize",
            };
            anyhow::bail!("{expectation} after waiting {max_wait}s");
        }

        match discovery::read_lock_file(project) {
            Ok(lock) => match BridgeClient::connect(&lock).await {
                Ok(mut client) => {
                    if client.handshake().await.is_ok() {
                        client.close().await;

                        let token_changed = previous_token
                            .map(|token| lock.token != token)
                            .unwrap_or(true);

                        let status = match mode {
                            WaitMode::FirstAvailable => Some(WaitStatus::Available),
                            WaitMode::RestartOptional => {
                                if token_changed || bridge_went_down {
                                    Some(WaitStatus::Restarted)
                                } else if start.elapsed() >= stable_after {
                                    Some(WaitStatus::Stable)
                                } else {
                                    None
                                }
                            }
                        };

                        if let Some(status) = status {
                            return Ok(BridgeWaitOutcome {
                                status,
                                nudged_editor,
                                handled_dialogs,
                            });
                        }
                    } else {
                        bridge_went_down = true;
                    }
                }
                Err(_) => {
                    bridge_went_down = true;
                }
            },
            Err(_) => {
                bridge_went_down = true;
            }
        }

        if let Ok(newly_handled) = discovery::handle_unity_startup_dialogs(project, dialog_policy) {
            for handled in newly_handled {
                if !handled_dialogs.contains(&handled) {
                    handled_dialogs.push(handled);
                }
            }
        }

        if last_nudge.elapsed() >= Duration::from_secs(10) {
            nudged_editor =
                discovery::focus_unity_editor(project).unwrap_or(false) || nudged_editor;
            last_nudge = Instant::now();
        }

        tokio::time::sleep(Duration::from_secs(2)).await;
    }
}

fn maybe_report_editor_log_failure(project: &Path) {
    let log_path = crate::config::editor_log_path(project);
    let Ok(content) = fs::read_to_string(&log_path) else {
        return;
    };

    let last_lines = content
        .lines()
        .rev()
        .take(200)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect::<Vec<_>>();

    let has_blocker = last_lines.iter().any(|line| {
        let lower = line.to_ascii_lowercase();
        (lower.contains("project has invalid dependencies")
            || lower.contains("an error occurred while resolving packages")
            || lower.contains("error cs"))
            && !lower.contains("[ucp] error handling")
    });

    if !has_blocker {
        return;
    }

    output::print_warn("Unity editor log shows startup-blocking errors:");
    eprintln!("{}", last_lines.join("\n"));
}

/// Wait until Unity stops reporting editor-side background work such as
/// compilation or asset-database updates. This is the standard readiness
/// primitive for post-mutation command flows, including cases where the bridge
/// temporarily disappears during a domain reload and the editor must be nudged
/// back into foreground processing.
pub async fn wait_for_editor_settle(
    project: &Path,
    previous_lock: Option<&LockFile>,
    timeout_secs: u64,
    dialog_policy: StartupDialogPolicy,
) -> anyhow::Result<EditorSettleOutcome> {
    if !discovery::is_unity_editor_running_for_project(project) {
        return Ok(EditorSettleOutcome {
            status: EditorSettleStatus::EditorNotRunning,
        });
    }

    let max_wait = timeout_secs.max(5);
    let stable_after = Duration::from_millis(750);
    let poll_interval = Duration::from_millis(500);
    let start = Instant::now();
    let previous_token = previous_lock.map(|lock| lock.token.as_str());
    let mut bridge_went_down = false;
    let mut nudged_editor = discovery::focus_unity_editor(project).unwrap_or(false);
    let mut last_nudge = start;
    let mut handled_dialogs = Vec::new();
    let mut stable_since: Option<Instant> = None;

    loop {
        if start.elapsed().as_secs() > max_wait {
            anyhow::bail!("Unity editor did not settle after waiting {max_wait}s");
        }

        if !discovery::is_unity_editor_running_for_project(project) {
            return Ok(EditorSettleOutcome {
                status: EditorSettleStatus::EditorNotRunning,
            });
        }

        let mut editor_is_busy = true;

        match discovery::read_lock_file(project) {
            Ok(lock) => match BridgeClient::connect(&lock).await {
                Ok(mut client) => {
                    if client.handshake().await.is_ok() {
                        let token_changed = previous_token
                            .map(|token| lock.token != token)
                            .unwrap_or(false);
                        bridge_went_down |= token_changed;

                        match client.call("editor/status", serde_json::json!({})).await {
                            Ok(result) => {
                                let status: EditorStatusResponse = serde_json::from_value(result)
                                    .map_err(|err| {
                                    anyhow::anyhow!("Invalid editor/status payload: {err}")
                                })?;
                                editor_is_busy = status.compiling || status.updating;
                            }
                            Err(_) => {
                                bridge_went_down = true;
                                editor_is_busy = true;
                            }
                        }
                    } else {
                        bridge_went_down = true;
                    }
                    client.close().await;
                }
                Err(_) => {
                    bridge_went_down = true;
                }
            },
            Err(_) => {
                bridge_went_down = true;
            }
        }

        if !editor_is_busy {
            stable_since.get_or_insert_with(Instant::now);
            if stable_since
                .map(|stable_start| stable_start.elapsed() >= stable_after)
                .unwrap_or(false)
            {
                return Ok(EditorSettleOutcome {
                    status: if bridge_went_down {
                        EditorSettleStatus::BridgeRestarted
                    } else {
                        EditorSettleStatus::Settled
                    },
                });
            }
        } else {
            stable_since = None;
        }

        if let Ok(newly_handled) = discovery::handle_unity_startup_dialogs(project, dialog_policy) {
            for handled in newly_handled {
                if !handled_dialogs.contains(&handled) {
                    handled_dialogs.push(handled);
                }
            }
        }

        if last_nudge.elapsed() >= Duration::from_secs(2) {
            nudged_editor =
                discovery::focus_unity_editor(project).unwrap_or(false) || nudged_editor;
            last_nudge = Instant::now();
        }

        tokio::time::sleep(poll_interval).await;
    }
}
