use rayon::prelude::*;
use std::collections::HashMap;
use std::path::{Path, PathBuf};

/// The approach used to build the reference index.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IndexApproach {
    /// Fast regex scan for guid/fileID patterns — lowest latency, less structural detail
    Grep,
    /// Structured line-by-line parser — understands YAML document boundaries, property paths
    Yaml,
}

/// A single reference hit from source → target.
#[derive(Debug, Clone, serde::Serialize)]
pub struct ReferenceHit {
    /// Relative path of the source file (e.g. "Assets/Scenes/Main.unity")
    pub source_path: String,
    /// Local fileId of the source object containing the reference
    pub source_file_id: i64,
    /// Unity class name of the source object (e.g. "MonoBehaviour", "MeshRenderer")
    pub source_object_type: String,
    /// Inferred name of the source object (from m_Name or m_GameObject context)
    pub source_object_name: Option<String>,
    /// Property path or context hint (e.g. "m_Material", "m_Script")
    pub property_hint: String,
    /// GUID of the target asset being referenced
    pub target_guid: String,
    /// Optional fileId within the target asset
    pub target_file_id: Option<i64>,
    /// The reference type from Unity serialization (0=internal, 2=asset, 3=source)
    pub ref_type: Option<i32>,
}

/// Summary of a detected repetitive reference pattern.
#[derive(Debug, Clone, serde::Serialize)]
pub struct ReferencePattern {
    /// The component type producing this pattern (e.g. "MeshRenderer")
    pub source_type: String,
    /// The property producing this pattern (e.g. "m_Materials")
    pub property: String,
    /// Number of references matching this pattern in this file/scope
    pub count: usize,
    /// Sample source object names (first few, for context)
    pub sample_names: Vec<String>,
}

/// A grouped reference summary for one source file.
#[derive(Debug, Clone, serde::Serialize)]
pub struct FileReferenceSummary {
    /// Source file path
    pub path: String,
    /// Total references from this file to the target
    pub total_refs: usize,
    /// Distinct source objects that reference the target
    pub distinct_objects: usize,
    /// Detected repetitive patterns (collapsed)
    pub patterns: Vec<ReferencePattern>,
    /// Individual references (only included up to detail limit)
    pub details: Vec<ReferenceHit>,
    /// Whether details were truncated
    pub truncated: bool,
}

/// Grouped and intelligently truncated reference results.
#[derive(Debug, Clone, serde::Serialize)]
pub struct GroupedResults {
    pub target_guid: String,
    pub target_file_id: Option<i64>,
    pub total_refs: usize,
    pub total_files: usize,
    pub total_distinct_objects: usize,
    pub files: Vec<FileReferenceSummary>,
    pub elapsed_ms: u64,
    /// Top-level pattern summary across all files
    pub top_patterns: Vec<ReferencePattern>,
}

/// Detail level for output control.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DetailLevel {
    /// Only file-level summaries with pattern counts
    Summary,
    /// File summaries + collapsed patterns + limited per-file details (default)
    Normal,
    /// Full individual references, no truncation
    Verbose,
}

/// Complete reference index built from a project scan.
pub struct ReferenceIndex {
    pub files_scanned: usize,
    pub total_references: usize,
    /// All references keyed by target GUID for fast lookup
    references_by_guid: HashMap<String, Vec<ReferenceHit>>,
}

impl ReferenceIndex {
    pub fn unique_targets(&self) -> usize {
        self.references_by_guid.len()
    }

    /// Find references with intelligent grouping and truncation.
    pub fn find_grouped(
        &self,
        guid: &str,
        file_id: Option<i64>,
        max_files: usize,
        max_detail_per_file: usize,
        pattern_threshold: usize,
        detail: DetailLevel,
        elapsed: std::time::Duration,
    ) -> GroupedResults {
        let refs = match self.references_by_guid.get(guid) {
            Some(r) => r,
            None => {
                return GroupedResults {
                    target_guid: guid.to_string(),
                    target_file_id: file_id,
                    total_refs: 0,
                    total_files: 0,
                    total_distinct_objects: 0,
                    files: Vec::new(),
                    elapsed_ms: elapsed.as_millis() as u64,
                    top_patterns: Vec::new(),
                }
            }
        };

        // Filter by target file_id if specified
        let filtered: Vec<&ReferenceHit> = refs
            .iter()
            .filter(|r| file_id.map_or(true, |fid| r.target_file_id == Some(fid)))
            .collect();

        let total_refs = filtered.len();

        // Group by source file
        let mut by_file: HashMap<&str, Vec<&ReferenceHit>> = HashMap::new();
        for r in &filtered {
            by_file.entry(&r.source_path).or_default().push(r);
        }

        // Count all distinct source objects across files
        let total_distinct_objects: usize = by_file
            .values()
            .map(|refs| {
                let mut ids: Vec<i64> = refs.iter().map(|r| r.source_file_id).collect();
                ids.sort_unstable();
                ids.dedup();
                ids.len()
            })
            .sum();

        // Collect top-level patterns across all files (using leaf property)
        let mut global_type_prop: HashMap<(&str, String), usize> = HashMap::new();
        for r in &filtered {
            let leaf = r
                .property_hint
                .rsplit('.')
                .next()
                .unwrap_or(&r.property_hint)
                .to_string();
            *global_type_prop
                .entry((&r.source_object_type, leaf))
                .or_default() += 1;
        }
        let mut top_patterns: Vec<ReferencePattern> = global_type_prop
            .into_iter()
            .filter(|(_, count)| *count >= pattern_threshold)
            .map(|((t, p), count)| ReferencePattern {
                source_type: t.to_string(),
                property: p,
                count,
                sample_names: Vec::new(),
            })
            .collect();
        top_patterns.sort_by(|a, b| b.count.cmp(&a.count));

        // Sort files by reference count descending for most-relevant-first
        let mut file_entries: Vec<(&str, Vec<&ReferenceHit>)> =
            by_file.into_iter().collect();
        file_entries.sort_by(|a, b| b.1.len().cmp(&a.1.len()));

        let total_files = file_entries.len();
        let files: Vec<FileReferenceSummary> = file_entries
            .into_iter()
            .take(max_files)
            .map(|(path, refs)| {
                build_file_summary(path, &refs, max_detail_per_file, pattern_threshold, detail)
            })
            .collect();

        GroupedResults {
            target_guid: guid.to_string(),
            target_file_id: file_id,
            total_refs,
            total_files,
            total_distinct_objects,
            files,
            elapsed_ms: elapsed.as_millis() as u64,
            top_patterns,
        }
    }

    /// Legacy flat find — used by unit tests and potential future callers.
    #[allow(dead_code)]
    pub fn find_references(
        &self,
        guid: &str,
        file_id: Option<i64>,
        max: usize,
    ) -> Vec<ReferenceHit> {
        let Some(refs) = self.references_by_guid.get(guid) else {
            return Vec::new();
        };

        refs.iter()
            .filter(|r| {
                if let Some(fid) = file_id {
                    r.target_file_id == Some(fid)
                } else {
                    true
                }
            })
            .take(max)
            .cloned()
            .collect()
    }
}

/// Build an intelligently summarized view of references from one file.
fn build_file_summary(
    path: &str,
    refs: &[&ReferenceHit],
    max_detail: usize,
    pattern_threshold: usize,
    detail: DetailLevel,
) -> FileReferenceSummary {
    // Count distinct source objects
    let mut object_ids: Vec<i64> = refs.iter().map(|r| r.source_file_id).collect();
    object_ids.sort_unstable();
    object_ids.dedup();

    // Detect repetitive patterns: group by (source_object_type, leaf_property)
    let mut type_prop_groups: HashMap<(&str, String), Vec<&ReferenceHit>> = HashMap::new();
    for r in refs {
        // Normalize property path to leaf for grouping (e.g. "Material.m_Shader" → "m_Shader")
        let leaf_prop = r
            .property_hint
            .rsplit('.')
            .next()
            .unwrap_or(&r.property_hint)
            .to_string();
        type_prop_groups
            .entry((&r.source_object_type, leaf_prop))
            .or_default()
            .push(r);
    }

    let mut patterns: Vec<ReferencePattern> = Vec::new();
    let mut non_pattern_refs: Vec<&ReferenceHit> = Vec::new();

    for ((stype, prop), group) in &type_prop_groups {
        if group.len() >= pattern_threshold {
            let sample_names: Vec<String> = group
                .iter()
                .filter_map(|r| r.source_object_name.clone())
                .take(3)
                .collect();
            patterns.push(ReferencePattern {
                source_type: stype.to_string(),
                property: prop.clone(),
                count: group.len(),
                sample_names,
            });
        } else {
            non_pattern_refs.extend(group.iter());
        }
    }

    // Sort patterns by count descending
    patterns.sort_by(|a, b| b.count.cmp(&a.count));

    // Determine details based on detail level
    let (details, truncated) = match detail {
        DetailLevel::Summary => (Vec::new(), !refs.is_empty()),
        DetailLevel::Normal => {
            // If all refs are captured by patterns, skip individual details
            if non_pattern_refs.is_empty() {
                (Vec::new(), false)
            } else {
                let detail_refs: Vec<ReferenceHit> = non_pattern_refs
                    .iter()
                    .take(max_detail)
                    .map(|r| (*r).clone())
                    .collect();
                let truncated = non_pattern_refs.len() > detail_refs.len();
                (detail_refs, truncated)
            }
        }
        DetailLevel::Verbose => {
            let all: Vec<ReferenceHit> = refs.iter().map(|r| (*r).clone()).collect();
            (all, false)
        }
    };

    FileReferenceSummary {
        path: path.to_string(),
        total_refs: refs.len(),
        distinct_objects: object_ids.len(),
        patterns,
        details,
        truncated,
    }
}

/// File extensions we scan for references.
const SCANNABLE_EXTENSIONS: &[&str] = &[
    "unity",
    "prefab",
    "mat",
    "asset",
    "controller",
    "anim",
    "overrideController",
    "playable",
    "signal",
    "flare",
    "physicsMaterial",
    "physicMaterial",
    "renderTexture",
    "lighting",
    "giparams",
    "mask",
];

/// Build a reference index for the project using parallel file scanning.
pub fn build_index(project: &Path, approach: IndexApproach) -> anyhow::Result<ReferenceIndex> {
    let assets_dir = project.join("Assets");
    if !assets_dir.is_dir() {
        anyhow::bail!("Assets directory not found at {}", assets_dir.display());
    }

    let packages_dir = project.join("Packages");
    let settings_dir = project.join("ProjectSettings");

    let mut files = Vec::new();
    collect_scannable_files(&assets_dir, &mut files);
    if packages_dir.is_dir() {
        collect_scannable_files(&packages_dir, &mut files);
    }
    if settings_dir.is_dir() {
        collect_scannable_files(&settings_dir, &mut files);
    }

    let files_scanned = files.len();

    // Parallel scan: read + parse each file on a rayon thread
    let all_refs: Vec<ReferenceHit> = files
        .par_iter()
        .flat_map(|file| {
            let relative = file
                .strip_prefix(project)
                .unwrap_or(file)
                .to_string_lossy()
                .replace('\\', "/");

            let content = match std::fs::read_to_string(file) {
                Ok(c) => c,
                Err(_) => return Vec::new(),
            };

            let mut refs = Vec::new();
            match approach {
                IndexApproach::Grep => grep_scan(&content, &relative, &mut refs),
                IndexApproach::Yaml => yaml_scan(&content, &relative, &mut refs),
            }
            refs
        })
        .collect();

    let total_references = all_refs.len();
    let mut references_by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
    for r in all_refs {
        references_by_guid
            .entry(r.target_guid.clone())
            .or_default()
            .push(r);
    }

    Ok(ReferenceIndex {
        files_scanned,
        total_references,
        references_by_guid,
    })
}

fn collect_scannable_files(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            collect_scannable_files(&path, out);
        } else if let Some(ext) = path.extension().and_then(|e| e.to_str()) {
            if SCANNABLE_EXTENSIONS.contains(&ext) {
                out.push(path);
            }
        }
    }
}

// ─── Approach B: Grep-based scan ───────────────────────────────────────────

/// Fast regex-free scan for `guid:` patterns in file content.
/// Uses simple string matching for maximum speed.
fn grep_scan(content: &str, source_path: &str, out: &mut Vec<ReferenceHit>) {
    // Track current YAML document context
    let mut current_file_id: i64 = 0;
    let mut current_type = String::new();

    for line in content.lines() {
        // Detect YAML document boundaries: --- !u!<classId> &<fileId>
        if line.starts_with("--- !u!") {
            if let Some((class_id, file_id)) = parse_document_header(line) {
                current_file_id = file_id;
                current_type = unity_class_name(class_id).to_string();
            }
            continue;
        }

        // Look for external references: {fileID: <id>, guid: <guid>, type: <t>}
        let mut search_start = 0;
        while let Some(guid_pos) = content_find(line, "guid:", search_start) {
            if let Some(ref_info) = parse_inline_reference(line, guid_pos) {
                // Skip null/empty GUIDs
                if !ref_info.guid.is_empty()
                    && ref_info.guid != "0000000000000000e000000000000000"
                    && ref_info.guid != "0000000000000000f000000000000000"
                {
                    let property = extract_property_name(line);
                    out.push(ReferenceHit {
                        source_path: source_path.to_string(),
                        source_file_id: current_file_id,
                        source_object_type: current_type.clone(),
                        source_object_name: None,
                        property_hint: property,
                        target_guid: ref_info.guid,
                        target_file_id: ref_info.file_id,
                        ref_type: ref_info.ref_type,
                    });
                }
            }
            search_start = guid_pos + 5;
        }
    }
}

// ─── Approach C: Structured YAML scan ──────────────────────────────────────

/// Structured line-by-line parser that understands Unity YAML document boundaries,
/// tracks object names, and recovers property paths.
fn yaml_scan(content: &str, source_path: &str, out: &mut Vec<ReferenceHit>) {
    // First pass: collect document boundaries and names
    struct DocInfo {
        file_id: i64,
        class_name: String,
        name: Option<String>,
    }
    let mut docs: Vec<DocInfo> = Vec::new();
    let mut current_doc: Option<DocInfo> = None;

    for line in content.lines() {
        if line.starts_with("--- !u!") {
            if let Some(doc) = current_doc.take() {
                docs.push(doc);
            }
            if let Some((class_id, file_id)) = parse_document_header(line) {
                current_doc = Some(DocInfo {
                    file_id,
                    class_name: unity_class_name(class_id).to_string(),
                    name: None,
                });
            }
            continue;
        }

        let trimmed = line.trim();
        if trimmed.starts_with("m_Name:") {
            if let Some(ref mut doc) = current_doc {
                doc.name = trimmed
                    .split(':')
                    .nth(1)
                    .map(|v| v.trim().to_string())
                    .filter(|s| !s.is_empty());
            }
        }
    }
    if let Some(doc) = current_doc.take() {
        docs.push(doc);
    }

    // Build lookup from fileId → (class_name, name)
    let doc_map: HashMap<i64, (&str, Option<&str>)> = docs
        .iter()
        .map(|d| (d.file_id, (d.class_name.as_str(), d.name.as_deref())))
        .collect();

    // Second pass: extract references with property paths
    let mut current_file_id: i64 = 0;
    let mut property_stack: Vec<(usize, String)> = Vec::new();

    for line in content.lines() {
        if line.starts_with("--- !u!") {
            if let Some((_, file_id)) = parse_document_header(line) {
                current_file_id = file_id;
                property_stack.clear();
            }
            continue;
        }

        if line.starts_with('%') || line.starts_with('#') || line.is_empty() {
            continue;
        }

        let trimmed = line.trim();

        let indent = line.len() - line.trim_start().len();
        let property_key = trimmed.find(':').and_then(|colon_pos| {
            let key = trimmed[..colon_pos].trim_start_matches("- ");
            if !key.is_empty() && !key.contains('{') {
                Some(key.to_string())
            } else {
                None
            }
        });

        // Update property stack based on indentation. Array items that contain
        // inline references should retain the parent collection key.
        while let Some(last) = property_stack.last() {
            let should_pop = if property_key.is_some() {
                last.0 >= indent
            } else {
                last.0 > indent
            };
            if should_pop {
                property_stack.pop();
            } else {
                break;
            }
        }

        if let Some(key) = property_key {
            property_stack.push((indent, key));
        }

        // Look for external references
        let mut search_start = 0;
        while let Some(guid_pos) = content_find(line, "guid:", search_start) {
            if let Some(ref_info) = parse_inline_reference(line, guid_pos) {
                if !ref_info.guid.is_empty()
                    && ref_info.guid != "0000000000000000e000000000000000"
                    && ref_info.guid != "0000000000000000f000000000000000"
                {
                    let property_path = build_property_path(&property_stack);
                    let (obj_type, obj_name) = doc_map
                        .get(&current_file_id)
                        .copied()
                        .unwrap_or(("UnknownType", None));
                    out.push(ReferenceHit {
                        source_path: source_path.to_string(),
                        source_file_id: current_file_id,
                        source_object_type: obj_type.to_string(),
                        source_object_name: obj_name.map(|s| s.to_string()),
                        property_hint: property_path,
                        target_guid: ref_info.guid,
                        target_file_id: ref_info.file_id,
                        ref_type: ref_info.ref_type,
                    });
                }
            }
            search_start = guid_pos + 5;
        }
    }
}

// ─── Shared parsing utilities ──────────────────────────────────────────────

struct InlineRef {
    guid: String,
    file_id: Option<i64>,
    ref_type: Option<i32>,
}

/// Parse a `--- !u!<classId> &<fileId>` YAML document header.
fn parse_document_header(line: &str) -> Option<(u32, i64)> {
    // Format: "--- !u!<classId> &<fileId>"
    let after_tag = line.strip_prefix("--- !u!")?;
    let mut parts = after_tag.split_whitespace();
    let class_id = parts.next()?.parse::<u32>().ok()?;
    let file_id_str = parts.next()?.strip_prefix('&')?;
    let file_id = file_id_str.parse::<i64>().ok()?;
    Some((class_id, file_id))
}

/// Parse an inline reference `{fileID: <id>, guid: <guid>, type: <t>}` from a line.
fn parse_inline_reference(line: &str, guid_start: usize) -> Option<InlineRef> {
    // Find the guid value: after "guid:" skip whitespace, read 32 hex chars
    let after_guid = &line[guid_start + 5..]; // skip "guid:"
    let after_guid = after_guid.trim_start();

    // Read hex characters
    let guid: String = after_guid
        .chars()
        .take_while(|c| c.is_ascii_hexdigit())
        .collect();

    if guid.len() != 32 {
        return None;
    }

    // Try to find fileID in the same inline block
    // Look backward from guid_start for {fileID:
    let line_before = &line[..guid_start];
    let file_id = if let Some(fid_pos) = line_before.rfind("fileID:") {
        let after_fid = &line_before[fid_pos + 7..]; // skip "fileID:"
        let after_fid = after_fid.trim_start();
        let num_str: String = after_fid
            .chars()
            .take_while(|c| c.is_ascii_digit() || *c == '-')
            .collect();
        num_str.parse::<i64>().ok()
    } else {
        None
    };

    // Try to find type: after the guid
    let after_guid_full = &line[guid_start + 5 + guid.len()..];
    let ref_type = if let Some(type_pos) = after_guid_full.find("type:") {
        let after_type = &after_guid_full[type_pos + 5..];
        let after_type = after_type.trim_start();
        let num_str: String = after_type
            .chars()
            .take_while(|c| c.is_ascii_digit())
            .collect();
        num_str.parse::<i32>().ok()
    } else {
        None
    };

    Some(InlineRef {
        guid,
        file_id,
        ref_type,
    })
}

/// Simple substring find starting at a given offset.
fn content_find(haystack: &str, needle: &str, start: usize) -> Option<usize> {
    if start >= haystack.len() {
        return None;
    }
    haystack[start..].find(needle).map(|pos| pos + start)
}

/// Extract the property name from a YAML line (left side of colon before the reference).
fn extract_property_name(line: &str) -> String {
    let trimmed = line.trim();
    // Handle "- propertyName: {fileID...}" (array element)
    let cleaned = trimmed.strip_prefix("- ").unwrap_or(trimmed);
    if let Some(colon_pos) = cleaned.find(':') {
        cleaned[..colon_pos].to_string()
    } else {
        "inline".to_string()
    }
}

/// Build a dotted property path from the indentation-tracked stack.
fn build_property_path(stack: &[(usize, String)]) -> String {
    if stack.is_empty() {
        return "root".to_string();
    }
    stack
        .iter()
        .map(|(_, name)| name.as_str())
        .collect::<Vec<_>>()
        .join(".")
}

/// Map Unity class ID to a human-readable name.
fn unity_class_name(class_id: u32) -> &'static str {
    match class_id {
        1 => "GameObject",
        2 => "Component",
        4 => "Transform",
        20 => "Camera",
        21 => "Material",
        23 => "MeshRenderer",
        25 => "Renderer",
        29 => "OcclusionCullingSettings",
        33 => "MeshFilter",
        43 => "Mesh",
        48 => "Shader",
        54 => "Rigidbody",
        64 => "MeshCollider",
        65 => "BoxCollider",
        74 => "AnimationClip",
        82 => "AudioSource",
        91 => "AnimatorController",
        95 => "Animator",
        104 => "RenderSettings",
        108 => "Light",
        111 => "Animation",
        114 => "MonoBehaviour",
        115 => "MonoScript",
        120 => "LineRenderer",
        124 => "Behaviour",
        135 => "SphereCollider",
        136 => "CapsuleCollider",
        137 => "SkinnedMeshRenderer",
        157 => "LightmapSettings",
        196 => "NavMeshSettings",
        198 => "ParticleSystem",
        199 => "ParticleSystemRenderer",
        205 => "LODGroup",
        212 => "SpriteRenderer",
        222 => "Canvas",
        223 => "CanvasRenderer",
        224 => "RectTransform",
        225 => "CanvasGroup",
        226 => "CanvasScaler",
        258 => "TextMesh",
        320 => "PlayableDirector",
        328 => "VideoPlayer",
        1001 => "PrefabInstance",
        1660057539 => "SceneRoots",
        _ => "UnknownType",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const SAMPLE_MATERIAL: &str = r#"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_Name: TestMat
  m_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}
  m_SavedProperties:
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {fileID: 0}
"#;

    const SAMPLE_PREFAB: &str = r#"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100000
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Player
  m_Component:
  - component: {fileID: 200000}
  - component: {fileID: 300000}
--- !u!4 &200000
Transform:
  m_GameObject: {fileID: 100000}
  m_Father: {fileID: 0}
--- !u!23 &300000
MeshRenderer:
  m_GameObject: {fileID: 100000}
  m_Materials:
  - {fileID: 2100000, guid: 3cb6f81f1baa99647b390eb642d1990c, type: 2}
--- !u!114 &400000
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: 9b755239cc70f664ab7d71ba4602347b, type: 3}
  m_Name: FloatingCollectible
"#;

    const SAMPLE_SCRIPTABLE_OBJECT: &str = r#"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_Script: {fileID: 11500000, guid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6, type: 3}
  m_Name: GameConfig_Main
  playerMaterial: {fileID: 2100000, guid: 3cb6f81f1baa99647b390eb642d1990c, type: 2}
  collectiblePrefab: {fileID: 100100000, guid: d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9, type: 2}
"#;

    #[test]
    fn parse_document_header_extracts_class_and_file_id() {
        let (class_id, file_id) = parse_document_header("--- !u!21 &2100000").unwrap();
        assert_eq!(class_id, 21);
        assert_eq!(file_id, 2100000);
    }

    #[test]
    fn parse_document_header_handles_negative_file_id() {
        let (class_id, file_id) =
            parse_document_header("--- !u!114 &-8024145731953230120").unwrap();
        assert_eq!(class_id, 114);
        assert_eq!(file_id, -8024145731953230120);
    }

    #[test]
    fn parse_document_header_handles_large_file_id() {
        let (class_id, file_id) =
            parse_document_header("--- !u!1 &762373481093513140").unwrap();
        assert_eq!(class_id, 1);
        assert_eq!(file_id, 762373481093513140);
    }

    #[test]
    fn parse_inline_reference_extracts_full_ref() {
        let line = "  m_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7, type: 3}";
        let guid_pos = line.find("guid:").unwrap();
        let r = parse_inline_reference(line, guid_pos).unwrap();
        assert_eq!(r.guid, "933532a4fcc9baf4fa0491de14d08ed7");
        assert_eq!(r.file_id, Some(4800000));
        assert_eq!(r.ref_type, Some(3));
    }

    #[test]
    fn parse_inline_reference_skips_null_guid() {
        let line = "  m_Texture: {fileID: 0}";
        assert!(line.find("guid:").is_none());
    }

    #[test]
    fn grep_scan_finds_material_shader_reference() {
        let mut refs = Vec::new();
        grep_scan(SAMPLE_MATERIAL, "test.mat", &mut refs);
        assert!(!refs.is_empty());
        let shader_ref = refs
            .iter()
            .find(|r| r.target_guid == "933532a4fcc9baf4fa0491de14d08ed7")
            .expect("should find shader reference");
        assert_eq!(shader_ref.source_file_id, 2100000);
        assert_eq!(shader_ref.source_object_type, "Material");
    }

    #[test]
    fn yaml_scan_finds_material_shader_with_name() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_MATERIAL, "test.mat", &mut refs);
        let shader_ref = refs
            .iter()
            .find(|r| r.target_guid == "933532a4fcc9baf4fa0491de14d08ed7")
            .expect("should find shader reference");
        assert_eq!(shader_ref.source_object_name.as_deref(), Some("TestMat"));
        assert_eq!(shader_ref.source_object_type, "Material");
    }

    #[test]
    fn yaml_scan_finds_prefab_material_reference() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut refs);
        let mat_ref = refs
            .iter()
            .find(|r| r.target_guid == "3cb6f81f1baa99647b390eb642d1990c")
            .expect("should find material reference");
        assert_eq!(mat_ref.source_object_type, "MeshRenderer");
        assert_eq!(mat_ref.source_file_id, 300000);
        assert_eq!(mat_ref.target_file_id, Some(2100000));
    }

    #[test]
    fn yaml_scan_finds_script_reference() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut refs);
        let script_ref = refs
            .iter()
            .find(|r| r.target_guid == "9b755239cc70f664ab7d71ba4602347b")
            .expect("should find script reference");
        assert_eq!(script_ref.source_object_type, "MonoBehaviour");
        assert_eq!(
            script_ref.source_object_name.as_deref(),
            Some("FloatingCollectible")
        );
    }

    #[test]
    fn yaml_scan_finds_scriptable_object_references() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_SCRIPTABLE_OBJECT, "config.asset", &mut refs);

        // Should find the script reference
        assert!(refs
            .iter()
            .any(|r| r.target_guid == "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6"));
        // Should find the material reference
        assert!(refs
            .iter()
            .any(|r| r.target_guid == "3cb6f81f1baa99647b390eb642d1990c"));
        // Should find the prefab reference
        assert!(refs
            .iter()
            .any(|r| r.target_guid == "d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9"));
    }

    #[test]
    fn grep_and_yaml_find_same_guids() {
        let mut grep_refs = Vec::new();
        let mut yaml_refs = Vec::new();
        grep_scan(SAMPLE_PREFAB, "test.prefab", &mut grep_refs);
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut yaml_refs);

        let mut grep_guids: Vec<_> = grep_refs.iter().map(|r| &r.target_guid).collect();
        let mut yaml_guids: Vec<_> = yaml_refs.iter().map(|r| &r.target_guid).collect();
        grep_guids.sort();
        yaml_guids.sort();
        assert_eq!(grep_guids, yaml_guids);
    }

    #[test]
    fn yaml_scan_tracks_property_paths() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_SCRIPTABLE_OBJECT, "config.asset", &mut refs);
        let mat_ref = refs
            .iter()
            .find(|r| r.target_guid == "3cb6f81f1baa99647b390eb642d1990c")
            .expect("should find material reference");
        assert!(
            mat_ref.property_hint.contains("playerMaterial"),
            "property path should include 'playerMaterial', got: {}",
            mat_ref.property_hint
        );
    }

    #[test]
    fn yaml_scan_preserves_parent_property_for_inline_array_refs() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut refs);
        let mat_ref = refs
            .iter()
            .find(|r| r.target_guid == "3cb6f81f1baa99647b390eb642d1990c")
            .expect("should find material reference");
        assert_eq!(mat_ref.property_hint, "MeshRenderer.m_Materials");
    }

    #[test]
    fn index_find_references_by_guid() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut refs);
        yaml_scan(SAMPLE_SCRIPTABLE_OBJECT, "config.asset", &mut refs);

        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }

        let index = ReferenceIndex {
            files_scanned: 2,
            total_references: total,
            references_by_guid: by_guid,
        };

        // PlayerBall.mat should be referenced from both prefab and SO
        let results = index.find_references("3cb6f81f1baa99647b390eb642d1990c", None, 100);
        assert_eq!(results.len(), 2);

        // FloatingCollectible.cs should be referenced from prefab only
        let results = index.find_references("9b755239cc70f664ab7d71ba4602347b", None, 100);
        assert_eq!(results.len(), 1);
    }

    #[test]
    fn index_find_references_filters_by_file_id() {
        let mut refs = Vec::new();
        yaml_scan(SAMPLE_PREFAB, "test.prefab", &mut refs);

        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }

        let index = ReferenceIndex {
            files_scanned: 1,
            total_references: total,
            references_by_guid: by_guid,
        };

        // With specific fileId should filter
        let results =
            index.find_references("3cb6f81f1baa99647b390eb642d1990c", Some(2100000), 100);
        assert_eq!(results.len(), 1);

        // With wrong fileId should return empty
        let results = index.find_references("3cb6f81f1baa99647b390eb642d1990c", Some(999), 100);
        assert_eq!(results.len(), 0);
    }

    #[test]
    fn unity_class_names_are_correct() {
        assert_eq!(unity_class_name(1), "GameObject");
        assert_eq!(unity_class_name(21), "Material");
        assert_eq!(unity_class_name(114), "MonoBehaviour");
        assert_eq!(unity_class_name(1001), "PrefabInstance");
        assert_eq!(unity_class_name(99999), "UnknownType");
    }

    #[test]
    fn grouped_results_detect_patterns() {
        // Simulate 10 MeshRenderers all referencing the same material
        let mut refs = Vec::new();
        for i in 0..10 {
            refs.push(ReferenceHit {
                source_path: "Assets/Scene.unity".to_string(),
                source_file_id: 1000 + i,
                source_object_type: "MeshRenderer".to_string(),
                source_object_name: Some(format!("Obj_{}", i)),
                property_hint: "MeshRenderer.m_Materials".to_string(),
                target_guid: "aabbccdd".repeat(4),
                target_file_id: Some(2100000),
                ref_type: Some(2),
            });
        }
        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }
        let index = ReferenceIndex {
            files_scanned: 1,
            total_references: total,
            references_by_guid: by_guid,
        };

        let grouped = index.find_grouped(
            &"aabbccdd".repeat(4),
            None,
            50,
            5,
            3, // pattern threshold
            DetailLevel::Normal,
            std::time::Duration::from_millis(10),
        );

        assert_eq!(grouped.total_refs, 10);
        assert_eq!(grouped.total_files, 1);
        assert_eq!(grouped.files.len(), 1);

        let file = &grouped.files[0];
        assert_eq!(file.patterns.len(), 1);
        assert_eq!(file.patterns[0].source_type, "MeshRenderer");
        assert_eq!(file.patterns[0].property, "m_Materials");
        assert_eq!(file.patterns[0].count, 10);
        // With pattern threshold 3 capturing all 10, Normal mode should have no details
        assert!(file.details.is_empty());
    }

    #[test]
    fn grouped_results_summary_mode_omits_details() {
        let mut refs = Vec::new();
        refs.push(ReferenceHit {
            source_path: "Assets/Mat.mat".to_string(),
            source_file_id: 2100000,
            source_object_type: "Material".to_string(),
            source_object_name: Some("MyMat".to_string()),
            property_hint: "Material.m_Shader".to_string(),
            target_guid: "11223344".repeat(4),
            target_file_id: Some(4800000),
            ref_type: Some(3),
        });
        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }
        let index = ReferenceIndex {
            files_scanned: 1,
            total_references: total,
            references_by_guid: by_guid,
        };

        let grouped = index.find_grouped(
            &"11223344".repeat(4),
            None,
            50,
            5,
            3,
            DetailLevel::Summary,
            std::time::Duration::from_millis(5),
        );

        assert_eq!(grouped.total_refs, 1);
        assert!(grouped.files[0].details.is_empty());
        assert!(grouped.files[0].truncated);
    }

    #[test]
    fn grouped_results_verbose_mode_includes_all() {
        let mut refs = Vec::new();
        for i in 0..20 {
            refs.push(ReferenceHit {
                source_path: "Assets/Scene.unity".to_string(),
                source_file_id: 1000 + i,
                source_object_type: "MeshRenderer".to_string(),
                source_object_name: None,
                property_hint: "MeshRenderer.m_Materials".to_string(),
                target_guid: "deadbeef".repeat(4),
                target_file_id: Some(2100000),
                ref_type: Some(2),
            });
        }
        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }
        let index = ReferenceIndex {
            files_scanned: 1,
            total_references: total,
            references_by_guid: by_guid,
        };

        let grouped = index.find_grouped(
            &"deadbeef".repeat(4),
            None,
            50,
            5,
            3,
            DetailLevel::Verbose,
            std::time::Duration::from_millis(5),
        );

        assert_eq!(grouped.files[0].details.len(), 20);
        assert!(!grouped.files[0].truncated);
    }

    #[test]
    fn top_patterns_aggregate_across_files() {
        let mut refs = Vec::new();
        for i in 0..5 {
            refs.push(ReferenceHit {
                source_path: format!("Assets/Mat_{}.mat", i),
                source_file_id: 2100000,
                source_object_type: "Material".to_string(),
                source_object_name: Some(format!("Mat_{}", i)),
                property_hint: "Material.m_Shader".to_string(),
                target_guid: "55667788".repeat(4),
                target_file_id: Some(4800000),
                ref_type: Some(3),
            });
        }
        let total = refs.len();
        let mut by_guid: HashMap<String, Vec<ReferenceHit>> = HashMap::new();
        for r in refs {
            by_guid.entry(r.target_guid.clone()).or_default().push(r);
        }
        let index = ReferenceIndex {
            files_scanned: 5,
            total_references: total,
            references_by_guid: by_guid,
        };

        let grouped = index.find_grouped(
            &"55667788".repeat(4),
            None,
            50,
            5,
            3,
            DetailLevel::Normal,
            std::time::Duration::from_millis(5),
        );

        assert_eq!(grouped.total_files, 5);
        assert_eq!(grouped.top_patterns.len(), 1);
        assert_eq!(grouped.top_patterns[0].count, 5);
        assert_eq!(grouped.top_patterns[0].source_type, "Material");
        assert_eq!(grouped.top_patterns[0].property, "m_Shader");
    }
}
