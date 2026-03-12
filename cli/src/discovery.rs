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

pub fn is_unity_editor_running_for_project(project: &Path) -> bool {
    unity_editor_pid_for_project(project).is_some()
}

pub fn unity_editor_pid_for_project(project: &Path) -> Option<u32> {
    let normalized_project = normalize_path(project);
    let system = System::new_all();

    for process in system.processes().values() {
        let args: Vec<String> = process
            .cmd()
            .iter()
            .map(|value| value.to_string_lossy().into_owned())
            .collect();

        let Some(project_arg) = extract_project_path_from_args(&args) else {
            continue;
        };

        if normalize_path(&project_arg) == normalized_project {
            return Some(process.pid().as_u32());
        }
    }

    None
}

pub fn focus_unity_editor(project: &Path) -> Result<bool, UcpError> {
    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(false);
    };

    focus_process_window(pid)
}

fn normalize_path(path: &Path) -> String {
    let resolved = std::fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf());
    let normalized = resolved.to_string_lossy().replace('\\', "/");
    if cfg!(windows) {
        normalized.to_ascii_lowercase()
    } else {
        normalized
    }
}

fn extract_project_path_from_args(args: &[String]) -> Option<PathBuf> {
    for (index, arg) in args.iter().enumerate() {
        if let Some((flag, value)) = arg.split_once('=') {
            if is_project_path_flag(flag) && !value.trim().is_empty() {
                return Some(PathBuf::from(value.trim_matches('"')));
            }
        }

        if is_project_path_flag(arg) {
            let value = args.get(index + 1)?;
            if !value.trim().is_empty() {
                return Some(PathBuf::from(value.trim_matches('"')));
            }
        }
    }

    None
}

fn is_project_path_flag(value: &str) -> bool {
    value.eq_ignore_ascii_case("-projectpath")
}

#[cfg(windows)]
fn focus_process_window(pid: u32) -> Result<bool, UcpError> {
    use std::ffi::c_void;
    use std::process::Command;

    type Bool = i32;
    type Hwnd = *mut c_void;
    type Lparam = isize;

    #[repr(C)]
    struct EnumState {
        target_pid: u32,
        hwnd: Hwnd,
    }

    unsafe extern "system" {
        fn EnumWindows(lp_enum_func: extern "system" fn(Hwnd, Lparam) -> Bool, l_param: Lparam) -> Bool;
        fn GetWindowThreadProcessId(hwnd: Hwnd, process_id: *mut u32) -> u32;
        fn IsWindowVisible(hwnd: Hwnd) -> Bool;
        fn ShowWindow(hwnd: Hwnd, cmd_show: i32) -> Bool;
        fn BringWindowToTop(hwnd: Hwnd) -> Bool;
        fn SetForegroundWindow(hwnd: Hwnd) -> Bool;
    }

    extern "system" fn enum_windows(hwnd: Hwnd, l_param: Lparam) -> Bool {
        let state = unsafe { &mut *(l_param as *mut EnumState) };
        let mut process_id = 0;
        unsafe {
            GetWindowThreadProcessId(hwnd, &mut process_id);
        }

        if process_id == state.target_pid && unsafe { IsWindowVisible(hwnd) } != 0 {
            state.hwnd = hwnd;
            0
        } else {
            1
        }
    }

    let mut state = EnumState {
        target_pid: pid,
        hwnd: std::ptr::null_mut(),
    };

    unsafe {
        EnumWindows(enum_windows, &mut state as *mut EnumState as isize);
    }

    if state.hwnd.is_null() {
        return Ok(false);
    }

    let mut focused = false;

    unsafe {
        const SW_RESTORE: i32 = 9;
        if ShowWindow(state.hwnd, SW_RESTORE) != 0 {
            focused = true;
        }
        if BringWindowToTop(state.hwnd) != 0 {
            focused = true;
        }
        if SetForegroundWindow(state.hwnd) != 0 {
            focused = true;
        }
    }

    if focused {
        return Ok(true);
    }

    let script = format!(
        "$shell = New-Object -ComObject WScript.Shell; exit [int]!($shell.AppActivate({pid}))"
    );
    let status = Command::new("powershell")
        .args(["-NoProfile", "-Command", &script])
        .status()
        .map_err(|e| UcpError::Other(format!("Failed to activate Unity window: {e}")))?;

    Ok(status.success())
}

#[cfg(not(windows))]
fn focus_process_window(_pid: u32) -> Result<bool, UcpError> {
    Ok(false)
}

#[cfg(test)]
mod tests {
    use super::extract_project_path_from_args;
    use std::path::PathBuf;

    #[test]
    fn extracts_project_path_from_split_flag() {
        let args = vec![
            "Unity.exe".to_string(),
            "-projectPath".to_string(),
            "D:/Unity/Projects/HijraVR".to_string(),
        ];

        assert_eq!(
            extract_project_path_from_args(&args),
            Some(PathBuf::from("D:/Unity/Projects/HijraVR"))
        );
    }

    #[test]
    fn extracts_project_path_from_equals_flag() {
        let args = vec![
            "Unity.exe".to_string(),
            "-projectpath=D:/Unity/Projects/HijraVR".to_string(),
        ];

        assert_eq!(
            extract_project_path_from_args(&args),
            Some(PathBuf::from("D:/Unity/Projects/HijraVR"))
        );
    }
}
