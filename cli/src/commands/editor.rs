use crate::bridge_lifecycle::{self, WaitMode, WaitStatus};
use crate::discovery;
use crate::editor_runtime;
use crate::output;
use clap::Subcommand;
use std::fs;
use std::io::{self, Write};

use super::{Context, resolve_project_path};

#[derive(Subcommand)]
pub enum EditorAction {
    /// Open the Unity editor and wait for the bridge
    Open,
    /// Close the Unity editor for the project
    Close {
        /// Force kill the editor if graceful shutdown times out
        #[arg(long)]
        force: bool,
    },
    /// Restart the Unity editor for the project
    Restart {
        /// Force kill the editor if graceful shutdown times out
        #[arg(long)]
        force: bool,
    },
    /// Show editor runtime status for the project
    Status,
    /// Print the Unity editor log for the project
    Logs {
        /// Number of trailing lines to show
        #[arg(long, default_value_t = 200)]
        lines: usize,
    },
    /// List all running Unity editor processes discovered by UCP
    Ps,
}

pub async fn run(action: EditorAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        EditorAction::Ps => ps(ctx),
        EditorAction::Open => open(ctx).await,
        EditorAction::Close { force } => close(ctx, force).await,
        EditorAction::Restart { force } => restart(ctx, force).await,
        EditorAction::Status => status(ctx),
        EditorAction::Logs { lines } => logs(ctx, lines),
    }
}

async fn open(ctx: &Context) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;
    let _ = crate::bridge_package::apply_update_policy(&project, ctx, true).await?;
    let previous_lock = discovery::read_lock_file(&project).ok();
    let outcome = editor_runtime::ensure_editor_running(&project, ctx).await?;

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

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "editor": outcome,
            "bridge": match wait_outcome.status {
                WaitStatus::Available => "available",
                WaitStatus::Restarted => "restarted",
                WaitStatus::Stable => "stable",
                WaitStatus::EditorNotRunning => "editor-not-running",
            }
        })));
        return Ok(());
    }

    if outcome.already_running {
        output::print_success("Unity editor already running");
    } else {
        output::print_success("Unity editor opened");
    }
    if let Some(pid) = outcome.pid {
        eprintln!("  PID: {pid}");
    }
    eprintln!("  Log: {}", outcome.log_path);
    for handled in wait_outcome.handled_dialogs {
        eprintln!("  Dialog: {handled}");
    }
    Ok(())
}

async fn close(ctx: &Context, force: bool) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;
    super::enforce_active_scene_guard_for_project(
        &project,
        super::ActiveSceneGuardPolicy::block_if_dirty("close the Unity editor"),
    )
    .await?;
    let outcome = editor_runtime::close_editor(&project, ctx, force).await?;

    if ctx.json {
        output::print_json(&output::success_json(serde_json::to_value(outcome)?));
        return Ok(());
    }

    if !outcome.was_running {
        output::print_info("Unity editor is not running for this project");
        return Ok(());
    }

    if !outcome.exited {
        output::print_warn("Unity editor shutdown requested, but the process is still closing");
    } else if outcome.forced {
        output::print_warn("Unity editor was force-terminated");
    } else {
        output::print_success("Unity editor closed");
    }
    Ok(())
}

async fn restart(ctx: &Context, force: bool) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;
    super::enforce_active_scene_guard_for_project(
        &project,
        super::ActiveSceneGuardPolicy::block_if_dirty("restart the Unity editor"),
    )
    .await?;
    let _ = editor_runtime::close_editor(&project, ctx, force).await?;
    open(ctx).await
}

fn status(ctx: &Context) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;
    let status = editor_runtime::status(&project, ctx);

    if ctx.json {
        output::print_json(&output::success_json(serde_json::to_value(status)?));
        return Ok(());
    }

    if status.running {
        output::print_success("Unity editor is running");
    } else {
        output::print_warn("Unity editor is not running");
    }
    if let Some(pid) = status.pid {
        eprintln!("  PID: {pid}");
    }
    if let Some(path) = status.executable_path.as_deref() {
        eprintln!("  Executable: {path}");
    } else if let Some(path) = status.resolved_unity_path.as_deref() {
        eprintln!("  Resolved Unity: {path}");
    }
    if let Some(version) = status.project_version.as_deref() {
        eprintln!("  Project Unity version: {version}");
    }
    if let Some(version) = status.requested_version.as_deref() {
        eprintln!("  Requested Unity version: {version}");
    }
    if !status.installed_versions.is_empty() {
        eprintln!(
            "  Installed Unity versions: {}",
            status.installed_versions.join(", ")
        );
    }
    eprintln!("  Log: {}", status.log_path);
    if let Some(warning) = status.resolution_warning.as_deref() {
        eprintln!("  Warning: {warning}");
    }
    if let Some(error) = status.resolution_error.as_deref() {
        eprintln!("  Resolution: {error}");
    }
    Ok(())
}

fn logs(ctx: &Context, lines: usize) -> anyhow::Result<()> {
    let project = resolve_project_path(ctx)?;
    let log_path = crate::config::editor_log_path(&project);
    if !log_path.is_file() {
        anyhow::bail!("Editor log file not found at {}", log_path.display());
    }

    let content = fs::read_to_string(&log_path)?;
    let rendered = tail_lines(&content, lines);

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "path": log_path.display().to_string(),
            "content": rendered,
        })));
    } else {
        io::stdout().write_all(rendered.as_bytes())?;
        if !rendered.ends_with('\n') {
            io::stdout().write_all(b"\n")?;
        }
    }

    Ok(())
}

fn ps(ctx: &Context) -> anyhow::Result<()> {
    let editors = discovery::list_running_unity_editors();
    if ctx.json {
        output::print_json(&output::success_json(serde_json::to_value(&editors)?));
        return Ok(());
    }

    if editors.is_empty() {
        output::print_info("No running Unity editor processes were found");
        return Ok(());
    }

    output::print_success(&format!("Found {} Unity editor process(es)", editors.len()));
    for editor in editors {
        eprintln!("  PID {}  {}", editor.pid, editor.project_path.display());
        if let Some(path) = editor.executable_path {
            eprintln!("    Executable: {}", path.display());
        }
    }
    Ok(())
}

fn tail_lines(content: &str, lines: usize) -> String {
    if lines == 0 {
        return String::new();
    }

    let collected = content.lines().rev().take(lines).collect::<Vec<_>>();
    collected.into_iter().rev().collect::<Vec<_>>().join("\n")
}
