pub mod engine;

use crate::output;
use clap::{Subcommand, ValueEnum};
use std::path::Path;
use std::time::Instant;

use super::Context;

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum FindDetailArg {
    Summary,
    Normal,
    Verbose,
}

impl FindDetailArg {
    fn into_detail_level(self) -> engine::DetailLevel {
        match self {
            Self::Summary => engine::DetailLevel::Summary,
            Self::Normal => engine::DetailLevel::Normal,
            Self::Verbose => engine::DetailLevel::Verbose,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum FindApproachArg {
    Auto,
    #[value(name = "rust-grep")]
    RustGrep,
    #[value(name = "rust-yaml")]
    RustYaml,
    Bridge,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum IndexBuildApproachArg {
    Auto,
    Grep,
    Yaml,
}

#[derive(Subcommand)]
pub enum ReferencesAction {
    /// Find all references to an asset or object
    Find {
        /// Asset path or GUID to search for
        #[arg(long, short = 'a')]
        asset: Option<String>,
        /// Specific object within an asset as guid:fileId
        #[arg(long, short = 'o')]
        object: Option<String>,
        /// Maximum files to include in results (default: 50)
        #[arg(long, default_value = "50")]
        max_files: usize,
        /// Maximum detail entries per file before pattern-collapsing (default: 5)
        #[arg(long, default_value = "5")]
        max_per_file: usize,
        /// Repetition threshold: collapse groups of N+ identical type/property refs (default: 3)
        #[arg(long, default_value = "3")]
        pattern_threshold: usize,
        /// Detail level: summary, normal, verbose
        #[arg(long, value_enum, default_value_t = FindDetailArg::Normal)]
        detail: FindDetailArg,
        /// Search approach: auto, rust-grep, rust-yaml, bridge
        #[arg(long, value_enum, default_value_t = FindApproachArg::Auto)]
        approach: FindApproachArg,
    },
    /// Build, query, or manage the reference index
    Index {
        #[command(subcommand)]
        action: IndexAction,
    },
    /// Check project serialization compatibility for native indexing
    Check,
}

#[derive(Subcommand)]
pub enum IndexAction {
    /// Build a full reference index from disk
    Build {
        /// Approach: grep, yaml, or auto (defaults to auto which picks yaml if available)
        #[arg(long, value_enum, default_value_t = IndexBuildApproachArg::Auto)]
        approach: IndexBuildApproachArg,
    },
    /// Show index status and statistics
    Status,
    /// Clear the cached reference index
    Clear,
}

/// Serialization compatibility status for native Rust indexing.
#[derive(Debug, Clone, serde::Serialize)]
pub struct SerializationStatus {
    pub force_text: bool,
    pub visible_meta: bool,
    pub native_capable: bool,
    pub message: String,
}

/// Check project serialization settings by reading EditorSettings.asset and VersionControlSettings.asset.
pub fn check_serialization(project: &Path) -> SerializationStatus {
    let editor_settings = project.join("ProjectSettings").join("EditorSettings.asset");
    let vcs_settings = project
        .join("ProjectSettings")
        .join("VersionControlSettings.asset");

    let force_text = std::fs::read_to_string(&editor_settings)
        .ok()
        .and_then(|content| {
            for line in content.lines() {
                let trimmed = line.trim();
                if trimmed.starts_with("m_SerializationMode:") {
                    return trimmed
                        .split(':')
                        .nth(1)
                        .and_then(|v| v.trim().parse::<u32>().ok())
                        .map(|v| v == 2);
                }
            }
            None
        })
        .unwrap_or(false);

    let visible_meta = std::fs::read_to_string(&vcs_settings)
        .ok()
        .and_then(|content| {
            for line in content.lines() {
                let trimmed = line.trim();
                if trimmed.starts_with("m_Mode:") {
                    return Some(
                        trimmed
                            .split(':')
                            .nth(1)
                            .map(|v| v.trim())
                            .unwrap_or("")
                            .contains("Visible Meta Files"),
                    );
                }
            }
            None
        })
        .unwrap_or(false);

    let native_capable = force_text && visible_meta;
    let message = if native_capable {
        "Project is configured for native Rust indexing (Force Text + Visible Meta Files)".into()
    } else {
        let mut issues = Vec::new();
        if !force_text {
            issues.push("Asset Serialization Mode is not Force Text (set to Force Text in Edit > Project Settings > Editor)");
        }
        if !visible_meta {
            issues.push("Version Control Mode is not Visible Meta Files (set in Edit > Project Settings > Version Control)");
        }
        format!(
            "Native indexing unavailable: {}. Falling back to bridge-based search.",
            issues.join("; ")
        )
    };

    SerializationStatus {
        force_text,
        visible_meta,
        native_capable,
        message,
    }
}

pub async fn run(action: ReferencesAction, ctx: &Context) -> anyhow::Result<()> {
    let project = super::resolve_project_path(ctx)?;

    match action {
        ReferencesAction::Check => {
            let status = check_serialization(&project);
            if ctx.json {
                output::print_json(&output::success_json(serde_json::to_value(&status)?));
            } else {
                let icon_ok = if output::supports_unicode() {
                    "✔"
                } else {
                    "[OK]"
                };
                let icon_err = if output::supports_unicode() {
                    "✖"
                } else {
                    "[ERR]"
                };
                let ft_icon = if status.force_text { icon_ok } else { icon_err };
                let vm_icon = if status.visible_meta { icon_ok } else { icon_err };
                eprintln!(
                    "  {} Force Text serialization",
                    console::style(ft_icon).bold()
                );
                eprintln!(
                    "  {} Visible Meta Files",
                    console::style(vm_icon).bold()
                );
                eprintln!();
                if status.native_capable {
                    output::print_success("Native Rust indexing is available");
                } else {
                    output::print_warn(&status.message);
                }
            }
            Ok(())
        }
        ReferencesAction::Index { action } => match action {
            IndexAction::Build { approach } => {
                let status = check_serialization(&project);
                if !status.native_capable {
                    if !ctx.json {
                        output::print_warn(&status.message);
                    }
                    anyhow::bail!(
                        "Cannot build native index: {}",
                        if !status.force_text {
                            "serialization mode is not Force Text"
                        } else {
                            "meta files are not visible"
                        }
                    );
                }

                let approach = match approach {
                    IndexBuildApproachArg::Grep => engine::IndexApproach::Grep,
                    IndexBuildApproachArg::Yaml | IndexBuildApproachArg::Auto => {
                        engine::IndexApproach::Yaml
                    }
                };

                if !ctx.json {
                    output::print_info(&format!(
                        "Building reference index ({:?} approach)...",
                        approach
                    ));
                }

                let start = Instant::now();
                let index = engine::build_index(&project, approach)?;
                let elapsed = start.elapsed();

                let stats = serde_json::json!({
                    "approach": format!("{:?}", approach),
                    "filesScanned": index.files_scanned,
                    "referencesFound": index.total_references,
                    "uniqueTargets": index.unique_targets(),
                    "elapsedMs": elapsed.as_millis() as u64,
                    "elapsedHuman": format!("{:.2}s", elapsed.as_secs_f64()),
                });

                if ctx.json {
                    output::print_json(&output::success_json(stats));
                } else {
                    output::print_success(&format!(
                        "Index built in {:.2}s: {} files, {} references, {} unique targets",
                        elapsed.as_secs_f64(),
                        index.files_scanned,
                        index.total_references,
                        index.unique_targets(),
                    ));
                }
                Ok(())
            }
            IndexAction::Status => {
                let status = check_serialization(&project);
                if ctx.json {
                    output::print_json(&output::success_json(serde_json::json!({
                        "serialization": status,
                    })));
                } else {
                    eprintln!("  Native capable: {}", status.native_capable);
                }
                Ok(())
            }
            IndexAction::Clear => {
                if !ctx.json {
                    output::print_success("Reference index cleared");
                } else {
                    output::print_json(&output::success_json(
                        serde_json::json!({"cleared": true}),
                    ));
                }
                Ok(())
            }
        },
        ReferencesAction::Find {
            asset,
            object,
            max_files,
            max_per_file,
            pattern_threshold,
            detail,
            approach,
        } => {
            if asset.is_some() == object.is_some() {
                anyhow::bail!("Provide exactly one of --asset or --object");
            }

            let status = check_serialization(&project);

            let target_guid = if let Some(ref asset_arg) = asset {
                resolve_guid(&project, asset_arg)?
            } else {
                Some(parse_object_target(object.as_deref().expect("validated above"))?.0)
            };

            let target_file_id = object
                .as_deref()
                .map(parse_object_target)
                .transpose()?
                .map(|(_, file_id)| file_id);

            let target_guid = target_guid.ok_or_else(|| {
                anyhow::anyhow!("Could not resolve GUID for the specified target")
            })?;

            let detail_level = detail.into_detail_level();

            let effective_approach = match approach {
                FindApproachArg::RustGrep => engine::IndexApproach::Grep,
                FindApproachArg::RustYaml => engine::IndexApproach::Yaml,
                FindApproachArg::Bridge => {
                    if !ctx.json {
                        output::print_info(
                            "Using bridge-based search (requires running Unity editor)",
                        );
                    }
                    return run_bridge_find(
                        ctx,
                        &target_guid,
                        target_file_id,
                        max_files.saturating_mul(max_per_file),
                    )
                    .await;
                }
                FindApproachArg::Auto => {
                    if status.native_capable {
                        engine::IndexApproach::Yaml
                    } else {
                        if !ctx.json {
                            output::print_warn(&status.message);
                            output::print_info("Falling back to bridge-based search");
                        }
                        return run_bridge_find(
                            ctx,
                            &target_guid,
                            target_file_id,
                            max_files.saturating_mul(max_per_file),
                        )
                        .await;
                    }
                }
            };

            if !ctx.json {
                output::print_info(&format!(
                    "Searching for references to {} ({:?} approach)...",
                    target_guid, effective_approach
                ));
            }

            let start = Instant::now();
            let index = engine::build_index(&project, effective_approach)?;
            let elapsed = start.elapsed();

            let grouped = index.find_grouped(
                &target_guid,
                target_file_id,
                max_files,
                max_per_file,
                pattern_threshold,
                detail_level,
                elapsed,
            );

            if ctx.json {
                output::print_json(&output::success_json(serde_json::to_value(&grouped)?));
            } else {
                print_human_grouped(&grouped, detail_level);
            }

            Ok(())
        }
    }
}

fn parse_object_target(value: &str) -> anyhow::Result<(String, i64)> {
    let mut parts = value.split(':');
    let guid = parts
        .next()
        .ok_or_else(|| anyhow::anyhow!("Object target must use guid:fileId format"))?;
    let file_id = parts
        .next()
        .ok_or_else(|| anyhow::anyhow!("Object target must use guid:fileId format"))?;
    if parts.next().is_some() {
        anyhow::bail!("Object target must use guid:fileId format");
    }
    if guid.len() != 32 || !guid.chars().all(|c| c.is_ascii_hexdigit()) {
        anyhow::bail!("Object target GUID must be a 32-character hex string");
    }

    let file_id = file_id
        .parse::<i64>()
        .map_err(|_| anyhow::anyhow!("Object target fileId must be a valid integer"))?;

    Ok((guid.to_string(), file_id))
}

fn resolve_guid(project: &Path, asset_arg: &str) -> anyhow::Result<Option<String>> {
    if asset_arg.len() == 32 && asset_arg.chars().all(|c| c.is_ascii_hexdigit()) {
        return Ok(Some(asset_arg.to_string()));
    }

    let asset_path = if asset_arg.starts_with("Assets") {
        project.join(asset_arg)
    } else {
        project.join("Assets").join(asset_arg)
    };

    let meta_path = format!("{}.meta", asset_path.display());
    let meta_content = std::fs::read_to_string(&meta_path)
        .map_err(|_| anyhow::anyhow!("Cannot read meta file: {}", meta_path))?;

    for line in meta_content.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("guid:") {
            return Ok(Some(
                trimmed.split(':').nth(1).unwrap_or("").trim().to_string(),
            ));
        }
    }

    Err(anyhow::anyhow!(
        "No GUID found in meta file: {}",
        meta_path
    ))
}

#[cfg(test)]
mod tests {
    use super::{
        FindApproachArg, FindDetailArg, IndexBuildApproachArg, parse_object_target, resolve_guid,
    };
    use clap::ValueEnum;
    use std::fs;

    #[test]
    fn parse_object_target_accepts_guid_and_file_id() {
        let (guid, file_id) =
            parse_object_target("1234567890abcdef1234567890abcdef:11400000").unwrap();
        assert_eq!(guid, "1234567890abcdef1234567890abcdef");
        assert_eq!(file_id, 11_400_000);
    }

    #[test]
    fn parse_object_target_rejects_invalid_formats() {
        assert!(parse_object_target("missing-colon").is_err());
        assert!(parse_object_target("too:many:parts").is_err());
        assert!(parse_object_target("shortguid:1").is_err());
        assert!(parse_object_target("1234567890abcdef1234567890abcdef:not-a-number").is_err());
    }

    #[test]
    fn resolve_guid_supports_relative_asset_path() {
        let temp_root =
            std::env::temp_dir().join(format!("ucp-resolve-guid-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&temp_root);
        fs::create_dir_all(temp_root.join("Assets").join("Materials")).unwrap();
        fs::write(
            temp_root
                .join("Assets")
                .join("Materials")
                .join("Agent.mat.meta"),
            "fileFormatVersion: 2\nguid: adbf4a7415ede7c42ada304e953520f6\n",
        )
        .unwrap();

        let guid = resolve_guid(&temp_root, "Materials/Agent.mat").unwrap();
        assert_eq!(guid.as_deref(), Some("adbf4a7415ede7c42ada304e953520f6"));

        let _ = fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn clap_value_enums_expose_expected_variants() {
        let detail_values: Vec<_> = FindDetailArg::value_variants()
            .iter()
            .filter_map(|v| v.to_possible_value())
            .map(|v| v.get_name().to_string())
            .collect();
        assert_eq!(detail_values, vec!["summary", "normal", "verbose"]);

        let find_approaches: Vec<_> = FindApproachArg::value_variants()
            .iter()
            .filter_map(|v| v.to_possible_value())
            .map(|v| v.get_name().to_string())
            .collect();
        assert_eq!(
            find_approaches,
            vec!["auto", "rust-grep", "rust-yaml", "bridge"]
        );

        let build_approaches: Vec<_> = IndexBuildApproachArg::value_variants()
            .iter()
            .filter_map(|v| v.to_possible_value())
            .map(|v| v.get_name().to_string())
            .collect();
        assert_eq!(build_approaches, vec!["auto", "grep", "yaml"]);
    }
}

async fn run_bridge_find(
    ctx: &Context,
    target_guid: &str,
    target_file_id: Option<i64>,
    max: usize,
) -> anyhow::Result<()> {
    let (_project, _lock, mut client) = super::connect_client(ctx).await?;

    let mut params = serde_json::json!({
        "guid": target_guid,
        "maxResults": max,
    });
    if let Some(fid) = target_file_id {
        params["fileId"] = serde_json::json!(fid);
    }

    let result = client.call("references/find", params).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        let count = result
            .get("references")
            .and_then(|v| v.as_array())
            .map(|a| a.len())
            .unwrap_or(0);
        output::print_success(&format!("Found {} reference(s) via bridge", count));
        if let Some(refs) = result.get("references").and_then(|v| v.as_array()) {
            for r in refs {
                let source = r
                    .get("sourcePath")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                let prop = r
                    .get("propertyPath")
                    .and_then(|v| v.as_str())
                    .unwrap_or("?");
                eprintln!("  {} → {}", source, prop);
            }
        }
    }

    Ok(())
}

/// Render grouped results with intelligent truncation for human consumption.
fn print_human_grouped(grouped: &engine::GroupedResults, detail: engine::DetailLevel) {
    output::print_success(&format!(
        "Found {} reference(s) across {} file(s) ({} distinct objects) in {}ms",
        grouped.total_refs,
        grouped.total_files,
        grouped.total_distinct_objects,
        grouped.elapsed_ms,
    ));

    // Print top-level pattern summary if patterns detected
    if !grouped.top_patterns.is_empty()
        && matches!(detail, engine::DetailLevel::Summary | engine::DetailLevel::Normal)
    {
        eprintln!();
        eprintln!("  {}", console::style("Dominant patterns:").dim());
        for p in &grouped.top_patterns {
            eprintln!(
                "    {} × {}.{}",
                console::style(format!("{:>4}", p.count)).cyan().bold(),
                p.source_type,
                p.property
            );
        }
    }

    for file in &grouped.files {
        eprintln!();
        let refs_label = if file.total_refs == 1 {
            "ref".to_string()
        } else {
            format!("{} refs", file.total_refs)
        };
        let objects_label = if file.distinct_objects == file.total_refs {
            String::new()
        } else {
            format!(", {} objects", file.distinct_objects)
        };
        eprintln!(
            "  {} ({}{})",
            console::style(&file.path).bold(),
            refs_label,
            objects_label,
        );

        // Print collapsed patterns for this file
        for pattern in &file.patterns {
            let samples = if pattern.sample_names.is_empty() {
                String::new()
            } else {
                let names = pattern.sample_names.join(", ");
                if pattern.count > pattern.sample_names.len() {
                    format!(" (e.g. {})", names)
                } else {
                    format!(" ({})", names)
                }
            };
            eprintln!(
                "    {} × {}.{}{}",
                console::style(format!("{:>3}", pattern.count)).cyan(),
                console::style(&pattern.source_type).dim(),
                &pattern.property,
                console::style(samples).dim(),
            );
        }

        // Print individual details
        for hit in &file.details {
            let obj = hit
                .source_object_name
                .as_deref()
                .unwrap_or(&hit.source_object_type);
            let fid = hit.source_file_id;
            let prop = &hit.property_hint;
            eprintln!("    [{obj}#{fid}] {prop}");
        }

        if file.truncated {
            let shown = file.details.len() + file.patterns.iter().map(|p| p.count).sum::<usize>();
            if shown < file.total_refs {
                eprintln!(
                    "    {}",
                    console::style(format!(
                        "... and {} more (use --detail verbose to see all)",
                        file.total_refs - file.details.len()
                    ))
                    .dim()
                );
            }
        }
    }

    if grouped.files.len() < grouped.total_files {
        eprintln!();
        eprintln!(
            "  {}",
            console::style(format!(
                "Showing {}/{} files (use --max-files to see more)",
                grouped.files.len(),
                grouped.total_files
            ))
            .dim()
        );
    }
}
