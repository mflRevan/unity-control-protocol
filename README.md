<p align="center">
  <img src="assets/branding/ucp-icon.svg" alt="UCP logo" width="80" />
</p>

<h1 align="center">Unity Control Protocol</h1>

<p align="center">
  <strong>The complete command-line interface for the Unity Editor.</strong><br>
  Control every part of the editor lifecycle from your terminal — scenes, objects,<br>assets, builds, tests, profiling — all through one CLI.
</p>

<p align="center">
  <a href="https://www.npmjs.com/package/@mflrevan/ucp"><img src="https://img.shields.io/npm/v/@mflrevan/ucp?style=flat&color=7c3aed&label=npm" alt="npm version" /></a>&nbsp;
  <a href="https://github.com/mflRevan/unity-control-protocol/releases"><img src="https://img.shields.io/github/v/release/mflRevan/unity-control-protocol?style=flat&color=7c3aed&label=release" alt="GitHub release" /></a>&nbsp;
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-7c3aed?style=flat" alt="MIT license" /></a>&nbsp;
  <a href="https://discord.gg/F4RjhdVTbz"><img src="https://img.shields.io/badge/discord-join-5865F2?style=flat&logo=discord&logoColor=white" alt="Discord" /></a>
</p>

<p align="center">
  <a href="https://unityctl.dev/docs">Documentation</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://github.com/mflRevan/unity-control-protocol/releases">Releases</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://discord.gg/F4RjhdVTbz">Discord</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://www.npmjs.com/package/@mflrevan/ucp">npm</a>
</p>

<br>

## Why UCP

Unity has no native CLI. If you want to automate anything — move an asset, run tests, adjust a material, trigger a build — you're stuck clicking through the editor or writing throwaway editor scripts.

UCP fixes that. It's a Rust CLI that talks to a lightweight bridge package running inside the Unity Editor over WebSocket. Every editor operation becomes a terminal command. Every command supports `--json` for machine consumption.

This makes the Unity Editor fully scriptable — for you, for CI/CD, and for AI agents.

<br>

<table>
<tr>
<td width="50%">

**Without UCP**
- Open Unity, wait for it to load
- Click through menus to find assets
- Manually move files, hope references don't break
- Run tests by clicking a button and reading the console
- Build by navigating three dialog boxes
- Repeat for every project, every time

</td>
<td width="50%">

**With UCP**
```bash
ucp connect
ucp asset search -t Prefab -n "Enemy"
ucp asset bulk-move --moves '[...]'
ucp references check Assets/Prefabs
ucp run-tests --mode edit
ucp build start --output "Builds/Game.exe"
```

</td>
</tr>
</table>

<br>

## How it works

```
  Your Terminal                                          Unity Editor
 ┌──────────────────┐     WebSocket / JSON-RPC      ┌──────────────────┐
 │                  │     on localhost with token     │                  │
 │   ucp CLI        │◄──────────────────────────────►│   UCP Bridge     │
 │   (Rust binary)  │                                │   (Editor pkg)   │
 │                  │                                │                  │
 └──────────────────┘                                └──────────────────┘
   ▲                                                   │
   │  AI agents, CI/CD pipelines,                      │  Executes Unity API calls:
   │  scripts, and humans all use                      │  AssetDatabase, SceneManager,
   │  the same CLI interface                           │  EditorBuildSettings, TestRunner,
   │                                                   │  SerializedObject, and more
   └───────────────────────────────────────────────────┘
```

The bridge installs as a standard Unity package. When the editor opens, it starts a local WebSocket server and writes a lockfile. The CLI reads that lockfile, authenticates, and sends commands. No cloud services, no accounts, no editor plugins to configure.

<br>

## What you can control

UCP covers the full Unity Editor lifecycle:

```
 SETUP & LIFECYCLE       SCENE AUTHORING          RUNTIME & TESTING       PROJECT & SHIPPING
 ──────────────────      ──────────────────       ──────────────────      ──────────────────
 doctor                  scene list|load|save     play / stop / pause     build targets|start
 install / uninstall     scene snapshot|focus     compile                 settings player|editor
 open / close            object create|get|set    run-tests               settings set-player|...
 connect                 asset search|move        logs --follow           packages add|remove
 bridge status|update    asset import-settings    screenshot              packages search|info
 editor restart|status   material set-property    profiler session        packages unitypackage
                         prefab create|apply      profiler summary        packages registries
                         files read|write|patch   exec run                vcs
                         references find|check
```

> Every command supports `--json` for structured output. Most support `--project`, `--timeout`, and `--verbose`.

<br>

## Workflows

### 🔄 Massive asset refactor

Rename and reorganize hundreds of assets without breaking a single reference:

```bash
ucp references find --asset "Assets/Materials/Legacy/" --detail summary
ucp asset bulk-move --moves '[
  {"from":"Assets/Materials/Legacy/Metal.mat",   "to":"Assets/Materials/PBR/Metal.mat"},
  {"from":"Assets/Materials/Legacy/Wood.mat",    "to":"Assets/Materials/PBR/Wood.mat"},
  {"from":"Assets/Prefabs/Old/Enemy.prefab",     "to":"Assets/Prefabs/Characters/Enemy.prefab"}
]'
ucp references check Assets/Prefabs Assets/Materials   # verify nothing broke
ucp compile
ucp run-tests --mode edit
```

`asset move` and `bulk-move` go through Unity's `AssetDatabase.MoveAsset` — `.meta` files and GUIDs stay intact, so every scene, prefab, and serialized reference keeps working.

---

### 🛠️ Full feature implementation cycle

Build a feature end-to-end from the terminal — write code, assemble scene objects, test, and capture results:

```bash
# 1. Edit scripts in your IDE, then recompile
ucp compile

# 2. Build up scene hierarchy through the bridge
ucp object create "EnemySpawner"
ucp object add-component --id -15774 --component BoxCollider
ucp object set-property --id -15774 --component BoxCollider --property m_Size --value "[5,2,5]"
ucp prefab create --id -15774 --path "Assets/Prefabs/EnemySpawner.prefab"

# 3. Visual verification
ucp scene focus --id -15774 --axis 0 0 -1
ucp screenshot --view scene --output spawner-preview.png

# 4. Test and validate
ucp run-tests --mode edit --filter "SpawnerTests"
ucp logs --pattern "Exception|Error" --count 50
```

---

### 🚀 CI/CD pipeline

Gate your builds on real editor validation — not just compilation:

```bash
ucp connect                                    # start editor, wait for bridge
ucp compile                                    # ensure scripts compile
ucp run-tests --mode edit --json               # structured test results
ucp build set-defines "CI;RELEASE"             # configure defines
ucp build start --output "Builds/Game.exe"     # build player
ucp close                                      # shut down cleanly
```

---

### 🔍 Performance profiling

Capture profiler data without touching Unity's profiler window:

```bash
ucp profiler session start --mode play
ucp play
# ... let the game run ...
ucp profiler summary --limit 10
ucp profiler hierarchy --frame latest --thread 0 --limit 20
ucp profiler capture save --output session.json
ucp stop
```

<br>

## Quick start

**1. Install the CLI**

```bash
npm install -g @mflrevan/ucp
```

<details>
<summary>Other install methods</summary>

**pnpm**
```bash
pnpm add -g @mflrevan/ucp
pnpm approve-builds
```

**From source**
```bash
git clone https://github.com/mflRevan/unity-control-protocol.git
cd unity-control-protocol/cli
cargo build --release
```

**Binary** — download from [GitHub Releases](https://github.com/mflRevan/unity-control-protocol/releases).

</details>

**2. Install the bridge in your Unity project**

```bash
cd /path/to/YourUnityProject
ucp install
```

This adds `com.ucp.bridge` to `Packages/manifest.json`, pinned to the CLI version.

**3. Connect and go**

```bash
ucp open                           # launch Unity, wait for bridge
ucp scene snapshot --depth 1       # see your scene hierarchy
ucp screenshot --output snap.png   # capture the scene view
```

> **Tip:** Run `ucp doctor` to validate your setup — Unity resolution, bridge health, and project serialization settings.

<br>

## AI agent integration

UCP is built to be driven by AI coding agents. It ships as a [Claude Code](https://docs.anthropic.com/en/docs/claude-code) plugin with a skill file that teaches agents the full command surface and common workflows.

```bash
# Local plugin mode
claude --plugin-dir /path/to/unity-control-protocol

# Marketplace install
/plugin marketplace add mflRevan/unity-control-protocol
```

Every command's `--json` flag gives agents structured, parseable output instead of human-formatted text.

<br>

## Platform support

| Platform | Architecture |
|---|---|
| Windows | x64 |
| macOS | x64, ARM (Apple Silicon) |
| Linux | x64 |

Requires Unity 2021.3 or later. Tested across Unity 6 (`6000.0` – `6000.4`).

<br>

## Repository layout

```
cli/                              Rust CLI — the ucp binary
unity-package/com.ucp.bridge/    Unity Editor bridge package
npm/                              npm distribution wrapper
docs/                             Markdown documentation source
website/                          Docs site (unityctl.dev)
skills/                           AI agent skill files
scripts/                          Build, validation, and release helpers
assets/branding/                  Logo and icon assets
```

<br>

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, testing, and the release workflow.

```bash
cargo test --manifest-path cli/Cargo.toml    # CLI unit tests
cargo check --manifest-path cli/Cargo.toml   # type check
cd website && npm run build                  # docs site build
```

<br>

## License

[MIT](LICENSE.md)
