# Installation

## Install the CLI

### Via npm (recommended)

```bash
npm install -g @mflrevan/ucp
```

### Via Cargo

```bash
cargo install ucp
```

### Prebuilt Binaries

Download the latest release for your platform from [GitHub Releases](https://github.com/mflRevan/unity-control-protocol/releases).

Available platforms:

- **Windows** — `ucp-x86_64-pc-windows-msvc.zip`
- **macOS (Intel)** — `ucp-x86_64-apple-darwin.tar.gz`
- **macOS (Apple Silicon)** — `ucp-aarch64-apple-darwin.tar.gz`
- **Linux** — `ucp-x86_64-unknown-linux-gnu.tar.gz`

## Install the Bridge

Navigate to your Unity project directory and run:

```bash
cd /path/to/MyUnityProject
ucp install
```

By default, `ucp install` prefers a local-only embedded bridge mount when the CLI can find a matching bridge payload locally. That keeps the bridge out of tracked project dependencies while still making it available to the local Unity Editor.

The published npm package and bundled GitHub release archives include that bridge payload. Use those distributions if you want local-first bridge installs without touching `Packages/manifest.json`.

For local bridge development against this repository, use `ucp install --dev` instead. That mounts the repo-local bridge into `Packages/com.ucp.bridge` without changing `Packages/manifest.json`.

If you explicitly want the bridge recorded as a tracked project dependency, use `ucp install --manifest` or edit `Packages/manifest.json` manually.

### Manual Installation

Add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v0.2.2"
  }
}
```

## Verify Installation

```bash
ucp doctor
```

This checks that the CLI is installed, the bridge package is present, and Unity is running with an active connection.

## System Requirements

- **Unity** — 2021.3 LTS or newer (tested up to Unity 6)
- **Node.js** — 16+ (for npm installation only)
- **OS** — Windows 10+, macOS 12+, or Linux (x64)
