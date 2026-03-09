use crate::config::{self, LockFile};
use crate::error::UcpError;
use std::path::{Path, PathBuf};
use sysinfo::System;

/// Discover a Unity project by searching upward from `start` for ProjectSettings/.
pub fn find_unity_project(start: &Path) -> Result<PathBuf, UcpError> {
    let mut dir = start.to_path_buf();
    loop {
        if dir.join("ProjectSettings").is_dir() && dir.join("Assets").is_dir() {
            return Ok(dir);
        }
        if !dir.pop() {
            return Err(UcpError::ProjectNotFound);
        }
    }
}

/// Resolve the project path from explicit flag or CWD-based discovery.
pub fn resolve_project(explicit: Option<&str>) -> Result<PathBuf, UcpError> {
    if let Some(p) = explicit {
        let path = PathBuf::from(p);
        if path.join("ProjectSettings").is_dir() {
            return Ok(path);
        }
        return Err(UcpError::ProjectNotFound);
    }
    let cwd = std::env::current_dir().map_err(|e| UcpError::Other(e.to_string()))?;
    find_unity_project(&cwd)
}

/// Read and validate the bridge lock file.
pub fn read_lock_file(project: &Path) -> Result<LockFile, UcpError> {
    let path = config::lock_file_path(project);
    if !path.exists() {
        return Err(UcpError::BridgeNotRunning);
    }

    let contents = std::fs::read_to_string(&path)
        .map_err(|e| UcpError::Other(format!("Failed to read lock file: {e}")))?;

    let lock: LockFile = serde_json::from_str(&contents)
        .map_err(|e| UcpError::Other(format!("Invalid lock file: {e}")))?;

    // Verify PID is alive
    let sys = System::new_all();
    let pid = sysinfo::Pid::from_u32(lock.pid);
    if sys.process(pid).is_none() {
        // Stale lock file — clean it up
        let _ = std::fs::remove_file(&path);
        return Err(UcpError::BridgeNotRunning);
    }

    Ok(lock)
}
