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

This adds the UCP bridge package to your project's `Packages/manifest.json`. The bridge runs as an Editor-only package — it does not affect your builds.

### Manual Installation

Add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge"
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
