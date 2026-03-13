# Changelog

## [0.3.0] - 2026-03-13

### Added

- Dirty-scene automation controls in bridge scene/play handlers so unattended CLI workflows can avoid save-confirmation modal interruptions.

### Changed

- Scene load and play entry now auto-handle dirty scenes by default for non-interactive automation flows.

### Fixed

- Fixed edit-mode test execution when called during Play Mode by deferring run start until Play Mode exits.
- Fixed workflow interruptions caused by Unity save-scene prompts during scene transitions and play mode entry.

## [0.2.3] - 2026-03-12

### Fixed

- Fixed the release packaging pipeline so metadata validation no longer depends on optional website-only files

## [0.2.2] - 2026-03-12

### Changed

- The bridge is now intended to be consumed through CLI-managed local mounts by default, with tracked manifest installation remaining an explicit opt-in path

## [0.2.1] - 2026-03-12

### Added

- `LogsController` buffered history support for `logs/tail`, `logs/search`, and `logs/get`
- EditMode smoke coverage for buffered log truncation, regex filtering, and id-window filtering

### Changed

- Snapshot responses remain shallow by default and log-heavy reads are now designed for summary-first inspection

### Fixed

- EditMode test duration reporting now uses the editor uptime clock to avoid negative durations

## [0.2.0] - 2026-03-12

### Added

- Asset controller for asset search, inspection, field reads, writes, and ScriptableObject creation
- Property and hierarchy controllers for GameObject, component, and hierarchy manipulation
- Material controller for shader properties and keywords
- Prefab controller for status, overrides, apply, revert, unpack, and prefab creation
- Editor settings controller for player, quality, physics, lighting, tags, and layers
- Build controller for targets, scenes, scripting defines, and build execution

## [0.1.0] - 2026-03-09

### Added

- Initial WebSocket bridge server
- Play/stop/pause control
- Compilation trigger
- Scene management (list, load, active)
- State snapshots
- Screenshot capture (Game view)
- Console log streaming
- Test runner integration
- File read/write/patch operations
- JSON-RPC 2.0 protocol
- Lock file discovery mechanism
- Per-session token authentication
