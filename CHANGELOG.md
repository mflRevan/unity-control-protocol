# Changelog

## [0.6.1] - 2026-08-18

### Performance

- **Bridge commands are about 5x faster** — `ucp scene active` 498ms -> 100ms, and `ucp editor status` (which never touches the bridge at all) 210ms -> 34ms. Measured as a controlled 2x2 over the pre- and post-change CLI and bridge against the same project, median of 9 runs each:

  | `ucp scene active` | old bridge | new bridge |
  | --- | --- | --- |
  | old CLI | 498ms | 398ms |
  | new CLI | 199ms | **100ms** |

  Roughly 300ms of the saving is CLI-side and 100ms bridge-side. The residual is process start plus one editor main-thread round trip, which varies with how idle the editor is. Three independent causes:
  - `connect_client` now takes a fast path for the overwhelmingly common case, an editor that is already up with a live bridge. It previously ran the full lifecycle check every time: a machine-wide process scan, plus a throwaway connect-and-handshake that was closed and immediately redone. If the lock file names a live pid and the handshake succeeds, that *is* the readiness check, so the connection is kept. The full lifecycle path (launch or adopt the editor, dismiss startup dialogs, wait for the bridge) still runs whenever the fast path does not apply.
  - `sysinfo` refreshes are now targeted. `System::new_all()` sweeps every process plus CPU, memory, disks and networks (~70ms warm, ~200ms on a process's first call); `read_lock_file` needs exactly one pid (~7ms), and `list_running_unity_editors` needs command lines and executable paths but none of the rest.
  - The bridge answers `handshake` on the socket thread instead of queueing it for the editor's main-thread pump. Everything the handshake returns is editor identity, so it is captured once at startup; queueing it only bought a wait for the next `EditorApplication.update` tick, which every single CLI command paid before its real request could be dispatched.
- The "update available" check no longer blocks the command that triggered it. It reads from cache and refreshes in a detached task, so a cold or expired cache costs nothing instead of adding a network round trip to an unrelated command.
- `ucp exec` no longer rescans every loaded assembly on each call. Implementing types are discovered once per app domain (a domain reload resets the cache anyway) and only assemblies that actually reference the bridge assembly are scanned, checked via reference metadata rather than by materialising the type list of every framework and Unity assembly. `exec run` also stops at the first name match instead of constructing *every* `IUCPScript` in the project - previously any constructor side effect ran on every invocation of any script.

### Added

- `ucp packages add` now accepts multiple packages in a single call (`ucp packages add a b c`), where it previously errored on the extra arguments. Packages are added sequentially — the Unity Package Manager runs one operation at a time, so each resolve fully settles before the next add starts, and `--no-wait` applies only to the final package. CLI-only: no bridge/protocol change, so existing bridges keep working.
- `ucp compile` now reports real compile results instead of always claiming success. The bridge captures per-assembly `CompilerMessage`s (errors + warnings) into `SessionState` — which survives the domain reload on a successful build and stays put on a failed one — and a new `compile/diagnostics` RPC surfaces them. The CLI prints the error/warning breakdown and **exits non-zero when compilation fails**, so an agent no longer has to grep `Library/ScriptAssemblies` freshness or `CS####` log lines to know a build broke. Bridges without `compile/diagnostics` degrade to the previous plain-completion report.

### Changed

- Profiler responses are bounded and steerable, so a query cannot flood an agent's context or stall the editor:
  - `profiler hierarchy` and `profiler timeline` accept `--fields name,selfMs,...` to return only the columns you asked for. Profiler rows are wide; on a 20-row hierarchy, `--fields name,selfMs` cut the JSON payload by 71%.
  - Both now report `totalCount` alongside `count`. `truncated: true` on its own could not distinguish "50 of 52" from "50 of 50,000", so a caller could not tell whether to look further. Human output says so directly: `Showing 6 of 143 rows`.
  - Human output for `profiler hierarchy` and `profiler timeline` is a compact indented tree with inline timings, instead of pretty-printing every row of raw JSON.
  - `profiler summary` clamps its frame range to the most recent 600 frames and reports the clamp in `warnings`. Aggregation walks every raw frame view on the editor's main thread, so an unbounded range (`--first-frame 0` on a long session) froze the editor for seconds.
- Scene/object edits via `ucp object …` during **Play Mode** no longer fail opaquely on `--save` (Unity refuses to save scenes while playing). The CLI now detects Play Mode and returns a clear warning — in both human and `--json` output (`playMode: true`, `warning: …`) — that the change applies to the running instance only and will not persist, and it skips the doomed save instead of erroring.
- `ucp open` re-checks for an existing editor immediately before launching and adopts it instead of starting a second instance on the same project. A close/open race (or two concurrent `open`s) could otherwise spawn a rival Unity that locks `Library/ScriptAssemblies` and silently blocks recompilation.

### Fixed

- **The Unity editor no longer crashes when a `ucp exec` script returns a Unity struct.** `MiniJson`'s serializer reflected over every public property of any value it did not recognise, and Unity's math structs expose computed properties that return their own type — `Vector3.normalized` returns a `Vector3`, which has a `.normalized`, forever. The resulting infinite recursion overflowed the stack, which .NET cannot catch, so the editor process died outright; because the CLI relaunches a missing editor on the next command, this presented as "Unity keeps restarting" rather than "Unity crashed". A one-line script returning `new { pos = Vector3.zero }` was enough to trigger it. The serializer now:
  - emits explicit shapes for `Vector2/3/4`, `Vector2Int`/`Vector3Int`, `Quaternion`, `Color`/`Color32`, `Rect`/`RectInt`, `Bounds`/`BoundsInt` and `Matrix4x4` instead of reflecting over them;
  - projects any `UnityEngine.Object` to `{name, instanceId, type}` rather than walking a scene graph that is cyclic by construction (`GameObject.transform.gameObject`), and writes `null` for destroyed objects;
  - caps reflection depth (8) and total reflected objects, emitting `"<ucp:max-depth>"` / `"<ucp:truncated>"`, so an unrecognised type with self-returning properties degrades instead of crashing;
  - detects reference cycles and emits `"<ucp:cycle>"`.
- Serializer output is now always valid JSON. `NaN`/`Infinity` floats (routine in Unity — degenerate bounds, zero-length normalize) were written literally and made the whole response unparseable; they now serialize as `null`. Unsigned and narrow integers (`uint`, `ushort`, `byte`, `ulong`) serialized as `{}` and now emit numbers, non-`IList` enumerables serialize as arrays, and a property getter that throws mid-write no longer leaves a dangling `"key":` behind.
- `MiniJson.Deserialize` no longer spins forever on truncated input (an unterminated string literal wedged the editor's main thread) and rejects pathologically nested documents instead of overflowing the stack. Both now raise a normal, catchable `FormatException`.
- A bridge that dies mid-command is now reported as such. The CLI previously surfaced a bare `Connection failed: ... forcibly closed by the remote host`, giving no hint that the editor process was gone, and the next command silently relaunched it. `ucp` now checks whether the editor is still alive — waiting out Unity's crash-handler teardown so a dying editor is not misreported — and distinguishes "the editor exited while '<method>' was running" from "the bridge was restarted by a domain reload, retry".
- Unity Safe Mode is now diagnosed instead of misreported. The bridge ships as a UPM package, and Unity loads no packages in Safe Mode, so a project with C# compile errors could present as an editor that is running but answers nothing - reported as "it is likely still closing or stuck", which sends you to look in entirely the wrong place. UCP now reads its own project-scoped editor log (never Unity's per-user global `Editor.log`, which carries paths and project names from unrelated sessions), reports Safe Mode explicitly, and lists the deduplicated `CS####` errors that caused it, capped at 10.
  - Verified alongside this: UCP's default `--dialog-policy auto` answers Unity's "Enter Safe Mode?" prompt with *Ignore*, so the editor boots normally, the bridge loads, and commands keep working while the project still has compile errors - with `ucp compile` reporting them. Unity documents the same deadlock for its own `com.unity.pipeline` with no CLI-side workaround.
- `ucp scene focus` reported the *previous* Scene view camera pose. `cameraPosition`/`cameraRotationEuler` were read from `SceneView.camera.transform`, which Unity only syncs when the view actually repaints — `Repaint()` merely queues that — so any caller reading the response in the same frame as the focus, and every batch-mode run, got a stale pose. Both values are now derived from the authoritative view state (`pivot`/`rotation`/`cameraDistance`), which `LookAtDirect` updates immediately. This was surfacing as a long-standing failure in the `SceneFocus_WithAxis_AlignsSceneCameraTowardTarget` editmode test; the test now asserts the RPC contract and the Scene view state rather than a render-time derivative, and the suite is green at 75/75.
- The `AssetSearch_DoesNotEmitSceneReadObjectThreadedErrors` editmode test asserted that *zero* errors of any level reached the log buffer during an `asset/search`. That made it fail on a cold `Library/`, where the search pulls in lazy imports whose URP shader-fallback errors have nothing to do with the code path under test — reproducibly red on all five Unity 6 slots in the release matrix, on 0.6.0 as well. It now asserts on the specific regression it exists for (`Do not use ReadObjectThreaded on scene objects!`) and reports the offending messages instead of only a count.
- `scripts/generate-micro-skills.mjs --check`, the CI gate that keeps the `ucp-surfaces` micro-skills coherent with the CLI surface, has been reporting drift on all 15 skills since the 0.6.0 release. The generator hardcoded the stamped version as a literal that had to be bumped by hand each release, and 0.6.0 missed it; it now reads `version.json` directly, which is the same source `sync-version.mjs` rewrites the generated files from, so a fresh generate plus sync is a no-op by construction. No generated file changed - the gate is simply green again.
- The startup-failure reporter no longer dumps 200 raw log lines to stderr on any hit, and no longer treats compiler response-file entries (`-r:"...SafeModeModule.dll"`) as evidence of an error. It matches real compiler diagnostics (`file.cs(4,18): error CS1026: ...`), deduplicates them, and prints only those.
- Negative numeric values are now accepted directly by every `--value` argument (`object set-property`, `material set-property`, `asset write`, `asset import-settings write`, `settings set-player`/`set-quality`/`set-physics`/`set-lighting`). Previously `--value -55730` was parsed as an unknown flag and required the `--value=-55730` workaround.

## [0.6.0] - 2026-06-14

### Added

- In-scene authoring and spatial reasoning surface, the highest-leverage gap for agent-driven game-dev workflows:
  - `ucp transform move|rotate|scale|look-at|get` — first-class transform authoring with Euler angles, world/local space, and absolute/relative semantics, replacing error-prone raw serialized `m_LocalPosition`/quaternion writes. Targets address objects by `--id`, `--path`, or `--name`.
  - `ucp spatial raycast|overlap|bounds|ground|nearest` — geometric queries so an agent can perceive a scene instead of inferring it: ray/shape casts against colliders, world-space AABBs, drop-to-surface placement, and nearest-object search. Physics queries sync transforms first so they see the current edit-mode state.
  - `ucp view capture|isolate|orbit` — composed visual perception: target-framed renders, single-object isolation auto-framed from bounds, and multi-angle composite grids (with longest-edge caps and transparent backgrounds) so a vision model can read 3D shape from one image. Works headless via temporary cameras.
- `ucp object create --primitive Cube|Sphere|Capsule|Cylinder|Plane|Quad` builds a primitive with mesh + collider in one call. Surfaced by an agent eval: without it, "create a cube" is effectively impossible over the CLI (an agent has to hand-assemble MeshFilter/MeshRenderer and cannot reference the built-in mesh).
- Shared `{instanceId | path | name}` object locator (`ObjectLocator`) backing the new commands, so scene work no longer depends solely on reload-fragile instance ids.
- Agent-in-the-loop evaluation harness (`scripts/agent-eval/`) that drives the new CLI surface with a weak model to surface documentation/ergonomics defects; methodology documented in `AGENTS.md`.
- Per-command-surface micro-skills as a second Claude Code plugin, `ucp-surfaces` (`plugins/ucp-surfaces`). Users can install either the single omni skill (`ucp@unity-control-protocol` → `/ucp:unity-control-protocol`) or 15 focused skills (`ucp-surfaces@unity-control-protocol` → `/ucp-surfaces:ucp-objects`, `ucp-spatial`, `ucp-view`, …). Generated by `scripts/generate-micro-skills.mjs` (with a `--check` CI gate) so they stay coherent with the CLI surface; the existing omni plugin is byte-unchanged.
- Machine-readable docs for AI agents: the docs site now publishes per-page Markdown mirrors (`https://unityctl.dev/docs/<page>.md`, served as `text/markdown`), an `/llms.txt` index, `robots.txt`, and `sitemap.xml`, generated at build time from the docs nav so they cannot drift. The CLI root help points agents at these.

### Fixed

- `ucp editor close` can no longer hang the editor behind a native save-on-quit dialog. When the bridge was unreachable, close fell back to an OS window-close (WM_CLOSE) that makes Unity prompt to save a dirty scene — blocking the main thread with no one to dismiss it — and marked the close "graceful" so it never escalated to a force-kill. Close now uses only the in-editor quit (`EditorApplication.Exit(0)`, prompt-free) and force-kills if that is unavailable. Surfaced by an agent eval where the model triggered this while trying to recover a wedged editor.
- Restored Unity 6000.0–6000.4 bridge compilation. The 6000.5 EntityId compatibility work introduced an ambiguous `SceneHandle`-to-`long` conversion (CS0457) in the pre-6000.5 code path, which broke the editor bridge across the entire 6.0–6.4 support matrix. Validated by running the editmode suite on 6000.4.0f1.
- `ucp` commands no longer hang indefinitely when the Unity Editor is wedged behind a modal dialog (or stuck compiling/importing). RPC responses are now bounded by `--timeout` and surface a clear, actionable error instead of blocking forever. Operations that intentionally block the editor's main thread for a long time — `build start`, `compile`, and package add/remove — are exempt so they are never cut short.
- Linux release binaries are now built on Ubuntu 22.04 (glibc 2.35) instead of `ubuntu-latest` (glibc 2.39), restoring support for older distributions such as Ubuntu 22.04. ([#1](https://github.com/mflRevan/unity-control-protocol/issues/1))

### Changed

- Hardened the entire `ucp` help surface for structural and semantic consistency. The root `--help` now carries a `long_about` (what ucp is, an orientation block on instance ids / `--json` / `--timeout 0`, and a link to the docs site + its AI-readable mirrors). Every subcommand group gained an enum-level orientation doc; discoverability traps were fixed (settings keys now have examples + point to the listing getter; `run-tests`/`build`/`exec`/`instantiate`/`bulk-move` document their value shapes; `--timeout 0` and the global value-enum semantics are documented). Doc-only — no behavior changed.
- Redesigned the docs website to a restrained, content-first aesthetic per the project design brief: removed gradient-clip headings, the typewriter hero, decorative blur/glow chrome, per-card rainbow accents, the fake auto-typing terminal, and glassmorphism; content now always renders (no scroll-gated opacity hiding sections from full renders or JS crawlers); fixed a font bug where the declared font never loaded.
- Consolidated the dirty-scene auto-save behavior shared by `scene load` and `play` into a single modal-safe guard (`EditorModalGuard`), removing duplicated logic and centralizing the contract that bridge commands must never trigger a blocking editor dialog. Added editmode coverage for titled auto-save, untitled discard, and the no-discard error path.

## [0.5.2] - 2026-05-06

### Added

- Added `ucp object get-children --id <instanceId> [--depth <levels>]` for targeted hierarchy reads scoped to a single GameObject, returning the same child metadata shape used by `ucp scene snapshot` without requiring a scene-wide crawl.
- Added runtime observability surfaces: `ucp log tail --follow` alias support, filter expressions (`level>=warning`, `channel=Shader`, `text=...`), and `ucp play --log-file <path>` play-session log capture.
- Added live log follow recovery so `ucp log tail --follow` reconnects after compile/play bridge restarts and drains buffered catch-up logs instead of silently dropping restart-window output.
- Added `ucp shader errors` for project-wide shader compiler diagnostics, `ucp frame capture --out <file>.json` for structured frame/profiler exports, and `ucp profile --seconds <n>` for one-shot frame-time profiling.
- Added `ucp asset inspect <path>` for material shader/keyword/texture/property summaries and prefab renderer/material inspection.
- Added `ucp scene query "<expr>" --fields ...` for lightweight hierarchy queries over name/component/active/tag/layer without external JSON traversal.
- Added `ucp script doctor [--fix]` to detect and repair stale generated `.csproj` compile entries after raw filesystem script deletes.

### Changed

- Expanded object authoring docs with a focused object-command workflow, `get-children` output examples, JSON shape reference, and extra guidance for common property-write patterns and hierarchy operations.
- Extended the QA playground harness to create a root/child/grandchild hierarchy and validate the new `object get-children` subtree response in the canonical dev project.
- `ucp compile` now performs a synchronous asset database refresh and best-effort solution regeneration before requesting script compilation, so newly written `IUCPScript` files are discoverable immediately after compile in raw-file agent workflows.
- Log collection now seeds buffered history from the Unity Console after domain reloads and listens on the threaded Unity log callback, making compile-time, play-start, and explicit audit reads much more reliable for agent workflows.
- Expanded runtime, asset, scene, scripting, and material docs for the new observability/query/inspection/script freshness workflows.

### Fixed

- Fixed `ucp logs` reliability gaps where buffered history could appear empty immediately after compilation, during play-mode transitions, or after explicit log audits because the bridge had restarted and lost its in-memory log buffer.

## [0.5.1] - 2026-04-11

### Added

- Added console-aware Unity test-runner hardening so `ucp run-tests` records post-start log deltas, reports `consoleClean` / warning/error counts in structured output, and injects a synthetic failing guard when new Unity `error` or `exception` logs appear during a run.
- Added targeted smoke coverage for delta-scoped log status queries and scene-safe asset search behavior that avoids Unity's `ReadObjectThreaded` scene-object errors.

### Changed

- Rewrote project README as agent-first documentation with rendered architecture and capabilities diagrams, replacing the previous command-reference style with workflow-oriented descriptions of what agents can automate through UCP.
- `ucp run-tests --json` now fails structurally with the full Unity result payload attached, making CI and agent workflows rely on `success: false` plus detailed failure data instead of a generic process error.
- Human-readable test output now includes concise console warning/error summaries so non-test Unity issues stay visible at the command surface.
- Updated runtime testing docs to describe the console-log guard behavior and its failure semantics.

### Fixed

- Fixed `ucp asset search` loading `.unity` assets through `LoadAllAssetsAtPath(...)`, which could emit `Do not use ReadObjectThreaded on scene objects!` noise during broad searches; scene assets now use the safe main-asset path.
- Fixed batched `ucp asset bulk-move` folder preparation so destination parent folders are created before Unity enters asset-editing mode, avoiding `Parent directory is not in asset database` failures during real bulk moves.
- Fixed `ucp scene load --additive` response serialization to return a stable dictionary payload compatible with smoke tests and downstream structured consumers.
- Fixed the dev sandbox fixtures and smoke baseline by cleaning malformed scene YAML, removing broken prefab/script attachments, and eliminating orphaned prefab component references that were surfacing hundreds of Unity console errors despite nominally passing tests.

## [0.5.0] - 2026-04-04

### Added

- Added `ucp references` command family for high-performance, Rust-native asset reference search. Parses Unity's text-serialized YAML directly from disk using parallel scanning (rayon), building a reverse-reference index without requiring a running Unity editor.
  - `ucp references find --asset <path|guid>` finds all files and objects referencing a given asset, with intelligent output grouping and pattern detection.
  - `ucp references index build` builds a full reference index with benchmarking output.
  - `ucp references index status` and `ucp references index clear` for index management.
  - `ucp references check` verifies project serialization compatibility for native indexing.
- Added configurable output controls for reference queries: `--detail summary|normal|verbose`, `--max-files`, `--max-per-file`, `--pattern-threshold` to minimize context consumption for agent workflows.
- Added bridge-based fallback (`ReferenceController.cs`) for projects using binary serialization, using `AssetDatabase.GetDependencies` plus `SerializedObject` property walking.
- Added serialization compatibility checks (Force Text + Visible Meta Files) to `ucp doctor` and `ucp install`, with actionable recommendations when settings are missing.
- Added `docs/authoring/references.md` with full usage documentation, syntax reference, flag descriptions, and output examples.
- Added `ucp asset move <path> <destination>` for Unity-aware asset and folder moves through `AssetDatabase.MoveAsset`, preserving `.meta` files and GUIDs so references stay intact.
- Added `ucp asset bulk-move --moves <json>` for ordered batch move/refactor workflows with structured per-entry results and optional `--continue-on-error`.
- Added `ucp references find-strings --pattern ...` for string-based migration audits across serialized/text assets, plus `ucp references check <path>` for fast missing-target verification after moves/refactors.
- Added `ucp scene load --additive` for multi-scene workflows without replacing the active setup.
- Added `.agents/skills/release/SKILL.md` documenting the end-to-end release flow, local validation expectations, matrix guidance, tag/publish steps, and workflow monitoring.

### Changed

- `ucp install` now automatically appends `.ucp/` to the project's shared ignore file for the active VCS when available: `.gitignore` for Git worktrees and `ignore.conf` for Unity VCS / Plastic workspaces. Repeated installs detect existing entries and do not duplicate them.
- Updated SKILL.md with reference search guidance, including when to use `--detail summary` for agent-optimized context efficiency.
- Expanded asset docs and skill guidance to cover Unity-safe move and bulk-move workflows for asset cleanup and refactoring.
- Asset search now supports regex name matching, bulk-move supports `--dry-run` previews plus better missing-path hints, and asset reimport supports recursive folder reimport for larger serialized edit passes.
- `ucp play` now fails clearly when already in play mode instead of appearing to re-enter, and scene/reference docs now cover additive scene loading plus post-refactor verification flows.
- Reorganized documentation into workflow-oriented `overview`, `authoring`, `runtime`, and `project` sections, replaced the old command inventory overview with stable lifecycle/setup guidance, and preserved legacy `/docs/commands/*` links through website aliases.
- Frontend/docs redesign

## [0.4.6] - 2026-04-01

### Added

- Added `scripts/unity-version-matrix.ps1` to resolve Unity 6 compatibility slots (`6000.0` through `6000.4`), prefer exact installed editors, fall back to the next-best same-major editor when needed, and report skipped slots explicitly. Runs each version sequentially against the canonical dev project with manifest backup/restore and Library cleanup between slots.
- Added `scripts/validate-release.ps1` as a shared preflight entrypoint for local validation and release gating.
- Added `.github/workflows/validate.yml` to run Rust validation, website build validation, metadata sync checks, and the Unity compatibility matrix on pull requests and `main`.
- Added `UnityObjectCompat` compatibility shim (`Editor/Compatibility/UnityObjectCompat.cs`) centralizing `InstanceIDToObject` across all bridge controllers for cross-version safety.

### Changed

- Hardened `.github/workflows/release.yml` so release packaging waits for the same validation preflight, including Unity compatibility coverage, before building binaries and publishing npm artifacts.
- CLI startup dialog handler now recognizes "Project Upgrade Required", "Auto Graphics API Notice", and other common Unity startup dialogs, plus generic fallback matching for "Confirm" and "Yes" buttons with `--dialog-policy ignore`.
- Bridge `PropertyController` now calls `SerializedObject.Update()` before reading/writing properties and uses proper `try/finally` disposal, fixing a crash in Unity 6000.4 when setting Transform properties via `set-property`.
- Migrated all bridge controllers from `EntityIdToObject` to `EditorUtility.InstanceIDToObject()` via `UnityObjectCompat`, fixing compile errors across Unity 6.0–6.4.
- QA harness (`scripts/qa-playground.ps1`) hardened with `Test-UcpSuccess` helper, durable per-step JSON progress tracking, scene loading, editor force-close, and `-SkipInstall` support for matrix runs.
- CLI `run-tests` command now enforces a 10-minute timeout on bridge test-result notifications to prevent infinite hangs.
- Bridge lifecycle now provides editor-log feedback when bridge startup times out.
- Updated `PROJECT.md` with comprehensive pipeline documentation: validation commands, matrix runner usage, QA harness details, dialog handling reference, release flow steps, and cross-version compatibility notes.

### Fixed

- Fixed `PropertyController.SetPropertyValue` crash on Unity 6000.4 caused by missing `SerializedObject.Update()` call.
- Fixed manifest sanitization removing `com.unity.modules.adaptiveperformance` and `com.unity.modules.vectorgraphics` for Unity versions below 6000.3 where those modules don't exist.
- Fixed `ControllerSmokeTests` using internal `UnityObjectCompat` across assembly boundaries; tests now use `EditorUtility.InstanceIDToObject()` directly.
- Fixed release and validate workflows trying to run the Unity compatibility matrix on CI runners that lack Unity installs; the matrix is now local-only.

## [0.4.5] - 2026-03-25

### Added

- Added lightweight CLI release-awareness so interactive non-JSON commands can surface a cached notice when a newer UCP release is available.

### Changed

- `ucp doctor` now checks the latest GitHub release tag, reports whether the installed CLI is behind, and includes update guidance for the local CLI install plus the usual Unity bridge follow-up (`ucp doctor` / `ucp bridge update`).
- Release checks now use a local TTL-backed cache so normal CLI usage is not blocked by repeated network requests and transient upstream failures degrade gracefully.
- The npm package now publishes the bundled Unity bridge payload explicitly and its postinstall step always refreshes the matching platform binary instead of trusting any stale workspace-local `native/` artifact.

### Fixed

- Fixed npm release packaging so local workspace binaries can no longer leak into `npm pack` / publish output and override the expected release-download install path.

## [0.4.4] - 2026-03-23

### Changed

- Added a bridge-visible `editor/status` lifecycle surface and a shared CLI editor-settle wait path so relevant mutating commands can wait for Unity's import/update/compile work to finish before reporting success.
- Standardized Unity interaction handling around explicit lifecycle categories: read-only, editor-settle, restart-then-settle, and custom-confirmation flows.
- `ucp files write|patch`, mutating `ucp asset ...` flows, `ucp scene load`, mutating `ucp object ...`, `ucp material ...`, `ucp prefab ...`, `ucp settings ...`, `ucp build set-*`, file-mutating fallback `ucp vcs ...`, and package-changing `ucp packages ...` flows now keep the editor foregrounded as needed and wait for Unity to settle instead of returning while import/domain-reload work is still deferred in the background.
- Blocking settle/reload commands now append the curated `ucp logs status` summary automatically so warnings and errors stay visible at the moment lifecycle work finishes.
- `PROJECT.md`, `CONTRIBUTING.md`, and command docs now document the lifecycle-policy framework so future command surfaces extend the same readiness guarantees instead of adding ad hoc waits.
- Added active-scene dirty tracking plus explicit scene-save policy primitives so disruptive commands can fail early with a concise unsaved-scene summary instead of letting Unity raise its native save dialog.
- Added `ucp scene save`, `--save` support on scene-editing object/prefab/lighting commands, and first-class `ucp material create`.
- Added `ucp logs status` for a curated buffered-log overview with per-level counts, collapsed categories, and recent play-session timing/log summaries.
- Simplified the skill/plugin layout by restoring the canonical root `.claude-plugin` setup and removing the unused QA skill package.
- Removed the external skill publishing pipeline and related release/docs references so skill distribution now follows the repo and Claude Code marketplace surfaces only.

### Fixed

- Fixed the editor-readiness gap where bridge-mediated writes, importer edits, scene loads, and package changes could appear complete to the agent but still trigger Unity's normal catch-up import/refresh behavior only after the editor window regained focus.
- Fixed the play/compile/package/editor-transition workflow gap where unsaved active-scene changes could still fall through to Unity-owned save prompts instead of being surfaced deterministically in CLI output.
- Fixed prefab creation so `ucp prefab create` now creates real prefab-connected scene instances via Unity's `SaveAsPrefabAssetAndConnect(...)` path instead of leaving the source object decoupled from the saved asset.
- Fixed prefab/asset cleanup workflows by adding Unity-managed `ucp asset delete`, avoiding raw on-disk deletions that could desynchronize Unity's asset database and trigger import-worker errors.
- Fixed stop/play awareness by appending curated log-status output on `ucp stop` and exposing serialized `activeInputHandler` read/write support so input-system mismatches can be diagnosed and corrected from the CLI.
- Fixed scene-property workflows so renderer material arrays can now be assigned through `ucp object set-property`, enabling command-palette-driven category material assignment for live scene hierarchy iteration.
- Fixed the main Unity 6 bridge deprecation surface in source by migrating repeated `InstanceIDToObject(int)` and `BuildTargetGroup` PlayerSettings usage to their newer APIs.

## [0.4.3] - 2026-03-23

### Added

- Added release-metadata wiring for the repository skill and Claude Code marketplace surfaces so distribution metadata stayed aligned with the main release flow.

### Changed

- Updated skill distribution documentation and release metadata handling alongside the repository's marketplace-facing surfaces.

## [0.4.2] - 2026-03-21

### Added

- Added a first-class `ucp packages ...` domain for Unity Package Manager search/list/info/install/remove, manifest dependency management, and scoped registry management.
- Added `ucp packages unitypackage inspect|import` for machine-friendly `.unitypackage` inspection and selective import.

### Changed

- Package-management docs and skills now distinguish between normal `packages add|remove` installs, manifest-driven `packages dependency ...` flows for explicit local `file:` references, and selective `.unitypackage` import workflows.
- Scoped registry documentation now notes that Unity itself can show an "Importing a scoped registry" popup the first time a new registry is introduced.

### Fixed

- Fixed the missing package-management surface so agents can now browse packages, manage registries/dependencies, and selectively import `.unitypackage` archives without falling back to manual `manifest.json` or archive surgery.

## [0.4.1] - 2026-03-21

### Added

- Added `ucp asset reimport <path>` for explicit, targeted Unity reimport of an asset or its `.meta` file.
- Added `ucp asset import-settings read|write|write-batch` so agents can inspect and modify importer settings without hand-editing `.meta` files.
- Added end-to-end `ucp profiler ...` support for profiler status/config/session control, frame inspection, timeline/hierarchy analysis, callstacks, summaries, and structured snapshot export.

### Changed

- `ucp files write|patch` now trigger targeted synchronous reimport for edited assets and `.meta` files under `Assets/` and `Packages/` by default.
- Importer settings writes now apply automatically through Unity's importer pipeline, with `--no-reimport` available when callers want to defer the reimport step.
- `ucp asset info` now surfaces the Unity importer type when the target asset has an importer.
- Profiler sessions now default to bounded live-editor behavior: stale buffered frames are cleared before new sessions when needed, heavy profiler settings are restored on stop, summaries use a recent-frame window by default, and editor capture export prefers structured JSON snapshots.

### Fixed

- Fixed deferred editor catch-up after bridge-mediated writes, importer edits, package changes, and scene loads so relevant commands now wait for Unity to finish its import/compile/update work before returning.
- Fixed `ucp play` falsely reporting success when Unity blocked play-mode entry because compile-breaking console errors still needed to be resolved.
- Fixed imported-asset iteration gaps where agents had to patch `.meta` files manually and then remember to reimport assets before changes took effect.
- Fixed importer-setting workflows for assets such as FBX models and textures by exposing a first-class, importer-aware editing surface instead of raw meta-file surgery.
- Fixed profiler-driven editor memory blowups by clamping live profiler buffer budgets, bounding expensive export/summary paths, and avoiding long-lived allocation-callstack sessions after stop.

## [0.4.0] - 2026-03-15

### Added

- Added grouped `ucp files read|write|patch` commands as the canonical bridge-mediated file workflow.
- Added `ucp scene snapshot` as the canonical hierarchy snapshot command.
- Added `ucp scene focus --id <id> [--axis X Y Z]` for repeatable Scene view alignment during screenshot-driven iteration.
- Added bridge smoke coverage for synchronous asset refresh on file writes and Scene view focus behavior.
- Added a deterministic roll-a-ball greybox workflow in `unity-project-dev/ucp-dev`, including arena setup automation, runtime scripts, and edit-mode tests.

### Changed

- Renamed the primary lifecycle command from `ucp start` to `ucp open` and removed the old start alias.
- Removed top-level legacy command aliases for `snapshot`, `read-file`, `write-file`, and `patch-file`; the grouped `scene` and `files` commands are now the only supported surfaces.
- Simplified `ucp scene focus` to axis-based alignment only, removing distance overrides from the public command surface and docs.
- Updated the README, command docs, skills, project reference, smoke scripts, QA scripts, and generated website content to match the final command surface.
- The greybox arena builder now starts the player at center and arranges collectibles in an even circular ring for cleaner scene inspection.

### Fixed

- Fixed a bridge-side asset import gap where file writes and patches updated disk content without refreshing Unity's asset database.
- Fixed editor lifecycle handling so `close` distinguishes between fully exited and still-closing processes, and `open` no longer misreports a half-closed instance as safely running.
- Fixed compile waits to fail clearly when the editor disappears instead of hanging behind a stale lifecycle state.
- Fixed Unity process discovery so Unity Hub launcher processes are no longer mistaken for live editor instances.
- Fixed the extended QA harness so bridge waits are bounded and visible, and multi-word `ucp files write --content` payloads are passed correctly during stress runs.
- Fixed the dev-project edit-mode test assembly so editor-only automation types no longer break compilation and script discovery.
- Fixed scene-focus validation to match Unity SceneView behavior consistently across live automation and smoke tests.

## [0.3.3] - 2026-03-14

### Added

- Added `--force-unity-version <version>` so lifecycle commands can target a specific installed Unity editor version when the project's configured version is unavailable.
- Added `--dialog-policy <auto|manual|ignore|recover|safe-mode|cancel>` for startup-dialog handling during bridge waits.
- Added Unity Hub metadata probing for `projects-v1.json` and `secondaryInstallPath.json` so version and install discovery work with non-default Hub install roots.

### Changed

- `ucp editor status` now reports the project Unity version, requested Unity version, installed Unity versions, and any resolution warning.
- The dev smoke script now validates install, start, doctor, connect, edit-mode test execution, command smoke, and editor close in one pass.
- Bridge router validation errors now map cleanly to protocol error codes instead of logging internal-error noise for expected bad input.

### Fixed

- Fixed Unity executable auto-detection for editors installed under Unity Hub secondary install roots.
- Fixed the bridge package import gap by adding missing Unity `.meta` files for `EditorController.cs` and `ObjectReferenceResolver.cs`.
- Fixed negative object-reference and file path traversal test cases so they return protocol validation errors instead of spurious internal failures.

## [0.3.2] - 2026-03-14

### Added

- Added first-class Unity editor lifecycle commands: `ucp editor start|close|restart|status|logs|ps` plus top-level `ucp start` and `ucp close` aliases.
- Added `ucp bridge status` and `ucp bridge update` for explicit bridge dependency inspection and tracked git ref refreshes.
- Added per-project editor session/log bookkeeping under `.ucp/editor-session.json` and `.ucp/logs/editor.log`.

### Changed

- Bridge-backed CLI commands now auto-start Unity when the target project can be resolved and a Unity executable is available.
- `ucp doctor` and `ucp connect` now inspect tracked bridge package drift and auto-update stale refs by default (`--bridge-update-policy auto`).
- Added global lifecycle/config flags for `--unity` and `--bridge-update-policy`.
- Expanded docs and the primary skill to describe lifecycle management, bridge drift handling, and the new command surface.

### Fixed

- Fixed the bridge lifecycle gap where commands assumed Unity was already running and failed without guiding the user toward launch/configuration.
- Fixed stale tracked bridge refs on the local dev project by auto-updating `com.ucp.bridge` from `v0.3.0` to `v0.3.1` during doctor validation.

## [0.3.1] - 2026-03-14

### Added

- Added `ucp asset write-batch` for multi-field ScriptableObject and asset updates in a single request.
- Added a companion QA skill at `skills/unity-control-protocol-qa/` for release validation against the bundled dev project.

### Changed

- `ucp install` now enables automation-friendly PlayerSettings defaults by default: `runInBackground`, `1920x1080` windowed defaults, and `defaultIsNativeResolution = false`.
- Object reference reads now include asset `path` and `guid` when available, making follow-up writes more deterministic.
- Updated docs and skills for batch asset writes, installer defaults, and the revised log-query behavior.

### Fixed

- Fixed buffered log queries so regex searches filter before `--count` truncation, preventing false empty results when newer noise crowds out older matches.
- Fixed buffered log reads ignoring requested counts because of the hard 10-entry return cap.
- Fixed `object set-property` and asset writes silently no-oping on unresolved object references by failing explicitly instead.

## [0.3.0] - 2026-03-13

### Added

- Added unattended workflow controls for dirty-scene handling in `ucp play` and `ucp scene load`:
  - `--no-save`
  - `--keep-untitled`
- Added optional installer confirmation gate via `ucp install --confirm` (installer remains non-interactive by default).
- Added extensive playground QA harness coverage and reporting for full command-surface lifecycle validation.

### Changed

- `ucp install` is now **manifest-first by default** when no source flags are provided.
- Local embedded bridge install modes are now explicit (`--dev`, `--embedded`, `--bridge-path`).
- Updated docs (`README`, `PROJECT.md`, commands/install docs) to reflect manifest-first defaults and unattended automation guidance.
- Website deployment now targets Vercel instead of GitHub Pages, with the `website/` app made self-contained for deployment.

### Fixed

- Fixed Unity edit-mode test launch failures when triggered during Play Mode by queueing edit-mode execution until Play Mode exits.
- Fixed automation interruptions from Unity save-scene dialogs during scene load/play transitions by adding deterministic dirty-scene handling.
- Fixed install flow friction by removing default `y/n` prompt requirement (now opt-in via `--confirm`).
- Fixed QA harness false negatives around bridge reconnect windows (`play/pause/stop`), prefab unpack CLI args, screenshot assertions, and cleanup idempotency.
- Fixed website deployment structure by tracking the full `website/` app in the main repository and adding SPA rewrites for runtime routing.

## [0.2.3] - 2026-03-12

### Fixed

- Fixed release validation by making `scripts/sync-version.mjs` tolerate optional website demo files that are not present in every tagged tree

## [0.2.2] - 2026-03-12

### Changed

- `ucp install` now prefers a local embedded bridge mount when a bridge payload is available, while `ucp install --manifest` remains the explicit tracked-dependency path
- Published npm packages now bundle the Unity bridge payload, and GitHub releases now publish bundled CLI archives that include the bridge payload next to the binary

### Fixed

- Fixed migration from stale tracked `file:` bridge dependencies by scrubbing them from Unity manifests during local-first installs
- Fixed the GitHub Pages workflow by removing the failing dependency-cache setup that could not resolve the website lockfile path in Actions

## [0.2.1] - 2026-03-12

### Added

- Buffered log history reads with regex search, id-based inspection, and explicit history windowing
- Persistent Unity EditMode smoke tests for buffered log filtering and truncation behavior

### Changed

- `snapshot` now defaults to depth `0` with lean root-object metadata and the docs/skill now describe human-mode output guardrails explicitly
- `ucp install --dev` now supports repeat local package refreshes without requiring a changed manifest reference
- The docs website is now built for root hosting instead of `/unity-control-protocol/`, and Pages deploys when `docs/` or `skills/` content changes

### Fixed

- Fixed Windows local package `file:` references so dev bridge installs resolve cleanly in Unity
- Fixed Unity bridge reload nudging on Windows by falling back to `AppActivate` when native foreground APIs are insufficient
- Fixed `ucp object get-fields` human-mode headers to use the returned object name
- Fixed EditMode test duration reporting so completed runs no longer show negative elapsed time

## [0.2.0] - 2026-03-12

### Added

- New CLI domains for objects, assets, materials, prefabs, settings, and build pipeline automation
- Matching Unity bridge controllers for the new CLI domains
- Expanded markdown docs site with command pages and an Agents section
- Agent Skills-compatible skill directory at `skills/unity-control-protocol/`
- Skills docs page with raw SKILL preview and direct download

### Changed

- Bumped CLI, bridge, npm package, and protocol metadata to `0.2.0`
- Updated root documentation and repository reference material to match the current repo shape and release flow
- Aligned repository metadata to the canonical `mflRevan/unity-control-protocol` remote

### Fixed

- Fixed the docs skill preview frontmatter stripping on Windows line endings
- Fixed landing-page DotGrid stacking so the background effect renders above the document background

## [0.1.0] - 2026-03-09

### Added

- Initial WebSocket bridge server
- Play/stop/pause control
- Compilation trigger
- Scene management (list, load, active)
- State snapshots
- Screenshot capture
- Console log streaming
- Test runner integration
- File read/write/patch operations
- JSON-RPC 2.0 protocol
- Lock file discovery mechanism
- Per-session token authentication

