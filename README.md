# Unity Control Protocol (UCP)

A cross-platform CLI + Unity Editor bridge for programmatic control of Unity projects. Enables AI agents, CI/CD pipelines, and automation tools to interact with Unity Editor programmatically over WebSocket.

## Architecture

```
┌─────────┐   WebSocket/JSON-RPC   ┌───────────────────┐
│  ucp    │ ◄────────────────────► │  Unity Bridge     │
│  (CLI)  │    localhost:21342+    │  (Editor Plugin)  │
└─────────┘                        └───────────────────┘
```

**CLI** (`cli/`) — Rust binary providing human-friendly and JSON output modes  
**Bridge** (`unity-package/com.ucp.bridge/`) — Unity Editor package (UPM) that runs a WebSocket server inside the editor

## Recommended Workflow

**Work from your Unity project directory.** The CLI auto-detects the project and bridge connection when run from within a Unity project:

```bash
cd /path/to/MyUnityProject
ucp connect         # auto-discovers project + bridge
ucp snapshot         # immediate access to scene data
ucp write-file Assets/Scripts/MyScript.cs --content "..."
```

This is the primary workflow. The `--project` flag is available as a fallback for remote access or CI/CD where you can't `cd` into the project.

## Quick Start

### 1. Install the CLI

**Via cargo** (recommended):

```bash
cargo install --git https://github.com/mflRevan/unity-control-protocol --path cli
```

**From source:**

```bash
git clone https://github.com/mflRevan/unity-control-protocol.git
cd unity-control-protocol/cli
cargo build --release
# Add the binary to your PATH:
#   Linux/macOS: cp target/release/ucp ~/.local/bin/
#   Windows:     copy target\release\ucp.exe %USERPROFILE%\.cargo\bin\
```

**Via npm:**

```bash
npm install -g @mflrevan/ucp
```

> **pnpm users:** pnpm blocks postinstall scripts by default. After installing, run `pnpm approve-builds` to allow the binary download, or use npm instead.

**From GitHub releases:**

Download a prebuilt binary from [Releases](https://github.com/mflRevan/unity-control-protocol/releases) and place it on your PATH.

### 2. Install the Bridge

```bash
cd /path/to/MyUnityProject
ucp install
```

Or manually add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge"
  }
}
```

### 3. Connect

With Unity open and the bridge running:

```bash
cd /path/to/MyUnityProject
ucp connect
```

## Commands

| Command                 | Description                                    |
| ----------------------- | ---------------------------------------------- |
| `ucp connect`           | Verify connection to Unity bridge              |
| `ucp doctor`            | Run health checks on CLI, bridge, and protocol |
| `ucp install`           | Install bridge package into a Unity project    |
| `ucp uninstall`         | Remove bridge package from a Unity project     |
| `ucp play`              | Enter play mode                                |
| `ucp stop`              | Exit play mode                                 |
| `ucp pause`             | Toggle pause                                   |
| `ucp compile`           | Trigger script recompilation                   |
| `ucp scene list`        | List scenes in build settings                  |
| `ucp scene active`      | Show active scene info                         |
| `ucp scene load <path>` | Load a scene                                   |
| `ucp snapshot`          | Capture full scene hierarchy with components   |
| `ucp screenshot`        | Capture game/scene view screenshot             |
| `ucp logs`              | Stream Unity console logs in real time         |
| `ucp run-tests`         | Run edit-mode or play-mode tests               |
| `ucp read-file <path>`  | Read a project file                            |
| `ucp write-file <path>` | Write a project file                           |
| `ucp patch-file <path>` | Apply a find/replace patch to a file           |
| `ucp exec list`         | List available UCP automation scripts          |
| `ucp exec run <name>`   | Run a UCP automation script                    |
| `ucp vcs info`          | Show VCS provider status and connection info   |
| `ucp vcs status`        | Show pending changes                           |
| `ucp vcs checkout`      | Checkout files for editing                     |
| `ucp vcs revert`        | Revert files to repository version             |
| `ucp vcs commit`        | Commit (checkin) pending changes               |
| `ucp vcs diff`          | Show change summary or per-file status         |
| `ucp vcs incoming`      | List incoming changes from remote              |
| `ucp vcs update`        | Pull and apply incoming changes                |
| `ucp vcs branches`      | List branches                                  |
| `ucp vcs lock`          | Lock files                                     |
| `ucp vcs unlock`        | Unlock files                                   |
| `ucp vcs history`       | Show changeset history                         |
| `ucp vcs resolve`       | Resolve merge conflicts                        |

### Global Flags

- `--project <path>` — Path to Unity project (auto-detected if omitted)
- `--json` — Output in JSON format (for machine consumption)
- `--verbose` — Enable debug logging
- `--timeout <secs>` — Command timeout in seconds (default 30)

### JSON Output

All commands support `--json` for structured output:

```bash
ucp connect --json
# {"success":true,"data":{"unityVersion":"6000.3.1f1","projectName":"MyGame","protocolVersion":"0.1.0"}}
```

## UCP Scripts (Editor Automation)

UCP includes a Playwright-like script system for chaining editor operations. Write C# classes implementing `IUCPScript` and they are auto-discovered by the bridge.

### Writing a Script

```csharp
using UCP.Bridge;
using UnityEngine;
using System.Collections.Generic;

public class ValidateScene : IUCPScript
{
    public string Name => "validate-scene";
    public string Description => "Check scene meets quality requirements";

    public object Execute(string paramsJson)
    {
        var errors = new List<string>();
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        if (cameras.Length == 0)
            errors.Add("No camera found in scene");

        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        if (lights.Length == 0)
            errors.Add("No light found in scene");

        return new Dictionary<string, object>
        {
            ["valid"] = errors.Count == 0,
            ["errors"] = errors,
            ["cameraCount"] = cameras.Length,
            ["lightCount"] = lights.Length
        };
    }
}
```

### Running Scripts

```bash
ucp exec list                          # discover available scripts
ucp exec run validate-scene            # run a script
ucp exec run my-script --params '{"key":"value"}'  # with parameters
```

## Version Control (Unity VCS / Plastic SCM)

UCP provides full version control capabilities through Unity's built-in VersionControl API (`UnityEditor.VersionControl`). This works with Unity Version Control (formerly Plastic SCM) -- the provider configured in **Project Settings > Version Control**.

> **Warning:** VCS commands like `commit`, `revert`, `update`, and `resolve` can modify or discard work permanently. UCP does **not** add confirmation prompts or access restrictions by default. If you are integrating UCP into an agent or CI/CD pipeline, it is your responsibility to implement safeguards (confirmation dialogs, allowlists, dry-run modes, etc.) in your own automation layer.

### Prerequisites

- Unity Version Control (Plastic SCM) must be enabled in **Project Settings > Version Control**
- The workspace must be configured and connected to a Plastic SCM server
- For `branches` and `history` commands, the `cm` CLI must be in PATH

### Quick Reference

```bash
# Status and info
ucp vcs info                              # provider status
ucp vcs status                            # all pending changes
ucp vcs status --path Assets/Scripts      # changes in a specific path
ucp vcs diff                              # change summary (modified/added/deleted counts)
ucp vcs diff Assets/Scripts/Player.cs     # status of specific file(s)

# Working copy
ucp vcs checkout Assets/Scripts/Player.cs # checkout specific file
ucp vcs checkout --all                    # checkout all modified/added assets
ucp vcs revert Assets/Scripts/Player.cs   # revert specific file
ucp vcs revert --all                      # revert all pending changes
ucp vcs revert --all --keep-local         # undo checkout, keep modifications

# Commit and sync
ucp vcs commit -m "feat: add player controller"  # commit all pending
ucp vcs commit -m "fix" Assets/Scripts/Bug.cs     # commit specific files
ucp vcs incoming                          # check for remote changes
ucp vcs update                            # pull latest from remote

# Branches and history
ucp vcs branches                          # list all branches
ucp vcs history                           # recent changesets

# Locking
ucp vcs lock Assets/Art/character.fbx     # lock file(s)
ucp vcs unlock Assets/Art/character.fbx   # unlock file(s)

# Conflict resolution
ucp vcs resolve Assets/Scripts/Merged.cs              # resolve using merge
ucp vcs resolve Assets/Scripts/Merged.cs --method mine # keep local version
```

All VCS commands support `--json` for structured output, suitable for agent consumption.

## Protocol

Communication uses **JSON-RPC 2.0** over WebSocket on `127.0.0.1`. The bridge writes a lock file at `<ProjectRoot>/.ucp/bridge.lock` containing the port, token, and metadata.

### Discovery

1. CLI looks for `.ucp/bridge.lock` in the project directory
2. Lock file contains `{"pid", "port", "token", "protocolVersion", ...}`
3. CLI validates the PID is alive, then connects via WebSocket
4. Handshake authenticates using the token from the lock file

### Security

- Bridge binds only to `127.0.0.1` (localhost)
- Token-based authentication prevents unauthorized access
- File operations are sandboxed to the project directory

## Distribution

### cargo install (recommended)

```bash
cargo install --git https://github.com/mflRevan/unity-control-protocol --path cli
```

### npm

```bash
npm install -g @mflrevan/ucp
```

The npm package auto-downloads the correct prebuilt binary for your platform on install.

> **pnpm users:** pnpm blocks postinstall scripts by default. After installing, run `pnpm approve-builds` to allow the binary download, or use npm instead.

### Prebuilt binaries

Grab the latest binary for your platform from [GitHub Releases](https://github.com/mflRevan/unity-control-protocol/releases).

### From source

```bash
git clone https://github.com/mflRevan/unity-control-protocol.git
cd unity-control-protocol/cli
cargo build --release
```

### Cross-platform builds

Use the build script to compile for a specific target:

```bash
./scripts/build.sh                           # current platform
./scripts/build.sh x86_64-unknown-linux-gnu  # specific target
```

## Development

### Prerequisites

- **Rust** 1.70+ with cargo
- **Unity** 2021.3+ (tested with Unity 6000.3.1f1)
- **MSVC Build Tools** (Windows) or equivalent C toolchain

### Project Structure

```
unity-control-protocol/
├── cli/                              # Rust CLI
│   ├── Cargo.toml
│   └── src/
│       ├── main.rs                   # Entry point, argument parsing
│       ├── client.rs                 # WebSocket client
│       ├── protocol.rs              # JSON-RPC types
│       ├── error.rs                 # Error types
│       ├── output.rs                # Terminal output formatting
│       ├── config.rs                # Constants and lock file types
│       ├── discovery.rs             # Project and bridge discovery
│       └── commands/                # Command implementations
│           ├── mod.rs               # Command enum and dispatch
│           ├── connect.rs
│           ├── play.rs
│           ├── compile.rs
│           ├── scene.rs
│           ├── snapshot.rs
│           ├── screenshot.rs
│           ├── logs.rs
│           ├── tests.rs
│           ├── files.rs
│           ├── exec.rs              # Script execution
│           ├── install.rs
│           ├── doctor.rs
│           └── vcs.rs               # Version control commands
├── unity-package/
│   └── com.ucp.bridge/              # Unity Editor package
│       ├── package.json
│       └── Editor/
│           ├── Bridge/
│           │   └── BridgeServer.cs  # WebSocket server + lifecycle
│           ├── Protocol/
│           │   ├── MessageTypes.cs  # JSON-RPC types
│           │   ├── CommandRouter.cs # Method dispatch
│           │   └── MiniJson.cs      # Lightweight JSON parser
│           ├── Controllers/
│           │   ├── PlayModeController.cs
│           │   ├── CompilationController.cs
│           │   ├── SceneController.cs
│           │   ├── SnapshotController.cs
│           │   ├── ScreenshotController.cs
│           │   ├── FileController.cs
│           │   ├── TestRunnerController.cs
│           │   ├── ScriptController.cs  # UCP script execution
│           │   └── VcsController.cs     # Version control operations
│           └── Scripts/
│               └── IUCPScript.cs    # Script interface
├── npm/                              # npm distribution wrapper
│   ├── package.json                  # @mflrevan/ucp (GitHub Packages)
│   ├── bin/ucp.js                    # CLI wrapper
│   └── scripts/install.js           # Postinstall binary downloader
└── scripts/
    └── build.sh                     # Cross-platform build script
```

## License

MIT
