<p align="center">
  <img src="assets/branding/ucp-icon.svg" alt="UCP logo" width="96" />
</p>

<h1 align="center">Unity Control Protocol</h1>

<p align="center">
  CLI-first automation for the Unity Editor.
</p>

<p align="center">
  <a href="assets/branding/ucp-icon.svg">SVG logo</a>
  |
  <a href="assets/branding/ucp-icon-128.png">PNG icon (128)</a>
  |
  <a href="assets/branding/ucp-icon-512.png">PNG icon (512)</a>
  |
  <a href="https://github.com/mflRevan/unity-control-protocol/releases">Releases</a>
  |
  <a href="https://www.npmjs.com/package/@mflrevan/ucp">npm</a>
</p>

UCP is a cross-platform CLI plus Unity Editor bridge for programmatic control of Unity projects. It is built for local automation, AI agents, CI/CD, and repeatable editor workflows across scenes, objects, assets, importer settings, packages, tests, builds, and profiler inspection.

## What UCP is

UCP is split into two parts:

- `ucp`: a Rust CLI for operators, agents, and scripts
- `com.ucp.bridge`: a Unity Editor package that exposes editor operations over localhost WebSocket JSON-RPC

The bridge writes `.ucp/bridge.lock` in the Unity project root. The CLI reads that lock file, authenticates with a per-session token, and then talks to the editor through the bridge.

Most bridge-backed commands can now auto-start Unity when the project and editor path can be resolved.

## What ships in this repo

- `cli/`: Rust CLI exposed as `ucp`
- `unity-package/com.ucp.bridge/`: Unity bridge package
- `npm/`: npm wrapper that downloads the matching released binary and bundles the bridge payload
- `docs/`: markdown docs source
- `website/`: Vite/React docs site built from `docs/` and `skills/`
- `skills/unity-control-protocol/SKILL.md`: canonical agent skill file

## Recommended workflow

This is the current happy-path workflow for real project use:

```bash
cd /path/to/MyUnityProject
ucp install
ucp open
ucp connect
ucp scene snapshot
ucp scene focus --id 46894 --axis 1 0 0

# edit files locally in your workspace
ucp compile

ucp play
ucp screenshot --view scene --output capture.png
ucp stop
ucp run-tests --mode edit
ucp close
```

Recommended usage notes:

- Prefer normal workspace file edits plus `ucp compile` for script iteration.
- Use `ucp files read|write|patch` when you intentionally want bridge-mediated project file I/O.
- `ucp files write|patch` automatically reimport edited Unity assets and `.meta` files under `Assets/` and `Packages/` unless you pass `--no-reimport`.
- For imported assets such as textures, FBX models, and audio, prefer `ucp asset import-settings ...` over hand-editing `.meta` files.
- Use `ucp asset reimport <path>` when you intentionally deferred apply work or changed an imported asset on disk outside Unity's importer workflow.
- Use `ucp packages ...` for Unity Package Manager workflows and `ucp packages unitypackage ...` for selective `.unitypackage` inspection/import.
- Use `ucp open` when you want an explicit lifecycle step.
- Use `ucp connect` when you want "make sure Unity is running and verify the bridge is healthy" in one command.
- Use `ucp scene snapshot` to discover instance IDs, then `ucp scene focus` and `ucp screenshot` for visual iteration loops.

## Install

### npm

```bash
npm install -g @mflrevan/ucp
```

### pnpm

```bash
pnpm add -g @mflrevan/ucp
pnpm approve-builds
```

### from source

```bash
git clone https://github.com/mflRevan/unity-control-protocol.git
cd unity-control-protocol/cli
cargo build --release
```

## Install the Unity bridge

From a Unity project root:

```bash
ucp install
```

Default behavior:

- writes a tracked `com.ucp.bridge` dependency into `Packages/manifest.json`
- pins that dependency to the matching CLI tag
- stays non-interactive unless you pass `--confirm`
- does not inject a local `file:` dependency

Explicit local bridge development modes:

- `ucp install --dev`
- `ucp install --embedded`
- `ucp install --bridge-path <path>`

## Lifecycle and bridge behavior

### `ucp open`

- explicit editor bootstrap
- launches Unity for the target project
- waits for `.ucp/bridge.lock`
- waits for handshake success
- will not incorrectly treat a half-closed editor as healthy

### `ucp connect`

- ensures Unity is running
- waits for the bridge
- reports Unity version, project name, and protocol version
- is the simplest “make this project ready for automation” entrypoint

### `ucp close`

- requests graceful shutdown through the bridge first
- falls back to window-close behavior when needed
- can force terminate with `ucp editor close --force`
- now reports when the editor is still closing instead of falsely claiming success

### bridge drift handling

Before bridge-backed commands launch or connect, UCP checks whether the tracked bridge dependency is behind the current CLI version.

- `auto`: update the tracked dependency before launch or connect
- `warn`: report drift without mutating the project
- `off`: skip drift handling

Use `--bridge-update-policy warn` if you want notification-only behavior.

### Unity resolution

UCP resolves Unity in this order:

1. `--unity <path>`
2. `UCP_UNITY`
3. saved CLI settings
4. `--force-unity-version <version>`
5. `ProjectSettings/ProjectVersion.txt`
6. Unity Hub project metadata
7. standard and secondary Unity Hub install roots
8. `Unity.exe` on `PATH`

If the project's configured Unity version is known but not installed, UCP fails instead of silently picking a different editor. Use `--force-unity-version <ver>` only when you explicitly accept that risk.

## Core command surface

### Setup and lifecycle

- `ucp doctor`
- `ucp install`
- `ucp uninstall`
- `ucp bridge status|update`
- `ucp connect`
- `ucp editor open|close|restart|status|logs|ps`
- `ucp open|close`

### Runtime control

- `ucp play`
- `ucp stop`
- `ucp pause`
- `ucp compile`
- `ucp profiler ...`

### Scene, files, media, tests, scripts

- `ucp scene list|active|load|focus|snapshot`
- `ucp files read|write|patch`
- `ucp screenshot`
- `ucp logs`
- `ucp run-tests`
- `ucp exec list|run`

### Advanced editor control

- `ucp object ...`
- `ucp asset ...`
- `ucp packages ...`
- `ucp material ...`
- `ucp prefab ...`
- `ucp settings ...`
- `ucp build ...`
- `ucp vcs ...`

All commands support `--json`. Most also support `--project`, `--unity`, `--bridge-update-policy`, `--dialog-policy`, `--timeout`, and `--verbose`.

Profiler workflows are safe-by-default for live editor use: new sessions clear stale buffered frames when needed, recent-frame summaries are bounded, heavy flags are restored on stop, and editor-side capture export uses structured JSON snapshots rather than pretending live raw binary capture is available.

Asset workflows are importer-aware: `ucp asset import-settings read|write|write-batch` expose importer settings directly, and `ucp asset reimport` gives an explicit targeted reimport path for assets or their `.meta` files.

Package workflows now cover official Unity packages, manifest dependencies, scoped registries, and selective `.unitypackage` import. For external local packages, prefer `ucp packages dependency set <name> file:...` rather than treating folders under `Packages/` as normal add/remove targets.

## Practical examples

### Bootstrap and inspect

```bash
ucp doctor
ucp connect
ucp scene snapshot --depth 1
ucp scene active
```

### Visual iteration loop

```bash
ucp scene snapshot --filter "Player"
ucp scene focus --id 46894 --axis 0 0 -1
ucp screenshot --view scene --output scene-pass.png
ucp object set-property --id 46894 --component Transform --property m_LocalPosition --value "[0,1,0]"
ucp screenshot --view scene --output scene-pass-2.png
```

### Local code iteration

```bash
# edit files in your editor
ucp compile
ucp run-tests --mode edit
ucp logs --count 20
```

### Imported asset iteration

```bash
ucp asset import-settings read "Assets/Models/Enemy.fbx"
ucp asset import-settings write "Assets/Models/Enemy.fbx" --field m_GlobalScale --value 0.5
ucp asset import-settings write-batch "Assets/Textures/HUD.png" --values '{"m_IsReadable":true,"m_TextureType":8}'
ucp asset reimport "Assets/Textures/HUD.png.meta"
```

Use importer-settings commands instead of manually patching `.meta` files when Unity exposes importer-managed settings.

### Package management and selective import

```bash
ucp packages search com.unity.cinemachine
ucp packages add com.unity.cinemachine
ucp packages info com.unity.cinemachine

ucp packages dependency set com.company.tooling file:../tooling-package
ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company

ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees
```

Notes:

- `packages add|remove` is the normal UPM install/remove path.
- `packages dependency ...` is the reliable path for explicit manifest-managed references such as external local `file:` packages.
- Adding a brand-new scoped registry can trigger Unity's own **Importing a scoped registry** popup.
- Selective `.unitypackage` import is implemented by archive inspection plus targeted extraction, because Unity does not expose a non-interactive selective import API.

### Buffered or live logs

```bash
ucp logs --count 10
ucp logs --pattern "NullReference|Exception" --count 100
ucp logs --follow --level error
```

## Development in this repo

### Local validation

```bash
cargo test --manifest-path cli/Cargo.toml
cargo check --manifest-path cli/Cargo.toml
cd website && npm run build
```

### Live Unity smoke testing

Mount the repo-local bridge into a real Unity project:

```powershell
cargo run --manifest-path cli/Cargo.toml -- --project D:/Unity/Projects/MyGame install --dev
```

Or use the helper scripts:

```powershell
./scripts/smoke-dev.ps1 -Project D:/Unity/Projects/MyGame
./scripts/qa-playground.ps1 -Project unity-project-dev/ucp-dev -TimeoutSeconds 45
```

## Release flow

- `version.json` is the metadata source of truth
- `scripts/sync-version.mjs --check <version>` validates synced version-bearing files
- pushing a tag matching `v*` runs `.github/workflows/release.yml`
- the workflow builds Linux, macOS, and Windows binaries
- the same workflow creates the GitHub release and publishes `@mflrevan/ucp` to npm
- released npm packages bundle the bridge payload
- GitHub release archives include the CLI binary plus `bridge/com.ucp.bridge`

## Repository map

```text
cli/                              Rust CLI
unity-package/com.ucp.bridge/     Unity Editor bridge package
npm/                              npm wrapper and postinstall downloader
docs/                             Markdown documentation source
website/                          Docs site
skills/                           Agent skill files
scripts/                          Validation and build helpers
assets/branding/                  Shared logo assets
```

## License

MIT
