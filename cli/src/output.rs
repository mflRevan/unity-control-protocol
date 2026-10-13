use console::style;

pub fn supports_unicode() -> bool {
    // On Windows, default console doesn't handle multi-byte UTF-8 well
    // through PowerShell's output capture pipeline
    if cfg!(windows) {
        // Check if WT_SESSION is set (Windows Terminal supports Unicode)
        std::env::var("WT_SESSION").is_ok()
    } else {
        true
    }
}

pub fn print_success(msg: &str) {
    let icon = if supports_unicode() { "✔" } else { "[OK]" };
    eprintln!("{} {}", style(icon).green().bold(), msg);
}

pub fn print_error(msg: &str) {
    let icon = if supports_unicode() { "✖" } else { "[ERR]" };
    eprintln!("{} {}", style(icon).red().bold(), msg);
}

pub fn print_warn(msg: &str) {
    let icon = if supports_unicode() { "⚠" } else { "[!]" };
    eprintln!("{} {}", style(icon).yellow().bold(), msg);
}

pub fn print_info(msg: &str) {
    let icon = if supports_unicode() { "ℹ" } else { "[*]" };
    eprintln!("{} {}", style(icon).cyan().bold(), msg);
}

/// The dim trailing line that reports editor state after a command.
pub fn print_state(msg: &str) {
    eprintln!("{}", style(msg).dim());
}

/// JSON goes out compact: an agent reads it just as well as the pretty form and pays about 40%
/// fewer tokens for it. `UCP_JSON_PRETTY=1` restores indentation for humans.
pub fn print_json(value: &serde_json::Value) {
    println!("{}", render_json(value, pretty_json()));
}

pub fn pretty_json() -> bool {
    std::env::var("UCP_JSON_PRETTY")
        .map(|v| {
            let v = v.trim().to_ascii_lowercase();
            !(v.is_empty() || v == "0" || v == "off" || v == "false" || v == "no")
        })
        .unwrap_or(false)
}

/// A progress notice that only appears when the wait it describes actually takes a while.
/// Most waits finish within a second, and a "[*] Waiting..." line on every mutation is noise
/// an agent pays for; one that shows up after `delay` tells it something real is happening.
pub struct DelayedNotice {
    handle: Option<tokio::task::JoinHandle<()>>,
}

impl DelayedNotice {
    pub fn info(message: &str, delay: std::time::Duration) -> Self {
        let message = message.to_string();
        let handle = tokio::spawn(async move {
            tokio::time::sleep(delay).await;
            print_info(&message);
        });
        Self {
            handle: Some(handle),
        }
    }

    /// A notice that never prints (JSON mode).
    pub fn silent() -> Self {
        Self { handle: None }
    }
}

impl Drop for DelayedNotice {
    fn drop(&mut self) {
        if let Some(handle) = self.handle.take() {
            handle.abort();
        }
    }
}

/// The delay before a wait notice is worth printing.
pub const NOTICE_DELAY: std::time::Duration = std::time::Duration::from_millis(1500);

pub fn delayed_notice(message: &str, json: bool) -> DelayedNotice {
    if json {
        DelayedNotice::silent()
    } else {
        DelayedNotice::info(message, NOTICE_DELAY)
    }
}

pub fn print_json_compact(value: &serde_json::Value) {
    println!("{}", render_json(value, false));
}

/// Top-level command envelopes (objects with a `success` key) get the editor-state appendix
/// appended as a trailing `editor` member; everything else is printed verbatim. The wrapper
/// serializes by reference, so a large payload is never cloned for the sake of the appendix.
fn render_json(value: &serde_json::Value, pretty: bool) -> String {
    if crate::editor_state::enabled() && crate::editor_state::is_envelope(value) {
        let editor = crate::editor_state::current();
        let modal = crate::editor_state::modal();
        if editor.is_some() || modal.is_some() {
            let wrapped = crate::editor_state::WithEditorState {
                inner: value.as_object().expect("checked by is_envelope"),
                editor,
                modal,
            };
            return if pretty {
                serde_json::to_string_pretty(&wrapped).unwrap()
            } else {
                serde_json::to_string(&wrapped).unwrap()
            };
        }
    }
    if pretty {
        serde_json::to_string_pretty(value).unwrap()
    } else {
        serde_json::to_string(value).unwrap()
    }
}

pub fn success_json(data: serde_json::Value) -> serde_json::Value {
    serde_json::json!({ "success": true, "data": data })
}
