mod bridge_lifecycle;
mod bridge_package;
mod client;
mod commands;
mod config;
mod discovery;
mod editor_runtime;
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

    /// Path to the Unity Editor executable to use when UCP launches the editor
    #[arg(long, global = true, env = "UCP_UNITY")]
    unity: Option<String>,

    /// Force UCP to launch the project with a specific installed Unity editor version
    #[arg(long, global = true, env = "UCP_FORCE_UNITY_VERSION")]
    force_unity_version: Option<String>,

    /// Output as JSON
    #[arg(long, global = true)]
    json: bool,

    /// Command timeout in seconds
    #[arg(long, global = true, default_value = "30")]
    timeout: u64,

    /// Enable verbose logging
    #[arg(long, short, global = true)]
    verbose: bool,

    /// Policy for handling outdated bridge package references
    #[arg(long, global = true, env = "UCP_BRIDGE_UPDATE_POLICY", value_enum)]
    bridge_update_policy: Option<config::BridgeUpdatePolicy>,

    /// Policy for handling Unity startup dialogs such as Safe Mode or recovery prompts
    #[arg(long, global = true, env = "UCP_DIALOG_POLICY", value_enum)]
    dialog_policy: Option<config::StartupDialogPolicy>,
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
    let cli_settings = config::load_cli_settings();

    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| {
            if cli.verbose {
                EnvFilter::new("ucp=debug")
            } else {
                EnvFilter::new("ucp=warn")
            }
        }))
        .without_time()
        .init();

    let ctx = commands::Context {
        project: cli.project,
        port: cli.port,
        unity: cli.unity.or(cli_settings.unity_path),
        force_unity_version: cli.force_unity_version,
        json: cli.json,
        timeout: cli.timeout,
        verbose: cli.verbose,
        bridge_update_policy: cli
            .bridge_update_policy
            .or(cli_settings.bridge_update_policy)
            .unwrap_or_default(),
        dialog_policy: cli.dialog_policy.unwrap_or_default(),
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_grouped_files_read_command() {
        let cli = Cli::try_parse_from(["ucp", "files", "read", "Assets/Scripts/Player.cs"])
            .expect("grouped files read command should parse");

        match cli.command {
            commands::Command::Files {
                action: commands::files::FilesAction::Read { path },
            } => assert_eq!(path, "Assets/Scripts/Player.cs"),
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_scene_snapshot_command() {
        let cli = Cli::try_parse_from(["ucp", "scene", "snapshot", "--depth", "2"])
            .expect("scene snapshot command should parse");

        match cli.command {
            commands::Command::Scene {
                action: commands::scene::SceneAction::Snapshot { filter, depth },
            } => {
                assert!(filter.is_none());
                assert_eq!(depth, 2);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_scene_focus_command_with_axis() {
        let cli = Cli::try_parse_from([
            "ucp", "scene", "focus", "--id", "-42", "--axis", "1", "0.5", "-1",
        ])
        .expect("scene focus command should parse");

        match cli.command {
            commands::Command::Scene {
                action: commands::scene::SceneAction::Focus { id, axis },
            } => {
                assert_eq!(id, -42);
                assert_eq!(axis.unwrap(), vec![1.0, 0.5, -1.0]);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_top_level_open_command() {
        let cli = Cli::try_parse_from(["ucp", "open"]).expect("open command should parse");

        match cli.command {
            commands::Command::Open => {}
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_profiler_summary_command() {
        let cli =
            Cli::try_parse_from(["ucp", "profiler", "summary", "--limit", "5", "--thread", "0"])
                .expect("profiler summary command should parse");

        match cli.command {
            commands::Command::Profiler {
                action: commands::profiler::ProfilerAction::Summary {
                    first_frame,
                    last_frame,
                    thread,
                    limit,
                },
            } => {
                assert!(first_frame.is_none());
                assert!(last_frame.is_none());
                assert_eq!(thread, Some(0));
                assert_eq!(limit, 5);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_profiler_session_start_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "profiler",
            "session",
            "start",
            "--mode",
            "play",
            "--deep-profile",
            "true",
            "--enable-category",
            "Scripts",
        ])
        .expect("profiler session start command should parse");

        match cli.command {
            commands::Command::Profiler {
                action: commands::profiler::ProfilerAction::Session {
                    action:
                        commands::profiler::ProfilerSessionAction::Start {
                            mode,
                            deep_profile,
                            enable_categories,
                            ..
                        },
                },
            } => {
                assert_eq!(mode.as_deref(), Some("play"));
                assert_eq!(deep_profile, Some(true));
                assert_eq!(enable_categories, vec!["Scripts"]);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_packages_add_command() {
        let cli = Cli::try_parse_from(["ucp", "packages", "add", "com.unity.textmeshpro"])
            .expect("packages add command should parse");

        match cli.command {
            commands::Command::Packages {
                action: commands::packages::PackagesAction::Add { package, no_wait },
            } => {
                assert_eq!(package, "com.unity.textmeshpro");
                assert!(!no_wait);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_unitypackage_import_command_with_selection() {
        let cli = Cli::try_parse_from([
            "ucp",
            "packages",
            "unitypackage",
            "import",
            "bundle.unitypackage",
            "--select",
            "Assets/Keep",
            "--unselect",
            "Assets/Keep/Skip",
        ])
        .expect("unitypackage import command should parse");

        match cli.command {
            commands::Command::Packages {
                action:
                    commands::packages::PackagesAction::Unitypackage {
                        action:
                            commands::packages::UnitypackageAction::Import {
                                archive,
                                select,
                                unselect,
                                ..
                            },
                    },
            } => {
                assert_eq!(archive, "bundle.unitypackage");
                assert_eq!(select, vec!["Assets/Keep"]);
                assert_eq!(unselect, vec!["Assets/Keep/Skip"]);
            }
            _ => panic!("unexpected command variant"),
        }
    }
}
