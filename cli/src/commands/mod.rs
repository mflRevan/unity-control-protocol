pub mod asset;
pub mod bridge;
pub mod build;
pub mod compile;
pub mod connect;
pub mod doctor;
pub mod editor;
pub mod exec;
pub mod files;
pub mod install;
pub mod logs;
pub mod material;
pub mod object;
pub mod play;
pub mod profiler;
pub mod prefab;
pub mod scene;
pub mod screenshot;
pub mod settings;
pub mod snapshot;
pub mod tests;
pub mod vcs;

use crate::bridge_lifecycle::{self, WaitMode, WaitStatus};
use crate::bridge_package;
use crate::client::BridgeClient;
use crate::config::{self, LockFile};
use crate::discovery;
use crate::editor_runtime;
use crate::output;
use clap::Subcommand;
use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct Context {
    pub project: Option<String>,
    #[allow(dead_code)]
    pub port: Option<u16>,
    pub unity: Option<String>,
    pub force_unity_version: Option<String>,
    pub json: bool,
    pub timeout: u64,
    #[allow(dead_code)]
    pub verbose: bool,
    pub bridge_update_policy: config::BridgeUpdatePolicy,
    pub dialog_policy: config::StartupDialogPolicy,
}

#[derive(Subcommand)]
pub enum Command {
    /// Install the UCP bridge package into a Unity project
    Install {
        /// Path to Unity project (defaults to current directory)
        path: Option<String>,
        /// Force a local-only embedded install using the best available local bridge payload
        #[arg(long)]
        embedded: bool,
        /// Force a tracked manifest dependency install
        #[arg(long)]
        manifest: bool,
        /// Mount the bridge from the local repository package path as a local-only embedded package
        #[arg(long)]
        dev: bool,
        /// Mount the bridge from a local package path as a local-only embedded package
        #[arg(long, value_name = "PATH")]
        bridge_path: Option<String>,
        /// Install the bridge from an explicit manifest reference
        #[arg(long, value_name = "REF")]
        bridge_ref: Option<String>,
        /// Skip waiting for Unity to import and restart the bridge
        #[arg(long)]
        no_wait: bool,
        /// Ask for interactive confirmation before installation
        #[arg(long)]
        confirm: bool,
    },
    /// Uninstall the UCP bridge package
    Uninstall,
    /// Check CLI and bridge health
    Doctor,
    /// Test connection to the bridge
    Connect,
    /// Manage the Unity editor process lifecycle
    Editor {
        #[command(subcommand)]
        action: editor::EditorAction,
    },
    /// Update or inspect the UCP bridge package reference
    Bridge {
        #[command(subcommand)]
        action: bridge::BridgeAction,
    },
    /// Open the Unity editor for the detected project
    Open,
    /// Close the Unity editor for the detected project
    Close,
    /// Enter play mode
    Play {
        /// Do not auto-save dirty scenes before entering play mode
        #[arg(long)]
        no_save: bool,
        /// Keep dirty untitled scenes instead of discarding them when auto-save runs
        #[arg(long)]
        keep_untitled: bool,
    },
    /// Exit play mode
    Stop,
    /// Toggle pause
    Pause,
    /// Trigger recompilation (blocks until done by default)
    Compile {
        /// Return immediately without waiting for compilation to finish
        #[arg(long)]
        no_wait: bool,
    },
    /// Scene management
    Scene {
        #[command(subcommand)]
        action: scene::SceneAction,
    },
    /// Project file operations
    Files {
        #[command(subcommand)]
        action: files::FilesAction,
    },
    /// Capture a screenshot
    Screenshot {
        /// View to capture: game or scene
        #[arg(long, default_value = "game")]
        view: String,
        /// Width in pixels
        #[arg(long, default_value = "1920")]
        width: u32,
        /// Height in pixels
        #[arg(long, default_value = "1080")]
        height: u32,
        /// Output file path (base64 to stdout if omitted)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Stream console logs
    Logs {
        /// Filter by level: info, warn, error
        #[arg(long)]
        level: Option<String>,
        /// Read up to N buffered logs when searching or tailing, or stop after N live logs when following
        #[arg(long)]
        count: Option<u32>,
        /// Search buffered logs by regex pattern
        #[arg(long)]
        pattern: Option<String>,
        /// Read one buffered log entry by id
        #[arg(long)]
        id: Option<u64>,
        /// Restrict buffered reads to log ids lower than this value
        #[arg(long, value_name = "ID")]
        before_id: Option<u64>,
        /// Restrict buffered reads to log ids higher than this value
        #[arg(long, value_name = "ID")]
        after_id: Option<u64>,
        /// Follow live log notifications instead of reading buffered history
        #[arg(long)]
        follow: bool,
    },
    /// Run tests
    RunTests {
        /// Test mode: edit or play
        #[arg(long, default_value = "edit")]
        mode: String,
        /// Filter test names
        #[arg(long)]
        filter: Option<String>,
    },
    /// Execute a UCP script (Playwright-like editor automation)
    Exec {
        #[command(subcommand)]
        action: ExecAction,
    },
    /// Version control (Unity VCS / Plastic SCM)
    Vcs {
        #[command(subcommand)]
        action: vcs::VcsAction,
    },
    /// Inspect and modify GameObjects, components, and properties
    Object {
        #[command(subcommand)]
        action: object::ObjectAction,
    },
    /// Search and manage project assets
    Asset {
        #[command(subcommand)]
        action: asset::AssetAction,
    },
    /// Read and modify project settings
    Settings {
        #[command(subcommand)]
        action: settings::SettingsAction,
    },
    /// Inspect and modify materials
    Material {
        #[command(subcommand)]
        action: material::MaterialAction,
    },
    /// Prefab operations (status, apply, revert, unpack, create)
    Prefab {
        #[command(subcommand)]
        action: prefab::PrefabAction,
    },
    /// Build pipeline operations
    Build {
        #[command(subcommand)]
        action: build::BuildAction,
    },
    /// Unity profiler workflows and inspection
    Profiler {
        #[command(subcommand)]
        action: profiler::ProfilerAction,
    },
}

#[derive(Subcommand)]
pub enum ExecAction {
    /// List available scripts
    List,
    /// Run a named script
    Run {
        /// Script name
        name: String,
        /// JSON parameters
        #[arg(long)]
        params: Option<String>,
    },
}

pub fn resolve_project_path(ctx: &Context) -> anyhow::Result<PathBuf> {
    Ok(discovery::resolve_project(ctx.project.as_deref())?)
}

pub async fn ensure_bridge_ready(ctx: &Context) -> anyhow::Result<(PathBuf, LockFile)> {
    let project = resolve_project_path(ctx)?;
    let _ = bridge_package::apply_update_policy(&project, ctx, true).await?;

    let previous_lock = discovery::read_lock_file(&project).ok();
    let start_outcome = editor_runtime::ensure_editor_running(&project, ctx).await?;
    let mut lock = discovery::read_lock_file(&project).ok();

    if start_outcome.started || lock.is_none() {
        if !ctx.json {
            output::print_info("Waiting for Unity bridge...");
        }

        let wait_outcome = bridge_lifecycle::wait_for_bridge(
            &project,
            previous_lock.as_ref(),
            ctx.timeout,
            ctx.dialog_policy,
            if previous_lock.is_some() {
                WaitMode::RestartOptional
            } else {
                WaitMode::FirstAvailable
            },
        )
        .await?;

        if matches!(wait_outcome.status, WaitStatus::EditorNotRunning) {
            anyhow::bail!("Unity editor is not running for {}", project.display());
        }

        lock = Some(discovery::read_lock_file(&project)?);
    }

    let lock = match lock {
        Some(lock) => lock,
        None => discovery::read_lock_file(&project)?,
    };

    Ok((project, lock))
}

pub async fn connect_client(ctx: &Context) -> anyhow::Result<(PathBuf, LockFile, BridgeClient)> {
    let (project, lock) = ensure_bridge_ready(ctx).await?;

    if let Ok(mut client) = BridgeClient::connect(&lock).await {
        if client.handshake().await.is_ok() {
            return Ok((project, lock, client));
        }
        client.close().await;
    }

    let wait_outcome = bridge_lifecycle::wait_for_bridge(
        &project,
        Some(&lock),
        ctx.timeout,
        ctx.dialog_policy,
        WaitMode::RestartOptional,
    )
    .await?;

    if matches!(wait_outcome.status, WaitStatus::EditorNotRunning) {
        anyhow::bail!(
            "Unity editor exited before the bridge became available for {}",
            project.display()
        );
    }

    let lock = discovery::read_lock_file(&project)?;
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;
    Ok((project, lock, client))
}

pub async fn run(cmd: Command, ctx: Context) -> anyhow::Result<()> {
    match cmd {
        Command::Install {
            path,
            embedded,
            manifest,
            dev,
            bridge_path,
            bridge_ref,
            no_wait,
            confirm,
        } => {
            let options = install::InstallOptions {
                embedded,
                manifest,
                dev,
                bridge_path,
                bridge_ref,
                no_wait,
                confirm,
            };
            install::run(path.or(ctx.project.clone()), options, &ctx).await
        }
        Command::Uninstall => install::uninstall(&ctx).await,
        Command::Doctor => doctor::run(&ctx).await,
        Command::Connect => connect::run(&ctx).await,
        Command::Editor { action } => editor::run(action, &ctx).await,
        Command::Bridge { action } => bridge::run(action, &ctx).await,
        Command::Open => editor::run(editor::EditorAction::Open, &ctx).await,
        Command::Close => editor::run(editor::EditorAction::Close { force: false }, &ctx).await,
        Command::Play {
            no_save,
            keep_untitled,
        } => {
            let payload = serde_json::json!({
                "saveDirtyScenes": !no_save,
                "discardUntitled": !keep_untitled,
            });
            play::run("play", payload, &ctx).await
        }
        Command::Stop => play::run("stop", serde_json::json!({}), &ctx).await,
        Command::Pause => play::run("pause", serde_json::json!({}), &ctx).await,
        Command::Compile { no_wait } => compile::run(no_wait, &ctx).await,
        Command::Scene { action } => scene::run(action, &ctx).await,
        Command::Files { action } => files::run(action, &ctx).await,
        Command::Screenshot {
            view,
            width,
            height,
            output,
        } => screenshot::run(&view, width, height, output, &ctx).await,
        Command::Logs {
            level,
            count,
            pattern,
            id,
            before_id,
            after_id,
            follow,
        } => logs::run(level, count, pattern, id, before_id, after_id, follow, &ctx).await,
        Command::RunTests { mode, filter } => tests::run(&mode, filter, &ctx).await,
        Command::Exec { action } => match action {
            ExecAction::List => exec::list(&ctx).await,
            ExecAction::Run { name, params } => exec::run(&name, params, &ctx).await,
        },
        Command::Vcs { action } => vcs::run(action, &ctx).await,
        Command::Object { action } => object::run(action, &ctx).await,
        Command::Asset { action } => asset::run(action, &ctx).await,
        Command::Settings { action } => settings::run(action, &ctx).await,
        Command::Material { action } => material::run(action, &ctx).await,
        Command::Prefab { action } => prefab::run(action, &ctx).await,
        Command::Build { action } => build::run(action, &ctx).await,
        Command::Profiler { action } => profiler::run(action, &ctx).await,
    }
}
