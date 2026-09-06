use crate::client::BridgeClient;
use crate::output;
use clap::{Args, Subcommand, ValueEnum};
use serde_json::{Map, Value};
use std::collections::HashSet;
use std::fmt;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::Duration;

use super::{Context, UnityLifecyclePolicy};

const MAX_VIEWPORT_DIMENSION: u32 = 8192;
const MAX_VIEWPORT_PIXELS: u64 = 8_388_608;

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum UiDetailArg {
    Summary,
    Normal,
    Verbose,
}

impl UiDetailArg {
    fn as_str(self) -> &'static str {
        match self {
            Self::Summary => "summary",
            Self::Normal => "normal",
            Self::Verbose => "verbose",
        }
    }
}

/// A UXML document or UI scenario plus the state used to render it.
#[derive(Args, Debug, Clone)]
pub struct UiTargetArgs {
    /// UXML asset or .ucp-ui.json scenario to load
    pub target: String,
    /// Named scenario state to render
    #[arg(long)]
    pub state: Option<String>,
    /// Inline JSON object merged into the fixture data
    #[arg(long, value_name = "JSON", conflicts_with = "data_file")]
    pub data_json: Option<String>,
    /// JSON file whose object is merged into the fixture data
    #[arg(long, value_name = "PATH", conflicts_with = "data_json")]
    pub data_file: Option<PathBuf>,
    /// Logical viewport width in pixels
    #[arg(long, requires = "height")]
    pub width: Option<u32>,
    /// Logical viewport height in pixels
    #[arg(long, requires = "width")]
    pub height: Option<u32>,
}

/// Inspect, lint, and render UI Toolkit UXML and USS in an isolated Editor harness.
#[derive(Subcommand)]
pub enum UiAction {
    /// Discover UXML documents and .ucp-ui.json scenarios
    List {
        /// Asset folder to scan
        #[arg(long, default_value = "Assets")]
        root: String,
        /// Include UI targets from installed packages
        #[arg(long)]
        include_packages: bool,
        /// Maximum targets to return
        #[arg(long, default_value_t = 100)]
        limit: usize,
    },
    /// Import and validate UXML, USS, and TSS assets
    Lint {
        /// UXML, USS, TSS, scenario, or folder paths to lint
        #[arg(required = true, num_args = 1..)]
        paths: Vec<String>,
        /// Treat warnings as a failed lint result
        #[arg(long)]
        fail_on_warnings: bool,
        /// Maximum diagnostics to return
        #[arg(long, default_value_t = 100)]
        max_diagnostics: usize,
    },
    /// Instantiate a UI and return its bounded visual tree
    Inspect {
        #[command(flatten)]
        target: UiTargetArgs,
        /// Restrict the snapshot to elements matching a UI Toolkit query
        #[arg(long)]
        query: Option<String>,
        /// Maximum visual-tree depth
        #[arg(long, default_value_t = 6)]
        depth: usize,
        /// Maximum elements to return
        #[arg(long, default_value_t = 200)]
        max_elements: usize,
        /// Snapshot detail level
        #[arg(long, value_enum, default_value_t = UiDetailArg::Normal)]
        detail: UiDetailArg,
    },
    /// Render a UI to a deterministic PNG
    Screenshot {
        #[command(flatten)]
        target: UiTargetArgs,
        /// Copy the PNG artifact to this path
        #[arg(short = 'o', long, value_name = "PATH")]
        out: Option<PathBuf>,
        /// Replace an existing output file
        #[arg(long, requires = "out")]
        force: bool,
    },
    /// Lint, instantiate, inspect, audit, and capture a UI
    Check {
        #[command(flatten)]
        target: UiTargetArgs,
        /// Check every state declared by a scenario
        #[arg(long, conflicts_with = "state")]
        all_states: bool,
        /// Restrict snapshots to elements matching a UI Toolkit query
        #[arg(long)]
        query: Option<String>,
        /// Maximum visual-tree depth
        #[arg(long, default_value_t = 4)]
        depth: usize,
        /// Maximum elements per snapshot
        #[arg(long, default_value_t = 100)]
        max_elements: usize,
        /// Snapshot detail level
        #[arg(long, value_enum, default_value_t = UiDetailArg::Normal)]
        detail: UiDetailArg,
        /// Copy capture artifacts into this directory
        #[arg(long, value_name = "DIR")]
        out_dir: Option<PathBuf>,
        /// Treat warnings as a failed check result
        #[arg(long)]
        fail_on_warnings: bool,
        /// Replace existing files in --out-dir
        #[arg(long, requires = "out_dir")]
        force: bool,
    },
}

#[derive(Debug)]
pub struct UiCommandFailure {
    pub message: String,
    pub result: Value,
}

impl fmt::Display for UiCommandFailure {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.message)
    }
}

impl std::error::Error for UiCommandFailure {}

struct UiOperationOutcome {
    result: Value,
    failure: Option<String>,
}

pub async fn run(action: UiAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        UiAction::List {
            root,
            include_packages,
            limit,
        } => run_list(root, include_packages, limit, ctx).await,
        UiAction::Lint {
            paths,
            fail_on_warnings,
            max_diagnostics,
        } => run_lint(paths, fail_on_warnings, max_diagnostics, ctx).await,
        UiAction::Inspect {
            target,
            query,
            depth,
            max_elements,
            detail,
        } => {
            validate_range("--depth", depth, 0, 64)?;
            validate_range("--max-elements", max_elements, 1, 5000)?;
            let mut params = target_params(&target)?;
            insert_optional(&mut params, "query", query);
            params.insert("depth".into(), serde_json::json!(depth));
            params.insert("max".into(), serde_json::json!(max_elements));
            params.insert("detail".into(), serde_json::json!(detail.as_str()));
            run_async("ui/inspect", "inspect", params.into(), ctx, |_, _| Ok(())).await
        }
        UiAction::Screenshot { target, out, force } => {
            let params = target_params(&target)?;
            run_async(
                "ui/screenshot",
                "screenshot",
                params.into(),
                ctx,
                move |result, project| {
                    if let Some(destination) = out {
                        let source = first_artifact_path(result).ok_or_else(|| {
                            anyhow::anyhow!("UI screenshot completed without an artifact path")
                        })?;
                        let copied =
                            copy_artifact(project, Path::new(source), &destination, force)?;
                        insert_result_field(
                            result,
                            "outputPath",
                            Value::String(display_path(&copied)),
                        );
                    }
                    Ok(())
                },
            )
            .await
        }
        UiAction::Check {
            target,
            all_states,
            query,
            depth,
            max_elements,
            detail,
            out_dir,
            fail_on_warnings,
            force,
        } => {
            validate_range("--depth", depth, 0, 64)?;
            validate_range("--max-elements", max_elements, 1, 5000)?;
            let mut params = target_params(&target)?;
            params.insert("allStates".into(), serde_json::json!(all_states));
            insert_optional(&mut params, "query", query);
            params.insert("depth".into(), serde_json::json!(depth));
            params.insert("max".into(), serde_json::json!(max_elements));
            params.insert("detail".into(), serde_json::json!(detail.as_str()));
            params.insert("failOnWarnings".into(), serde_json::json!(fail_on_warnings));
            run_async(
                "ui/check",
                "check",
                params.into(),
                ctx,
                move |result, project| {
                    if let Some(directory) = out_dir {
                        let copied = copy_result_artifacts(project, result, &directory, force)?;
                        insert_result_field(
                            result,
                            "outputPaths",
                            serde_json::json!(
                                copied.iter().map(|p| display_path(p)).collect::<Vec<_>>()
                            ),
                        );
                    }
                    Ok(())
                },
            )
            .await
        }
    }
}

async fn run_list(
    root: String,
    include_packages: bool,
    limit: usize,
    ctx: &Context,
) -> anyhow::Result<()> {
    validate_range("--limit", limit, 1, 5000)?;
    let (_, _, mut client) = super::connect_client(ctx).await?;
    let result = client
        .call(
            "ui/list",
            serde_json::json!({
                "path": root,
                "includePackages": include_packages,
                "limit": limit,
            }),
        )
        .await
        .map_err(|err| super::map_bridge_method_error(err, "ui/list", "UI discovery"))?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        print_list_result(&result);
    }
    Ok(())
}

async fn run_lint(
    paths: Vec<String>,
    fail_on_warnings: bool,
    max_diagnostics: usize,
    ctx: &Context,
) -> anyhow::Result<()> {
    validate_range("--max-diagnostics", max_diagnostics, 1, 1000)?;
    let (project, lock, mut client) = super::connect_client(ctx).await?;
    let result = client
        .call(
            "ui/lint",
            serde_json::json!({
                "paths": paths,
                "failOnWarnings": fail_on_warnings,
                "maxDiagnostics": max_diagnostics,
            }),
        )
        .await
        .map_err(|err| super::map_bridge_method_error(err, "ui/lint", "UI linting"))?;
    client.close().await;

    super::await_unity_lifecycle(
        &project,
        Some(&lock),
        UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to finish UI asset work...",
            "UI linting",
        ),
        ctx,
    )
    .await?;

    let failure = failed_result_message(&result, "UI lint");
    if ctx.json {
        if let Some(message) = failure {
            return Err(UiCommandFailure { message, result }.into());
        }
        output::print_json(&output::success_json(result));
    } else {
        print_lint_result(&result);
        if let Some(message) = failure {
            return Err(UiCommandFailure { message, result }.into());
        }
    }
    Ok(())
}

async fn run_async<F>(
    method: &'static str,
    operation: &'static str,
    params: Value,
    ctx: &Context,
    post_process: F,
) -> anyhow::Result<()>
where
    F: FnOnce(&mut Value, &Path) -> anyhow::Result<()>,
{
    let (project, _, mut client) = super::connect_client(ctx).await?;
    if !ctx.json {
        output::print_info(&format!("Starting UI {operation}..."));
    }

    let start = client
        .call(method, params)
        .await
        .map_err(|err| super::map_bridge_method_error(err, method, "UI harness support"))?;
    let operation_id = start
        .get("operationId")
        .cloned()
        .ok_or_else(|| anyhow::anyhow!("{method} returned no operationId"))?;

    if !ctx.json {
        output::print_info(&format!(
            "UI {operation} started; waiting for the result..."
        ));
    }

    let outcome = wait_for_ui_result(&mut client, operation_id, operation, ctx.timeout).await?;
    client.close().await;
    let (result, operation_failure) = if outcome.failure.is_some() {
        (outcome.result, outcome.failure)
    } else {
        (
            apply_post_process(outcome.result, &project, post_process)
                .map_err(anyhow::Error::new)?,
            None,
        )
    };
    let failure =
        operation_failure.or_else(|| failed_result_message(&result, &format!("UI {operation}")));

    if ctx.json {
        if let Some(message) = failure {
            return Err(UiCommandFailure { message, result }.into());
        }
        output::print_json(&output::success_json(result));
    } else {
        if let Some(message) = failure {
            if operation == "check" && result.get("passed").and_then(Value::as_bool) == Some(false)
            {
                print_async_result(operation, &result);
            }
            return Err(UiCommandFailure { message, result }.into());
        }
        print_async_result(operation, &result);
    }
    Ok(())
}

fn apply_post_process<F>(
    mut result: Value,
    project: &Path,
    post_process: F,
) -> Result<Value, UiCommandFailure>
where
    F: FnOnce(&mut Value, &Path) -> anyhow::Result<()>,
{
    if let Err(error) = post_process(&mut result, project) {
        return Err(UiCommandFailure {
            message: format!("{error:#}"),
            result,
        });
    }
    Ok(result)
}

async fn wait_for_ui_result(
    client: &mut BridgeClient,
    operation_id: Value,
    operation: &str,
    timeout_secs: u64,
) -> anyhow::Result<UiOperationOutcome> {
    let wait = async {
        loop {
            let notification = client.next_notification().await?;
            if notification.method == "ui/result"
                && notification.params.get("operationId") == Some(&operation_id)
            {
                return Some(notification.params);
            }
        }
    };

    let received = if timeout_secs == 0 {
        Ok(wait.await)
    } else {
        tokio::time::timeout(Duration::from_secs(timeout_secs), wait).await
    };
    match received {
        Ok(Some(envelope)) => Ok(ui_operation_outcome(envelope, operation)),
        Ok(None) => Ok(UiOperationOutcome {
            result: serde_json::json!({
                "operationId": operation_id,
                "operation": operation,
                "status": "unknown",
                "error": { "code": "connection_closed" }
            }),
            failure: Some(format!(
                "Connection closed before UI {operation} ({operation_id}) completed; \
                 the Editor may have reloaded or quit. Completion could not be confirmed."
            )),
        }),
        Err(_) => {
            // A notification can be lost while a request is being read. Recover
            // terminal results, or report the bridge's last known state.
            let status = client
                .call_with_timeout(
                    "ui/status",
                    serde_json::json!({ "operationId": operation_id }),
                    Some(Duration::from_secs(5)),
                )
                .await;
            match status {
                Ok(envelope)
                    if matches!(
                        envelope.get("status").and_then(Value::as_str),
                        Some("completed" | "failed")
                    ) =>
                {
                    Ok(ui_operation_outcome(envelope, operation))
                }
                Ok(envelope) => {
                    let state = envelope
                        .get("status")
                        .and_then(Value::as_str)
                        .unwrap_or("not found");
                    let message = format!(
                        "Timed out after {timeout_secs}s waiting for UI {operation} ({operation_id}); \
                         bridge status: {state}. The operation was not cancelled and may still be running."
                    );
                    Ok(UiOperationOutcome {
                        result: envelope,
                        failure: Some(message),
                    })
                }
                Err(error) => Ok(UiOperationOutcome {
                    result: serde_json::json!({
                        "operationId": operation_id,
                        "operation": operation,
                        "status": "unknown",
                        "error": { "code": "status_unavailable", "message": error.to_string() }
                    }),
                    failure: Some(format!(
                        "Timed out after {timeout_secs}s waiting for UI {operation} ({operation_id}); \
                         status lookup failed: {error}. The operation was not cancelled."
                    )),
                }),
            }
        }
    }
}

fn ui_operation_outcome(envelope: Value, operation: &str) -> UiOperationOutcome {
    if envelope.get("status").and_then(Value::as_str) == Some("completed") {
        UiOperationOutcome {
            result: envelope.get("result").cloned().unwrap_or(Value::Null),
            failure: None,
        }
    } else {
        let message =
            operation_error_message(&envelope).unwrap_or_else(|| format!("UI {operation} failed"));
        UiOperationOutcome {
            result: envelope,
            failure: Some(message),
        }
    }
}

fn target_params(args: &UiTargetArgs) -> anyhow::Result<Map<String, Value>> {
    if args.width.is_some() != args.height.is_some() {
        anyhow::bail!("--width and --height must be supplied together");
    }

    let mut params = Map::new();
    params.insert("target".into(), Value::String(args.target.clone()));
    insert_optional(&mut params, "state", args.state.clone());
    if let Some(data) = load_data(args.data_json.as_deref(), args.data_file.as_deref())? {
        params.insert("data".into(), data);
    }
    if let (Some(width), Some(height)) = (args.width, args.height) {
        if width == 0
            || height == 0
            || width > MAX_VIEWPORT_DIMENSION
            || height > MAX_VIEWPORT_DIMENSION
        {
            anyhow::bail!("--width and --height must be between 1 and {MAX_VIEWPORT_DIMENSION}");
        }
        let pixel_count = u64::from(width) * u64::from(height);
        if pixel_count > MAX_VIEWPORT_PIXELS {
            anyhow::bail!(
                "--width × --height must not exceed {MAX_VIEWPORT_PIXELS} pixels \
                 (requested {pixel_count})"
            );
        }
        params.insert(
            "viewport".into(),
            serde_json::json!({ "width": width, "height": height }),
        );
    }
    Ok(params)
}

fn load_data(inline: Option<&str>, file: Option<&Path>) -> anyhow::Result<Option<Value>> {
    if inline.is_some() && file.is_some() {
        anyhow::bail!("--data-json and --data-file cannot be used together");
    }

    let (source, label) = match (inline, file) {
        (Some(value), None) => (value.to_owned(), "--data-json".to_string()),
        (None, Some(path)) => (
            fs::read_to_string(path)
                .map_err(|err| anyhow::anyhow!("Failed to read {}: {err}", path.display()))?,
            path.display().to_string(),
        ),
        (None, None) => return Ok(None),
        (Some(_), Some(_)) => unreachable!("validated above"),
    };

    let value: Value = serde_json::from_str(&source)
        .map_err(|err| anyhow::anyhow!("Invalid JSON in {label}: {err}"))?;
    if !value.is_object() {
        anyhow::bail!("UI data in {label} must be a JSON object");
    }
    Ok(Some(value))
}

fn validate_range(flag: &str, value: usize, min: usize, max: usize) -> anyhow::Result<()> {
    if value < min || value > max {
        anyhow::bail!("{flag} must be between {min} and {max}");
    }
    Ok(())
}

fn insert_optional(map: &mut Map<String, Value>, key: &str, value: Option<String>) {
    if let Some(value) = value {
        map.insert(key.to_string(), Value::String(value));
    }
}

fn failed_result_message(result: &Value, label: &str) -> Option<String> {
    let failed = result.get("passed").and_then(Value::as_bool) == Some(false)
        || result.get("success").and_then(Value::as_bool) == Some(false);
    if !failed {
        return None;
    }

    if let Some(message) = result.get("message").and_then(Value::as_str) {
        return Some(message.to_string());
    }
    let (errors, warnings) = diagnostic_counts(result);
    Some(format!(
        "{label} failed ({errors} error(s), {warnings} warning(s))"
    ))
}

fn operation_error_message(params: &Value) -> Option<String> {
    let error = params.get("error")?;
    error.as_str().map(str::to_owned).or_else(|| {
        error
            .get("message")
            .and_then(Value::as_str)
            .map(str::to_owned)
    })
}

fn diagnostic_counts(result: &Value) -> (u64, u64) {
    if result.get("errorCount").is_some() || result.get("warningCount").is_some() {
        return (
            result
                .get("errorCount")
                .and_then(Value::as_u64)
                .unwrap_or(0),
            result
                .get("warningCount")
                .and_then(Value::as_u64)
                .unwrap_or(0),
        );
    }

    let mut errors = 0;
    let mut warnings = 0;
    if let Some(lint) = result.get("lint") {
        errors += lint.get("errorCount").and_then(Value::as_u64).unwrap_or(0);
        warnings += lint
            .get("warningCount")
            .and_then(Value::as_u64)
            .unwrap_or(0);
    }
    if let Some(states) = result.get("states").and_then(Value::as_array) {
        for state in states {
            if let Some(audit) = state.get("audit") {
                errors += audit.get("errorCount").and_then(Value::as_u64).unwrap_or(0);
                warnings += audit
                    .get("warningCount")
                    .and_then(Value::as_u64)
                    .unwrap_or(0);
            }
        }
    }
    (errors, warnings)
}

fn first_artifact_path(result: &Value) -> Option<&str> {
    if let Some(path) = result.get("artifactPath").and_then(Value::as_str) {
        return Some(path);
    }
    match result {
        Value::Array(values) => values.iter().find_map(first_artifact_path),
        Value::Object(values) => values.values().find_map(first_artifact_path),
        _ => None,
    }
}

fn collect_artifact_paths(result: &Value, paths: &mut Vec<String>) {
    match result {
        Value::Array(values) => {
            for value in values {
                collect_artifact_paths(value, paths);
            }
        }
        Value::Object(values) => {
            if let Some(path) = values.get("artifactPath").and_then(Value::as_str) {
                paths.push(path.to_string());
            }
            for value in values.values() {
                collect_artifact_paths(value, paths);
            }
        }
        _ => {}
    }
}

fn copy_result_artifacts(
    project: &Path,
    result: &Value,
    out_dir: &Path,
    force: bool,
) -> anyhow::Result<Vec<PathBuf>> {
    let mut artifacts = Vec::new();
    collect_artifact_paths(result, &mut artifacts);
    let project = canonical_project(project)?;
    let mut seen_sources = HashSet::new();
    let mut seen_destinations = HashSet::new();
    let mut plans = Vec::new();

    for artifact in artifacts {
        let source = resolve_artifact_source(&project, Path::new(&artifact))?;
        if !seen_sources.insert(source.clone()) {
            continue;
        }
        let file_name = Path::new(&artifact)
            .file_name()
            .ok_or_else(|| anyhow::anyhow!("Artifact path has no file name: {artifact}"))?;
        let destination = absolute_destination(&out_dir.join(file_name))?;
        if !seen_destinations.insert(destination_key(&destination)) {
            anyhow::bail!(
                "Multiple UI artifacts would be copied to {}",
                destination.display()
            );
        }
        preflight_destination(&destination, force)?;
        plans.push((source, destination));
    }

    let mut copied = Vec::with_capacity(plans.len());
    for (source, destination) in plans {
        copied.push(copy_resolved_artifact(&source, &destination, force)?);
    }
    Ok(copied)
}

fn copy_artifact(
    project: &Path,
    artifact: &Path,
    destination: &Path,
    force: bool,
) -> anyhow::Result<PathBuf> {
    let project = canonical_project(project)?;
    let source = resolve_artifact_source(&project, artifact)?;
    let destination = absolute_destination(destination)?;
    preflight_destination(&destination, force)?;
    copy_resolved_artifact(&source, &destination, force)
}

fn canonical_project(project: &Path) -> anyhow::Result<PathBuf> {
    fs::canonicalize(project).map_err(|err| {
        anyhow::anyhow!(
            "Failed to resolve Unity project {}: {err}",
            project.display()
        )
    })
}

fn resolve_artifact_source(project: &Path, artifact: &Path) -> anyhow::Result<PathBuf> {
    let source_candidate = if artifact.is_absolute() {
        artifact.to_path_buf()
    } else {
        project.join(artifact)
    };
    let source = fs::canonicalize(&source_candidate).map_err(|err| {
        anyhow::anyhow!(
            "Failed to resolve UI artifact {}: {err}",
            source_candidate.display()
        )
    })?;
    if !source.starts_with(project) {
        anyhow::bail!(
            "Refusing to copy a UI artifact outside the Unity project: {}",
            source.display()
        );
    }
    if !source.is_file() {
        anyhow::bail!("UI artifact is not a file: {}", source.display());
    }
    Ok(source)
}

fn preflight_destination(destination: &Path, force: bool) -> anyhow::Result<()> {
    if destination.exists() && !force {
        anyhow::bail!(
            "Output already exists: {} (use --force to replace it)",
            destination.display()
        );
    }
    if destination.exists() && destination.is_dir() {
        anyhow::bail!("Output path is a directory: {}", destination.display());
    }
    let parent = destination
        .parent()
        .ok_or_else(|| anyhow::anyhow!("Output path has no parent: {}", destination.display()))?;
    let mut existing_ancestor = parent;
    while !existing_ancestor.exists() {
        existing_ancestor = existing_ancestor.parent().ok_or_else(|| {
            anyhow::anyhow!(
                "Output path has no existing ancestor: {}",
                destination.display()
            )
        })?;
    }
    if !existing_ancestor.is_dir() {
        anyhow::bail!(
            "Output parent is not a directory: {}",
            existing_ancestor.display()
        );
    }
    Ok(())
}

fn copy_resolved_artifact(
    source: &Path,
    destination: &Path,
    force: bool,
) -> anyhow::Result<PathBuf> {
    if destination == source {
        return Ok(destination.to_path_buf());
    }
    let parent = destination
        .parent()
        .ok_or_else(|| anyhow::anyhow!("Output path has no parent: {}", destination.display()))?;
    fs::create_dir_all(parent).map_err(|err| {
        anyhow::anyhow!(
            "Failed to create output directory {}: {err}",
            parent.display()
        )
    })?;

    let temporary = parent.join(format!(".ucp-ui-{}.tmp", uuid::Uuid::new_v4().as_simple()));
    if let Err(err) = fs::copy(source, &temporary) {
        let _ = fs::remove_file(&temporary);
        return Err(anyhow::anyhow!(
            "Failed to copy UI artifact to {}: {err}",
            destination.display()
        ));
    }
    if destination.exists() {
        if !force {
            let _ = fs::remove_file(&temporary);
            anyhow::bail!(
                "Output already exists: {} (use --force to replace it)",
                destination.display()
            );
        }
        if let Err(err) = fs::remove_file(destination) {
            let _ = fs::remove_file(&temporary);
            return Err(anyhow::anyhow!(
                "Failed to replace {}: {err}",
                destination.display()
            ));
        }
    }
    if let Err(err) = fs::rename(&temporary, destination) {
        let _ = fs::remove_file(&temporary);
        return Err(anyhow::anyhow!(
            "Failed to finalize UI artifact {}: {err}",
            destination.display()
        ));
    }
    Ok(destination.to_path_buf())
}

fn destination_key(path: &Path) -> String {
    // Keep multi-artifact output portable across case-sensitive and case-insensitive filesystems.
    display_path(path).to_lowercase()
}

fn absolute_destination(path: &Path) -> anyhow::Result<PathBuf> {
    if path.is_absolute() {
        Ok(path.to_path_buf())
    } else {
        Ok(std::env::current_dir()
            .map_err(|err| anyhow::anyhow!("Failed to resolve current directory: {err}"))?
            .join(path))
    }
}

fn insert_result_field(result: &mut Value, key: &str, value: Value) {
    if let Some(object) = result.as_object_mut() {
        object.insert(key.to_string(), value);
    }
}

fn display_path(path: &Path) -> String {
    path.to_string_lossy().replace('\\', "/")
}

fn print_list_result(result: &Value) {
    let entries = result.get("items").and_then(Value::as_array);
    let count = result
        .get("count")
        .and_then(Value::as_u64)
        .or_else(|| entries.map(|items| items.len() as u64))
        .unwrap_or(0);
    output::print_success(&format!("Found {count} UI target(s)"));
    if let Some(entries) = entries {
        for entry in entries.iter().take(20) {
            let path = entry.get("target").and_then(Value::as_str).unwrap_or("?");
            let kind = entry.get("type").and_then(Value::as_str).unwrap_or("ui");
            eprintln!("  {path} ({kind})");
        }
        if entries.len() > 20 {
            eprintln!("  ... and {} more", entries.len() - 20);
        }
    }
}

fn print_lint_result(result: &Value) {
    let assets = result
        .get("assets")
        .and_then(Value::as_array)
        .map(Vec::len)
        .unwrap_or(0);
    let errors = result
        .get("errorCount")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let warnings = result
        .get("warningCount")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    if result.get("passed").and_then(Value::as_bool) == Some(false) {
        output::print_warn(&format!(
            "Linted {assets} UI asset(s): {errors} error(s), {warnings} warning(s)"
        ));
    } else {
        output::print_success(&format!(
            "Linted {assets} UI asset(s): {errors} error(s), {warnings} warning(s)"
        ));
    }

    if let Some(diagnostics) = result.get("diagnostics").and_then(Value::as_array) {
        for diagnostic in diagnostics.iter().take(12) {
            let severity = diagnostic
                .get("severity")
                .and_then(Value::as_str)
                .unwrap_or("info");
            let path = diagnostic
                .get("assetPath")
                .or_else(|| diagnostic.get("path"))
                .and_then(Value::as_str)
                .unwrap_or("?");
            let line = diagnostic.get("line").and_then(Value::as_u64);
            let message = diagnostic
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or("?");
            let location = line
                .map(|line| format!("{path}:{line}"))
                .unwrap_or_else(|| path.to_string());
            eprintln!("  [{severity}] {location}: {message}");
        }
    }
}

fn print_async_result(operation: &str, result: &Value) {
    match operation {
        "screenshot" => {
            let path = result
                .get("outputPath")
                .and_then(Value::as_str)
                .or_else(|| first_artifact_path(result))
                .unwrap_or("artifact unavailable");
            let width = result.get("width").and_then(Value::as_u64).unwrap_or(0);
            let height = result.get("height").and_then(Value::as_u64).unwrap_or(0);
            output::print_success(&format!(
                "UI screenshot captured: {path} ({width}x{height})"
            ));
        }
        "inspect" => {
            let count = result
                .get("elementCount")
                .or_else(|| result.get("count"))
                .and_then(Value::as_u64)
                .or_else(|| {
                    result
                        .get("snapshot")
                        .and_then(|snapshot| {
                            snapshot
                                .get("returnedElementCount")
                                .or_else(|| snapshot.get("elementCount"))
                        })
                        .and_then(Value::as_u64)
                })
                .unwrap_or(0);
            output::print_success(&format!("UI inspection completed ({count} element(s))"));
        }
        "check" => {
            let (errors, warnings) = diagnostic_counts(result);
            let message = format!("UI check completed: {errors} error(s), {warnings} warning(s)");
            if result.get("passed").and_then(Value::as_bool) == Some(false) {
                output::print_warn(&message);
            } else {
                output::print_success(&message);
            }
        }
        _ => output::print_success(&format!("UI {operation} completed")),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn ui_wait_recovers_lost_results_and_reports_running_or_interrupted_operations() {
        use futures_util::{SinkExt, StreamExt};
        use tokio::io::{AsyncReadExt, AsyncWriteExt};
        use tokio_tungstenite::tungstenite::{Message, protocol::Role};

        for mode in [
            "completed",
            "failed",
            "running",
            "missing",
            "shutdown",
            "disconnected",
        ] {
            let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
            let port = listener.local_addr().unwrap().port();
            let (ready_tx, ready_rx) = tokio::sync::oneshot::channel();
            let server = tokio::spawn(async move {
                let (mut stream, _) = listener.accept().await.unwrap();
                let mut request = Vec::new();
                while !request.ends_with(b"\r\n\r\n") {
                    request.push(stream.read_u8().await.unwrap());
                }
                let request = String::from_utf8(request).unwrap();
                let key = request
                    .lines()
                    .find_map(|line| line.strip_prefix("Sec-WebSocket-Key:"))
                    .unwrap()
                    .trim();
                let accept = {
                    use sha1::{Digest, Sha1};
                    let mut hasher = Sha1::new();
                    hasher.update(key.as_bytes());
                    hasher.update("258EAFA5-E914-47DA-95CA-5AB5DC85B11B");
                    base64::Engine::encode(
                        &base64::engine::general_purpose::STANDARD,
                        hasher.finalize(),
                    )
                };
                stream.write_all(format!("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n").as_bytes()).await.unwrap();
                let mut ws =
                    tokio_tungstenite::WebSocketStream::from_raw_socket(stream, Role::Server, None)
                        .await;
                ready_rx.await.unwrap();
                if mode == "disconnected" {
                    ws.close(None).await.unwrap();
                    return;
                }
                let envelope = if mode == "missing" {
                    serde_json::json!({ "operationId": "ui-test", "found": false })
                } else {
                    serde_json::json!({
                        "operationId": "ui-test", "found": true,
                        "status": if mode == "shutdown" { "failed" } else { mode },
                        "result": { "passed": true, "artifactPath": "capture.png" },
                        "error": { "code": "editor_shutdown", "message": "Editor reloaded" }
                    })
                };
                if mode == "shutdown" {
                    // Unrelated results must not complete our operation.
                    ws.send(Message::Text(
                        serde_json::json!({ "jsonrpc": "2.0", "method": "ui/result", "params": {
                            "operationId": "other", "status": "completed"
                        }})
                        .to_string()
                        .into(),
                    ))
                    .await
                    .unwrap();
                    ws.send(Message::Text(
                        serde_json::json!({ "jsonrpc": "2.0", "method": "ui/result", "params": envelope })
                            .to_string()
                            .into(),
                    ))
                    .await
                    .unwrap();
                } else {
                    let request: Value =
                        serde_json::from_str(ws.next().await.unwrap().unwrap().to_text().unwrap())
                            .unwrap();
                    assert_eq!(request["method"], "ui/status");
                    assert_eq!(request["params"]["operationId"], "ui-test");
                    ws.send(Message::Text(serde_json::json!({ "jsonrpc": "2.0", "id": request["id"], "result": envelope }).to_string().into())).await.unwrap();
                }
            });
            let lock = crate::config::LockFile {
                pid: 0,
                port,
                protocol_version: "test".into(),
                unity_version: "test".into(),
                project_path: "test".into(),
                started_at: "test".into(),
                token: "test".into(),
            };
            let mut client = BridgeClient::connect(&lock).await.unwrap();
            ready_tx.send(()).unwrap();
            let outcome = wait_for_ui_result(&mut client, serde_json::json!("ui-test"), "check", 1)
                .await
                .unwrap();
            match mode {
                "completed" => {
                    assert!(outcome.failure.is_none());
                    assert_eq!(outcome.result["artifactPath"], "capture.png");
                }
                "failed" | "shutdown" => {
                    assert_eq!(outcome.failure.as_deref(), Some("Editor reloaded"))
                }
                "running" | "missing" => {
                    assert!(outcome.failure.unwrap().contains("was not cancelled"));
                    assert_eq!(outcome.result["operationId"], "ui-test");
                }
                "disconnected" => {
                    assert!(
                        outcome
                            .failure
                            .unwrap()
                            .contains("may have reloaded or quit")
                    );
                    assert_eq!(outcome.result["error"]["code"], "connection_closed");
                }
                _ => unreachable!(),
            }
            server.await.unwrap();
        }
    }

    #[test]
    fn loads_inline_and_file_data_objects() {
        let inline = load_data(Some(r#"{"title":"Inventory"}"#), None).unwrap();
        assert_eq!(inline.unwrap()["title"], "Inventory");

        let root = std::env::temp_dir().join(format!("ucp-ui-data-{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        let path = root.join("data.json");
        fs::write(&path, r#"{"count":3}"#).unwrap();
        let from_file = load_data(None, Some(&path)).unwrap();
        assert_eq!(from_file.unwrap()["count"], 3);
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn rejects_conflicting_or_non_object_data() {
        assert!(load_data(Some("{}"), Some(Path::new("data.json"))).is_err());
        assert!(load_data(Some("[]"), None).is_err());
        assert!(load_data(Some("not-json"), None).is_err());
    }

    #[test]
    fn target_payload_only_includes_an_explicit_complete_viewport() {
        let mut target = UiTargetArgs {
            target: "Assets/UI/Panel.uxml".into(),
            state: None,
            data_json: None,
            data_file: None,
            width: None,
            height: None,
        };
        assert!(target_params(&target).unwrap().get("viewport").is_none());

        target.width = Some(1280);
        assert!(target_params(&target).is_err());
        target.height = Some(720);
        assert_eq!(target_params(&target).unwrap()["viewport"]["width"], 1280);
    }

    #[test]
    fn target_payload_rejects_oversized_viewport_area() {
        let mut target = UiTargetArgs {
            target: "Assets/UI/Panel.uxml".into(),
            state: None,
            data_json: None,
            data_file: None,
            width: Some(4096),
            height: Some(2048),
        };
        assert!(target_params(&target).is_ok());

        target.height = Some(2160);
        let error = target_params(&target).unwrap_err().to_string();
        assert!(error.contains("8388608"));
        assert!(error.contains("8847360"));
    }

    #[test]
    fn artifact_copy_is_project_scoped_and_refuses_overwrite() {
        let root = std::env::temp_dir().join(format!("ucp-ui-copy-{}", uuid::Uuid::new_v4()));
        let project = root.join("Project");
        let artifact = project.join("Library").join("UcpUi").join("capture.png");
        let destination = root.join("out").join("capture.png");
        fs::create_dir_all(artifact.parent().unwrap()).unwrap();
        fs::write(&artifact, b"png").unwrap();

        let copied = copy_artifact(&project, &artifact, &destination, false).unwrap();
        assert_eq!(fs::read(copied).unwrap(), b"png");
        assert!(copy_artifact(&project, &artifact, &destination, false).is_err());

        fs::write(&artifact, b"new").unwrap();
        copy_artifact(&project, &artifact, &destination, true).unwrap();
        assert_eq!(fs::read(destination).unwrap(), b"new");
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn artifact_batch_preflights_every_destination_before_copying() {
        let root = std::env::temp_dir().join(format!("ucp-ui-preflight-{}", uuid::Uuid::new_v4()));
        let project = root.join("Project");
        let artifact_dir = project.join("Library").join("UcpUi");
        let out_dir = root.join("out");
        fs::create_dir_all(&artifact_dir).unwrap();
        fs::create_dir_all(&out_dir).unwrap();
        let first = artifact_dir.join("first.png");
        let second = artifact_dir.join("second.png");
        fs::write(&first, b"first").unwrap();
        fs::write(&second, b"second").unwrap();
        fs::write(out_dir.join("second.png"), b"existing").unwrap();
        let result = serde_json::json!({
            "captures": [
                { "artifactPath": display_path(&first) },
                { "artifactPath": display_path(&second) }
            ]
        });

        assert!(copy_result_artifacts(&project, &result, &out_dir, false).is_err());
        assert!(!out_dir.join("first.png").exists());
        assert_eq!(fs::read(out_dir.join("second.png")).unwrap(), b"existing");
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn artifact_batch_rejects_nonportable_basename_collisions_before_copying() {
        let root = std::env::temp_dir().join(format!("ucp-ui-collision-{}", uuid::Uuid::new_v4()));
        let project = root.join("Project");
        let first = project.join("Library").join("one").join("Capture.png");
        let second = project.join("Library").join("two").join("capture.png");
        let out_dir = root.join("out");
        fs::create_dir_all(first.parent().unwrap()).unwrap();
        fs::create_dir_all(second.parent().unwrap()).unwrap();
        fs::write(&first, b"first").unwrap();
        fs::write(&second, b"second").unwrap();
        let result = serde_json::json!({
            "captures": [
                { "artifactPath": display_path(&first) },
                { "artifactPath": display_path(&second) }
            ]
        });

        assert!(copy_result_artifacts(&project, &result, &out_dir, false).is_err());
        assert!(!out_dir.exists());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn post_process_failure_preserves_bridge_result() {
        let result = serde_json::json!({
            "artifactPath": "Library/UCP/UiCaptures/panel.png",
            "pixelHash": "abc123"
        });
        let failure = apply_post_process(result.clone(), Path::new("."), |_, _| {
            anyhow::bail!("output already exists")
        })
        .unwrap_err();

        assert_eq!(failure.message, "output already exists");
        assert_eq!(failure.result, result);
    }

    #[test]
    fn rejects_artifacts_outside_project() {
        let root = std::env::temp_dir().join(format!("ucp-ui-scope-{}", uuid::Uuid::new_v4()));
        let project = root.join("Project");
        let outside = root.join("outside.png");
        fs::create_dir_all(&project).unwrap();
        fs::write(&outside, b"png").unwrap();
        let destination = root.join("out.png");

        assert!(copy_artifact(&project, &outside, &destination, false).is_err());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn extracts_nested_check_artifacts_once() {
        let value = serde_json::json!({
            "captures": [
                { "artifactPath": "Library/UI/a.png" },
                { "artifactPath": "Library/UI/b.png" }
            ],
            "primary": { "artifactPath": "Library/UI/a.png" }
        });
        let mut paths = Vec::new();
        collect_artifact_paths(&value, &mut paths);
        assert_eq!(paths.len(), 3);
        assert_eq!(first_artifact_path(&value), Some("Library/UI/a.png"));
    }

    #[test]
    fn failed_result_message_aggregates_lint_and_audit_counts() {
        let nested = serde_json::json!({
            "passed": false,
            "lint": { "errorCount": 1, "warningCount": 2 },
            "states": [
                { "audit": { "errorCount": 3, "warningCount": 0 } },
                { "audit": { "errorCount": 0, "warningCount": 4 } }
            ]
        });
        assert_eq!(
            failed_result_message(&nested, "UI check").as_deref(),
            Some("UI check failed (4 error(s), 6 warning(s))")
        );

        // Top-level totals from the bridge win over re-counting nested sections.
        let flat = serde_json::json!({
            "passed": false,
            "errorCount": 7,
            "warningCount": 0,
            "lint": { "errorCount": 1 }
        });
        assert_eq!(
            failed_result_message(&flat, "UI lint").as_deref(),
            Some("UI lint failed (7 error(s), 0 warning(s))")
        );

        let explicit = serde_json::json!({ "success": false, "message": "bridge said no" });
        assert_eq!(
            failed_result_message(&explicit, "UI lint").as_deref(),
            Some("bridge said no")
        );

        assert!(failed_result_message(&serde_json::json!({ "passed": true }), "UI check").is_none());
        assert!(failed_result_message(&serde_json::json!({}), "UI check").is_none());
    }

    #[test]
    fn operation_outcomes_extract_results_and_fall_back_to_generic_failures() {
        let completed = ui_operation_outcome(
            serde_json::json!({ "status": "completed", "result": { "stateCount": 2 } }),
            "inspect",
        );
        assert!(completed.failure.is_none());
        assert_eq!(completed.result["stateCount"], 2);

        let bare = ui_operation_outcome(serde_json::json!({ "status": "completed" }), "inspect");
        assert!(bare.failure.is_none());
        assert_eq!(bare.result, Value::Null);

        let coded_only = ui_operation_outcome(
            serde_json::json!({ "status": "failed", "error": { "code": "timeout" } }),
            "inspect",
        );
        assert_eq!(coded_only.failure.as_deref(), Some("UI inspect failed"));
        assert_eq!(coded_only.result["error"]["code"], "timeout");

        let string_error =
            ui_operation_outcome(serde_json::json!({ "status": "failed", "error": "boom" }), "check");
        assert_eq!(string_error.failure.as_deref(), Some("boom"));
    }

    #[test]
    fn range_validation_names_the_flag_and_bounds() {
        assert!(validate_range("--depth", 0, 0, 64).is_ok());
        assert!(validate_range("--depth", 64, 0, 64).is_ok());
        let error = validate_range("--depth", 65, 0, 64).unwrap_err().to_string();
        assert_eq!(error, "--depth must be between 0 and 64");
        assert!(validate_range("--max-elements", 0, 1, 5000).is_err());
    }
}
