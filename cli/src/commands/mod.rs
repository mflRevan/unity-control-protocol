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
pub mod packages;
pub mod play;
pub mod profiler;
pub mod prefab;
pub mod scene;
pub mod screenshot;
pub mod settings;
pub mod snapshot;
pub mod tests;
pub mod vcs;

use crate::bridge_lifecycle::{self, EditorSettleStatus, WaitMode, WaitStatus};
use crate::bridge_package;
use crate::client::BridgeClient;
use crate::config::{self, LockFile};
use crate::discovery;
use crate::editor_runtime;
use crate::error::UcpError;
use crate::output;
use clap::Subcommand;
use serde::Deserialize;
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

/// Standard post-action lifecycle policy for Unity-facing commands.
///
/// Every command should classify itself into one of these buckets:
/// - `None` for read-only operations or commands with their own bespoke confirmation loop
/// - `EditorSettle` for mutations that can trigger asset refresh, metadata generation,
///   serialization, or other editor-side background processing
/// - `RestartThenSettle` for mutations that can restart the bridge or trigger domain reloads,
///   such as package changes, script recompilation, build target switches, or define changes
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UnityLifecyclePolicy {
    None,
    EditorSettle {
        progress_message: &'static str,
        failure_context: &'static str,
        min_timeout_secs: u64,
    },
    RestartThenSettle {
        progress_message: &'static str,
        failure_context: &'static str,
        min_timeout_secs: u64,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnityLifecycleOutcome {
    pub bridge_status: Option<WaitStatus>,
    pub editor_status: Option<EditorSettleStatus>,
    pub log_status: Option<serde_json::Value>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ActiveSceneGuardPolicy {
    None,
    BlockIfDirty {
        command_label: &'static str,
        max_entries: usize,
    },
}

impl ActiveSceneGuardPolicy {
    pub const fn block_if_dirty(command_label: &'static str) -> Self {
        Self::BlockIfDirty {
            command_label,
            max_entries: 8,
        }
    }
}

#[derive(Debug, Deserialize)]
struct ActiveSceneDirtySummary {
    #[serde(rename = "isDirty")]
    is_dirty: bool,
    name: String,
    #[serde(default)]
    path: String,
    #[serde(default, rename = "modifications")]
    changes: Vec<ActiveSceneDirtyChange>,
    #[serde(default, rename = "omittedCount")]
    omitted_count: usize,
}

#[derive(Debug, Deserialize)]
struct ActiveSceneDirtyChange {
    #[serde(rename = "instanceId")]
    instance_id: Option<i64>,
    name: String,
    #[serde(default)]
    components: Vec<String>,
}

impl UnityLifecyclePolicy {
    pub const fn editor_settle(
        progress_message: &'static str,
        failure_context: &'static str,
    ) -> Self {
        Self::EditorSettle {
            progress_message,
            failure_context,
            min_timeout_secs: 5,
        }
    }

    pub const fn editor_settle_with_timeout(
        progress_message: &'static str,
        failure_context: &'static str,
        min_timeout_secs: u64,
    ) -> Self {
        Self::EditorSettle {
            progress_message,
            failure_context,
            min_timeout_secs,
        }
    }

    pub const fn restart_then_settle(
        progress_message: &'static str,
        failure_context: &'static str,
        min_timeout_secs: u64,
    ) -> Self {
        Self::RestartThenSettle {
            progress_message,
            failure_context,
            min_timeout_secs,
        }
    }
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
        #[command(subcommand)]
        action: Option<logs::LogsAction>,
        #[command(flatten)]
        args: logs::LogsArgs,
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
    /// Unity package management and .unitypackage import workflows
    Packages {
        #[command(subcommand)]
        action: packages::PackagesAction,
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

pub async fn await_unity_lifecycle(
    project: &std::path::Path,
    previous_lock: Option<&LockFile>,
    policy: UnityLifecyclePolicy,
    ctx: &Context,
) -> anyhow::Result<UnityLifecycleOutcome> {
    let (progress_message, failure_context, min_timeout_secs, restart_first) = match policy {
        UnityLifecyclePolicy::None => {
            return Ok(UnityLifecycleOutcome {
                bridge_status: None,
                editor_status: None,
                log_status: None,
            });
        }
        UnityLifecyclePolicy::EditorSettle {
            progress_message,
            failure_context,
            min_timeout_secs,
        } => (progress_message, failure_context, min_timeout_secs, false),
        UnityLifecyclePolicy::RestartThenSettle {
            progress_message,
            failure_context,
            min_timeout_secs,
        } => (progress_message, failure_context, min_timeout_secs, true),
    };

    if !ctx.json {
        output::print_info(progress_message);
    }

    let timeout_secs = ctx.timeout.max(min_timeout_secs);
    let mut bridge_status = None;

    if restart_first {
        let wait_outcome = bridge_lifecycle::wait_for_bridge(
            project,
            previous_lock,
            timeout_secs,
            ctx.dialog_policy,
            if previous_lock.is_some() {
                WaitMode::RestartOptional
            } else {
                WaitMode::FirstAvailable
            },
        )
        .await?;

        if matches!(wait_outcome.status, WaitStatus::EditorNotRunning) {
            anyhow::bail!(
                "Unity editor exited before {} finished for {}",
                failure_context,
                project.display()
            );
        }

        bridge_status = Some(wait_outcome.status);
    }

    let settle = bridge_lifecycle::wait_for_editor_settle(
        project,
        previous_lock,
        timeout_secs,
        ctx.dialog_policy,
    )
    .await?;

    if matches!(settle.status, EditorSettleStatus::EditorNotRunning) {
        anyhow::bail!(
            "Unity editor exited before {} finished for {}",
            failure_context,
            project.display()
        );
    }

    let log_status = fetch_lifecycle_log_status(project).await;

    if !ctx.json {
        if let Some(status) = &log_status {
            crate::commands::logs::print_status(status, ctx);
        }
    }

    Ok(UnityLifecycleOutcome {
        bridge_status,
        editor_status: Some(settle.status),
        log_status,
    })
}

async fn fetch_lifecycle_log_status(project: &std::path::Path) -> Option<serde_json::Value> {
    let lock = discovery::read_lock_file(project).ok()?;
    let mut client = BridgeClient::connect(&lock).await.ok()?;
    if client.handshake().await.is_err() {
        client.close().await;
        return None;
    }

    let status = crate::commands::logs::fetch_status(&mut client).await.ok();
    client.close().await;
    status
}

pub fn attach_lifecycle_log_status(
    mut result: serde_json::Value,
    outcome: &UnityLifecycleOutcome,
) -> serde_json::Value {
    if let Some(status) = &outcome.log_status {
        result["logStatus"] = status.clone();
    }
    result
}

pub async fn enforce_active_scene_guard(
    client: &mut BridgeClient,
    policy: ActiveSceneGuardPolicy,
) -> anyhow::Result<()> {
    let ActiveSceneGuardPolicy::BlockIfDirty {
        command_label,
        max_entries,
    } = policy
    else {
        return Ok(());
    };

    let result = match client
        .call(
            "scene/dirty-summary",
            serde_json::json!({ "maxEntries": max_entries }),
        )
        .await
    {
        Ok(value) => value,
        Err(err) => {
            return Err(map_bridge_method_error(
                err,
                "scene/dirty-summary",
                "dirty-scene preflight",
            ));
        }
    };
    let summary: ActiveSceneDirtySummary = serde_json::from_value(result)
        .map_err(|err| anyhow::anyhow!("Invalid scene/dirty-summary payload: {err}"))?;

    if !summary.is_dirty {
        return Ok(());
    }

    anyhow::bail!("{}", format_active_scene_guard_message(command_label, &summary));
}

pub async fn enforce_active_scene_guard_for_project(
    project: &std::path::Path,
    policy: ActiveSceneGuardPolicy,
) -> anyhow::Result<()> {
    if matches!(policy, ActiveSceneGuardPolicy::None) {
        return Ok(());
    }

    let Ok(lock) = discovery::read_lock_file(project) else {
        return Ok(());
    };

    let Ok(mut client) = BridgeClient::connect(&lock).await else {
        return Ok(());
    };
    let handshake_ok = client.handshake().await.is_ok();
    if !handshake_ok {
        client.close().await;
        return Ok(());
    }

    let result = enforce_active_scene_guard(&mut client, policy).await;
    client.close().await;
    result
}

pub async fn save_active_scene(
    client: &mut BridgeClient,
    ctx: &Context,
) -> anyhow::Result<serde_json::Value> {
    if !ctx.json {
        output::print_info("Saving active scene...");
    }

    match client.call("scene/save-active", serde_json::json!({})).await {
        Ok(value) => Ok(value),
        Err(err) => Err(map_bridge_method_error(
            err,
            "scene/save-active",
            "active-scene save support",
        )),
    }
}

pub fn map_bridge_method_error(
    err: UcpError,
    method: &str,
    capability: &str,
) -> anyhow::Error {
    match err {
        UcpError::BridgeError { code, message }
            if code == -32601 && message.contains(method) =>
        {
            anyhow::anyhow!(
                "The connected Unity bridge does not expose `{method}` yet, so {capability} is unavailable. Refresh or update the bridge package, then let Unity finish recompiling before retrying."
            )
        }
        other => other.into(),
    }
}

fn format_active_scene_guard_message(
    command_label: &str,
    summary: &ActiveSceneDirtySummary,
) -> String {
    let scene_path = if summary.path.is_empty() {
        "<untitled scene>".to_string()
    } else {
        summary.path.clone()
    };

    let mut lines = vec![format!(
        "Cannot {command_label} while the active scene has unsaved changes: {} ({scene_path})",
        summary.name
    )];

    if summary.changes.is_empty() {
        lines.push("Modified objects: unavailable".to_string());
    } else {
        lines.push("Modified objects:".to_string());
        for change in &summary.changes {
            let id = change
                .instance_id
                .map(|value| value.to_string())
                .unwrap_or_else(|| "-".to_string());
            let component_summary = if change.components.is_empty() {
                "Unknown".to_string()
            } else {
                change.components.join(", ")
            };
            lines.push(format!("  {id} {} [{component_summary}]", change.name));
        }
        if summary.omitted_count > 0 {
            lines.push(format!("  ... {} more object(s) modified", summary.omitted_count));
        }
    }

    lines.push("Run `ucp scene save` to persist the active scene, or re-run the scene-editing command with `--save` if supported.".to_string());
    lines.join("\n")
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
        Command::Logs { action, args } => logs::run(action, args, &ctx).await,
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
        Command::Packages { action } => packages::run(action, &ctx).await,
        Command::Build { action } => build::run(action, &ctx).await,
        Command::Profiler { action } => profiler::run(action, &ctx).await,
    }
}
