# Changelog

## [0.6.3] - Unreleased

### Added

- Added the `ui/list`, `ui/lint`, `ui/inspect`, `ui/screenshot`, `ui/check`, and `ui/status`
  RPCs plus the `ui/result` completion notification for UI Toolkit authoring on Unity 6 and
  newer. On older editors the methods are registered but return an explicit unsupported error.
- Lint runs the synchronous UXML/USS/TSS importers, reads their import logs and
  `importedWithErrors`/`importedWithWarnings` flags, follows UI dependencies, and clone-probes
  each `VisualTreeAsset` under a scoped log capture. Immutable package assets are inspected from
  their existing import artifacts without a forced reimport.
- Render operations (`inspect`, `screenshot`, `check`) are queued and ticked on
  `EditorApplication.update` in a transient `HideAndDontSave` utility window, wait for stable
  finite geometry and stable pixels, capture through `InternalEditorUtility.CaptureEditorWindow`
  with DPI compensation, write PNGs under `Library/UCP/UiCaptures`, and restore the previously
  focused window. A 300-second overall ceiling and per-state settle timeouts bound every run;
  `BridgeServer.Shutdown` fails in-flight operations with `editor_shutdown` before a reload.
- Scenario support: strict `.ucp-ui.json` parsing with unknown-field rejection, deep data
  overlays, an allowlisted `set` block, `repeat` and virtualized `list-view` collections driven
  by JSON pointers, and automatic rewriting of `DataBinding` paths to dictionary keys through
  `Unity.Properties`.

### Added

- Every response produced on the main thread carries an `editor` summary next to `result` or
  `error` (`EditorStateSummary`): mode, active scene dirtiness, Console badge counts read
  through `LogEntries.GetCountsByType`, entries logged since the request was dispatched, and
  conditional flags for compile errors, compiling, importing, building, prefab stage, and
  recording. The handshake, which is answered off the main thread, reports
  `mainThreadTickAgeMs` so a client can recognise a blocked main thread. Added `editor/state`
  to fetch the summary on its own.

### Changed

- Object ids are 64-bit end to end. `UnityObjectCompat.GetId()` returns a `long`
  (`EntityId.ToULong` on 6000.5+, the instance id below that), `ResolveByInstanceId` takes a
  `long`, and every controller reads `instanceId` parameters with `Convert.ToInt64`. On
  6000.5 an `EntityId` carries a per-session upper word, so the previous `int` truncation
  produced ids that could not be resolved back.

### Fixed

- Fixed the `tests/run` console guard counting log lines emitted inside a test's
  `LogAssert.ignoreFailingMessages` window; the `IgnoreFailingMessages:true/false` markers the
  test runner writes now delimit an exclusion range.
- Fixed the edit-mode test assembly failing to compile on Unity 6000.5 (`GetInstanceID()` is
  obsolete-as-error there); tests use the `EntityId`-aware `GetId()` helper throughout.
- Removed the empty `UCP.Bridge.Runtime` assembly definition that logged an "will not be
  compiled, because it has no scripts associated with it" warning on every import (#4).

## [0.6.2] - 2026-08-31

### Added

- Added native, scene-object-free `record/start`, `record/stop`, `record/status`, `record/arm`, and
  `record/signal` RPCs for Game and Scene view video capture.
- Added aspect-preserving longest-edge sizing and explicit width/height canvases with letterboxing,
  even-dimension normalization, configurable FPS/bitrate/duration, H.264/MP4 and VP8/WebM encoding,
  and platform-aware format selection.
- Added play-enter, play-exit, bounded log-regex, and named-signal triggers. Armed play triggers use
  `SessionState` to survive domain reloads, and `record/stop` also cancels a pending trigger.
- Added a `slowdown` parameter to `record/start` and `record/arm`. Frames are still captured at the
  requested cadence in real time; only the encoder's declared frame rate is divided by the factor, so
  playback is stretched without duplicating frames or re-encoding. `record/status` reports both
  `slowdown` and the resulting `playbackFps`. Values outside 1-20 are rejected. This raises effective
  temporal resolution for consumers that sample a clip at a fixed low rate rather than playing it.
- Documented that `record/start --view game` resolves `Camera.main`, not the Game view's composited
  camera stack. Projects rendering through several enabled cameras record only the `MainCamera`-tagged
  one, and camera depth does not change the selection; `--view scene` captures the Scene view camera,
  which is unaffected by gameplay.

### Fixed

- Fixed `tests/run` counting an empty root suite as a passed test. When a filter matched nothing,
  the run finished with a childless root whose result was collected as a leaf, so the summary
  reported one passed test named after the project. Suite results are now skipped, so a run that
  executed nothing reports a total of zero.
- Changed `tests/run` filtering from `Filter.testNames` to `Filter.groupNames`. `testNames` requires
  an exact fully-qualified match, which silently selected nothing for a class or method name;
  `groupNames` is matched as a regular expression against each test's full name, so partial names
  select what a caller expects and fully-qualified names still match.
- Fixed `object/get-property` double-converting values that `SerializedPropertyToValue` had already
  shaped for JSON. `GetPropertyValue` resolves a name through `SerializedObject.FindProperty` first
  and returns a ready `List`/`Dictionary`; `ConvertToJson` then ran over that result, matched none
  of its cases, and fell through to `value.ToString()`. A `Vector3` field came back as
  ``"System.Collections.Generic.List`1[System.Object]"`` with
  ``"type": "List`1"``. Every serialized Vector2/3/4, Quaternion, Color, Rect, Bounds and
  object-reference field was affected, and float fields reported a `Double` type name.
  `get-property` now takes its type name from the `SerializedProperty` so it agrees with
  `get-fields`, converts only values read through the reflection fallback, and `ConvertToJson` is
  idempotent for already-shaped `IList`/`IDictionary` values.

### Performance

- Frames render through a hidden reusable `RenderTexture` and reusable CPU readback texture into
  Unity's native `MediaEncoder`; recording creates no scene objects or scripts and avoids per-frame
  managed texture allocation.

### Reliability

- Added sibling `.partial` output and final rename, explicit overwrite handling, extension/format
  validation, encoder and temporary-file cleanup on failure, detached safety deadlines, trigger wait
  deadlines, dropped-frame accounting, and structured completed/failed status metadata.
- Active encoders finalize during assembly reload or editor shutdown. Play-boundary capture is
  handled by the persisted `play-enter`/`play-exit` arm flow rather than attempting to retain a
  native encoder across a domain reload.

### Tests

- Added editor smoke tests for recording RPC registration and idle status, aspect-preserving even
  dimensions, signal matching, and cancellation of armed recordings.

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
