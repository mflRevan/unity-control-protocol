# Changelog

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
