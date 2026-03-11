# PROJECT.md — Unity Control Protocol

This file is the repo-level ground truth for structure, workflows, release mechanics, and operational constraints.

## Product summary

UCP exposes the Unity Editor as a locally reachable automation target through a Rust CLI and a Unity Editor bridge package. The CLI talks to the editor over WebSocket using JSON-RPC 2.0. The primary consumers are humans in terminals, CI jobs, and coding agents.

Current release target: `0.2.0`
Current protocol version: `0.2.0`

## Repository layout

```text
unity-control-protocol/
├── cli/                              Rust CLI crate
│   ├── Cargo.toml
│   ├── Cargo.lock
│   └── src/
│       ├── main.rs                   CLI entrypoint
│       ├── client.rs                 WebSocket client and RPC transport
│       ├── config.rs                 protocol constants and lock-file types
│       ├── discovery.rs              Unity project and bridge discovery
│       ├── error.rs                  shared CLI error handling
│       ├── output.rs                 human and JSON output helpers
│       ├── protocol.rs               JSON-RPC data types
│       └── commands/                 command implementations
│           ├── connect.rs
│           ├── doctor.rs
│           ├── install.rs
│           ├── play.rs
│           ├── compile.rs
│           ├── scene.rs
│           ├── snapshot.rs
│           ├── screenshot.rs
│           ├── logs.rs
│           ├── tests.rs
│           ├── files.rs
│           ├── exec.rs
│           ├── vcs.rs
│           ├── object.rs
│           ├── asset.rs
│           ├── material.rs
│           ├── prefab.rs
│           ├── settings.rs
│           └── build.rs
├── unity-package/
│   └── com.ucp.bridge/               Unity package shipped to projects
│       ├── package.json
│       ├── CHANGELOG.md
│       └── Editor/
│           ├── Bridge/               server lifecycle and registration
│           ├── Controllers/          command handlers grouped by domain
│           ├── Protocol/             JSON-RPC plumbing and router
│           ├── Scripts/              `IUCPScript` interface and related helpers
│           └── UCP.Bridge.Editor.asmdef
├── npm/                              npm distribution wrapper
│   ├── package.json
│   ├── bin/ucp.js                    executable wrapper around downloaded binary
│   ├── scripts/install.js            downloads release binary by tag version
│   └── native/                       install target for downloaded binary
├── website/                          docs website
│   ├── package.json
│   ├── package-lock.json
│   └── src/
│       ├── App.tsx
│       ├── components/
│       ├── lib/
│       └── pages/
├── docs/                             markdown content rendered by website
│   ├── getting-started/
│   ├── commands/
│   └── agents/
├── skills/
│   └── unity-control-protocol/
│       └── SKILL.md                  Agent Skills-format canonical skill
├── scripts/
│   └── build.sh                      CLI build helper
├── .github/workflows/
│   ├── pages.yml                     GitHub Pages deployment
│   └── release.yml                   binary release and npm publish
├── AGENTS.md
├── README.md
├── PROJECT.md
└── CHANGELOG.md
```

## Runtime model

### Transport

- The bridge listens only on localhost.
- The CLI discovers the active bridge through a lock file inside the Unity project.
- The transport is WebSocket with JSON-RPC 2.0 request and notification semantics.

### Discovery

- The bridge writes `.ucp/bridge.lock` into the Unity project root.
- The lock file carries at least process id, port, protocol version, project path, start time, and token.
- The CLI validates the lock file and then performs a handshake.

### Security model

- Localhost-only listener.
- Per-session token taken from the lock file.
- File operations are sandboxed to the target project root.
- High-impact VCS operations are intentionally exposed, so external automation must decide policy and guardrails.

## Current command surface

### Core lifecycle

- `doctor`
- `connect`
- `install`
- `uninstall`
- `play`
- `stop`
- `pause`
- `compile`

### Scene and project inspection

- `scene list`
- `scene active`
- `scene load`
- `snapshot`
- `logs`
- `screenshot`
- `run-tests`
- `exec list`
- `exec run`

### File automation

- `read-file`
- `write-file`
- `patch-file`

### Advanced editor control added in the 0.2 line

- `object ...` for GameObjects and components
- `asset ...` for asset search, metadata, and ScriptableObjects
- `material ...` for material/shader properties
- `prefab ...` for prefab status, overrides, apply, revert, unpack, create
- `settings ...` for player, quality, physics, lighting, tags, and layers
- `build ...` for target selection, scenes, defines, and builds
- `vcs ...` for Unity VCS / Plastic SCM workflows

## Docs and site pipeline

### Source of truth

- Long-form docs live in `docs/` as markdown.
- The website imports markdown directly from `docs/` using Vite raw imports.
- The skill preview page imports the raw skill file from `skills/unity-control-protocol/SKILL.md`.

### Website routing

- `website/src/App.tsx` routes `/docs/*` into the docs layout.
- Markdown pages are rendered by the shared markdown component.
- `agents/skills` uses a dedicated page because it needs both markdown intro content and raw file download/preview behavior.

### Visual constraints

- The landing page uses a full-page DotGrid canvas background.
- Background effects must stay under site content but above the document background.
- The docs site is dark-first and relies on custom prose styling rather than a stock markdown theme.

## Release and deployment pipeline

### Website deployment

- Trigger: push to `main` affecting `website/**` or the Pages workflow.
- Workflow: `.github/workflows/pages.yml`
- Build: `npm ci` then `npm run build` inside `website/`
- Output: `website/dist`
- Destination: GitHub Pages

### Product release

- Trigger: git tag matching `v*`
- Workflow: `.github/workflows/release.yml`
- Artifacts built:
  - `ucp-linux-x64`
  - `ucp-darwin-x64`
  - `ucp-darwin-arm64`
  - `ucp-win32-x64.exe`
- The workflow creates a GitHub release with those binaries plus a checksum file.
- After the GitHub release step, the same workflow publishes `npm/` to npmjs.

### npm packaging contract

- `npm/package.json` version must exactly match the git tag version.
- `npm/scripts/install.js` downloads a release asset from `https://github.com/mflRevan/unity-control-protocol/releases/download/v<version>/...`
- Because of that, GitHub release assets and npm publish must stay in lockstep.
- pnpm users must approve the postinstall binary download.

## Validation workflow

### Minimum release validation

1. `cargo check --manifest-path cli/Cargo.toml`
2. `npm run build` inside `website/`
3. Smoke test the CLI against an open Unity project with non-destructive commands
4. Verify GitHub remote, versions, changelog, and release notes are aligned

### Safe smoke-test command set

- `ucp doctor`
- `ucp connect`
- `ucp snapshot --json`
- `ucp scene active`
- `ucp logs --count 10`
- `ucp asset search -t Material --max 5`
- `ucp build active-target`
- `ucp build targets`
- `ucp settings player`
- `ucp object get-fields --id <id> --component Transform`

Avoid destructive or stateful commands in smoke tests unless explicitly intended.

## Hardening notes

- Keep protocol version changes synchronized between `cli/src/config.rs`, bridge handshake code, docs examples, and any website demos.
- Do not leave duplicate skill files in `skills/`; agents should discover one canonical skill directory.
- Release metadata must always point at the actual remote: `https://github.com/mflRevan/unity-control-protocol`.
- If npm publish succeeds but release assets are missing, fresh npm installs will fail because the postinstall downloader cannot resolve the binary.
- If new command families are added, update all of: CLI docs, website sidebar, skill file, README, and changelog.
}
```

Bridge responds:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "serverVersion": "0.1.0",
    "protocolVersion": "0.1.0",
    "unityVersion": "2022.3.20f1",
    "projectName": "MyGame",
    "projectPath": "/Users/dev/MyGame"
  }
}
```

### 5.3 Snapshot Schema

```json
{
  "scene": "Assets/Scenes/MainScene.unity",
  "sceneName": "MainScene",
  "playMode": false,
  "timestamp": "2026-03-09T12:00:00Z",
  "objects": [
    {
      "instanceId": 1234,
      "name": "Player",
      "active": true,
      "tag": "Player",
      "layer": 0,
      "position": [0.0, 1.0, 0.0],
      "rotation": [0.0, 0.0, 0.0, 1.0],
      "scale": [1.0, 1.0, 1.0],
      "components": [
        {
          "type": "PlayerController",
          "enabled": true
        },
        {
          "type": "Rigidbody",
          "enabled": true
        }
      ],
      "children": []
    }
  ],
  "logs": [
    {
      "level": "info",
      "message": "Player spawned",
      "timestamp": "2026-03-09T12:00:00Z"
    }
  ],
  "stats": {
    "objectCount": 42,
    "componentCount": 128
  }
}
```

### 5.4 Screenshot Schema

Request:
```json
{
  "method": "screenshot",
  "params": {
    "view": "game",
    "width": 1920,
    "height": 1080,
    "format": "png"
  }
}
```

Response:
```json
{
  "result": {
    "width": 1920,
    "height": 1080,
    "format": "png",
    "encoding": "base64",
    "data": "iVBORw0KGgo..."
  }
}
```

### 5.5 Test Results Schema

```json
{
  "result": {
    "summary": {
      "total": 25,
      "passed": 23,
      "failed": 1,
      "skipped": 1,
      "duration": 4.32
    },
    "tests": [
      {
        "name": "PlayerController.TestJump",
        "status": "passed",
        "duration": 0.12
      },
      {
        "name": "InventorySystem.TestOverflow",
        "status": "failed",
        "duration": 0.08,
        "message": "Expected 10 but got 11",
        "stackTrace": "at InventorySystem.Add() in Assets/Scripts/Inventory.cs:42"
      }
    ]
  }
}
```

### 5.6 Error Codes

| Code   | Meaning                         |
|--------|---------------------------------|
| -32700 | Parse error (invalid JSON)      |
| -32600 | Invalid request                 |
| -32601 | Method not found                |
| -32602 | Invalid params                  |
| -32603 | Internal error                  |
| -32000 | Unity error (generic)           |
| -32001 | Scene not loaded                |
| -32002 | Compilation failed              |
| -32003 | Play mode conflict              |
| -32004 | File access denied              |
| -32005 | Test execution failed           |
| -32006 | Screenshot capture failed       |
| -32007 | Object not found                |
| -32008 | Timeout                         |
| -32009 | Authentication failed           |

---

## 6. CLI Design (Detailed)

### 6.1 Rust Crate Dependencies

```toml
[dependencies]
clap = { version = "4", features = ["derive"] }
tokio = { version = "1", features = ["full"] }
tokio-tungstenite = "0.24"
serde = { version = "1", features = ["derive"] }
serde_json = "1"
thiserror = "2"
anyhow = "1"
tracing = "0.1"
tracing-subscriber = "0.3"
base64 = "0.22"
similar = "2"
directories = "5"            # Platform-standard config paths
sysinfo = "0.32"             # PID liveness check
```

### 6.2 CLI Command Tree

```
ucp
├── init                          # Initialize UCP in a Unity project
├── install                       # Install bridge package into Unity project
├── uninstall                     # Remove bridge package
├── update                        # Update bridge package
├── doctor                        # Verify CLI + bridge + protocol compatibility
│
├── connect                       # Test connection to bridge
├── play                          # Enter play mode
├── stop                          # Exit play mode
├── pause                         # Toggle pause
├── compile                       # Trigger recompilation
│
├── scene
│   ├── list                      # List scenes
│   ├── load <path|index>         # Load scene
│   └── active                    # Get active scene
│
├── snapshot                      # Capture state snapshot
│   ├── --filter <pattern>        # Filter objects by name
│   ├── --depth <n>               # Hierarchy depth limit
│   └── --components              # Include component details
│
├── screenshot                    # Capture screenshot
│   ├── --view <game|scene>       # Which view to capture
│   ├── --width <px>              # Resolution width
│   ├── --height <px>             # Resolution height
│   └── --output <file>           # Save to file (else base64 stdout)
│
├── logs                          # Stream logs (blocking)
│   ├── --level <info|warn|error> # Filter by level
│   └── --count <n>               # Get last N logs then exit
│
├── run-tests                     # Execute tests
│   ├── --mode <edit|play>        # Test mode
│   ├── --filter <pattern>        # Filter test names
│   └── --timeout <seconds>       # Max execution time
│
├── read-file <path>              # Read file from project
├── write-file <path>             # Write file to project (stdin or --content)
├── patch-file <path>             # Apply unified diff patch
│
└── list-projects                 # List discovered Unity projects
```

### 6.3 Global Flags

```
--project <path>        # Explicit Unity project path (skip discovery)
--port <number>         # Explicit bridge port (skip lock file)
--json                  # Force JSON output
--timeout <seconds>     # Command timeout (default: 30)
--verbose               # Debug-level logging
--quiet                 # Suppress non-essential output
```

### 6.4 Output Modes

Every command supports two output modes:

- **Human mode** (default): Formatted, colored terminal output
- **JSON mode** (`--json`): Machine-readable structured output, one JSON object per line

JSON output always follows the shape:
```json
{
  "success": true,
  "data": { ... }
}
```

or on error:
```json
{
  "success": false,
  "error": {
    "code": "BRIDGE_UNREACHABLE",
    "message": "Could not connect to bridge on port 21342"
  }
}
```

### 6.5 Discovery Logic

```
1. If --project flag given → use that path
2. Else search upward from CWD for a directory containing:
   - ProjectSettings/ProjectSettings.asset
   - Assets/
3. If found → check for .ucp/bridge.lock
4. If lock file exists → validate PID is alive
5. If valid → connect to bridge on recorded port
6. If invalid → clean stale lock, report bridge not running
```

### 6.6 Retry & Timeout Strategy

- Default timeout per command: **30 seconds**
- Connection retries: **3 attempts**, 1s backoff
- Compilation waits: poll-based with **60 second** max
- Log streaming: indefinite until Ctrl+C or `--count` reached
- All timeouts overridable via `--timeout` flag

---

## 7. Unity Bridge Design (Detailed)

### 7.1 Initialization

```csharp
[InitializeOnLoad]
public static class BridgeBootstrap
{
    static BridgeBootstrap()
    {
        BridgeServer.Instance.Start();
    }
}
```

- Bridge starts automatically when Unity loads the editor.
- Writes lock file on successful server start.
- Cleans lock file on domain unload / editor quit.
- Registers `EditorApplication.quitting` callback for cleanup.

### 7.2 Server Architecture

```
BridgeServer
├── WebSocket listener (System.Net.HttpListener + WebSocket upgrade)
├── ConnectionManager (track connected clients, max 4)
├── LockFileManager (write/read/cleanup)
└── CommandRouter
    ├── Method registry (Dictionary<string, Func<JObject, Task<JObject>>>)
    ├── JSON-RPC parsing
    └── Error wrapping
```

**Threading model:**
- WebSocket listener runs on a background thread.
- All Unity API calls are marshalled to the main thread via `EditorApplication.update`.
- Command handlers return `Task<JObject>` — router handles main-thread dispatch.

### 7.3 Controller Contracts

Each controller implements a handler registration pattern:

```csharp
public interface ICommandHandler
{
    void RegisterCommands(CommandRouter router);
}
```

Controllers register their methods at startup:
```csharp
public class PlayModeController : ICommandHandler
{
    public void RegisterCommands(CommandRouter router)
    {
        router.Register("play", HandlePlay);
        router.Register("stop", HandleStop);
        router.Register("pause", HandlePause);
    }
}
```

### 7.4 Main-Thread Marshalling

Unity Editor API is not thread-safe. Bridge uses a main-thread dispatcher:

```csharp
// Queue action to main thread, return result via TaskCompletionSource
public Task<T> RunOnMainThread<T>(Func<T> action);
```

Backed by `EditorApplication.update` pump.

### 7.5 Screenshot Implementation

```csharp
// Game View capture
var renderTexture = new RenderTexture(width, height, 24);
Camera.main.targetTexture = renderTexture;
Camera.main.Render();

var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
RenderTexture.active = renderTexture;
texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
texture.Apply();

byte[] png = texture.EncodeToPNG();
string base64 = Convert.ToBase64String(png);

// Cleanup
Camera.main.targetTexture = null;
RenderTexture.active = null;
Object.DestroyImmediate(renderTexture);
Object.DestroyImmediate(texture);
```

### 7.6 Log Streaming

```csharp
Application.logMessageReceived += OnLogMessage;

void OnLogMessage(string message, string stackTrace, LogType type)
{
    var notification = new JsonRpcNotification
    {
        Method = "log",
        Params = new {
            level = type.ToString().ToLower(),
            message = message,
            stackTrace = stackTrace,
            timestamp = DateTime.UtcNow.ToString("o")
        }
    };
    connectionManager.BroadcastToSubscribers(notification);
}
```

### 7.7 Test Runner Integration

```csharp
var testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
var filter = new Filter { testMode = TestMode.EditMode };

testRunnerApi.Execute(new ExecutionSettings(filter));

// Results collected via ICallbacks interface
public class TestCallbacks : ICallbacks
{
    public void RunFinished(ITestResultAdaptor result) { ... }
    public void TestFinished(ITestResultAdaptor result) { ... }
}
```

### 7.8 File Operations Security

All file operations are **sandboxed** to the Unity project directory:

```csharp
private bool IsPathSafe(string requestedPath)
{
    string fullPath = Path.GetFullPath(requestedPath);
    string projectRoot = Path.GetFullPath(Application.dataPath + "/..");
    return fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase);
}
```

Rejects:
- Paths outside project root
- Path traversal (`../../../etc/passwd`)
- Symbolic links pointing outside project

---

## 8. Security Model

| Concern               | Mitigation                                        |
|-----------------------|---------------------------------------------------|
| Network exposure      | Bind to `127.0.0.1` only, never `0.0.0.0`         |
| Unauthorized access   | Per-session token in lock file (readable only by local user) |
| Path traversal        | All file ops sandboxed to project root             |
| Code injection        | No `eval`, no dynamic code execution               |
| Resource exhaustion   | Max 4 concurrent connections, request size limits   |
| Stale sessions        | PID liveness check, lock file cleanup              |

---

## 9. Implementation Phases

### Phase 0 — Foundation (Week 1)

**Goal:** Repository scaffolding, protocol definitions, build infrastructure.

| Task                                  | Component  | Priority |
|---------------------------------------|------------|----------|
| Initialize Rust project with clap     | CLI        | P0       |
| Create UPM package structure          | Bridge     | P0       |
| Define protocol-version.json          | Protocol   | P0       |
| Define initial command schemas        | Protocol   | P0       |
| Set up CI workflows (Rust build)      | Infra      | P1       |
| Create README skeleton                | Docs       | P1       |

**Exit criteria:** `cargo build` succeeds, UPM package.json valid, schemas parse.

---

### Phase 1 — Minimal Bridge (Week 2)

**Goal:** WebSocket server running inside Unity, accepting connections, routing commands.

| Task                                      | File(s)                          |
|-------------------------------------------|----------------------------------|
| Implement WebSocket server                | BridgeServer.cs                  |
| Implement connection manager              | ConnectionManager.cs             |
| Implement lock file write/cleanup         | LockFileManager.cs               |
| Implement JSON-RPC message parser         | MessageParser.cs, MessageTypes.cs |
| Implement command router                  | CommandRouter.cs                 |
| Implement handshake handler               | BridgeServer.cs                  |
| Implement `play` / `stop` / `pause`       | PlayModeController.cs            |
| Main-thread marshalling                   | BridgeServer.cs                  |

**Exit criteria:** Unity loads, bridge starts, WebSocket client can connect, `play`/`stop` work via raw WebSocket.

---

### Phase 2 — Minimal CLI (Week 3)

**Goal:** CLI connects to bridge, sends commands, displays results.

| Task                                      | File(s)                    |
|-------------------------------------------|----------------------------|
| CLI argument parsing skeleton             | main.rs                    |
| WebSocket RPC client                      | client.rs                  |
| Discovery logic (project + lock file)     | discovery.rs               |
| Output formatting (human + JSON)          | output.rs                  |
| Error types                               | error.rs                   |
| `ucp connect` command                     | commands/connect.rs        |
| `ucp play` / `ucp stop` commands          | commands/play.rs           |
| Timeout + retry                           | retry.rs                   |

**Exit criteria:** `ucp connect` reports bridge status, `ucp play` enters play mode, `ucp stop` exits. Both human and JSON output work.

---

### Phase 3 — Core Editor Control (Week 4–5)

**Goal:** Compilation, scene control, object inspection, snapshots.

| Task                                      | CLI File            | Bridge File                |
|-------------------------------------------|---------------------|----------------------------|
| Trigger recompilation                     | commands/compile.rs | CompilationController.cs   |
| Asset database refresh                    | commands/compile.rs | CompilationController.cs   |
| Scene list / load / active                | commands/scene.rs   | SceneController.cs         |
| List GameObjects                          | commands/snapshot.rs| GameObjectInspector.cs     |
| Get components                            | commands/snapshot.rs| ComponentInspector.cs      |
| Get transforms                            | commands/snapshot.rs| TransformInspector.cs      |
| Full state snapshot                       | commands/snapshot.rs| SnapshotAPI.cs             |

**Exit criteria:** Agent can compile, switch scenes, inspect full object hierarchy, get structured snapshots.

---

### Phase 4 — Visual & Logging (Week 5–6)

**Goal:** Screenshot capture, log streaming.

| Task                                  | CLI File               | Bridge File        |
|---------------------------------------|------------------------|--------------------|
| Game view screenshot                  | commands/screenshot.rs | ScreenshotAPI.cs   |
| Scene view screenshot                 | commands/screenshot.rs | ScreenshotAPI.cs   |
| Save screenshot to file              | commands/screenshot.rs | —                  |
| Log capture hook                      | commands/logs.rs       | LogStreamer.cs     |
| Log streaming (subscribe/push)        | commands/logs.rs       | LogStreamer.cs     |
| Log level filtering                   | commands/logs.rs       | LogStreamer.cs     |

**Exit criteria:** `ucp screenshot --output frame.png` saves image, `ucp logs` streams logs in real time.

---

### Phase 5 — Testing & File Operations (Week 6–7)

**Goal:** Run tests, read/write/patch files.

| Task                                  | CLI File             | Bridge File        |
|---------------------------------------|----------------------|--------------------|
| Run edit-mode tests                   | commands/tests.rs    | TestRunnerAPI.cs   |
| Run play-mode tests                   | commands/tests.rs    | TestRunnerAPI.cs   |
| Structured test results               | commands/tests.rs    | TestRunnerAPI.cs   |
| Read file                             | commands/files.rs    | FileController.cs  |
| Write file                            | commands/files.rs    | FileController.cs  |
| Patch file (unified diff)             | commands/files.rs    | FileController.cs  |
| Path sandboxing security              | —                    | FileController.cs  |

**Exit criteria:** `ucp run-tests --mode edit --json` returns structured results. File operations work within project sandbox.

---

### Phase 6 — Installation & Distribution (Week 7–8)

**Goal:** Seamless install experience, health checks.

| Task                                  | Component          |
|---------------------------------------|--------------------|
| `ucp init` (create config)            | CLI                |
| `ucp install` (modify manifest.json)  | CLI                |
| `ucp uninstall` (remove from manifest)| CLI                |
| `ucp update` (bump version)           | CLI                |
| `ucp doctor` (compatibility check)    | CLI + Bridge       |
| Cross-compile macOS + Windows binaries | CI                 |
| UPM package tagging                   | CI                 |
| Release workflow                      | CI                 |

**Exit criteria:** New user can `ucp install` in a Unity project and immediately use `ucp play`.

---

### Phase 7 — Agent Loop & Polish (Week 8–10)

**Goal:** End-to-end agent workflow, documentation.

| Task                                  | Component          |
|---------------------------------------|--------------------|
| Agent loop orchestration example      | CLI / Examples     |
| Getting started guide                 | Docs               |
| CLI reference docs                    | Docs               |
| Protocol reference docs              | Docs               |
| Agent integration guide               | Docs               |
| Performance optimization (snapshots)  | Bridge             |
| Edge case hardening                   | Both               |

**Exit criteria:** An AI agent can autonomously run a patch → compile → test → screenshot loop.

---

## 10. Technical Decisions & Rationale

| Decision                          | Choice                    | Rationale                                                |
|-----------------------------------|---------------------------|----------------------------------------------------------|
| CLI language                      | Rust                      | Static binaries, cross-platform, performance             |
| Transport                         | WebSocket                 | Bidirectional, persistent connection, log streaming       |
| Message format                    | JSON-RPC 2.0              | Standard, tooling support, request/response + notifications |
| Unity JSON library                | JsonUtility + Newtonsoft   | Newtonsoft ships with Unity, handles complex types        |
| Bridge threading                  | Background WS + main-thread dispatch | Unity API requires main thread        |
| Auth                              | Per-session token in lock  | Simple, local-only, no network auth overhead             |
| File patching                     | Unified diff format        | Standard, diffable, mergeable                            |
| Async runtime (Rust)              | Tokio                     | Industry standard for async Rust                         |
| CLI parsing                       | clap (derive)             | Type-safe, auto-generated help, widely used              |
| Error handling (Rust)             | thiserror + anyhow        | Typed errors for library, anyhow for application         |
| Port discovery                    | Lock file                 | No polling, no broadcast, deterministic                  |

---

## 11. Testing Strategy

### CLI Tests

| Layer           | Tool                    | Scope                                       |
|-----------------|-------------------------|---------------------------------------------|
| Unit tests      | `cargo test`            | Discovery logic, output formatting, config parsing |
| Integration     | Mock WebSocket server   | Client ↔ server communication               |
| E2E             | Example Unity project   | Full loop: CLI → Bridge → Unity → CLI       |

### Bridge Tests

| Layer           | Tool                    | Scope                                       |
|-----------------|-------------------------|---------------------------------------------|
| Unit tests      | Unity Test Runner (Edit) | Message parsing, routing, sandboxing        |
| Integration     | Unity Test Runner (Edit) | Controller logic with mocked editor APIs    |
| E2E             | Unity Test Runner (Play) | Full command execution in play mode         |

### Protocol Tests

| Layer           | Tool                    | Scope                                       |
|-----------------|-------------------------|---------------------------------------------|
| Schema validation | JSON Schema validator  | All schemas are valid JSON Schema            |
| Compatibility   | Custom script            | CLI + Bridge agree on all schemas           |

---

## 12. CI / CD Pipeline

### CI Triggers

- Push to `main`
- Pull requests

### CI Jobs

```yaml
cli-check:
  - cargo fmt --check
  - cargo clippy -- -D warnings
  - cargo test
  - cargo build --release (matrix: windows, macos, linux)

bridge-check:
  - Validate package.json
  - C# syntax validation (optional: Roslyn analyzer)

protocol-check:
  - JSON schema validation
  - Command registry consistency check

release:
  - Tag-triggered
  - Build release binaries (matrix)
  - Create GitHub release with artifacts
  - Tag Unity package for UPM
```

### Release Artifacts

| Artifact              | Format            |
|-----------------------|-------------------|
| CLI (macOS arm64)     | `ucp-darwin-arm64` |
| CLI (macOS x64)       | `ucp-darwin-x64`   |
| CLI (Windows x64)     | `ucp-windows-x64.exe` |
| CLI (Linux x64)       | `ucp-linux-x64`    |
| Unity Package         | Git tag on `unity-package/` subtree |

---

## 13. Performance Budgets

| Operation            | Target Latency     | Notes                                   |
|----------------------|--------------------|-----------------------------------------|
| Handshake            | < 50ms             | Local WS connection                     |
| Play/Stop            | < 100ms            | Bridge → EditorApplication.isPlaying    |
| Compile trigger      | < 100ms (trigger)  | Compilation itself is async             |
| Snapshot (100 objects)| < 200ms           | Hierarchy traversal + serialization     |
| Snapshot (1000 objects)| < 1s             | May need pagination for larger scenes   |
| Screenshot (1080p)   | < 500ms            | Render + encode + base64                |
| Log delivery         | < 50ms             | From Unity log event to CLI output      |
| File read (1MB)      | < 100ms            | Read + transmit                         |

---

## 14. Configuration

### CLI Config (`~/.ucp/config.json`)

```json
{
  "defaultTimeout": 30,
  "outputFormat": "human",
  "defaultPort": 21342,
  "verbose": false
}
```

### Per-Project Config (`<project>/.ucp/config.json`)

```json
{
  "port": 21342,
  "autoStart": true,
  "logLevel": "info",
  "maxConnections": 4,
  "screenshotDefaults": {
    "width": 1920,
    "height": 1080,
    "view": "game"
  }
}
```

---

## 15. Future Extensions (Post-MVP)

Tracked here for awareness, **not** in initial scope:

| Feature                     | Complexity | Value  |
|-----------------------------|------------|--------|
| Incremental snapshots       | Medium     | High   |
| Delta updates               | Medium     | High   |
| Component property editing  | High       | High   |
| Scene hierarchy modification| High       | Medium |
| Runtime variable inspection | High       | High   |
| Physics debugging           | High       | Medium |
| NavMesh debugging           | Medium     | Medium |
| Performance profiler access | High       | Medium |
| Timeline recording          | High       | Low    |
| Headless mode support       | Medium     | High   |
| Multiple simultaneous projects | Medium  | Medium |
| Plugin/extension system     | High       | Medium |

---

## 16. Open Questions

- [ ] Minimum Unity version to support? (2021 LTS? 2022 LTS?)
- [ ] Should bridge expose an HTTP REST fallback alongside WebSocket?
- [ ] Package namespace: `com.ucp.bridge` vs `com.unity-control.bridge`?
- [ ] Should lock file use `.ucp/` subdirectory or single `.ucp.lock` file?
- [ ] License choice: MIT? Apache 2.0?
- [ ] Should CLI support `--watch` mode for continuous snapshot polling?
- [ ] Homebrew tap for macOS distribution, or just GitHub releases?
- [ ] Should file write/patch trigger automatic asset refresh?

---

## 17. Risk Register

| Risk                                    | Impact | Likelihood | Mitigation                              |
|-----------------------------------------|--------|------------|-----------------------------------------|
| Unity API thread safety issues          | High   | High       | Strict main-thread marshalling          |
| WebSocket library instability in Unity  | High   | Medium     | Fallback to raw TCP + custom framing    |
| Unity version API breaking changes      | Medium | Medium     | Conditional compilation, version checks |
| Large scene snapshot performance        | Medium | High       | Pagination, depth limits, filters       |
| Screenshot fails in batch mode          | Medium | Medium     | Detect headless, return clear error     |
| Play mode state corruption              | High   | Low        | Read-only inspection by default         |

---

## 18. Glossary

| Term       | Definition                                                    |
|------------|---------------------------------------------------------------|
| Bridge     | The Unity Editor package that runs the in-process server      |
| CLI        | The external Rust binary that communicates with the bridge    |
| Lock file  | File written by bridge advertising its connection details     |
| Snapshot   | Structured JSON representation of current editor/game state   |
| UPM        | Unity Package Manager                                        |
| JSON-RPC   | JSON-based remote procedure call protocol (version 2.0)       |
| UCP        | Unity Control Protocol (this project)                         |
