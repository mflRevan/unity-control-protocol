mod bridge_lifecycle;
mod bridge_package;
mod client;
mod commands;
mod config;
mod discovery;
mod editor_diagnosis;
mod editor_runtime;
mod editor_state;
mod error;
mod output;
mod protocol;
mod release_check;

use clap::Parser;
use tracing_subscriber::EnvFilter;

#[derive(Parser)]
#[command(
    name = "ucp",
    about = "Unity Control Protocol - programmatic Unity Editor control",
    long_about = "Unity Control Protocol (ucp) drives the Unity Editor over a local WebSocket/JSON-RPC \
bridge so scenes, GameObjects, assets, builds, and tests can be inspected and changed from scripts \
or headless agents without touching the Editor UI.\n\
\n\
Orientation:\n\
  - Object commands take an instance id from `ucp scene snapshot`. Ids are short-lived: they change \
after domain reloads, recompiles, and scene reloads, so re-snapshot before reusing one. Where a \
command also accepts `--path` or `--name`, those survive reloads.\n\
  - Run `ucp <command> --help` for any surface to see its subcommands, args, and value hints.\n\
  - Pass `--json` for machine-readable output suitable for parsing.\n\
  - Pass `--timeout 0` to wait indefinitely instead of failing after the default deadline.\n\
\n\
Full docs (human + machine-readable): https://unityctl.dev - per-page Markdown mirrors and \
https://unityctl.dev/llms.txt are available for AI agents.",
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

    /// Command timeout in seconds (default: 310 for UI renders, 30 otherwise); 0 waits indefinitely
    #[arg(long, global = true)]
    timeout: Option<u64>,

    /// Enable verbose logging
    #[arg(long, short, global = true)]
    verbose: bool,

    /// Policy for an outdated bridge package reference: auto (update silently),
    /// warn (report but proceed), off (ignore)
    #[arg(long, global = true, env = "UCP_BRIDGE_UPDATE_POLICY", value_enum)]
    bridge_update_policy: Option<config::BridgeUpdatePolicy>,

    /// Policy for Unity startup dialogs (Safe Mode, recovery): auto (resolve the
    /// usual way), manual (leave for a human), ignore (dismiss), recover (accept
    /// recovery), safe-mode (enter Safe Mode), cancel (decline)
    #[arg(long, global = true, env = "UCP_DIALOG_POLICY", value_enum)]
    dialog_policy: Option<config::StartupDialogPolicy>,
}

impl Cli {
    fn effective_timeout(&self) -> u64 {
        self.timeout.unwrap_or(match &self.command {
            commands::Command::Ui {
                action:
                    commands::ui::UiAction::Inspect { .. }
                    | commands::ui::UiAction::Screenshot { .. }
                    | commands::ui::UiAction::Check { .. },
            } => 310, // Allow the bridge's 300-second ceiling to report its result.
            _ => 30,
        })
    }
}

/// The command futures are large state machines (every bridge wait, dialog check, and retry
/// loop is inlined into them), and a debug build easily exceeds the 1 MB main-thread stack
/// Windows gives a process. Run the CLI on a thread with a generous stack instead of trusting
/// the default; the cost is one thread spawn.
fn main() -> anyhow::Result<()> {
    const STACK_BYTES: usize = 64 * 1024 * 1024;
    let handle = std::thread::Builder::new()
        .name("ucp-main".to_string())
        .stack_size(STACK_BYTES)
        .spawn(|| {
            tokio::runtime::Builder::new_multi_thread()
                .enable_all()
                .thread_stack_size(STACK_BYTES)
                .build()
                .expect("tokio runtime")
                .block_on(async_main())
        })
        .expect("spawn the CLI thread");
    match handle.join() {
        Ok(result) => result,
        Err(panic) => std::panic::resume_unwind(panic),
    }
}

async fn async_main() -> anyhow::Result<()> {
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
        // Diagnostics never belong on stdout: `--json` callers parse it.
        .with_writer(std::io::stderr)
        .init();

    let timeout = cli.effective_timeout();
    let project_arg = cli.project.clone();
    let dialog_policy = cli.dialog_policy.unwrap_or_default();
    let ctx = commands::Context {
        project: cli.project,
        port: cli.port,
        unity: cli.unity.or(cli_settings.unity_path),
        force_unity_version: cli.force_unity_version,
        json: cli.json,
        timeout,
        verbose: cli.verbose,
        bridge_update_policy: cli
            .bridge_update_policy
            .or(cli_settings.bridge_update_policy)
            .unwrap_or_default(),
        dialog_policy: cli.dialog_policy.unwrap_or_default(),
    };

    if !ctx.json && !matches!(cli.command, commands::Command::Doctor) {
        release_check::maybe_print_update_notice().await;
    }

    let json_output = ctx.json;
    let outcome = commands::run(cli.command, ctx).await;
    if let Err(e) = outcome {
        let e = explain_request_timeout(e, project_arg.as_deref(), dialog_policy);
        if json_output {
            let err = if let Some(test_run_failure) =
                e.downcast_ref::<commands::tests::TestRunFailure>()
            {
                serde_json::json!({
                    "success": false,
                    "error": { "message": test_run_failure.message },
                    "data": test_run_failure.result
                })
            } else if let Some(compile_failure) =
                e.downcast_ref::<commands::compile::CompileFailure>()
            {
                serde_json::json!({
                    "success": false,
                    "error": { "message": compile_failure.message },
                    "data": compile_failure.result
                })
            } else if let Some(ui_failure) = e.downcast_ref::<commands::ui::UiCommandFailure>() {
                serde_json::json!({
                    "success": false,
                    "error": { "message": ui_failure.message },
                    "data": ui_failure.result
                })
            } else {
                serde_json::json!({
                    "success": false,
                    "error": { "message": format!("{e:#}") }
                })
            };
            // Error envelopes go through the same appendix path as success envelopes.
            output::print_json_compact(&err);
        } else {
            output::print_error(&format!("{e:#}"));
            if let Some(line) = editor_state::summary_line() {
                output::print_state(&line);
            }
        }
        std::process::exit(1);
    }

    if !json_output {
        if let Some(line) = editor_state::summary_line() {
            output::print_state(&line);
        }
    }

    Ok(())
}

/// A request that timed out on the main thread may have been blocked by a dialog that opened
/// after the connect-time check. Look now: answer a recognised prompt so a retry goes through,
/// and name an unrecognised one so the agent can answer it deliberately.
fn explain_request_timeout(
    error: anyhow::Error,
    project_arg: Option<&str>,
    policy: config::StartupDialogPolicy,
) -> anyhow::Error {
    let Some(error::UcpError::RequestTimeout { .. }) = error.downcast_ref::<error::UcpError>()
    else {
        return error;
    };
    let Ok(project) = discovery::resolve_project(project_arg) else {
        return error;
    };

    let answered = discovery::answer_known_unity_dialogs(&project, policy).unwrap_or_default();
    let remaining = discovery::list_unity_dialogs(&project);
    let mut hints = Vec::new();
    if !answered.is_empty() {
        hints.push(format!(
            "Unity was showing a dialog; answered {}. Retry the command.",
            answered.join(", ")
        ));
    }
    if let Some(dialog) = remaining.first() {
        editor_state::note_modal(&dialog.title, &dialog.buttons);
        hints.push(format!(
            "Unity is showing the dialog \"{}\" [{}], which blocks every command. Answer it with `ucp editor dialog --answer \"<button>\"` (or in the editor), then retry.",
            dialog.title,
            dialog.buttons.join(" | ")
        ));
    }
    if hints.is_empty() {
        return error;
    }
    anyhow::anyhow!("{error:#}\n  {}", hints.join("\n  "))
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
    fn parses_object_get_children_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "object",
            "get-children",
            "--id",
            "-42",
            "--depth",
            "2",
        ])
        .expect("object get-children command should parse");

        match cli.command {
            commands::Command::Object {
                action: commands::object::ObjectAction::GetChildren { id, depth },
            } => {
                assert_eq!(id, -42);
                assert_eq!(depth, 2);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_scene_load_additive_command() {
        let cli = Cli::try_parse_from(["ucp", "scene", "load", "Assets/Main.unity", "--additive"])
            .expect("scene load additive command should parse");

        match cli.command {
            commands::Command::Scene {
                action:
                    commands::scene::SceneAction::Load {
                        path,
                        additive,
                        no_save,
                        keep_untitled,
                    },
            } => {
                assert_eq!(path, "Assets/Main.unity");
                assert!(additive);
                assert!(!no_save);
                assert!(!keep_untitled);
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
        let cli = Cli::try_parse_from([
            "ucp", "profiler", "summary", "--limit", "5", "--thread", "0",
        ])
        .expect("profiler summary command should parse");

        match cli.command {
            commands::Command::Profiler {
                action:
                    commands::profiler::ProfilerAction::Summary {
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
                action:
                    commands::profiler::ProfilerAction::Session {
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
                action: commands::packages::PackagesAction::Add { packages, no_wait },
            } => {
                assert_eq!(packages, vec!["com.unity.textmeshpro"]);
                assert!(!no_wait);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_packages_add_command_with_multiple_packages() {
        let cli = Cli::try_parse_from([
            "ucp",
            "packages",
            "add",
            "com.unity.a",
            "com.unity.b@1.2.3",
            "com.unity.c",
            "--no-wait",
        ])
        .expect("packages add should accept multiple packages");

        match cli.command {
            commands::Command::Packages {
                action: commands::packages::PackagesAction::Add { packages, no_wait },
            } => {
                assert_eq!(
                    packages,
                    vec!["com.unity.a", "com.unity.b@1.2.3", "com.unity.c"]
                );
                assert!(no_wait);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn packages_add_requires_at_least_one_package() {
        let result = Cli::try_parse_from(["ucp", "packages", "add"]);
        assert!(
            result.is_err(),
            "packages add with no packages should be rejected"
        );
    }

    #[test]
    fn parses_set_property_with_negative_value() {
        // Regression: `--value -55730` (e.g. an object-reference instance id) used to be parsed
        // as an unknown flag, forcing the `--value=-55730` workaround.
        let cli = Cli::try_parse_from([
            "ucp",
            "object",
            "set-property",
            "--id",
            "-5",
            "--component",
            "Rigidbody",
            "--property",
            "m_Mass",
            "--value",
            "-55730",
        ])
        .expect("negative --value should parse without the = workaround");

        match cli.command {
            commands::Command::Object {
                action:
                    commands::object::ObjectAction::SetProperty {
                        id,
                        property,
                        value,
                        ..
                    },
            } => {
                assert_eq!(id, -5);
                assert_eq!(property, "m_Mass");
                assert_eq!(value, "-55730");
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

    #[test]
    fn parses_logs_status_command() {
        let cli = Cli::try_parse_from(["ucp", "logs", "status"])
            .expect("logs status command should parse");

        match cli.command {
            commands::Command::Logs { action, .. } => {
                assert!(matches!(action, Some(commands::logs::LogsAction::Status)));
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_log_tail_follow_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "log",
            "tail",
            "--follow",
            "--filter",
            "level>=warning",
            "--filter",
            "channel=Shader",
        ])
        .expect("log tail command should parse");

        match cli.command {
            commands::Command::Log { action, .. } => match action {
                Some(commands::logs::LogsAction::Tail { args }) => {
                    assert!(args.follow);
                    assert_eq!(args.filters.len(), 2);
                }
                _ => panic!("unexpected logs action"),
            },
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_observability_commands() {
        let shader = Cli::try_parse_from(["ucp", "shader", "errors", "--errors-only"])
            .expect("shader errors command should parse");
        assert!(matches!(
            shader.command,
            commands::Command::Shader {
                action: commands::shader::ShaderAction::Errors {
                    errors_only: true,
                    ..
                }
            }
        ));

        let frame = Cli::try_parse_from(["ucp", "frame", "capture", "--out", "frame.json"])
            .expect("frame capture command should parse");
        assert!(matches!(
            frame.command,
            commands::Command::Frame {
                action: commands::FrameAction::Capture { .. }
            }
        ));

        let profile = Cli::try_parse_from(["ucp", "profile", "--seconds", "2"])
            .expect("profile command should parse");
        match profile.command {
            commands::Command::Profile { seconds, .. } => assert_eq!(seconds, 2),
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_record_capture_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "record",
            "capture",
            "--view",
            "scene",
            "--duration",
            "3.5",
            "--max-edge",
            "720",
            "--fps",
            "12",
            "--output",
            "clip.webm",
            "--format",
            "webm",
        ])
        .expect("record capture command should parse");

        match cli.command {
            commands::Command::Record {
                action:
                    commands::record::RecordAction::Capture {
                        settings,
                        duration,
                        output,
                    },
            } => {
                assert_eq!(settings.view, "scene");
                assert_eq!(settings.max_edge, 720);
                assert_eq!(settings.fps, 12);
                assert_eq!(settings.format, "webm");
                assert_eq!(duration, 3.5);
                assert_eq!(output.as_deref(), Some("clip.webm"));
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_record_arm_and_exec_recording_commands() {
        let armed = Cli::try_parse_from([
            "ucp",
            "record",
            "arm",
            "--on",
            "signal:boss-spawned",
            "--duration",
            "5",
        ])
        .expect("record arm command should parse");
        assert!(matches!(
            armed.command,
            commands::Command::Record {
                action: commands::record::RecordAction::Arm { .. }
            }
        ));

        let exec = Cli::try_parse_from([
            "ucp",
            "exec",
            "run",
            "RunDemo",
            "--record",
            "demo.mp4",
            "--record-view",
            "game",
            "--record-bitrate-kbps",
            "1500",
            "--record-overwrite",
        ])
        .expect("exec recording flags should parse");
        match exec.command {
            commands::Command::Exec {
                action:
                    commands::ExecAction::Run {
                        record,
                        record_view,
                        record_bitrate_kbps,
                        record_overwrite,
                        ..
                    },
            } => {
                assert_eq!(record.as_deref(), Some("demo.mp4"));
                assert_eq!(record_view, "game");
                assert_eq!(record_bitrate_kbps, 1500);
                assert!(record_overwrite);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn rejects_invalid_record_trigger() {
        assert!(
            Cli::try_parse_from([
                "ucp",
                "record",
                "arm",
                "--on",
                "anything",
                "--duration",
                "5",
            ])
            .is_err()
        );
    }

    #[test]
    fn parses_query_inspect_and_script_doctor_commands() {
        let scene = Cli::try_parse_from([
            "ucp",
            "scene",
            "query",
            "name=XRCamera",
            "--fields",
            "active,components",
        ])
        .expect("scene query command should parse");
        assert!(matches!(
            scene.command,
            commands::Command::Scene {
                action: commands::scene::SceneAction::Query { .. }
            }
        ));

        let asset = Cli::try_parse_from(["ucp", "asset", "inspect", "Assets/Materials/Foo.mat"])
            .expect("asset inspect command should parse");
        assert!(matches!(
            asset.command,
            commands::Command::Asset {
                action: commands::asset::AssetAction::Inspect { .. }
            }
        ));

        let script = Cli::try_parse_from(["ucp", "script", "doctor", "--fix"])
            .expect("script doctor command should parse");
        assert!(matches!(
            script.command,
            commands::Command::Script {
                action: commands::script::ScriptAction::Doctor { fix: true }
            }
        ));
    }

    #[test]
    fn parses_references_find_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "references",
            "find",
            "--asset",
            "3cb6f81f1baa99647b390eb642d1990c",
        ])
        .expect("references find command should parse");

        match cli.command {
            commands::Command::References {
                action: commands::references::ReferencesAction::Find { asset, object, .. },
            } => {
                assert_eq!(asset.as_deref(), Some("3cb6f81f1baa99647b390eb642d1990c"));
                assert!(object.is_none());
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_references_index_build_command() {
        let cli =
            Cli::try_parse_from(["ucp", "references", "index", "build", "--approach", "yaml"])
                .expect("references index build command should parse");

        match cli.command {
            commands::Command::References {
                action:
                    commands::references::ReferencesAction::Index {
                        action: commands::references::IndexAction::Build { approach },
                    },
            } => {
                assert_eq!(approach, commands::references::IndexBuildApproachArg::Yaml);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_references_check_command() {
        let cli = Cli::try_parse_from(["ucp", "references", "check", "Assets/Scenes"])
            .expect("references check should parse");

        match cli.command {
            commands::Command::References {
                action: commands::references::ReferencesAction::Check { path },
            } => assert_eq!(path.as_deref(), Some("Assets/Scenes")),
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_references_find_strings_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "references",
            "find-strings",
            "--pattern",
            "SCN_",
            "--path",
            "Assets/Configs",
            "--regex",
        ])
        .expect("references find-strings should parse");

        match cli.command {
            commands::Command::References {
                action:
                    commands::references::ReferencesAction::FindStrings {
                        pattern,
                        path,
                        regex,
                        max_files,
                        max_per_file,
                    },
            } => {
                assert_eq!(pattern, "SCN_");
                assert_eq!(path.as_deref(), Some("Assets/Configs"));
                assert!(regex);
                assert_eq!(max_files, 20);
                assert_eq!(max_per_file, 5);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn rejects_invalid_references_detail_value() {
        let result = Cli::try_parse_from([
            "ucp",
            "references",
            "find",
            "--asset",
            "3cb6f81f1baa99647b390eb642d1990c",
            "--detail",
            "full",
        ]);

        assert!(result.is_err());
    }

    #[test]
    fn parses_asset_move_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "asset",
            "move",
            "Assets/Old.prefab",
            "Assets/New/Old.prefab",
        ])
        .expect("asset move command should parse");

        match cli.command {
            commands::Command::Asset {
                action: commands::asset::AssetAction::Move { path, destination },
            } => {
                assert_eq!(path, "Assets/Old.prefab");
                assert_eq!(destination, "Assets/New/Old.prefab");
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_asset_bulk_move_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "asset",
            "bulk-move",
            "--moves",
            "[{\"from\":\"Assets/A.mat\",\"to\":\"Assets/B.mat\"}]",
            "--continue-on-error",
        ])
        .expect("asset bulk-move command should parse");

        match cli.command {
            commands::Command::Asset {
                action:
                    commands::asset::AssetAction::BulkMove {
                        moves,
                        continue_on_error,
                        dry_run,
                    },
            } => {
                assert!(moves.contains("\"from\""));
                assert!(continue_on_error);
                assert!(!dry_run);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_asset_search_regex_command() {
        let cli =
            Cli::try_parse_from(["ucp", "asset", "search", "--name", "^SCN_\\d+$", "--regex"])
                .expect("asset search regex command should parse");

        match cli.command {
            commands::Command::Asset {
                action: commands::asset::AssetAction::Search { name, regex, .. },
            } => {
                assert_eq!(name.as_deref(), Some("^SCN_\\d+$"));
                assert!(regex);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_asset_bulk_move_dry_run_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "asset",
            "bulk-move",
            "--moves",
            "{\"Assets/A.mat\":\"Assets/B.mat\"}",
            "--dry-run",
        ])
        .expect("asset bulk-move dry-run should parse");

        match cli.command {
            commands::Command::Asset {
                action:
                    commands::asset::AssetAction::BulkMove {
                        dry_run,
                        continue_on_error,
                        ..
                    },
            } => {
                assert!(dry_run);
                assert!(!continue_on_error);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_asset_reimport_recursive_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "asset",
            "reimport",
            "Assets/Generated",
            "--recursive",
        ])
        .expect("asset reimport recursive should parse");

        match cli.command {
            commands::Command::Asset {
                action: commands::asset::AssetAction::Reimport { path, recursive },
            } => {
                assert_eq!(path, "Assets/Generated");
                assert!(recursive);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn ui_render_timeout_defaults_allow_the_bridge_ceiling_and_preserve_overrides() {
        for action in ["inspect", "screenshot", "check"] {
            let args = ["ucp", "ui", action, "Assets/Panel.uxml"];
            assert_eq!(Cli::try_parse_from(args).unwrap().effective_timeout(), 310);
            for explicit in ["0", "30", "60"] {
                let mut args = args.to_vec();
                args.extend(["--timeout", explicit]);
                assert_eq!(
                    Cli::try_parse_from(args).unwrap().effective_timeout(),
                    explicit.parse::<u64>().unwrap()
                );
            }
        }
        for args in [
            vec!["ucp", "ui", "list"],
            vec!["ucp", "ui", "lint", "Assets/UI"],
            vec!["ucp", "doctor"],
        ] {
            assert_eq!(Cli::try_parse_from(args).unwrap().effective_timeout(), 30);
        }
        assert_eq!(
            Cli::try_parse_from([
                "ucp",
                "--timeout",
                "30",
                "ui",
                "check",
                "Assets/Panel.uxml",
                "--all-states"
            ])
            .unwrap()
            .effective_timeout(),
            30
        );
    }

    #[test]
    fn parses_ui_inspect_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "ui",
            "inspect",
            "Assets/UI/Inventory.ucp-ui.json",
            "--state",
            "populated",
            "--data-json",
            r#"{"title":"Inventory"}"#,
            "--width",
            "1280",
            "--height",
            "720",
            "--query",
            "#cards",
            "--depth",
            "8",
            "--max-elements",
            "250",
            "--detail",
            "verbose",
        ])
        .expect("UI inspect command should parse");

        match cli.command {
            commands::Command::Ui {
                action:
                    commands::ui::UiAction::Inspect {
                        target,
                        query,
                        depth,
                        max_elements,
                        detail,
                    },
            } => {
                assert_eq!(target.target, "Assets/UI/Inventory.ucp-ui.json");
                assert_eq!(target.state.as_deref(), Some("populated"));
                assert!(target.data_json.is_some());
                assert_eq!((target.width, target.height), (Some(1280), Some(720)));
                assert_eq!(query.as_deref(), Some("#cards"));
                assert_eq!(depth, 8);
                assert_eq!(max_elements, 250);
                assert_eq!(detail, commands::ui::UiDetailArg::Verbose);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_ui_check_all_states_command() {
        let cli = Cli::try_parse_from([
            "ucp",
            "ui",
            "check",
            "Assets/UI/Inventory.ucp-ui.json",
            "--all-states",
            "--out-dir",
            "artifacts/ui",
            "--fail-on-warnings",
            "--force",
        ])
        .expect("UI check command should parse");

        match cli.command {
            commands::Command::Ui {
                action:
                    commands::ui::UiAction::Check {
                        all_states,
                        out_dir,
                        fail_on_warnings,
                        force,
                        ..
                    },
            } => {
                assert!(all_states);
                assert_eq!(
                    out_dir.as_deref(),
                    Some(std::path::Path::new("artifacts/ui"))
                );
                assert!(fail_on_warnings);
                assert!(force);
            }
            _ => panic!("unexpected command variant"),
        }
    }

    #[test]
    fn parses_ui_list_lint_and_screenshot_commands() {
        let list = Cli::try_parse_from([
            "ucp",
            "ui",
            "list",
            "--root",
            "Assets/UI",
            "--include-packages",
            "--limit",
            "25",
        ])
        .expect("UI list command should parse");
        assert!(matches!(
            list.command,
            commands::Command::Ui {
                action: commands::ui::UiAction::List {
                    root,
                    include_packages: true,
                    limit: 25,
                }
            } if root == "Assets/UI"
        ));

        let lint = Cli::try_parse_from([
            "ucp",
            "ui",
            "lint",
            "Assets/UI/Panel.uxml",
            "Assets/UI/Panel.uss",
            "--fail-on-warnings",
            "--max-diagnostics",
            "20",
        ])
        .expect("UI lint command should parse");
        assert!(matches!(
            lint.command,
            commands::Command::Ui {
                action: commands::ui::UiAction::Lint {
                    paths,
                    fail_on_warnings: true,
                    max_diagnostics: 20,
                }
            } if paths.len() == 2
        ));

        let screenshot = Cli::try_parse_from([
            "ucp",
            "ui",
            "screenshot",
            "Assets/UI/Panel.uxml",
            "--out",
            "panel.png",
            "--force",
        ])
        .expect("UI screenshot command should parse");
        assert!(matches!(
            screenshot.command,
            commands::Command::Ui {
                action: commands::ui::UiAction::Screenshot {
                    out: Some(_),
                    force: true,
                    ..
                }
            }
        ));
    }

    #[test]
    fn rejects_conflicting_ui_data_and_state_options() {
        let conflicting_data = Cli::try_parse_from([
            "ucp",
            "ui",
            "screenshot",
            "Assets/UI/Panel.uxml",
            "--data-json",
            "{}",
            "--data-file",
            "data.json",
        ]);
        assert!(conflicting_data.is_err());

        let conflicting_states = Cli::try_parse_from([
            "ucp",
            "ui",
            "check",
            "Assets/UI/Panel.ucp-ui.json",
            "--state",
            "empty",
            "--all-states",
        ]);
        assert!(conflicting_states.is_err());

        let incomplete_viewport = Cli::try_parse_from([
            "ucp",
            "ui",
            "inspect",
            "Assets/UI/Panel.uxml",
            "--width",
            "800",
        ]);
        assert!(incomplete_viewport.is_err());
    }

    #[test]
    fn ui_force_requires_an_output_target() {
        assert!(
            Cli::try_parse_from(["ucp", "ui", "screenshot", "Assets/UI/Panel.uxml", "--force"])
                .is_err()
        );
        assert!(
            Cli::try_parse_from(["ucp", "ui", "check", "Assets/UI/Panel.uxml", "--force"]).is_err()
        );
        assert!(
            Cli::try_parse_from([
                "ucp",
                "ui",
                "check",
                "Assets/UI/Panel.uxml",
                "--out-dir",
                "artifacts",
                "--force"
            ])
            .is_ok()
        );
    }
}
