# Changelog

## [0.4.1] - 2026-03-21

### Added

- Added `asset/reimport` for explicit targeted Unity reimport of an asset or its `.meta` file.
- Added `asset/import-settings/read`, `asset/import-settings/write`, and `asset/import-settings/write-batch` for importer-aware settings inspection and updates.

### Changed

- File writes and patches now trigger targeted synchronous reimport for edited assets and `.meta` files under `Assets/` and `Packages/`.
- Importer settings writes now save through Unity's importer pipeline and reimport automatically by default, with an opt-out path for deferred apply workflows.
- Asset metadata responses now include the importer type when Unity resolves one for the target path.

### Fixed

- Fixed imported asset workflows that previously required manual `.meta` editing and separate reimport steps before changes became visible in the editor.

## [0.4.0] - 2026-03-15

### Added

- Added `scene/focus` so the CLI can align the Unity Scene view to a target object for screenshot-driven iteration.
- Added smoke coverage for asset refresh after `file/write` and Scene view focus axis handling.

### Changed

- `scene/focus` now exposes the stable axis-alignment workflow only, without public distance overrides.

### Fixed

- Fixed `file/write` and `file/patch` so changes under `Assets/` and `Packages/` trigger a synchronous `AssetDatabase.Refresh`, making newly created scripts and assets available immediately.
- Fixed Scene view focus behavior and validation coverage so live automation and package smoke tests agree on the resulting alignment.

## [0.3.3] - 2026-03-14

### Added

- Added missing Unity metadata for `EditorController.cs` and `ObjectReferenceResolver.cs` so both controllers import reliably in embedded and tracked package installs.

### Changed

- `CommandRouter` now maps `ArgumentException` to `InvalidParams` and `UnauthorizedAccessException` to `FileAccessDenied` without emitting misleading internal-error logs.

### Fixed

- Fixed negative smoke tests around unresolved object references and project-root path traversal.

## [0.3.2] - 2026-03-14

### Added

- Added `editor/quit` so the CLI can request graceful Unity editor shutdown before falling back to OS-level close/terminate behavior.

### Changed

- Bridge server registration now includes editor lifecycle RPC handlers alongside the existing play, compile, scene, asset, and build controllers.

## [0.3.1] - 2026-03-14

### Added

- Added `asset/write-batch` for multi-field serialized asset updates in one bridge call.

### Changed

- Player settings now expose `defaultIsNativeResolution` so installer automation can reconcile live editor state as well as on-disk project settings.
- Object reference payloads now include asset `path` and `guid` when available.

### Fixed

- Fixed buffered log searches by applying regex filtering before count truncation.
- Fixed buffered log list requests being capped to 10 returned entries regardless of requested `count`.
- Fixed serialized object reference writes silently accepting unresolved references in both object and asset controllers.

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
