# Unity Control Protocol (UCP)

UCP is a cross-platform CLI plus Unity Editor bridge for programmatic control of Unity projects. It is built for local automation, AI agents, CI/CD, and repeatable editor workflows.

Release: `0.2.0`

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

Or add this dependency manually:

```json
{
    "dependencies": {
        "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge"
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
- `ucp snapshot`
- `ucp read-file|write-file|patch-file`
- `ucp screenshot`
- `ucp logs`
- `ucp run-tests`
- `ucp exec list|run`

### Advanced editor control in `0.2.0`

- `ucp object ...`
- `ucp asset ...`
- `ucp material ...`
- `ucp prefab ...`
- `ucp settings ...`
- `ucp build ...`
- `ucp vcs ...`

All commands support `--json`. Most commands also support `--project`, `--timeout`, and `--verbose`.

Example:

```bash
ucp connect --json
# {"success":true,"data":{"unityVersion":"6000.3.1f1","projectName":"MyGame","protocolVersion":"0.2.0"}}
```

## Skills and docs

- Docs site source lives in `docs/` and is rendered by `website/`
- The docs site includes an `Agents > Skills` page with a live preview and direct download for `SKILL.md`
- The canonical agent skill lives in `skills/unity-control-protocol/SKILL.md`

## Release flow

- Pushes to `main` touching `website/**` deploy GitHub Pages via `.github/workflows/pages.yml`
- Pushing a tag matching `v*` runs `.github/workflows/release.yml`
- The tag workflow builds binaries for Linux, macOS, and Windows
- The same workflow creates the GitHub release and publishes `@mflrevan/ucp` to npm
- The npm package downloads the tagged release asset during `postinstall`

## Development

### Local validation

```bash
cargo check --manifest-path cli/Cargo.toml
cd website && npm run build
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
