# Reference Search

Find all references to any asset, prefab, material, script, or object across the entire Unity project. The Rust-native engine parses Unity's text-serialized YAML directly from disk with parallel scanning — no running Unity editor required.

For projects using binary serialization, a bridge-based fallback searches via the Unity editor's `AssetDatabase` and `SerializedObject` APIs.

## Requirements

Native Rust indexing requires two project settings:

| Setting                | Where                                           | Expected value       |
| ---------------------- | ----------------------------------------------- | -------------------- |
| Asset Serialization    | Edit > Project Settings > Editor                | **Force Text**       |
| Version Control Mode   | Edit > Project Settings > Version Control       | **Visible Meta Files** |

Run `ucp references check` to verify. If either setting is missing, `ucp references find` automatically falls back to bridge-based search and prints a recommendation.

`ucp doctor` and `ucp install` also surface these checks.

## Commands

### `ucp references check`

Verify serialization compatibility for native indexing.

```bash
ucp references check
```

**Output:**

```
  ✔ Force Text serialization
  ✔ Visible Meta Files

[OK] Native Rust indexing is available
```

### `ucp references find`

Find all files and objects that reference a given asset.

```bash
# By asset path (reads GUID from .meta)
ucp references find --asset "Assets/Materials/Agent.mat"

# By GUID directly
ucp references find --asset 933532a4fcc9baf4fa0491de14d08ed7

# By specific object within an asset (guid:fileId)
ucp references find --object "d4e5f6a7:11400000"
```

| Flag                            | Description                                                      |
| ------------------------------- | ---------------------------------------------------------------- |
| `-a, --asset <path\|guid>`      | Asset path or 32-char hex GUID to search for                     |
| `-o, --object <guid:fileId>`    | Specific object reference (GUID:localFileId)                     |
| `--approach <mode>`             | `auto` (default), `rust-grep`, `rust-yaml`, or `bridge`          |
| `--detail <level>`              | `summary`, `normal` (default), or `verbose`                      |
| `--max-files <n>`               | Maximum files in results (default: 50)                           |
| `--max-per-file <n>`            | Max detail entries per file before pattern-collapsing (default: 5)|
| `--pattern-threshold <n>`       | Collapse groups of N+ identical type/property refs (default: 3)  |

#### Detail levels

- **`summary`** — File counts and dominant patterns only. Minimal context usage. Best for large-scale triage.
- **`normal`** — Patterns plus non-pattern individual references up to `--max-per-file`. Good balance of context and detail.
- **`verbose`** — Every individual reference, no truncation. Use for targeted debugging on small result sets.

#### Output examples

**Normal detail** — a material referenced by many MeshRenderers:

```
[OK] Found 264 reference(s) across 24 file(s) (198 distinct objects) in 25ms

  Dominant patterns:
     198 × MeshRenderer.m_Materials
      42 × PrefabInstance.m_Modification
      24 × Material.m_Shader

  Assets/Scenes/City.unity (86 refs, 72 objects)
     72 × MeshRenderer.m_Materials (e.g. Building_01, Building_02, Lamp_Post)
     14 × PrefabInstance.m_Modification (e.g. Building_01, Building_02)

  Assets/Prefabs/Building_01.prefab (8 refs, 6 objects)
      6 × MeshRenderer.m_Materials (e.g. Wall, Roof, Floor)
    [Door#4894578] m_Materials
    [Window#4894602] m_Materials
```

**Summary detail** — same query, agent-optimized:

```
[OK] Found 264 reference(s) across 24 file(s) (198 distinct objects) in 25ms

  Dominant patterns:
     198 × MeshRenderer.m_Materials
      42 × PrefabInstance.m_Modification
      24 × Material.m_Shader

  Assets/Scenes/City.unity (86 refs, 72 objects)
  Assets/Scenes/Arena.unity (44 refs, 38 objects)
  Assets/Prefabs/Building_01.prefab (8 refs, 6 objects)
  ...
```

**JSON output** (`--json`):

```bash
ucp references find --asset "Assets/Materials/Agent.mat" --json --detail summary
```

```json
{
  "success": true,
  "data": {
    "targetGuid": "adbf4a7415ede7c42ada304e953520f6",
    "totalRefs": 264,
    "totalFiles": 24,
    "totalDistinctObjects": 198,
    "elapsedMs": 25,
    "topPatterns": [
      { "sourceType": "MeshRenderer", "property": "m_Materials", "count": 198 }
    ],
    "files": [
      { "path": "Assets/Scenes/City.unity", "totalRefs": 86, "distinctObjects": 72 }
    ]
  }
}
```

### `ucp references index build`

Build a full reference index from disk. Useful for benchmarking or pre-warming.

```bash
ucp references index build
ucp references index build --approach grep
```

| Flag                  | Description                                          |
| --------------------- | ---------------------------------------------------- |
| `--approach <mode>`   | `grep`, `yaml` (default), or `auto`                  |

**Output:**

```
[OK] Index built in 0.02s: 424 files, 4377 references, 488 unique targets
```

### `ucp references index status`

Show project serialization status and native indexing capability.

```bash
ucp references index status
```

### `ucp references index clear`

Clear any cached reference index.

```bash
ucp references index clear
```

## Approaches

| Approach       | Speed   | Data quality  | Requires editor |
| -------------- | ------- | ------------- | --------------- |
| `rust-yaml`    | ~24ms   | High (names, types, property paths) | No  |
| `rust-grep`    | ~22ms   | Medium (GUIDs, paths only)          | No  |
| `bridge`       | ~500ms+ | Full (Unity API, binary projects)   | Yes |

`auto` (default) picks `rust-yaml` when serialization settings allow it, otherwise falls back to `bridge`.

## File types scanned

The native engine scans all Unity-serialized text files:

`.unity`, `.prefab`, `.mat`, `.asset`, `.controller`, `.anim`, `.overrideController`, `.playable`, `.signal`, `.flare`, `.physicsMaterial`, `.physicMaterial`, `.renderTexture`, `.lighting`, `.giparams`, `.mask`

## Agent usage tips

- Use `--detail summary` for large projects to minimize context consumption. A query returning 264 references compresses to ~189 characters of JSON.
- Use `--detail normal` (default) for actionable results with pattern grouping. Repetitive reference patterns (e.g., 200 MeshRenderers referencing the same material) collapse to a single line with count.
- When cleaning up a large project, query by script/material GUID to find all dependents before removing or refactoring an asset.
- Chain with `ucp asset info` to resolve GUIDs to human-readable names.
- No editor connection needed for the Rust path — works in CI, offline, or from agents without Unity running.
