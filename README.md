# Unity Control Protocol (UCP)

UCP is a cross-platform CLI plus Unity Editor bridge for programmatic control of Unity projects. It is built for local automation, AI agents, CI/CD, and repeatable editor workflows.

Release: `0.3.0`

## What ships

- `cli/`: Rust CLI exposed as `ucp`
- `unity-package/com.ucp.bridge/`: Unity Editor bridge package (`com.ucp.bridge`)
- `npm/`: npm wrapper that downloads the correct released binary at install time
- `website/`: Vite/React docs site deployed to GitHub Pages
- `docs/`: markdown source for the docs site
- `skills/unity-control-protocol/SKILL.md`: Agent Skills-compatible skill file

## Architecture

```text
Terminal / Agent / CI
                |
                v
         ucp CLI
                |
    WebSocket + JSON-RPC 2.0
                |
                v
Unity Bridge package
                |
                v
 Unity Editor APIs
```

The bridge binds to localhost, writes a lock file in the Unity project, and authenticates the CLI with a per-session token.

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

### cargo

```bash
cargo install --git https://github.com/mflRevan/unity-control-protocol --path cli
```

### bridge package

From your Unity project:

```bash
ucp install
```

By default, `ucp install` writes a tracked manifest dependency (`com.ucp.bridge` git URL pinned to the CLI version) into `Packages/manifest.json`.

Default install does **not** add a local `file:` dependency. Local embedded installs are explicit via `ucp install --dev`, `ucp install --embedded`, or `ucp install --bridge-path <path>`.

`ucp install` is non-interactive by default (no `y/n` prompt). Use `ucp install --confirm` if you want an explicit confirmation step.

Or add this dependency manually:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v0.3.0"
  }
}
```

## Quick start

```bash
cd /path/to/MyUnityProject
ucp doctor
ucp connect
ucp snapshot
ucp play
ucp screenshot --output capture.png
ucp stop
```

If you need fully unattended runs, keep scene/save prompts disabled by using defaults or explicit flags:

```bash
ucp scene load Assets/Scenes/SampleScene.unity
ucp play
# opt out of auto-save/discard behavior:
ucp scene load Assets/Scenes/SampleScene.unity --no-save --keep-untitled
ucp play --no-save --keep-untitled
```

## Command surface

### Core

- `ucp doctor`
- `ucp connect`
- `ucp install`
- `ucp uninstall`
- `ucp play`
- `ucp stop`
- `ucp pause`
- `ucp compile`

### Scene and file automation

- `ucp scene list|active|load`
- `ucp snapshot` (defaults to depth `0` and lean object metadata)
- `ucp read-file|write-file|patch-file`
- `ucp screenshot`
- `ucp logs`
- `ucp run-tests`
- `ucp exec list|run`

### Advanced editor control in `0.3.0`

- `ucp object ...`
- `ucp asset ...`
- `ucp material ...`
- `ucp prefab ...`
- `ucp settings ...`
- `ucp build ...`
- `ucp vcs ...`

All commands support `--json`. Most commands also support `--project`, `--timeout`, and `--verbose`.

Without `--json`, commands use human mode: concise terminal-oriented summaries meant for people and agent review loops. Broad read commands intentionally truncate in human mode so large scenes, settings blobs, and log searches do not flood the terminal.

Example:

```bash
ucp connect --json
# {"success":true,"data":{"unityVersion":"6000.3.1f1","projectName":"MyGame","protocolVersion":"0.3.0"}}
```

## Skills and docs

- Docs site source lives in `docs/` and is rendered by `website/`
- The docs site includes an `Agents > Skills` page with a live preview and direct download for `SKILL.md`
- The canonical agent skill lives in `skills/unity-control-protocol/SKILL.md`

## Release flow

- Pushes to `main` touching `website/**`, `docs/**`, or `skills/**` deploy GitHub Pages via `.github/workflows/pages.yml`
- Pushing a tag matching `v*` runs `.github/workflows/release.yml`
- The tag workflow builds binaries for Linux, macOS, and Windows
- The same workflow creates the GitHub release and publishes `@mflrevan/ucp` to npm
- The npm package downloads the tagged release asset during `postinstall` and bundles the matching Unity bridge payload into the published package
- GitHub releases publish bundled CLI archives that include the Unity bridge payload next to the binary
- `ucp install` (default) pins the Unity bridge package to the matching CLI tag as a tracked dependency
- `ucp install --dev` / `--embedded` / `--bridge-path` are explicit local embedded install modes

## Development

### Local validation

```bash
cargo test --manifest-path cli/Cargo.toml
cargo check --manifest-path cli/Cargo.toml
cd website && npm run build
```

### Live Unity smoke testing

Install the local bridge package into a real Unity project without touching the released tag-pinned workflow:

```powershell
cargo run --manifest-path cli/Cargo.toml -- --project D:/Unity/Projects/MyGame install --dev
```

Or use the helper script:

```powershell
./scripts/smoke-dev.ps1 -Project D:/Unity/Projects/MyGame
```

`ucp install` is manifest-first by default. Use `install --dev` to mount the repo-local package source, `install --embedded` to force embedded local mount mode, and `install --bridge-path` to mount another local package source.

The clean long-term packaging model for UCP is: ship or cache a versioned bridge payload with the CLI, then mount it locally into `Packages/com.ucp.bridge` on demand. That keeps the product centered on the CLI instead of treating the bridge as a repo dependency users have to commit.

Log inspection now supports both live follow mode and buffered history queries:

```powershell
ucp logs --follow --level error
ucp logs --count 10
ucp logs --pattern "NullReference|Exception" --count 100
ucp logs --id 42
```

### Repository map

```text
cli/                              Rust CLI
unity-package/com.ucp.bridge/     Unity Editor bridge package
npm/                              npm wrapper and postinstall downloader
website/                          Docs site
docs/                             Markdown documentation source
skills/                           Agent skill files
scripts/                          Build helpers
```

## License

MIT
