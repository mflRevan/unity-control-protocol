//! Explains *why* an editor is up but its bridge is not.
//!
//! The bridge ships as a UPM package, and Unity does not load packages in Safe Mode. So a project
//! with C# compile errors can present as "the editor is running but nothing answers" -- which used
//! to be reported as "it is likely still closing or stuck", sending callers to look in the wrong
//! place entirely. Unity documents the same deadlock for its own `com.unity.pipeline` package with
//! no CLI-side workaround.
//!
//! UCP's default `--dialog-policy auto` answers Unity's "Enter Safe Mode?" prompt with *Ignore*, so
//! the editor boots normally and the bridge loads even with broken scripts. This module handles the
//! cases where that did not happen: `--dialog-policy safe-mode`/`recover`, an editor the user
//! started themselves, or a prompt that appeared before UCP was watching.

use serde::Serialize;
use std::collections::HashSet;
use std::fs;
use std::path::Path;

/// Cap on how many distinct error lines are surfaced. Compile errors repeat verbatim across
/// assemblies and Unity's own log dump is enormous; agents pay for every line of it.
const MAX_REPORTED: usize = 10;

/// How far back in the log to look. Startup diagnostics all land in the final stretch.
const TAIL_LINES: usize = 400;

#[derive(Debug, Clone, Default, Serialize)]
pub struct EditorDiagnosis {
    /// Unity booted into Safe Mode, so no package -- including the bridge -- is loaded.
    pub safe_mode: bool,
    /// Distinct `error CSxxxx` lines, deduplicated and capped.
    pub compile_errors: Vec<String>,
    /// Package-resolution failures, which also prevent the bridge from loading.
    pub package_errors: Vec<String>,
}

impl EditorDiagnosis {
    pub fn is_empty(&self) -> bool {
        !self.safe_mode && self.compile_errors.is_empty() && self.package_errors.is_empty()
    }

    /// A short, actionable explanation. Ordered most-specific first.
    pub fn explain(&self) -> Vec<String> {
        let mut lines = Vec::new();

        if self.safe_mode {
            lines.push(
                "Unity is in Safe Mode, so no packages are loaded -- including the UCP bridge. \
                 That is why nothing answers. Fix the compile errors below and reopen; \
                 `ucp open` uses --dialog-policy auto by default, which declines the Safe Mode \
                 prompt so the bridge stays reachable while you work through them."
                    .to_string(),
            );
        } else if !self.compile_errors.is_empty() {
            lines.push(
                "The project has C# compile errors. The editor may be waiting on the \
                 \"Enter Safe Mode?\" prompt; reopen with `ucp open --dialog-policy auto` to \
                 decline it and keep the bridge reachable."
                    .to_string(),
            );
        }

        if !self.package_errors.is_empty() {
            lines.push(
                "Unity could not resolve the project's packages, which prevents the bridge \
                 assembly from loading. Check Packages/manifest.json."
                    .to_string(),
            );
        }

        lines
    }

    /// The error lines themselves, already deduplicated and capped.
    pub fn details(&self) -> Vec<String> {
        self.compile_errors
            .iter()
            .chain(self.package_errors.iter())
            .cloned()
            .collect()
    }
}

/// Scan the project's UCP-managed editor log for reasons the bridge is absent.
///
/// Reads only `<project>/.ucp/logs/editor.log` -- the log UCP itself launched the editor with --
/// never Unity's per-user global `Editor.log`, which carries paths and project names from
/// unrelated sessions.
pub fn diagnose(project: &Path) -> EditorDiagnosis {
    let log_path = crate::config::editor_log_path(project);
    let Ok(content) = fs::read_to_string(&log_path) else {
        return EditorDiagnosis::default();
    };
    diagnose_log(&content)
}

fn diagnose_log(content: &str) -> EditorDiagnosis {
    let tail: Vec<&str> = {
        let all: Vec<&str> = content.lines().collect();
        let start = all.len().saturating_sub(TAIL_LINES);
        all[start..].to_vec()
    };

    let mut diagnosis = EditorDiagnosis::default();
    let mut seen_compile = HashSet::new();
    let mut seen_package = HashSet::new();

    for line in tail {
        let lower = line.to_ascii_lowercase();

        // The bridge logs its own handler failures through the same file; those are not
        // startup blockers and must not be mistaken for them.
        if lower.contains("[ucp] error handling") {
            continue;
        }

        if lower.contains("safe mode: only loading a subset of assemblies")
            || lower.contains("changemode(safe_mode)")
        {
            diagnosis.safe_mode = true;
            continue;
        }

        let trimmed = line.trim();

        if is_compile_error(trimmed) {
            if seen_compile.insert(trimmed.to_string())
                && diagnosis.compile_errors.len() < MAX_REPORTED
            {
                diagnosis.compile_errors.push(trimmed.to_string());
            }
            continue;
        }

        if lower.contains("project has invalid dependencies")
            || lower.contains("an error occurred while resolving packages")
        {
            if seen_package.insert(trimmed.to_string())
                && diagnosis.package_errors.len() < MAX_REPORTED
            {
                diagnosis.package_errors.push(trimmed.to_string());
            }
        }
    }

    diagnosis
}

/// Match a real compiler diagnostic (`Foo.cs(4,18): error CS1026: ...`) rather than any line that
/// happens to contain the substring "error cs". The previous substring test fired on compiler
/// response-file entries and assembly paths, then dumped 200 lines of raw log as "evidence".
fn is_compile_error(line: &str) -> bool {
    let Some(idx) = line.find("error CS") else {
        return false;
    };
    line[idx + "error CS".len()..]
        .chars()
        .take(4)
        .filter(|c| c.is_ascii_digit())
        .count()
        == 4
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_safe_mode_and_dedupes_compile_errors() {
        let log = "\
-r:\"D:/Unity/Editor/Data/Managed/UnityEngine/UnityEditor.SafeModeModule.dll\"
Assets\\Probe.cs(4,18): error CS1026: ) expected
Assets\\Probe.cs(4,18): error CS1026: ) expected
Safe Mode: Only loading a subset of assemblies
";
        let d = diagnose_log(log);
        assert!(d.safe_mode);
        assert_eq!(d.compile_errors.len(), 1, "repeated diagnostics collapse");
        assert!(!d.explain().is_empty());
    }

    #[test]
    fn ignores_reference_lines_that_merely_mention_safemode() {
        let log =
            "-r:\"Library/PackageCache/com.unity.collections/UnityEditor.SafeModeModule.dll\"\n";
        let d = diagnose_log(log);
        assert!(!d.safe_mode);
        assert!(d.is_empty());
    }

    #[test]
    fn ignores_bridge_handler_errors() {
        let log = "[UCP] Error handling request: error CS9999 in a message body\n";
        assert!(diagnose_log(log).is_empty());
    }

    #[test]
    fn caps_reported_errors() {
        let log: String = (0..50)
            .map(|i| format!("Assets\\A.cs({i},1): error CS0103: bad {i}\n"))
            .collect();
        assert_eq!(diagnose_log(&log).compile_errors.len(), MAX_REPORTED);
    }
}
