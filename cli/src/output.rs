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

pub fn print_json(value: &serde_json::Value) {
    println!("{}", serde_json::to_string_pretty(value).unwrap());
}

pub fn print_json_compact(value: &serde_json::Value) {
    println!("{}", serde_json::to_string(value).unwrap());
}

pub fn success_json(data: serde_json::Value) -> serde_json::Value {
    serde_json::json!({ "success": true, "data": data })
}
