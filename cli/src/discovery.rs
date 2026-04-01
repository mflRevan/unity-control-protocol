use crate::config::{self, LockFile};
use crate::error::UcpError;
use serde::Serialize;
use std::path::{Path, PathBuf};
use sysinfo::System;

#[derive(Debug, Clone, Serialize)]
pub struct UnityEditorProcess {
    pub pid: u32,
    pub project_path: PathBuf,
    pub executable_path: Option<PathBuf>,
    pub args: Vec<String>,
}

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
        // Stale lock file - clean it up
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
    list_running_unity_editors()
        .into_iter()
        .find(|process| normalize_path(&process.project_path) == normalized_project)
        .map(|process| process.pid)
}

pub fn list_running_unity_editors() -> Vec<UnityEditorProcess> {
    let system = System::new_all();
    let mut processes = Vec::new();

    for process in system.processes().values() {
        let args: Vec<String> = process
            .cmd()
            .iter()
            .map(|value| value.to_string_lossy().into_owned())
            .collect();

        let Some(project_arg) = extract_project_path_from_args(&args) else {
            continue;
        };

        let executable_path = process.exe().map(|value| value.to_path_buf());
        if !is_unity_editor_executable(executable_path.as_deref(), &args) {
            continue;
        }

        processes.push(UnityEditorProcess {
            pid: process.pid().as_u32(),
            project_path: project_arg,
            executable_path,
            args,
        });
    }

    processes
}

pub fn focus_unity_editor(project: &Path) -> Result<bool, UcpError> {
    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(false);
    };

    focus_process_window(pid)
}

pub fn request_unity_editor_close(project: &Path) -> Result<bool, UcpError> {
    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(false);
    };

    request_process_window_close(pid)
}

pub fn handle_unity_startup_dialogs(
    project: &Path,
    policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    if matches!(policy, config::StartupDialogPolicy::Manual) {
        return Ok(Vec::new());
    }

    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(Vec::new());
    };

    handle_process_startup_dialogs(pid, policy)
}

pub fn is_process_running(pid: u32) -> bool {
    let system = System::new_all();
    system.process(sysinfo::Pid::from_u32(pid)).is_some()
}

pub fn terminate_process(pid: u32) -> Result<bool, UcpError> {
    let mut system = System::new_all();
    system.refresh_all();

    let Some(process) = system.process(sysinfo::Pid::from_u32(pid)) else {
        return Ok(false);
    };

    Ok(process.kill())
}

fn preferred_dialog_button_label(
    title: &str,
    labels: &[String],
    policy: config::StartupDialogPolicy,
) -> Option<String> {
    let normalized_title = normalize_dialog_label(title);
    let title_preferences: Option<&[&str]> = if normalized_title.contains("openingprojectinnonmatchingeditorinstallation")
    {
        match policy {
            config::StartupDialogPolicy::Auto
            | config::StartupDialogPolicy::Ignore
            | config::StartupDialogPolicy::Recover => {
                Some(&["continue", "openproject", "openanyway", "ok"])
            }
            config::StartupDialogPolicy::SafeMode => Some(&["quit", "cancel"]),
            config::StartupDialogPolicy::Cancel => Some(&["quit", "cancel", "close", "no"]),
            config::StartupDialogPolicy::Manual => None,
        }
    } else if normalized_title.contains("entersafemode") {
        match policy {
            config::StartupDialogPolicy::Auto | config::StartupDialogPolicy::Ignore => {
                Some(&["ignore", "continue", "ok"])
            }
            config::StartupDialogPolicy::Recover | config::StartupDialogPolicy::SafeMode => {
                Some(&["entersafemode", "safemode"])
            }
            config::StartupDialogPolicy::Cancel => Some(&["quit", "cancel", "close", "no"]),
            config::StartupDialogPolicy::Manual => None,
        }
    } else if normalized_title.contains("projectupgraderequired") {
        match policy {
            config::StartupDialogPolicy::Auto
            | config::StartupDialogPolicy::Ignore
            | config::StartupDialogPolicy::Recover => {
                Some(&["confirm", "continue", "openproject", "openanyway", "ok", "yes"])
            }
            config::StartupDialogPolicy::Cancel => Some(&["quit", "cancel", "close", "no"]),
            config::StartupDialogPolicy::SafeMode | config::StartupDialogPolicy::Manual => None,
        }
    } else if normalized_title.contains("autographicsapi") {
        match policy {
            config::StartupDialogPolicy::Auto
            | config::StartupDialogPolicy::Ignore
            | config::StartupDialogPolicy::Recover => {
                Some(&["ok", "continue", "confirm", "yes"])
            }
            config::StartupDialogPolicy::Cancel => Some(&["quit", "cancel", "close", "no"]),
            config::StartupDialogPolicy::SafeMode | config::StartupDialogPolicy::Manual => None,
        }
    } else {
        None
    };

    let normalized: Vec<(String, &String)> = labels
        .iter()
        .map(|label| (normalize_dialog_label(label), label))
        .collect();

    if let Some(preferences) = title_preferences {
        for preferred in preferences {
            if let Some((_, label)) = normalized
                .iter()
                .find(|(candidate, _)| candidate.contains(preferred))
            {
                return Some((*label).clone());
            }
        }
    }

    let preferences: &[&str] = match policy {
        config::StartupDialogPolicy::Auto => &[
            "ignore",
            "continue",
            "confirm",
            "skiprecovery",
            "skip",
            "openproject",
            "openanyway",
            "ok",
            "yes",
            "loadrecovery",
            "recover",
            "restore",
            "entersafemode",
            "safemode",
        ],
        config::StartupDialogPolicy::Ignore => &[
            "ignore",
            "continue",
            "confirm",
            "skiprecovery",
            "skip",
            "openproject",
            "openanyway",
            "ok",
            "yes",
        ],
        config::StartupDialogPolicy::Recover => &[
            "continue",
            "confirm",
            "openproject",
            "openanyway",
            "loadrecovery",
            "recover",
            "restore",
            "ok",
            "yes",
        ],
        config::StartupDialogPolicy::SafeMode => &["entersafemode", "safemode"],
        config::StartupDialogPolicy::Cancel => &["cancel", "quit", "close", "no"],
        config::StartupDialogPolicy::Manual => &[],
    };

    for preferred in preferences {
        if let Some((_, label)) = normalized
            .iter()
            .find(|(candidate, _)| candidate.contains(preferred))
        {
            return Some((*label).clone());
        }
    }

    None
}

fn normalize_dialog_label(value: &str) -> String {
    value
        .chars()
        .filter(|ch| ch.is_ascii_alphanumeric())
        .flat_map(|ch| ch.to_lowercase())
        .collect()
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

pub fn extract_project_path_from_args(args: &[String]) -> Option<PathBuf> {
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

fn is_unity_editor_executable(executable_path: Option<&Path>, args: &[String]) -> bool {
    if let Some(executable_path) = executable_path {
        if let Some(name) = executable_path.file_name().and_then(|value| value.to_str()) {
            return is_unity_editor_name(name);
        }
    }

    args.first()
        .and_then(|value| Path::new(value).file_name().and_then(|part| part.to_str()))
        .is_some_and(is_unity_editor_name)
}

fn is_unity_editor_name(name: &str) -> bool {
    name.eq_ignore_ascii_case("Unity.exe") || name.eq_ignore_ascii_case("Unity")
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
        fn EnumWindows(
            lp_enum_func: extern "system" fn(Hwnd, Lparam) -> Bool,
            l_param: Lparam,
        ) -> Bool;
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

#[cfg(windows)]
fn handle_process_startup_dialogs(
    pid: u32,
    policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    use std::ffi::c_void;

    type Bool = i32;
    type Hwnd = *mut c_void;
    type Lparam = isize;

    #[derive(Clone)]
    struct WindowInfo {
        hwnd: Hwnd,
        title: String,
    }

    #[repr(C)]
    struct EnumWindowsState {
        target_pid: u32,
        windows: Vec<WindowInfo>,
    }

    #[derive(Clone)]
    struct ButtonInfo {
        hwnd: Hwnd,
        label: String,
    }

    unsafe extern "system" {
        fn EnumWindows(
            lp_enum_func: extern "system" fn(Hwnd, Lparam) -> Bool,
            l_param: Lparam,
        ) -> Bool;
        fn EnumChildWindows(
            hwnd: Hwnd,
            lp_enum_func: extern "system" fn(Hwnd, Lparam) -> Bool,
            l_param: Lparam,
        ) -> Bool;
        fn GetWindow(hwnd: Hwnd, cmd: u32) -> Hwnd;
        fn GetWindowThreadProcessId(hwnd: Hwnd, process_id: *mut u32) -> u32;
        fn GetWindowTextLengthW(hwnd: Hwnd) -> i32;
        fn GetWindowTextW(hwnd: Hwnd, text: *mut u16, max_count: i32) -> i32;
        fn GetClassNameW(hwnd: Hwnd, class_name: *mut u16, max_count: i32) -> i32;
        fn IsWindowVisible(hwnd: Hwnd) -> Bool;
        fn SendMessageW(hwnd: Hwnd, msg: u32, w_param: usize, l_param: isize) -> isize;
    }

    extern "system" fn enum_windows(hwnd: Hwnd, l_param: Lparam) -> Bool {
        let state = unsafe { &mut *(l_param as *mut EnumWindowsState) };
        let mut process_id = 0;
        unsafe {
            GetWindowThreadProcessId(hwnd, &mut process_id);
        }

        const GW_OWNER: u32 = 4;
        let owner = unsafe { GetWindow(hwnd, GW_OWNER) };
        if process_id != 0
            && process_id == state.target_pid
            && unsafe { IsWindowVisible(hwnd) } != 0
        {
            let title = read_window_text(hwnd);
            let is_top_level_dialog = owner.is_null() && !title.trim().is_empty();
            let is_owned_popup = !owner.is_null();
            if !is_top_level_dialog && !is_owned_popup {
                return 1;
            }
            state.windows.push(WindowInfo {
                hwnd,
                title,
            });
        }

        1
    }

    extern "system" fn enum_child_windows(hwnd: Hwnd, l_param: Lparam) -> Bool {
        let buttons = unsafe { &mut *(l_param as *mut Vec<ButtonInfo>) };
        let class_name = read_class_name(hwnd).to_ascii_lowercase();
        let label = read_window_text(hwnd);
        if class_name.contains("button") && !label.trim().is_empty() {
            buttons.push(ButtonInfo { hwnd, label });
        }
        1
    }

    fn read_window_text(hwnd: Hwnd) -> String {
        unsafe {
            let length = GetWindowTextLengthW(hwnd);
            if length <= 0 {
                return String::new();
            }
            let mut buffer = vec![0u16; (length as usize) + 1];
            let written = GetWindowTextW(hwnd, buffer.as_mut_ptr(), buffer.len() as i32);
            String::from_utf16_lossy(&buffer[..written as usize])
                .trim()
                .to_string()
        }
    }

    fn read_class_name(hwnd: Hwnd) -> String {
        unsafe {
            let mut buffer = vec![0u16; 256];
            let written = GetClassNameW(hwnd, buffer.as_mut_ptr(), buffer.len() as i32);
            String::from_utf16_lossy(&buffer[..written as usize])
        }
    }

    let mut state = EnumWindowsState {
        target_pid: pid,
        windows: Vec::new(),
    };
    let l_param = &mut state as *mut EnumWindowsState as isize;

    unsafe {
        EnumWindows(enum_windows, l_param);
    }

    let mut handled = Vec::new();
    for window in state.windows {
        let mut process_id = 0;
        unsafe {
            GetWindowThreadProcessId(window.hwnd, &mut process_id);
        }
        if process_id != pid {
            continue;
        }

        let mut buttons = Vec::<ButtonInfo>::new();
        let child_l_param = &mut buttons as *mut Vec<ButtonInfo> as isize;
        unsafe {
            EnumChildWindows(window.hwnd, enum_child_windows, child_l_param);
        }

        let labels = buttons
            .iter()
            .map(|button| button.label.clone())
            .collect::<Vec<_>>();
        let Some(selected_label) = preferred_dialog_button_label(&window.title, &labels, policy) else {
            continue;
        };

        let Some(button) = buttons.into_iter().find(|button| {
            normalize_dialog_label(&button.label) == normalize_dialog_label(&selected_label)
        }) else {
            continue;
        };

        const BM_CLICK: u32 = 0x00F5;
        unsafe {
            SendMessageW(button.hwnd, BM_CLICK, 0, 0);
        }

        let title = if window.title.is_empty() {
            "Unity startup dialog".to_string()
        } else {
            window.title
        };
        handled.push(format!("{title}: {}", button.label));
    }

    Ok(handled)
}

#[cfg(windows)]
fn request_process_window_close(pid: u32) -> Result<bool, UcpError> {
    use std::ffi::c_void;

    type Bool = i32;
    type Hwnd = *mut c_void;
    type Lparam = isize;

    #[repr(C)]
    struct EnumState {
        target_pid: u32,
        hwnd: Hwnd,
    }

    unsafe extern "system" {
        fn EnumWindows(
            lp_enum_func: extern "system" fn(Hwnd, Lparam) -> Bool,
            l_param: Lparam,
        ) -> Bool;
        fn GetWindowThreadProcessId(hwnd: Hwnd, process_id: *mut u32) -> u32;
        fn IsWindowVisible(hwnd: Hwnd) -> Bool;
        fn PostMessageW(hwnd: Hwnd, msg: u32, w_param: usize, l_param: isize) -> Bool;
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

    const WM_CLOSE: u32 = 0x0010;
    Ok(unsafe { PostMessageW(state.hwnd, WM_CLOSE, 0, 0) } != 0)
}

#[cfg(not(windows))]
fn handle_process_startup_dialogs(
    _pid: u32,
    _policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    Ok(Vec::new())
}

#[cfg(not(windows))]
fn focus_process_window(_pid: u32) -> Result<bool, UcpError> {
    Ok(false)
}

#[cfg(not(windows))]
fn request_process_window_close(_pid: u32) -> Result<bool, UcpError> {
    Ok(false)
}

#[cfg(test)]
mod tests {
    use super::{
        extract_project_path_from_args, is_unity_editor_executable, normalize_dialog_label,
        preferred_dialog_button_label,
    };
    use crate::config::StartupDialogPolicy;
    use std::path::{Path, PathBuf};

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

    #[test]
    fn normalizes_dialog_labels() {
        assert_eq!(normalize_dialog_label("Enter Safe Mode"), "entersafemode");
        assert_eq!(normalize_dialog_label("Load Recovery..."), "loadrecovery");
    }

    #[test]
    fn chooses_dialog_button_for_ignore_policy() {
        let labels = vec![
            "Cancel".to_string(),
            "Ignore".to_string(),
            "Enter Safe Mode".to_string(),
        ];

        assert_eq!(
            preferred_dialog_button_label("", &labels, StartupDialogPolicy::Ignore),
            Some("Ignore".to_string())
        );
    }

    #[test]
    fn chooses_dialog_button_for_recovery_policy() {
        let labels = vec!["Skip Recovery".to_string(), "Load Recovery".to_string()];

        assert_eq!(
            preferred_dialog_button_label("", &labels, StartupDialogPolicy::Recover),
            Some("Load Recovery".to_string())
        );
    }

    #[test]
    fn chooses_continue_for_recovery_policy() {
        let labels = vec!["Quit".to_string(), "Continue".to_string()];

        assert_eq!(
            preferred_dialog_button_label("", &labels, StartupDialogPolicy::Recover),
            Some("Continue".to_string())
        );
    }

    #[test]
    fn chooses_continue_for_non_matching_editor_dialog() {
        let labels = vec!["Continue".to_string(), "Quit".to_string()];

        assert_eq!(
            preferred_dialog_button_label(
                "Opening Project in Non-Matching Editor Installation",
                &labels,
                StartupDialogPolicy::Ignore,
            ),
            Some("Continue".to_string())
        );
    }

    #[test]
    fn chooses_ignore_for_safe_mode_dialog_when_ignoring() {
        let labels = vec![
            "Enter Safe Mode".to_string(),
            "Ignore".to_string(),
            "Quit".to_string(),
        ];

        assert_eq!(
            preferred_dialog_button_label("Enter Safe Mode?", &labels, StartupDialogPolicy::Ignore),
            Some("Ignore".to_string())
        );
    }

    #[test]
    fn chooses_confirm_for_project_upgrade_required() {
        let labels = vec!["Quit".to_string(), "Confirm".to_string()];

        assert_eq!(
            preferred_dialog_button_label(
                "Project Upgrade Required",
                &labels,
                StartupDialogPolicy::Ignore,
            ),
            Some("Confirm".to_string())
        );
    }

    #[test]
    fn chooses_ok_for_auto_graphics_api_notice() {
        let labels = vec!["OK".to_string()];

        assert_eq!(
            preferred_dialog_button_label(
                "Auto Graphics API Notice",
                &labels,
                StartupDialogPolicy::Ignore,
            ),
            Some("OK".to_string())
        );
    }

    #[test]
    fn generic_fallback_matches_confirm_and_yes() {
        let labels = vec!["Cancel".to_string(), "Confirm".to_string()];
        assert_eq!(
            preferred_dialog_button_label("Some Unknown Dialog", &labels, StartupDialogPolicy::Ignore),
            Some("Confirm".to_string())
        );

        let labels2 = vec!["No".to_string(), "Yes".to_string()];
        assert_eq!(
            preferred_dialog_button_label("Another Unknown Dialog", &labels2, StartupDialogPolicy::Auto),
            Some("Yes".to_string())
        );
    }

    #[test]
    fn excludes_unity_hub_launcher_processes() {
        let args = vec![
            "C:/Program Files/Unity Hub/Unity Hub.exe".to_string(),
            "--editor-path".to_string(),
            "D:/Unity/Installs/6000.3.1f1/Editor/Unity.exe".to_string(),
            "-projectPath".to_string(),
            "D:/Unity/Projects/HijraVR".to_string(),
        ];

        assert!(!is_unity_editor_executable(
            Some(Path::new("C:/Program Files/Unity Hub/Unity Hub.exe")),
            &args,
        ));
        assert!(is_unity_editor_executable(
            Some(Path::new("D:/Unity/Installs/6000.3.1f1/Editor/Unity.exe")),
            &args,
        ));
    }
}
