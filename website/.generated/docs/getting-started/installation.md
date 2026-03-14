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

By default, `ucp install` writes a tracked manifest dependency to `Packages/manifest.json`, pinned to the CLI version.

Default install does **not** add a local `file:` dependency.

Install now also enables automation-friendly PlayerSettings defaults in the target project:

- `runInBackground: true`
- `defaultScreenWidth: 1920`
- `defaultScreenHeight: 1080`
- `defaultIsNativeResolution: false`

Those defaults make unattended screenshots, playmode control, and agent-driven automation more reliable. You can change them later in Unity's Player Settings, but disabling them may degrade workflow quality.

For local bridge development against this repository, use `ucp install --dev` instead. That mounts the repo-local bridge into `Packages/com.ucp.bridge` without changing `Packages/manifest.json`.

Use `ucp install --embedded` or `ucp install --bridge-path <path>` for other explicit local embedded workflows.

### Manual Installation

Add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v0.3.3"
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
