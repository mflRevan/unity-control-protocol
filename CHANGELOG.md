# Changelog

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

### Fixed

- Fixed Unity edit-mode test launch failures when triggered during Play Mode by queueing edit-mode execution until Play Mode exits.
- Fixed automation interruptions from Unity save-scene dialogs during scene load/play transitions by adding deterministic dirty-scene handling.
- Fixed install flow friction by removing default `y/n` prompt requirement (now opt-in via `--confirm`).
- Fixed QA harness false negatives around bridge reconnect windows (`play/pause/stop`), prefab unpack CLI args, screenshot assertions, and cleanup idempotency.

### Validation

- Full command-palette playground QA harness passed: `50/50`.
- EditMode bridge smoke suite passed in-run: `11/11`.

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
