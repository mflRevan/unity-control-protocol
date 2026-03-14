use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

pub const PROTOCOL_VERSION: &str = "0.3.1";

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

pub fn lock_file_path(project: &Path) -> PathBuf {
    ucp_dir(project).join("bridge.lock")
}
