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

pub fn print_json(value: &serde_json::Value) {
    println!("{}", render_json(value, true));
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
