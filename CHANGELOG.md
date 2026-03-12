# Changelog

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
