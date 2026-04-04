# PROJECT.md - Unity Control Protocol Architecture And Principles

This document captures the current architectural shape of the repository and the general engineering principles that should guide future development.

It is intentionally not a low-level rulebook. The goal is to preserve clarity, scalability, and maintainability as the CLI and Unity bridge continue to grow.

It is grounded in the current implementation in `cli/` and `unity-package/com.ucp.bridge/`.

## Scope

UCP is built from four main layers:

- a Rust CLI in `cli/`
- a Unity Editor bridge package in `unity-package/com.ucp.bridge/`
- an npm distribution wrapper in `npm/`
- documentation and website content in `docs/`, `website/`, and `skills/`

The core product is the CLI plus the Unity bridge. The other layers exist to package, distribute, and explain that system.

## Current Architecture

At a high level, the system works like this:

1. The CLI parses user intent and global runtime options.
2. Command modules resolve project context and bridge readiness.
3. The Rust client communicates with the bridge over localhost WebSocket using JSON-RPC.
4. The Unity bridge routes RPC methods into editor-side controllers.
5. Controllers translate protocol requests into Unity Editor operations and structured responses.
6. The CLI renders the result either for humans or for machine consumption.

That flow should remain easy to follow. Future work should preserve a clear separation between:

- command orchestration
- transport and lifecycle management
- protocol definition
- Unity-side execution
- presentation and documentation

## Unity Interaction Lifecycle Policy

Unity automation is only polished when a command leaves the editor genuinely ready for the next step. In practice, many Unity APIs return before the editor has finished import, metadata generation, serialization, compilation, or domain reload work.

UCP therefore treats Unity-facing commands as lifecycle categories, not just RPC calls:

- **Read-only inspection**: queries such as status, reads, search, snapshots, and logs. These do not wait after the call.
- **Editor-settle mutations**: scene/object/material/prefab/settings/file/asset operations that can trigger asset refresh, metadata writes, serialization, or other editor background work. These must wait for the editor to settle before reporting success.
- **Restart-then-settle mutations**: compile-heavy or package-heavy operations that can restart the bridge or trigger a domain reload, such as package changes, explicit recompilation, scripting-define changes, and build-target switches. These must survive bridge restart and then wait for editor settle.
- **Custom confirmation flows**: commands whose completion is defined by a domain-specific signal rather than generic editor settle, such as play-mode entry confirmation, test-run notifications, or build completion reports.

This policy is a core architectural rule, not a UX nicety. A mutating command should not claim success while Unity still has deferred catch-up work that will only surface later when the editor regains focus.

The same policy now also distinguishes between scene-editing mutations and scene-disruptive mutations:

- **Scene-editing mutations**: object, prefab, and scene-lighting operations can intentionally leave the active scene dirty. These surfaces should expose an explicit `--save` option rather than silently persisting scene changes.
- **Scene-disruptive mutations**: commands that can close the editor, switch scenes, enter play mode, trigger recompilation/domain reloads, or kick off package/build-target/define refresh flows must preflight the active scene first. If the active scene is dirty, they should fail with a concise scene-change summary instead of letting Unity show its native save dialog.

That separation is a core operability contract for autonomous Unity work: edits may stay unsaved by default, but disruptive transitions must never surprise the user with an unmanaged modal.

## Repository Shape

The current repository has a clear top-level split:

- `cli/` contains the Rust command-line product.
- `unity-package/com.ucp.bridge/` contains the Unity bridge package.
- `npm/` packages the released CLI and bridge payload for JavaScript users.
- `docs/`, `website/`, and `skills/` describe and expose the system externally.
- `scripts/` and `version.json` support release and metadata consistency.

This separation is useful and should be preserved. Code should generally evolve deeper within the right layer rather than by adding new cross-cutting entrypoints or parallel structures.

## Source Of Truth

Some parts of the repository are foundational and should stay aligned:

- `version.json` is the source of truth for release and protocol metadata.
- `scripts/sync-version.mjs` exists to propagate that metadata consistently.
- `scripts/validate-release.ps1` is the shared validation entrypoint for local release preflight and GitHub Actions gating.
- `scripts/unity-version-matrix.ps1` is the canonical Unity compatibility harness for Unity 6 slot coverage (`6000.0` through `6000.4`) with explicit fallback and skip reporting.
- `skills/unity-control-protocol/` is the canonical agent skill source used by docs and the Claude Code plugin wrapper.
- `.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json` define the Claude Code marketplace-facing wrapper for the base skill.
- the CLI and bridge must remain aligned on protocol version and compatibility expectations.
- repo-level docs should reflect the implemented system, not an aspirational redesign.

## Engineering Principles

### 1. Preserve strong boundaries

The project scales best when each layer has a clear responsibility.

- The CLI should remain the user-facing orchestration layer.
- Transport and lifecycle behavior should stay centralized rather than being reimplemented in individual commands.
- The Unity package should remain the editor-execution layer.
- Docs and packaging should describe and distribute the product, not reshape its runtime behavior.

Good changes strengthen those boundaries. Fragile changes usually blur them.

### 2. Prefer coherent extension over structural expansion

As the command surface grows, maintainability depends more on coherence than on novelty.

- Prefer extending an existing domain before creating a new top-level abstraction.
- Prefer evolving the current command and controller families before introducing parallel patterns.
- Prefer local improvements in the correct layer over broad rewrites that spread responsibility.

New concepts should only be introduced when the current structure is no longer carrying its weight.

### 3. Keep data flow explicit

This codebase works best when runtime behavior is easy to trace.

- Inputs should be validated near the boundary where they enter the system.
- Context should be passed clearly, not hidden behind ambient state.
- Protocol shapes should be deliberate, stable, and understandable.
- Error paths should remain meaningful enough to debug real automation failures quickly.

This is especially important here because UCP spans a native CLI, a network transport, and Unity Editor internals.

### 4. Optimize for operational clarity

UCP is not just a library; it is a workflow tool used by humans, scripts, and agents.

That means maintainability includes:

- predictable lifecycle behavior
- understandable output
- consistent protocol behavior
- clear recovery when Unity, the bridge, or a project is in a bad state

Features that make the system more powerful but harder to operate should be treated carefully.

For Unity-facing mutations, operational clarity specifically means:

- the command chooses the correct lifecycle category up front
- post-action waits stay centralized instead of being reimplemented ad hoc
- the CLI reports success only after the category's readiness guarantee is satisfied
- future commands extend the same lifecycle framework instead of inventing local exceptions

### 5. Favor stable internal conventions over cleverness

The codebase already benefits from recurring patterns in both the Rust CLI and the Unity bridge. Future work should continue that direction.

In practice this means:

- readable module ownership
- unsurprising naming
- straightforward request and response shaping
- localized helpers instead of hidden magic
- explicit tradeoffs when something more complex is truly necessary

The standard to aim for is not minimal code. It is code that remains easy to extend correctly months later.

### 6. Respect the strengths of the tech stack

Each part of the stack suggests a natural style.

- In Rust, lean toward explicit types, clear ownership, and focused modules.
- In the CLI layer, keep user intent, lifecycle handling, transport, and presentation conceptually separate.
- In Unity Editor code, respect editor semantics, serialization pathways, and assembly boundaries.
- In bridge code, prefer simple and robust protocol behavior over overly abstract frameworks.

The goal is not purity for its own sake. The goal is to use each layer in a way that remains maintainable under growth.

### 7. Treat documentation as part of the architecture

In this repository, documentation is not just explanatory text. It is part of the maintenance surface.

- `README.md` explains the product and workflow.
- `PROJECT.md` explains architectural direction.
- `CONTRIBUTING.md` explains contribution expectations.
- docs and skills explain the public surface.

When the system changes materially, these documents should change with it so future work continues from reality rather than stale assumptions.

## Technology-Oriented Guidance

### Rust CLI

The Rust CLI should continue to emphasize:

- small command-oriented modules
- centralized lifecycle and bridge readiness handling
- mutating commands that leave the Unity editor settled before they report success
- a shared lifecycle-policy layer that classifies Unity mutations into read-only, settle, restart-then-settle, or custom-confirmation flows
- explicit protocol interactions
- predictable user and JSON output
- errors that are useful in automation contexts

The CLI is the orchestration layer. It should remain easy to reason about even as the command surface expands.

### Unity Bridge

The Unity package should continue to emphasize:

- a clear editor-only execution boundary
- domain-oriented controller organization
- simple RPC routing
- explicit request parsing and response shaping
- compatibility with normal Unity editor behavior

The bridge should stay pragmatic and reliable. It should not become harder to evolve than the editor workflows it is meant to automate.

### Packaging And Metadata

Release metadata, package metadata, and protocol metadata should continue to move through a small number of known sources rather than through ad hoc edits across the repo.

Claude Code plugin metadata should also stay version-aligned with the same source of truth so marketplace installs track the same release identity as the CLI, bridge, npm package, docs, and agent skill.

This matters because UCP ships through multiple channels and the cost of drift is high.

### Validation And Release Pipeline

#### Local Validation Commands

```powershell
# CLI compile check and unit tests (76 tests)
cargo test --manifest-path cli\Cargo.toml

# Version metadata sync check
node scripts/sync-version.mjs --check <version>

# Website build validation
Push-Location website; npm run sync-content && npm run build; Pop-Location

# Single-version QA against the dev project (runs all 52 bridge exercise steps)
.\scripts\qa-playground.ps1 -Project unity-project-dev\ucp-dev -TimeoutSeconds 180

# Full Unity compatibility matrix (6000.0 through 6000.4)
.\scripts\unity-version-matrix.ps1 -Project 'unity-project-dev\ucp-dev' `
    -RequestedSlots @('6000.0','6000.1','6000.2','6000.3','6000.4') `
    -TimeoutSeconds 180 -Run

# Shared preflight entrypoint (runs cargo test + version check + website build)
.\scripts\validate-release.ps1 -Version <version>
```

#### Unity Compatibility Matrix (`scripts/unity-version-matrix.ps1`)

The matrix runner resolves each requested slot (e.g. `6000.2`) against locally installed Unity editors, preferring the exact minor version, then falling back to the nearest installed same-major editor.

Execution is sequential against a **single canonical dev project** (`unity-project-dev/ucp-dev`):

1. Close any running editor for the project.
2. Backup `Packages/manifest.json`.
3. Sanitize the manifest for the target version (removes modules unavailable in older editors).
4. Delete `Library/` for a clean reimport.
5. Run `scripts/qa-playground.ps1` with `-ForceUnityVersion` and `-SkipInstall`.
6. Restore the original manifest.
7. Close the editor and repeat for the next slot.

Per-slot JSON results are written to `.matrix-results/<slot>.json`. A markdown summary is written to `.matrix-results/summary.md`.

If a requested slot has no compatible installed editor, it is skipped with a warning rather than failing the entire run.

#### QA Harness (`scripts/qa-playground.ps1`)

The QA harness exercises all major bridge command families in sequence:

- **Lifecycle**: open, connect, scene load
- **Object operations**: create, add-component, set/get-property, remove-component, destroy
- **Prefabs**: create, instantiate, unpack, apply overrides, revert, delete
- **Assets**: search, info, reimport, delete
- **Materials**: list, info
- **Files**: read, write, search
- **Settings**: get, set
- **Build**: status, set-scenes
- **Logs**: status, recent
- **Screenshot**: capture
- **Play mode**: play, pause, stop (with domain-reload resilience)
- **Test runner**: run-tests in edit mode
- **VCS**: status

Each step is tracked in a durable JSON summary file. If the harness crashes mid-run, the partial results survive for investigation.

Key parameters:
- `-Project <path>`: target Unity project
- `-ForceUnityVersion <id>`: override which editor version to use
- `-TimeoutSeconds <n>`: per-command timeout
- `-SkipInstall`: skip bridge installation (bridge already embedded)
- `-SummaryPath <path>`: where to write the JSON summary

#### CLI Dialog Handling

The CLI automatically detects and dismisses Unity startup dialogs during editor launch and bridge wait cycles. Dialog handling is controlled by `--dialog-policy`:

- `ignore` (recommended for automation): dismisses all recognized dialogs with the safest non-destructive button
- `auto`: dismisses dialogs with a balanced preference order
- `recover`: prefers recovery/restore buttons
- `safe-mode`: enters safe mode when offered
- `cancel`: cancels/quits when offered
- `manual`: never auto-dismisses; waits for human interaction

Recognized dialog titles:
- "Opening Project in Non-Matching Editor Installation" → Continue
- "Enter Safe Mode?" → Ignore (policy: ignore) / Enter Safe Mode (policy: safe-mode)
- "Project Upgrade Required" → Confirm
- "Auto Graphics API Notice" → OK

Unrecognized dialogs fall through to a generic button preference list that includes: ignore, continue, confirm, skip, ok, yes.

#### Release Flow

1. **Run local Unity matrix** (pre-release, local only — requires Unity installs):
   ```powershell
   .\scripts\unity-version-matrix.ps1 -Project 'unity-project-dev\ucp-dev' `
       -RequestedSlots @('6000.0','6000.1','6000.2','6000.3','6000.4') `
       -TimeoutSeconds 180 -Run
   ```
2. **Bump version**: edit `version.json`, then run `node scripts/sync-version.mjs <new-version>` to propagate to Cargo.toml, npm/package.json, Unity package.json, docs, and skill metadata.
3. **Verify sync**: `node scripts/sync-version.mjs --check <new-version>` must report all files in sync.
4. **Update CHANGELOG.md**: move `[Unreleased]` entries under a dated version heading.
5. **Commit**: `git commit -am "chore(release): prepare <version>"`
6. **Tag**: `git tag v<version>`
7. **Push**: `git push origin main --tags`
8. **GitHub Actions** (`.github/workflows/release.yml`):
   - Runs CI preflight (cargo test, version sync check) — **no Unity matrix on CI**.
   - Builds platform binaries (Linux, macOS, Windows).
   - Creates a GitHub Release with the binaries attached.
   - Publishes the npm package (`@mflrevan/ucp`) to npmjs.
9. **Website** (`.github/workflows/pages.yml`): deploys automatically from `main` on push.

> **Note**: The Unity compatibility matrix is a local-only validation step. It requires physical Unity editor installs and cannot run on GitHub Actions runners. Always run it locally before tagging a release.

#### Cross-Version Compatibility Notes

The bridge package must compile and function correctly across Unity 6.0 through 6.4. Key compatibility considerations:

- `EditorUtility.EntityIdToObject` does not exist in any Unity 6 version; use `EditorUtility.InstanceIDToObject()` via the `UnityObjectCompat` shim.
- `SerializedObject` must always call `.Update()` before reading/writing properties and use `try/finally` for `.Dispose()`. Omitting `.Update()` crashes Unity 6000.4.
- `com.unity.modules.adaptiveperformance` and `com.unity.modules.vectorgraphics` do not exist before Unity 6000.3; the matrix sanitizes these from the manifest for older editors.
- Tests in the test assembly cannot reference `internal` types from the bridge assembly; use `EditorUtility` directly instead of `UnityObjectCompat`.

### Reference Indexing (`cli/src/commands/references/`)

The `ucp references` command family provides high-performance asset reference search entirely from the Rust CLI without requiring an active Unity editor session.

#### Architecture

The feature is split into two layers:

- **Rust engine** (`engine.rs`): parses Unity text-serialized YAML files directly from disk using parallel scanning (rayon), extracting `{fileID, guid, type}` references and building an in-memory reverse-reference index. Two scanning approaches are available:
  - `grep`: fast regex-based extraction (~22ms for 424 files in release). Lower data quality — captures GUIDs and source paths but limited object context.
  - `yaml`: structured document-boundary parsing with two-pass name resolution (~24ms for 424 files in release). Captures source object type, name, property path, and file IDs. Default approach.
- **Bridge fallback** (`ReferenceController.cs`): for projects that use binary serialization, falls back to `AssetDatabase.GetDependencies` plus `SerializedObject` property walking. Requires an active editor connection.

#### Intelligent Output Grouping

Results are grouped and truncated to minimize context bloat, especially important for agent consumers:

- **Pattern detection**: repetitive reference patterns (e.g., 100 MeshRenderers all referencing the same material) are collapsed into `N × Type.property` summaries with sample names
- **File-level aggregation**: references are grouped by source file with per-file pattern summaries and distinct object counts
- **Detail levels**: `--detail summary|normal|verbose` controls output depth:
  - `summary`: file counts + patterns only (264 refs → 189 chars JSON)
  - `normal`: patterns + non-pattern details up to `--max-per-file` limit (264 refs → 499 chars)
  - `verbose`: all individual references, no truncation (264 refs → 1299 chars)
- **Configurable limits**: `--max-files`, `--max-per-file`, `--pattern-threshold`

#### Serialization Requirements

Native Rust indexing requires both:
- **Force Text** serialization (`EditorSettings.asset → m_SerializationMode: 2`)
- **Visible Meta Files** (`VersionControlSettings.asset → m_Mode: Visible Meta Files`)

The CLI auto-detects these settings from disk. If either is missing, it prints a recommendation and falls back to the bridge. `ucp doctor` and `ucp install` both surface these checks.

#### Commands

```
ucp references find --asset <path|guid> [--approach auto|rust-grep|rust-yaml|bridge]
                                        [--detail summary|normal|verbose]
                                        [--max-files 50] [--max-per-file 5]
                                        [--pattern-threshold 3]
ucp references find --object <guid:fileId>
ucp references index build [--approach grep|yaml|auto]
ucp references index status
ucp references index clear
ucp references check
```

#### Data Model

Each reference hit contains: source path, source object type, source object name (when available), source file ID, target GUID, target file ID, and the property path where the reference was found.

Grouped results (`GroupedResults`) add: per-file summaries with pattern detection, top-level cross-file patterns, total/distinct-object counts, and truncation indicators.

The index scans all Unity-serialized file types: `.unity`, `.prefab`, `.mat`, `.asset`, `.controller`, `.anim`, `.overrideController`, `.playable`, `.signal`, `.flare`, `.physicsMaterial`, `.physicMaterial`, `.renderTexture`, `.lighting`, `.giparams`, `.mask`.

#### Performance (424 files, 4377 refs, release build)

- Full index build: grep 22ms, yaml 24ms (parallel via rayon)
- Any find query: ~25ms regardless of result count
- JSON output: 112–1299 chars depending on detail level (vs 50KB+ raw)

#### Lifecycle

Reference queries are **read-only** — no editor mutations, no bridge connection required for the Rust path.

## What To Preserve As The Codebase Grows

As the repository evolves, maintainability depends on preserving a few core qualities:

- a visible architecture with low surprise
- narrow responsibilities per layer
- extension by domain rather than by duplication
- protocol and lifecycle behavior that remain coherent
- documentation that stays synchronized with implementation

If a change adds capability but weakens those qualities, it should be reconsidered or reframed.

## Practical Standard For Future Work

Future work should generally make the repository:

- easier to navigate
- easier to extend in the correct place
- easier to debug in real Unity projects
- easier to review without hidden assumptions

That is the standard this project should optimize for.
