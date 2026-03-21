use clap::ValueEnum;
use directories::ProjectDirs;
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

pub const PROTOCOL_VERSION: &str = "0.4.1";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, ValueEnum, Default)]
#[serde(rename_all = "lowercase")]
pub enum BridgeUpdatePolicy {
    #[default]
    Auto,
    Warn,
    Off,
}

impl std::fmt::Display for BridgeUpdatePolicy {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let value = match self {
            BridgeUpdatePolicy::Auto => "auto",
            BridgeUpdatePolicy::Warn => "warn",
            BridgeUpdatePolicy::Off => "off",
        };

        write!(f, "{value}")
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, ValueEnum, Default)]
#[serde(rename_all = "kebab-case")]
pub enum StartupDialogPolicy {
    #[default]
    Auto,
    Manual,
    Ignore,
    Recover,
    SafeMode,
    Cancel,
}

impl std::fmt::Display for StartupDialogPolicy {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let value = match self {
            StartupDialogPolicy::Auto => "auto",
            StartupDialogPolicy::Manual => "manual",
            StartupDialogPolicy::Ignore => "ignore",
            StartupDialogPolicy::Recover => "recover",
            StartupDialogPolicy::SafeMode => "safe-mode",
            StartupDialogPolicy::Cancel => "cancel",
        };

        write!(f, "{value}")
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct CliSettings {
    #[serde(rename = "bridgeUpdatePolicy", default)]
    pub bridge_update_policy: Option<BridgeUpdatePolicy>,
    #[serde(rename = "unityPath", default)]
    pub unity_path: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct LockFile {
    pub pid: u32,
    pub port: u16,
    #[serde(rename = "protocolVersion")]
    pub protocol_version: String,
    #[serde(rename = "unityVersion")]
    pub unity_version: String,
    #[serde(rename = "projectPath")]
    pub project_path: String,
    #[serde(rename = "startedAt")]
    pub started_at: String,
    pub token: String,
}

pub fn ucp_dir(project: &Path) -> PathBuf {
    project.join(".ucp")
}

pub fn editor_logs_dir(project: &Path) -> PathBuf {
    ucp_dir(project).join("logs")
}

pub fn editor_log_path(project: &Path) -> PathBuf {
    editor_logs_dir(project).join("editor.log")
}

pub fn editor_session_path(project: &Path) -> PathBuf {
    ucp_dir(project).join("editor-session.json")
}

pub fn lock_file_path(project: &Path) -> PathBuf {
    ucp_dir(project).join("bridge.lock")
}

pub fn cli_settings_path() -> Option<PathBuf> {
    ProjectDirs::from("io", "mflrevan", "ucp").map(|dirs| dirs.config_dir().join("settings.json"))
}

pub fn load_cli_settings() -> CliSettings {
    let Some(path) = cli_settings_path() else {
        return CliSettings::default();
    };

    let Ok(content) = std::fs::read_to_string(path) else {
        return CliSettings::default();
    };

    serde_json::from_str(&content).unwrap_or_default()
}
