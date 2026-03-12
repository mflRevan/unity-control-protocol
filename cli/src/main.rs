mod bridge_lifecycle;
mod client;
mod commands;
mod config;
mod discovery;
mod error;
mod output;
mod protocol;

use clap::Parser;
use tracing_subscriber::EnvFilter;

#[derive(Parser)]
#[command(
    name = "ucp",
    about = "Unity Control Protocol - programmatic Unity Editor control",
    version,
    propagate_version = true
)]
struct Cli {
    #[command(subcommand)]
    command: commands::Command,

    /// Unity project path (auto-detected if omitted)
    #[arg(long, global = true, env = "UCP_PROJECT")]
    project: Option<String>,

    /// Bridge port (read from lock file if omitted)
    #[arg(long, global = true, env = "UCP_PORT")]
    port: Option<u16>,

    /// Output as JSON
    #[arg(long, global = true)]
    json: bool,

    /// Command timeout in seconds
    #[arg(long, global = true, default_value = "30")]
    timeout: u64,

    /// Enable verbose logging
    #[arg(long, short, global = true)]
    verbose: bool,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Ensure UTF-8 output on Windows consoles
    #[cfg(windows)]
    unsafe {
        unsafe extern "system" {
            fn SetConsoleOutputCP(wCodePageID: u32) -> i32;
            fn SetConsoleCP(wCodePageID: u32) -> i32;
            fn GetStdHandle(nStdHandle: u32) -> *mut core::ffi::c_void;
            fn SetConsoleMode(hConsoleHandle: *mut core::ffi::c_void, dwMode: u32) -> i32;
            fn GetConsoleMode(hConsoleHandle: *mut core::ffi::c_void, lpMode: *mut u32) -> i32;
        }
        SetConsoleOutputCP(65001);
        SetConsoleCP(65001);
        // Enable virtual terminal processing for stdout and stderr
        // This makes modern terminals interpret ANSI/UTF-8 properly
        const STD_OUTPUT_HANDLE: u32 = 0xFFFF_FFF5;
        const STD_ERROR_HANDLE: u32 = 0xFFFF_FFF4;
        const ENABLE_VIRTUAL_TERMINAL_PROCESSING: u32 = 0x0004;
        for handle_id in [STD_OUTPUT_HANDLE, STD_ERROR_HANDLE] {
            let handle = GetStdHandle(handle_id);
            let mut mode: u32 = 0;
            if GetConsoleMode(handle, &mut mode) != 0 {
                SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
            }
        }
    }

    let cli = Cli::parse();

    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| {
                if cli.verbose {
                    EnvFilter::new("ucp=debug")
                } else {
                    EnvFilter::new("ucp=warn")
                }
            }),
        )
        .without_time()
        .init();

    let ctx = commands::Context {
        project: cli.project,
        port: cli.port,
        json: cli.json,
        timeout: cli.timeout,
        verbose: cli.verbose,
    };

    let json_output = ctx.json;
    if let Err(e) = commands::run(cli.command, ctx).await {
        if json_output {
            let err = serde_json::json!({
                "success": false,
                "error": { "message": format!("{e:#}") }
            });
            println!("{}", serde_json::to_string(&err).unwrap());
        } else {
            output::print_error(&format!("{e:#}"));
        }
        std::process::exit(1);
    }

    Ok(())
}
