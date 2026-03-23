use crate::output;
use anyhow::Context as AnyhowContext;
use clap::Subcommand;
use flate2::read::GzDecoder;
use serde::Serialize;
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fs::{self, File};
use std::io::Read;
use std::path::{Component, Path, PathBuf};
use tar::Archive;

use super::{Context, UnityLifecyclePolicy};

const DEFAULT_SEARCH_RESULTS: usize = 50;

#[derive(Subcommand)]
pub enum PackagesAction {
    /// List installed Unity packages
    List {
        /// Use cached data only
        #[arg(long)]
        offline: bool,
        /// Include indirect dependencies
        #[arg(long)]
        all: bool,
    },
    /// Search available Unity packages
    Search {
        /// Package name or query fragment (omit to browse all discoverable packages)
        query: Option<String>,
        /// Use cached data only
        #[arg(long)]
        offline: bool,
        /// Maximum results to return
        #[arg(long, default_value_t = DEFAULT_SEARCH_RESULTS)]
        max: usize,
    },
    /// Inspect one Unity package
    Info {
        /// Package name or package id
        name: String,
        /// Use cached data only
        #[arg(long)]
        offline: bool,
    },
    /// Add or install a package reference
    Add {
        /// Package id or reference (e.g. com.unity.textmeshpro, name@version, git url, file path)
        package: String,
        /// Return after the Package Manager request completes without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
    /// Remove an installed package
    Remove {
        /// Package name
        name: String,
        /// Return after the Package Manager request completes without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
    /// List manifest dependencies
    Dependencies,
    /// Manage one manifest dependency directly
    Dependency {
        #[command(subcommand)]
        action: PackageDependencyAction,
    },
    /// Manage scoped registries in Packages/manifest.json
    Registries {
        #[command(subcommand)]
        action: PackageRegistryAction,
    },
    /// Inspect and selectively import a .unitypackage archive
    Unitypackage {
        #[command(subcommand)]
        action: UnitypackageAction,
    },
}

#[derive(Subcommand)]
pub enum PackageDependencyAction {
    /// Set or update one manifest dependency reference
    Set {
        /// Dependency name
        name: String,
        /// Dependency reference
        reference: String,
        /// Return after resolve without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
    /// Remove one manifest dependency
    Remove {
        /// Dependency name
        name: String,
        /// Return after resolve without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
}

#[derive(Subcommand)]
pub enum PackageRegistryAction {
    /// List scoped registries from Packages/manifest.json
    List,
    /// Add or update a scoped registry
    Add {
        /// Registry name
        #[arg(long)]
        name: String,
        /// Registry URL
        #[arg(long)]
        url: String,
        /// One or more scopes handled by this registry
        #[arg(long = "scope", required = true)]
        scopes: Vec<String>,
        /// Return after resolve without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
    /// Remove a scoped registry by name
    Remove {
        /// Registry name
        #[arg(long)]
        name: String,
        /// Return after resolve without waiting for bridge stabilization
        #[arg(long)]
        no_wait: bool,
    },
}

#[derive(Subcommand)]
pub enum UnitypackageAction {
    /// Inspect a .unitypackage archive and return its asset hierarchy
    Inspect {
        /// Path to the .unitypackage archive
        archive: String,
    },
    /// Import a .unitypackage archive, optionally selecting only some paths
    Import {
        /// Path to the .unitypackage archive
        archive: String,
        /// Select only these asset paths or folders from the archive
        #[arg(long = "select", alias = "include")]
        select: Vec<String>,
        /// Exclude these asset paths or folders from the archive
        #[arg(long = "unselect", alias = "exclude")]
        unselect: Vec<String>,
        /// Preview the selection without writing files
        #[arg(long)]
        dry_run: bool,
        /// Skip the Unity asset refresh/reimport after extraction
        #[arg(long)]
        no_reimport: bool,
    },
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct UnitypackageItem {
    archive_root: String,
    path: String,
    has_asset: bool,
    has_meta: bool,
    asset_size: u64,
    meta_size: u64,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct UnitypackageTreeNode {
    name: String,
    path: String,
    kind: String,
    children: Vec<UnitypackageTreeNode>,
}

#[derive(Debug, Clone, Default)]
struct UnitypackageEntryState {
    path: Option<String>,
    has_asset: bool,
    has_meta: bool,
    asset_size: u64,
    meta_size: u64,
}

pub async fn run(action: PackagesAction, ctx: &Context) -> anyhow::Result<()> {
    match action {
        PackagesAction::List { offline, all } => {
            run_bridge_action(
                "packages/list",
                serde_json::json!({ "offline": offline, "includeIndirect": all }),
                None,
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        print_package_list(&result, all);
                    }
                },
            )
            .await
        }
        PackagesAction::Search {
            query,
            offline,
            max,
        } => {
            run_bridge_action(
                "packages/search",
                serde_json::json!({ "query": query, "offline": offline, "maxResults": max }),
                None,
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        print_package_search(&result);
                    }
                },
            )
            .await
        }
        PackagesAction::Info { name, offline } => {
            run_bridge_action(
                "packages/info",
                serde_json::json!({ "name": name, "offline": offline }),
                None,
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        print_package_info(&result);
                    }
                },
            )
            .await
        }
        PackagesAction::Add { package, no_wait } => {
            run_bridge_action(
                "packages/add",
                serde_json::json!({ "packageId": package }),
                Some(no_wait),
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        let package_name = result
                            .get("package")
                            .and_then(|item| item.get("name"))
                            .and_then(|value| value.as_str())
                            .unwrap_or("package");
                        output::print_success(&format!("Installed {package_name}"));
                    }
                },
            )
            .await
        }
        PackagesAction::Remove { name, no_wait } => {
            run_bridge_action(
                "packages/remove",
                serde_json::json!({ "name": name }),
                Some(no_wait),
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        let removed = result
                            .get("name")
                            .and_then(|v| v.as_str())
                            .unwrap_or("package");
                        output::print_success(&format!("Removed {removed}"));
                    }
                },
            )
            .await
        }
        PackagesAction::Dependencies => {
            run_bridge_action(
                "packages/dependencies",
                serde_json::json!({}),
                None,
                ctx,
                |result, json| {
                    if json {
                        output::print_json(&output::success_json(result));
                    } else {
                        print_dependencies(&result);
                    }
                },
            )
            .await
        }
        PackagesAction::Dependency { action } => match action {
            PackageDependencyAction::Set {
                name,
                reference,
                no_wait,
            } => {
                run_bridge_action(
                    "packages/dependency/set",
                    serde_json::json!({ "name": name, "reference": reference }),
                    Some(no_wait),
                    ctx,
                    |result, json| {
                        if json {
                            output::print_json(&output::success_json(result));
                        } else {
                            let dep = result
                                .get("name")
                                .and_then(|v| v.as_str())
                                .unwrap_or("dependency");
                            output::print_success(&format!("Set dependency {dep}"));
                        }
                    },
                )
                .await
            }
            PackageDependencyAction::Remove { name, no_wait } => {
                run_bridge_action(
                    "packages/dependency/remove",
                    serde_json::json!({ "name": name }),
                    Some(no_wait),
                    ctx,
                    |result, json| {
                        if json {
                            output::print_json(&output::success_json(result));
                        } else {
                            let dep = result
                                .get("name")
                                .and_then(|v| v.as_str())
                                .unwrap_or("dependency");
                            output::print_success(&format!("Removed dependency {dep}"));
                        }
                    },
                )
                .await
            }
        },
        PackagesAction::Registries { action } => match action {
            PackageRegistryAction::List => {
                run_bridge_action(
                    "packages/registries/list",
                    serde_json::json!({}),
                    None,
                    ctx,
                    |result, json| {
                        if json {
                            output::print_json(&output::success_json(result));
                        } else {
                            print_registries(&result);
                        }
                    },
                )
                .await
            }
            PackageRegistryAction::Add {
                name,
                url,
                scopes,
                no_wait,
            } => {
                run_bridge_action(
                    "packages/registries/add",
                    serde_json::json!({ "name": name, "url": url, "scopes": scopes }),
                    Some(no_wait),
                    ctx,
                    |result, json| {
                        if json {
                            output::print_json(&output::success_json(result));
                        } else {
                            let name = result
                                .get("registry")
                                .and_then(|v| v.get("name"))
                                .and_then(|v| v.as_str())
                                .unwrap_or("registry");
                            output::print_success(&format!("Configured scoped registry {name}"));
                        }
                    },
                )
                .await
            }
            PackageRegistryAction::Remove { name, no_wait } => {
                run_bridge_action(
                    "packages/registries/remove",
                    serde_json::json!({ "name": name }),
                    Some(no_wait),
                    ctx,
                    |result, json| {
                        if json {
                            output::print_json(&output::success_json(result));
                        } else {
                            let name = result
                                .get("registry")
                                .and_then(|v| v.get("name"))
                                .and_then(|v| v.as_str())
                                .unwrap_or("registry");
                            output::print_success(&format!("Removed scoped registry {name}"));
                        }
                    },
                )
                .await
            }
        },
        PackagesAction::Unitypackage { action } => match action {
            UnitypackageAction::Inspect { archive } => {
                inspect_unitypackage_command(&archive, ctx).await
            }
            UnitypackageAction::Import {
                archive,
                select,
                unselect,
                dry_run,
                no_reimport,
            } => {
                import_unitypackage_command(&archive, &select, &unselect, dry_run, no_reimport, ctx)
                    .await
            }
        },
    }
}

async fn run_bridge_action<F>(
    method: &str,
    params: serde_json::Value,
    wait_for_settle: Option<bool>,
    ctx: &Context,
    render: F,
) -> anyhow::Result<()>
where
    F: FnOnce(serde_json::Value, bool),
{
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    if wait_for_settle.is_some() {
        super::enforce_active_scene_guard(
            &mut client,
            super::ActiveSceneGuardPolicy::block_if_dirty("change package dependencies"),
        )
        .await?;
    }

    let mut result = client.call(method, params).await?;
    client.close().await;

    if let Some(no_wait) = wait_for_settle {
        if !no_wait {
            let lifecycle = super::await_unity_lifecycle(
                &project,
                Some(&lock),
                UnityLifecyclePolicy::restart_then_settle(
                    "Waiting for Unity package import/resolve...",
                    "package processing",
                    120,
                ),
                ctx,
            )
            .await?;
            result = super::attach_lifecycle_log_status(result, &lifecycle);
        }
    }

    render(result, ctx.json);
    Ok(())
}


async fn inspect_unitypackage_command(archive: &str, ctx: &Context) -> anyhow::Result<()> {
    let archive_path = PathBuf::from(archive);
    let inspection = inspect_unitypackage(&archive_path)?;
    let payload = serde_json::to_value(&inspection)?;
    if ctx.json {
        output::print_json(&output::success_json(payload));
    } else {
        output::print_success(&format!(
            "{} importable asset(s) in {}",
            inspection.items.len(),
            archive_path.display()
        ));
        for item in &inspection.items {
            eprintln!(
                "  {}{}{}",
                item.path,
                if item.has_asset { "" } else { " [meta-only]" },
                if item.has_meta { "" } else { " [no-meta]" }
            );
        }
    }
    Ok(())
}

async fn import_unitypackage_command(
    archive: &str,
    include: &[String],
    exclude: &[String],
    dry_run: bool,
    no_reimport: bool,
    ctx: &Context,
) -> anyhow::Result<()> {
    let archive_path = PathBuf::from(archive);
    let inspection = inspect_unitypackage(&archive_path)?;
    let selected = select_unitypackage_items(&inspection.items, include, exclude);
    if selected.is_empty() {
        anyhow::bail!("Selection did not match any assets in the .unitypackage archive");
    }

    let project = super::resolve_project_path(ctx)?;
    if !dry_run && !no_reimport {
        super::enforce_active_scene_guard_for_project(
            &project,
            super::ActiveSceneGuardPolicy::block_if_dirty("import package assets"),
        )
        .await?;
    }
    let written_paths = if dry_run {
        Vec::new()
    } else {
        extract_unitypackage_selection(&archive_path, &project, &selected)?
    };

    let refresh = if dry_run || no_reimport {
        serde_json::json!({
            "performed": false,
            "skipped": true,
            "reason": if dry_run { "dry-run" } else { "skipped by request" }
        })
    } else {
        let (_, lock, mut client) = super::connect_client(ctx).await?;
        let refresh_result = client.call("refresh-assets", serde_json::json!({})).await?;
        client.close().await;
        let lifecycle = super::await_unity_lifecycle(
            &project,
            Some(&lock),
            UnityLifecyclePolicy::restart_then_settle(
                "Waiting for Unity package import/resolve...",
                "package processing",
                120,
            ),
            ctx,
        )
        .await?;
        serde_json::json!({
            "performed": true,
            "skipped": false,
            "result": super::attach_lifecycle_log_status(refresh_result, &lifecycle)
        })
    };

    let selected_items: Vec<UnitypackageItem> = inspection
        .items
        .into_iter()
        .filter(|item| selected.contains(&item.archive_root))
        .collect();
    let selected_hierarchy = build_hierarchy(&selected_items);
    let payload = serde_json::json!({
        "archive": archive_path.display().to_string(),
        "project": project.display().to_string(),
        "selectedCount": selected_items.len(),
        "selectedPaths": selected_items.iter().map(|item| item.path.clone()).collect::<Vec<_>>(),
        "items": selected_items,
        "hierarchy": selected_hierarchy,
        "writtenPaths": written_paths,
        "refresh": refresh,
        "dryRun": dry_run
    });

    if ctx.json {
        output::print_json(&output::success_json(payload));
    } else if dry_run {
        output::print_success(&format!(
            "Selected {} asset(s) from {}",
            payload["selectedCount"].as_u64().unwrap_or(0),
            archive_path.display()
        ));
        for path in payload["selectedPaths"].as_array().into_iter().flatten() {
            if let Some(path) = path.as_str() {
                eprintln!("  {path}");
            }
        }
    } else {
        output::print_success(&format!(
            "Imported {} asset(s) from {}",
            payload["selectedCount"].as_u64().unwrap_or(0),
            archive_path.display()
        ));
        for path in payload["writtenPaths"].as_array().into_iter().flatten() {
            if let Some(path) = path.as_str() {
                eprintln!("  {path}");
            }
        }
        if no_reimport {
            output::print_info("Skipped Unity refresh/reimport (--no-reimport)");
        }
    }

    Ok(())
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct UnitypackageInspection {
    archive: String,
    count: usize,
    items: Vec<UnitypackageItem>,
    hierarchy: Vec<UnitypackageTreeNode>,
}

fn inspect_unitypackage(archive_path: &Path) -> anyhow::Result<UnitypackageInspection> {
    let file = File::open(archive_path).with_context(|| {
        format!(
            "Failed to open .unitypackage archive {}",
            archive_path.display()
        )
    })?;
    let decoder = GzDecoder::new(file);
    let mut archive = Archive::new(decoder);
    let mut entries: HashMap<String, UnitypackageEntryState> = HashMap::new();

    for entry_result in archive.entries()? {
        let mut entry = entry_result?;
        let entry_path = entry.path()?.to_string_lossy().replace('\\', "/");
        let mut parts = entry_path.splitn(2, '/');
        let archive_root = parts.next().unwrap_or_default().to_string();
        let item_name = parts.next().unwrap_or_default();
        if archive_root.is_empty() || item_name.is_empty() {
            continue;
        }

        let state = entries.entry(archive_root).or_default();
        match item_name {
            "pathname" => {
                let mut buffer = String::new();
                entry.read_to_string(&mut buffer)?;
                let normalized = normalize_asset_path(buffer.trim());
                if !normalized.is_empty() {
                    state.path = Some(normalized);
                }
            }
            "asset" => {
                state.has_asset = true;
                state.asset_size = entry.header().size().unwrap_or(0);
            }
            "asset.meta" => {
                state.has_meta = true;
                state.meta_size = entry.header().size().unwrap_or(0);
            }
            _ => {}
        }
    }

    let mut items: Vec<UnitypackageItem> = entries
        .into_iter()
        .filter_map(|(archive_root, state)| {
            state.path.map(|path| UnitypackageItem {
                archive_root,
                path,
                has_asset: state.has_asset,
                has_meta: state.has_meta,
                asset_size: state.asset_size,
                meta_size: state.meta_size,
            })
        })
        .collect();
    items.sort_by(|left, right| left.path.cmp(&right.path));

    Ok(UnitypackageInspection {
        archive: archive_path.display().to_string(),
        count: items.len(),
        hierarchy: build_hierarchy(&items),
        items,
    })
}

fn select_unitypackage_items(
    items: &[UnitypackageItem],
    include: &[String],
    exclude: &[String],
) -> HashSet<String> {
    let normalized_include: Vec<String> = include
        .iter()
        .map(|path| normalize_asset_path(path))
        .collect();
    let normalized_exclude: Vec<String> = exclude
        .iter()
        .map(|path| normalize_asset_path(path))
        .collect();

    items
        .iter()
        .filter(|item| matches_selection(&item.path, &normalized_include, &normalized_exclude))
        .map(|item| item.archive_root.clone())
        .collect()
}

fn matches_selection(path: &str, include: &[String], exclude: &[String]) -> bool {
    let included = if include.is_empty() {
        true
    } else {
        include
            .iter()
            .any(|prefix| path_matches_prefix(path, prefix))
    };
    included
        && !exclude
            .iter()
            .any(|prefix| path_matches_prefix(path, prefix))
}

fn path_matches_prefix(path: &str, prefix: &str) -> bool {
    path == prefix
        || path
            .strip_prefix(prefix)
            .map(|suffix| suffix.starts_with('/'))
            .unwrap_or(false)
}

fn extract_unitypackage_selection(
    archive_path: &Path,
    project_root: &Path,
    selected_roots: &HashSet<String>,
) -> anyhow::Result<Vec<String>> {
    let file = File::open(archive_path)?;
    let decoder = GzDecoder::new(file);
    let mut archive = Archive::new(decoder);
    let selection_map = inspect_unitypackage(archive_path)?
        .items
        .into_iter()
        .filter(|item| selected_roots.contains(&item.archive_root))
        .map(|item| (item.archive_root, item.path))
        .collect::<HashMap<_, _>>();
    let mut written = BTreeMap::<String, ()>::new();

    for entry_result in archive.entries()? {
        let mut entry = entry_result?;
        let entry_path = entry.path()?.to_string_lossy().replace('\\', "/");
        let mut parts = entry_path.splitn(2, '/');
        let archive_root = parts.next().unwrap_or_default().to_string();
        let item_name = parts.next().unwrap_or_default();
        let Some(asset_path) = selection_map.get(&archive_root) else {
            continue;
        };

        let destination = match item_name {
            "asset" => Some(resolve_safe_project_path(project_root, asset_path)?),
            "asset.meta" => Some(resolve_safe_project_path(
                project_root,
                &format!("{asset_path}.meta"),
            )?),
            _ => None,
        };
        let Some(destination) = destination else {
            continue;
        };

        if let Some(parent) = destination.parent() {
            fs::create_dir_all(parent)?;
        }

        let mut output = File::create(&destination)?;
        std::io::copy(&mut entry, &mut output)?;
        written.insert(asset_path.clone(), ());
    }

    Ok(written.into_keys().collect())
}

fn resolve_safe_project_path(project_root: &Path, relative_path: &str) -> anyhow::Result<PathBuf> {
    let relative = Path::new(relative_path);
    if relative.is_absolute() {
        anyhow::bail!("Archive asset path must be relative: {}", relative_path);
    }
    for component in relative.components() {
        if matches!(
            component,
            Component::ParentDir | Component::RootDir | Component::Prefix(_)
        ) {
            anyhow::bail!("Archive asset path escapes project root: {}", relative_path);
        }
    }
    Ok(project_root.join(relative))
}

fn build_hierarchy(items: &[UnitypackageItem]) -> Vec<UnitypackageTreeNode> {
    #[derive(Default)]
    struct Tree {
        children: BTreeMap<String, Tree>,
        path: String,
        is_asset: bool,
    }

    fn insert(root: &mut Tree, path: &str) {
        let mut current = root;
        let mut current_path = String::new();
        let segments: Vec<&str> = path.split('/').collect();
        for (index, segment) in segments.iter().enumerate() {
            if !current_path.is_empty() {
                current_path.push('/');
            }
            current_path.push_str(segment);
            current = current
                .children
                .entry((*segment).to_string())
                .or_insert_with(|| Tree {
                    path: current_path.clone(),
                    ..Tree::default()
                });
            if index == segments.len() - 1 {
                current.is_asset = true;
            }
        }
    }

    fn to_nodes(tree: Tree) -> Vec<UnitypackageTreeNode> {
        tree.children
            .into_iter()
            .map(|(name, child)| UnitypackageTreeNode {
                name,
                path: child.path.clone(),
                kind: if child.is_asset {
                    "asset".to_string()
                } else {
                    "folder".to_string()
                },
                children: to_nodes(child),
            })
            .collect()
    }

    let mut root = Tree::default();
    for item in items {
        insert(&mut root, &item.path);
    }
    to_nodes(root)
}

fn normalize_asset_path(path: &str) -> String {
    path.trim().replace('\\', "/").trim_matches('/').to_string()
}

fn print_package_list(result: &serde_json::Value, include_indirect: bool) {
    let packages = result
        .get("packages")
        .and_then(|value| value.as_array())
        .cloned()
        .unwrap_or_default();
    let count = packages.len();
    if include_indirect {
        output::print_success(&format!(
            "{count} package(s) including indirect dependencies"
        ));
    } else {
        output::print_success(&format!("{count} direct package(s)"));
    }
    for package in packages {
        let name = package
            .get("name")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let version = package
            .get("version")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let source = package
            .get("source")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let direct = package
            .get("directDependency")
            .and_then(|value| value.as_bool())
            .unwrap_or(false);
        let marker = if direct { "*" } else { "-" };
        eprintln!("  [{marker}] {name} {version} ({source})");
    }
}

fn print_package_search(result: &serde_json::Value) {
    let packages = result
        .get("results")
        .and_then(|value| value.as_array())
        .cloned()
        .unwrap_or_default();
    output::print_success(&format!("{} package(s) found", packages.len()));
    for package in packages {
        let name = package
            .get("name")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let display_name = package
            .get("displayName")
            .and_then(|value| value.as_str())
            .unwrap_or(name);
        let version = package
            .get("version")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        eprintln!("  {name} {version} :: {display_name}");
    }
}

fn print_package_info(result: &serde_json::Value) {
    let name = result
        .get("name")
        .and_then(|value| value.as_str())
        .unwrap_or("?");
    let version = result
        .get("version")
        .and_then(|value| value.as_str())
        .unwrap_or("?");
    let source = result
        .get("source")
        .and_then(|value| value.as_str())
        .unwrap_or("?");
    output::print_success(&format!("{name} {version} ({source})"));
    if let Some(display_name) = result.get("displayName").and_then(|value| value.as_str()) {
        eprintln!("  Display: {display_name}");
    }
    if let Some(package_id) = result.get("packageId").and_then(|value| value.as_str()) {
        eprintln!("  Package ID: {package_id}");
    }
    if let Some(path) = result.get("resolvedPath").and_then(|value| value.as_str()) {
        eprintln!("  Path: {path}");
    }
    if let Some(description) = result.get("description").and_then(|value| value.as_str()) {
        if !description.is_empty() {
            eprintln!("  Description: {description}");
        }
    }
    if let Some(dependencies) = result
        .get("dependencies")
        .and_then(|value| value.as_array())
    {
        eprintln!("  Dependencies: {}", dependencies.len());
        for dependency in dependencies {
            let dep_name = dependency
                .get("name")
                .and_then(|value| value.as_str())
                .unwrap_or("?");
            let dep_version = dependency
                .get("version")
                .and_then(|value| value.as_str())
                .unwrap_or("?");
            eprintln!("    {dep_name} {dep_version}");
        }
    }
}

fn print_dependencies(result: &serde_json::Value) {
    let dependencies = result
        .get("dependencies")
        .and_then(|value| value.as_array())
        .cloned()
        .unwrap_or_default();
    output::print_success(&format!("{} manifest dependenc(ies)", dependencies.len()));
    for dependency in dependencies {
        let name = dependency
            .get("name")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let reference = dependency
            .get("reference")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        eprintln!("  {name} -> {reference}");
    }
}

fn print_registries(result: &serde_json::Value) {
    let registries = result
        .get("registries")
        .and_then(|value| value.as_array())
        .cloned()
        .unwrap_or_default();
    output::print_success(&format!("{} scoped registr(ies)", registries.len()));
    for registry in registries {
        let name = registry
            .get("name")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let url = registry
            .get("url")
            .and_then(|value| value.as_str())
            .unwrap_or("?");
        let scopes = registry
            .get("scopes")
            .and_then(|value| value.as_array())
            .map(|scopes| {
                scopes
                    .iter()
                    .filter_map(|scope| scope.as_str())
                    .collect::<Vec<_>>()
                    .join(", ")
            })
            .unwrap_or_default();
        eprintln!("  {name} -> {url}");
        if !scopes.is_empty() {
            eprintln!("    scopes: {scopes}");
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use flate2::Compression;
    use flate2::write::GzEncoder;
    use std::io::Cursor;
    use tar::Builder;

    #[test]
    fn inspect_unitypackage_collects_assets_and_meta() {
        let archive_path = write_test_unitypackage(&[
            ("root-a", "Assets/Test/Keep.txt", b"hello", b"meta-a"),
            ("root-b", "Assets/Test/Sub/Skip.txt", b"world", b"meta-b"),
        ]);

        let inspection = inspect_unitypackage(&archive_path).expect("inspect unitypackage");
        assert_eq!(inspection.count, 2);
        assert_eq!(inspection.items[0].path, "Assets/Test/Keep.txt");
        assert!(inspection.items[0].has_asset);
        assert!(inspection.items[0].has_meta);
        assert_eq!(inspection.items[1].path, "Assets/Test/Sub/Skip.txt");

        let _ = std::fs::remove_file(archive_path);
    }

    #[test]
    fn selection_supports_folder_include_and_exclude() {
        let items = vec![
            UnitypackageItem {
                archive_root: "a".into(),
                path: "Assets/Test/Keep.txt".into(),
                has_asset: true,
                has_meta: true,
                asset_size: 1,
                meta_size: 1,
            },
            UnitypackageItem {
                archive_root: "b".into(),
                path: "Assets/Test/Sub/Skip.txt".into(),
                has_asset: true,
                has_meta: true,
                asset_size: 1,
                meta_size: 1,
            },
        ];

        let selected = select_unitypackage_items(
            &items,
            &[String::from("Assets/Test")],
            &[String::from("Assets/Test/Sub")],
        );
        assert!(selected.contains("a"));
        assert!(!selected.contains("b"));
    }

    #[test]
    fn extract_unitypackage_selection_writes_only_selected_assets() {
        let archive_path = write_test_unitypackage(&[
            ("root-a", "Assets/Test/Keep.txt", b"hello", b"meta-a"),
            ("root-b", "Assets/Test/Sub/Skip.txt", b"world", b"meta-b"),
        ]);
        let temp_root =
            std::env::temp_dir().join(format!("ucp-unitypackage-extract-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&temp_root).unwrap();
        let mut selected = HashSet::new();
        selected.insert(String::from("root-a"));

        let written = extract_unitypackage_selection(&archive_path, &temp_root, &selected)
            .expect("extract unitypackage selection");
        assert_eq!(written, vec![String::from("Assets/Test/Keep.txt")]);
        assert!(
            temp_root
                .join("Assets")
                .join("Test")
                .join("Keep.txt")
                .is_file()
        );
        assert!(
            temp_root
                .join("Assets")
                .join("Test")
                .join("Keep.txt.meta")
                .is_file()
        );
        assert!(
            !temp_root
                .join("Assets")
                .join("Test")
                .join("Sub")
                .join("Skip.txt")
                .exists()
        );

        let _ = std::fs::remove_file(archive_path);
        let _ = std::fs::remove_dir_all(temp_root);
    }

    fn write_test_unitypackage(entries: &[(&str, &str, &[u8], &[u8])]) -> PathBuf {
        let archive_path =
            std::env::temp_dir().join(format!("ucp-test-{}.unitypackage", uuid::Uuid::new_v4()));
        let file = File::create(&archive_path).unwrap();
        let encoder = GzEncoder::new(file, Compression::default());
        let mut builder = Builder::new(encoder);

        for (root, pathname, asset, meta) in entries {
            append_tar_file(
                &mut builder,
                &format!("{root}/pathname"),
                pathname.as_bytes(),
            );
            append_tar_file(&mut builder, &format!("{root}/asset"), asset);
            append_tar_file(&mut builder, &format!("{root}/asset.meta"), meta);
        }

        builder.finish().unwrap();
        archive_path
    }

    fn append_tar_file(builder: &mut Builder<GzEncoder<File>>, path: &str, content: &[u8]) {
        let mut header = tar::Header::new_gnu();
        header.set_size(content.len() as u64);
        header.set_mode(0o644);
        header.set_cksum();
        builder
            .append_data(&mut header, path, Cursor::new(content))
            .unwrap();
    }
}
