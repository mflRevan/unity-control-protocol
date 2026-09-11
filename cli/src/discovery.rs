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

    // Verify PID is alive. This runs on the hot path of every bridge command, so refresh only
    // the one process we care about -- a full `System::new_all()` sweep here cost ~70 ms warm
    // (~200 ms on the first call in a process) versus ~7 ms for a targeted refresh.
    let pid = sysinfo::Pid::from_u32(lock.pid);
    let mut sys = System::new();
    sys.refresh_processes(sysinfo::ProcessesToUpdate::Some(&[pid]), true);
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
    // Only the command line and executable path are read below, so skip the disk/memory/user/
    // environment probes `System::new_all()` performs for every process on the machine.
    let mut system = System::new();
    system.refresh_processes_specifics(
        sysinfo::ProcessesToUpdate::All,
        true,
        sysinfo::ProcessRefreshKind::nothing()
            .with_cmd(sysinfo::UpdateKind::Always)
            .with_exe(sysinfo::UpdateKind::Always),
    );
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
        // Asset import workers are Unity.exe processes launched by the editor with the same
        // -projectPath. They own no windows and no bridge; picking one at random made dialog
        // detection and focus flaky.
        if is_asset_import_worker(&args) {
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

pub fn handle_unity_startup_dialogs(
    project: &Path,
    policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    if matches!(policy, config::StartupDialogPolicy::Manual) {
        return Ok(Vec::new());
    }

    let Some(pid) = unity_editor_pid_for_project(project) else {
        tracing::debug!(
            "startup dialogs: no Unity editor process matched {}",
            project.display()
        );
        return Ok(Vec::new());
    };

    handle_process_startup_dialogs(pid, policy)
}

pub fn is_process_running(pid: u32) -> bool {
    // Refresh only the pid we care about. `System::new_all()` also sweeps CPU, memory and disks,
    // which made this far too expensive to call in a poll loop.
    let target = sysinfo::Pid::from_u32(pid);
    let mut system = System::new();
    system.refresh_processes(sysinfo::ProcessesToUpdate::Some(&[target]), true);
    system.process(target).is_some()
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
    dialog_button_label(title, labels, policy, true)
}

/// Like `preferred_dialog_button_label` but without the generic per-policy fallback: only a
/// dialog the CLI recognises by title gets an answer.
/// Unity's progress window ("Hold on...") is not a dialog: it reports work in flight and its only
/// button aborts that work. Both the stall check and the startup policy must leave it alone.
fn is_progress_window(title: &str) -> bool {
    let normalized = normalize_dialog_label(title);
    normalized.starts_with("holdon") || normalized.starts_with("progress")
}

fn known_dialog_button_label(
    title: &str,
    labels: &[String],
    policy: config::StartupDialogPolicy,
) -> Option<String> {
    dialog_button_label(title, labels, policy, false)
}

fn dialog_button_label(
    title: &str,
    labels: &[String],
    policy: config::StartupDialogPolicy,
    allow_generic: bool,
) -> Option<String> {
    if is_progress_window(title) {
        return None;
    }
    let normalized_title = normalize_dialog_label(title);
    let title_preferences: Option<&[&str]> = if normalized_title
        .contains("openingprojectinnonmatchingeditorinstallation")
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
    } else if normalized_title.contains("projectupgraderequired")
        || normalized_title.contains("projectdowngraderequired")
    {
        // "Project Upgrade Required" (older project, newer editor) and "Project Downgrade
        // Required" (the reverse, shown by 6000.3+ instead of the non-matching-editor dialog).
        // Both are answered the same way: proceed, since the caller chose this editor.
        match policy {
            config::StartupDialogPolicy::Auto
            | config::StartupDialogPolicy::Ignore
            | config::StartupDialogPolicy::Recover => Some(&[
                "confirm",
                "continue",
                "openproject",
                "openanyway",
                "ok",
                "yes",
            ]),
            config::StartupDialogPolicy::Cancel => Some(&["quit", "cancel", "close", "no"]),
            config::StartupDialogPolicy::SafeMode | config::StartupDialogPolicy::Manual => None,
        }
    } else if normalized_title.contains("packageswitherrors") {
        // "This project contains one or more packages with errors. Do you want to open Package
        // Manager?" -- shown once per session after load. Closing it is always right for an
        // automated session; "Dismiss Forever" would hide a real signal from the human.
        match policy {
            config::StartupDialogPolicy::Manual => None,
            _ => Some(&["dismiss"]),
        }
    } else if normalized_title.contains("autographicsapi") {
        match policy {
            config::StartupDialogPolicy::Auto
            | config::StartupDialogPolicy::Ignore
            | config::StartupDialogPolicy::Recover => Some(&["ok", "continue", "confirm", "yes"]),
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

    // Exact matches win over substring matches, otherwise "dismiss" would pick "Dismiss Forever"
    // when it happens to be enumerated first.
    fn pick(normalized: &[(String, &String)], preferred: &str) -> Option<String> {
        normalized
            .iter()
            .find(|(candidate, _)| candidate == preferred)
            .or_else(|| {
                normalized
                    .iter()
                    .find(|(candidate, _)| candidate.contains(preferred))
            })
            .map(|(_, label)| (*label).clone())
    }

    if let Some(preferences) = title_preferences {
        for preferred in preferences {
            if let Some(label) = pick(&normalized, preferred) {
                return Some(label);
            }
        }
    }

    if !allow_generic {
        return None;
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
        if let Some(label) = pick(&normalized, preferred) {
            return Some(label);
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

fn is_asset_import_worker(args: &[String]) -> bool {
    args.iter().any(|arg| {
        let arg = arg.trim_matches('"');
        arg.starts_with("AssetImportWorker") || arg == "-adb2"
    })
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

/// A modal dialog owned by a Unity editor process: its title and button labels, plus the native
/// handles needed to press one of them.
#[derive(Debug, Clone)]
pub struct DialogInfo {
    pub title: String,
    pub buttons: Vec<String>,
    button_handles: Vec<usize>,
}

impl DialogInfo {
    fn button_handle(&self, label: &str) -> Option<usize> {
        let wanted = normalize_dialog_label(label);
        let exact = self
            .buttons
            .iter()
            .position(|candidate| normalize_dialog_label(candidate) == wanted);
        let index = exact.or_else(|| {
            self.buttons
                .iter()
                .position(|candidate| normalize_dialog_label(candidate).contains(&wanted))
        })?;
        self.button_handles.get(index).copied()
    }
}

/// Visible top-level dialogs (with at least one button) belonging to the project's editor.
pub fn list_unity_dialogs(project: &Path) -> Vec<DialogInfo> {
    match unity_editor_pid_for_project(project) {
        Some(pid) => enumerate_process_dialogs(pid),
        None => Vec::new(),
    }
}

/// Answers only dialogs the CLI recognises by title (Safe Mode, package errors, version
/// mismatch, ...). Unknown dialogs are left alone: mid-session they may be a user script's own
/// prompt, and pressing "OK" on one blindly is exactly the kind of surprise an agent must not cause.
pub fn answer_known_unity_dialogs(
    project: &Path,
    policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    if matches!(policy, config::StartupDialogPolicy::Manual) {
        return Ok(Vec::new());
    }
    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(Vec::new());
    };
    Ok(answer_process_dialogs(pid, policy, false))
}

/// Presses the button whose label matches `label` (exact first, then substring, case- and
/// punctuation-insensitive) on the first dialog that has it. Returns "<title>: <button>".
pub fn answer_unity_dialog(project: &Path, label: &str) -> Result<Option<String>, UcpError> {
    let Some(pid) = unity_editor_pid_for_project(project) else {
        return Ok(None);
    };
    for dialog in enumerate_process_dialogs(pid) {
        if let Some(handle) = dialog.button_handle(label) {
            let index = dialog
                .button_handles
                .iter()
                .position(|h| *h == handle)
                .unwrap_or(0);
            let pressed = dialog.buttons.get(index).cloned().unwrap_or_default();
            click_button(handle);
            return Ok(Some(format!("{}: {pressed}", dialog.title)));
        }
    }
    Ok(None)
}

fn handle_process_startup_dialogs(
    pid: u32,
    policy: config::StartupDialogPolicy,
) -> Result<Vec<String>, UcpError> {
    Ok(answer_process_dialogs(pid, policy, true))
}

fn answer_process_dialogs(
    pid: u32,
    policy: config::StartupDialogPolicy,
    allow_generic: bool,
) -> Vec<String> {
    let dialogs = enumerate_process_dialogs(pid);
    tracing::debug!(
        "startup dialogs: pid {pid} has {} candidate window(s): {:?}",
        dialogs.len(),
        dialogs.iter().map(|d| d.title.as_str()).collect::<Vec<_>>()
    );

    let mut handled = Vec::new();
    for dialog in dialogs {
        let selected = if allow_generic {
            preferred_dialog_button_label(&dialog.title, &dialog.buttons, policy)
        } else {
            known_dialog_button_label(&dialog.title, &dialog.buttons, policy)
        };
        tracing::debug!(
            "startup dialogs: window {:?} buttons {:?} -> policy {policy} selects {:?}",
            dialog.title,
            dialog.buttons,
            selected
        );
        let Some(label) = selected else {
            continue;
        };
        let Some(handle) = dialog.button_handle(&label) else {
            continue;
        };
        click_button(handle);
        let title = if dialog.title.is_empty() {
            "Unity startup dialog".to_string()
        } else {
            dialog.title.clone()
        };
        handled.push(format!("{title}: {label}"));
    }
    handled
}

#[cfg(windows)]
fn enumerate_process_dialogs(pid: u32) -> Vec<DialogInfo> {
    use std::ffi::c_void;

    type Bool = i32;
    type Hwnd = *mut c_void;
    type Lparam = isize;

    #[repr(C)]
    struct EnumWindowsState {
        target_pid: u32,
        windows: Vec<(Hwnd, String)>,
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
            if is_top_level_dialog || is_owned_popup {
                state.windows.push((hwnd, title));
            }
        }
        1
    }

    extern "system" fn enum_child_windows(hwnd: Hwnd, l_param: Lparam) -> Bool {
        let buttons = unsafe { &mut *(l_param as *mut Vec<(Hwnd, String)>) };
        let class_name = read_class_name(hwnd).to_ascii_lowercase();
        let label = read_window_text(hwnd);
        if class_name.contains("button") && !label.trim().is_empty() {
            buttons.push((hwnd, label));
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

    let mut dialogs = Vec::new();
    for (hwnd, title) in state.windows {
        let mut buttons = Vec::<(Hwnd, String)>::new();
        let child_l_param = &mut buttons as *mut Vec<(Hwnd, String)> as isize;
        unsafe {
            EnumChildWindows(hwnd, enum_child_windows, child_l_param);
        }
        if buttons.is_empty() {
            // Unity's own tool windows are owned popups too; only button-bearing ones are dialogs.
            continue;
        }
        if is_progress_window(&title) {
            // "Hold on..." is Unity's progress bar (import, compile, play-mode entry). It carries a
            // cancel-style button ("Skip Transcoding", "Cancel") that must never be pressed on the
            // editor's behalf, and it is not a modal waiting for an answer.
            continue;
        }
        dialogs.push(DialogInfo {
            title,
            button_handles: buttons.iter().map(|(h, _)| *h as usize).collect(),
            buttons: buttons.into_iter().map(|(_, label)| label).collect(),
        });
    }
    dialogs
}

#[cfg(windows)]
fn click_button(handle: usize) {
    use std::ffi::c_void;
    unsafe extern "system" {
        fn SendMessageW(hwnd: *mut c_void, msg: u32, w_param: usize, l_param: isize) -> isize;
    }
    const BM_CLICK: u32 = 0x00F5;
    unsafe {
        SendMessageW(handle as *mut c_void, BM_CLICK, 0, 0);
    }
}

#[cfg(not(windows))]
fn enumerate_process_dialogs(_pid: u32) -> Vec<DialogInfo> {
    Vec::new()
}

#[cfg(not(windows))]
fn click_button(_handle: usize) {}

#[cfg(not(windows))]
fn focus_process_window(_pid: u32) -> Result<bool, UcpError> {
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

    fn labels(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| value.to_string()).collect()
    }

    #[test]
    fn answers_the_startup_dialogs_observed_on_unity_6() {
        // Button lists exactly as Unity 6000.5.1f1 enumerates them (Win32 child order).
        let safe_mode = labels(&["Enter Safe Mode", "Ignore", "Quit"]);
        let package_errors = labels(&["Open Package Manager", "Dismiss Forever", "Dismiss"]);
        let non_matching = labels(&["Quit", "Continue"]);

        for policy in [
            StartupDialogPolicy::Auto,
            StartupDialogPolicy::Ignore,
            StartupDialogPolicy::Recover,
            StartupDialogPolicy::Cancel,
        ] {
            assert_eq!(
                preferred_dialog_button_label("Packages with Errors", &package_errors, policy)
                    .as_deref(),
                Some("Dismiss"),
                "{policy}: must close the dialog without hiding it forever"
            );
        }
        assert_eq!(
            preferred_dialog_button_label(
                "Packages with Errors",
                &package_errors,
                StartupDialogPolicy::Manual
            ),
            None
        );

        assert_eq!(
            preferred_dialog_button_label(
                "Enter Safe Mode?",
                &safe_mode,
                StartupDialogPolicy::Auto
            )
            .as_deref(),
            Some("Ignore")
        );
        assert_eq!(
            preferred_dialog_button_label(
                "Enter Safe Mode?",
                &safe_mode,
                StartupDialogPolicy::SafeMode
            )
            .as_deref(),
            Some("Enter Safe Mode")
        );
        assert_eq!(
            preferred_dialog_button_label(
                "Opening Project in Non-Matching Editor Installation",
                &non_matching,
                StartupDialogPolicy::Auto
            )
            .as_deref(),
            Some("Continue")
        );
    }

    #[test]
    fn known_only_selection_leaves_unrecognised_dialogs_alone() {
        use super::known_dialog_button_label;
        let buttons = labels(&["Yes", "No"]);
        assert_eq!(
            known_dialog_button_label("Delete everything?", &buttons, StartupDialogPolicy::Auto),
            None
        );
        assert_eq!(
            known_dialog_button_label(
                "Enter Safe Mode?",
                &labels(&["Enter Safe Mode", "Ignore", "Quit"]),
                StartupDialogPolicy::Auto
            )
            .as_deref(),
            Some("Ignore")
        );
        // The generic path still answers it, as it always did at startup.
        assert_eq!(
            preferred_dialog_button_label(
                "Delete everything?",
                &buttons,
                StartupDialogPolicy::Auto
            )
            .as_deref(),
            Some("Yes")
        );
    }

    #[test]
    fn asset_import_workers_are_not_editors() {
        use super::is_asset_import_worker;
        let worker = labels(&[
            "D:\\Unity\\Installs\\6000.4.0f1\\Editor\\Unity.exe",
            "-adb2",
            "-batchMode",
            "-noUpm",
            "-name",
            "AssetImportWorker0",
            "-projectPath",
            "C:/Projects/Demo",
        ]);
        assert!(is_asset_import_worker(&worker));

        let editor = labels(&[
            "D:\\Unity\\Installs\\6000.4.0f1\\Editor\\Unity.exe",
            "-projectPath",
            "C:/Projects/Demo",
            "-logFile",
            "C:/Projects/Demo/.ucp/logs/editor.log",
        ]);
        assert!(!is_asset_import_worker(&editor));

        let batch_tests = labels(&[
            "Unity.exe",
            "-batchmode",
            "-projectPath",
            "C:/Projects/Demo",
            "-runTests",
        ]);
        assert!(!is_asset_import_worker(&batch_tests));
    }

    #[test]
    fn exact_button_labels_beat_substring_matches() {
        let buttons = labels(&["OK Forever", "OK"]);
        assert_eq!(
            preferred_dialog_button_label("Anything", &buttons, StartupDialogPolicy::Auto)
                .as_deref(),
            Some("OK")
        );
    }

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
    fn continues_through_project_downgrade_required() {
        // Unity 6000.3.1f1 opening a project last saved by 6000.5: buttons in Win32 order.
        let labels = labels(&["Continue", "Quit"]);
        for policy in [
            StartupDialogPolicy::Auto,
            StartupDialogPolicy::Ignore,
            StartupDialogPolicy::Recover,
        ] {
            assert_eq!(
                preferred_dialog_button_label("Project Downgrade Required", &labels, policy)
                    .as_deref(),
                Some("Continue"),
                "{policy}: a downgrade the caller asked for must proceed"
            );
        }
        assert_eq!(
            preferred_dialog_button_label(
                "Project Downgrade Required",
                &labels,
                StartupDialogPolicy::Cancel
            )
            .as_deref(),
            Some("Quit")
        );
        assert_eq!(
            preferred_dialog_button_label(
                "Project Downgrade Required",
                &labels,
                StartupDialogPolicy::Manual
            ),
            None
        );
    }

    #[test]
    fn never_presses_the_progress_windows_button() {
        // Unity 6000.6 entering play mode: the "Hold on..." progress bar exposes "Skip Transcoding".
        let labels = labels(&["Skip Transcoding"]);
        for policy in [
            StartupDialogPolicy::Auto,
            StartupDialogPolicy::Ignore,
            StartupDialogPolicy::Recover,
            StartupDialogPolicy::SafeMode,
            StartupDialogPolicy::Cancel,
        ] {
            assert_eq!(
                preferred_dialog_button_label("Hold on...", &labels, policy),
                None,
                "{policy}: a progress window is work in flight, not a question"
            );
        }
        assert!(super::is_progress_window("Hold on..."));
        assert!(!super::is_progress_window("Project Downgrade Required"));
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
            preferred_dialog_button_label(
                "Some Unknown Dialog",
                &labels,
                StartupDialogPolicy::Ignore
            ),
            Some("Confirm".to_string())
        );

        let labels2 = vec!["No".to_string(), "Yes".to_string()];
        assert_eq!(
            preferred_dialog_button_label(
                "Another Unknown Dialog",
                &labels2,
                StartupDialogPolicy::Auto
            ),
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
