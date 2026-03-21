use std::path::Path;
use std::time::{Duration, Instant};

use crate::client::BridgeClient;
use crate::config::LockFile;
use crate::config::StartupDialogPolicy;
use crate::discovery;

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
