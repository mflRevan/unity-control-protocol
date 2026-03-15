---
name: unity-control-protocol
description: >-
  Programmatic control of the Unity Editor from the terminal via the `ucp` CLI.
  Automate scenes, GameObjects, components, assets, materials, prefabs, build
  pipelines, settings, tests, and version control over a WebSocket/JSON-RPC 2.0
  bridge. Use when the user asks to inspect, create, modify, or automate anything
  inside a Unity project without opening the Editor UI.
compatibility: Requires the `ucp` CLI (install via npm, cargo, or binary) and the UCP Bridge package installed in the target Unity project. Unity 2021.3+ required.
metadata:
  author: mflRevan
  version: '0.4.0'
---

# Unity Control Protocol (UCP)

UCP is a cross-platform CLI + Unity Editor bridge. The CLI (`ucp`) sends commands over WebSocket to a bridge package running inside the Unity Editor, which executes them via JSON-RPC 2.0 and returns results. Every command supports `--json` for machine-readable output.

## When to use this skill

- The user wants to inspect or modify GameObjects, components, materials, prefabs, or assets
- The user asks to enter/exit play mode, run tests, capture screenshots, or read logs
- The user needs to manage scenes, project settings, build pipelines, or scripting defines
- The user wants to automate Unity Editor workflows from CI/CD, scripts, or agents
- The user mentions Plastic SCM / Unity VCS operations

## When NOT to use this skill

- The user is writing C# game code without needing editor automation
- The user is working in a non-Unity project
- UCP is not installed (`ucp doctor` will confirm)

## Setup & connection

Always verify or bootstrap the bridge before issuing commands.

```bash
ucp doctor                  # Health check
ucp open                    # Launch Unity and wait for the bridge
ucp connect                 # Verify live connection
ucp bridge status           # Inspect bridge ref/version drift
ucp bridge update           # Re-pin the tracked bridge dependency to this CLI version
ucp install                 # Install bridge into current Unity project (manifest dependency by default)
ucp install --dev           # Mount the repo-local bridge package as a local-only embedded package
ucp install --manifest      # Explicit tracked manifest install mode (same as default)
ucp uninstall               # Remove bridge
```

`ucp connect` and other bridge-backed commands now auto-start Unity when the project can be resolved and a Unity executable is available. If auto-start fails, pass `--unity <path>` or set `UCP_UNITY`.

When the project's configured Unity version is not installed, inspect `ucp editor status` and only use `--force-unity-version <version>` if you accept the risk of Unity upgrading project metadata or assets. Back up the project or commit your work first.

If startup prompts block launch, use `--dialog-policy <mode>` to steer UCP's best-effort response to Safe Mode or recovery dialogs.

If `ucp connect` fails after startup, either Unity is still importing/compiling or the bridge is not installed. Run `ucp install` from the project root and retry.

`ucp install` is manifest-first by default and writes a tracked git dependency pinned to the CLI version. Default install does not add a local `file:` dependency. `ucp install --dev` leaves the target project's manifest alone and forces the repo-local bridge source.

Bridge drift handling defaults to `--bridge-update-policy auto`, which means `ucp doctor`, `ucp connect`, and other bridge-backed commands will update stale tracked bridge refs before connecting. Use `--bridge-update-policy warn` when you want notification-only behavior.

Install also enables automation-friendly PlayerSettings defaults by default: `runInBackground = true`, `defaultScreenWidth = 1920`, `defaultScreenHeight = 1080`, and `defaultIsNativeResolution = false`. Those defaults improve unattended capture and control.

Without `--json`, commands use human mode: concise terminal-oriented summaries meant for people and agent review loops. Broad read commands intentionally truncate in human mode so scenes, settings, and log searches do not flood the terminal.

## Global flags

| Flag                            | Purpose                                                                               |
| ------------------------------- | ------------------------------------------------------------------------------------- |
| `--json`                        | Machine-readable JSON output                                                          |
| `--project <path>`              | Target a specific Unity project (defaults to cwd)                                     |
| `--unity <path>`                | Explicit Unity executable for lifecycle commands                                      |
| `--force-unity-version <ver>`   | Force a specific installed Unity editor version                                       |
| `--bridge-update-policy <mode>` | Outdated bridge handling: `auto`, `warn`, `off`                                       |
| `--dialog-policy <mode>`        | Startup dialog handling: `auto`, `manual`, `ignore`, `recover`, `safe-mode`, `cancel` |
| `--timeout <s>`                 | Timeout in seconds (default 30)                                                       |
| `--verbose`                     | Extra diagnostic output                                                               |

## Scene management

```bash
ucp scene list                              # All scenes in the project
ucp scene active                            # Currently loaded scene
ucp scene focus --id 46894 --axis 1 0 0              # Align the Scene view camera to a simple axis for repeatable screenshots
ucp scene focus --id 46894 --axis 0 0 -1             # Negative axes work too
ucp scene load Assets/Scenes/Level1.unity   # Load a scene
```

### Hierarchy snapshot

Run a snapshot before any GameObject command to discover instance IDs.

```bash
ucp scene snapshot                  # Root objects only, with lean metadata
ucp scene snapshot --filter "Player" # Filter by name
ucp scene snapshot --depth 2        # Limit depth
ucp scene snapshot --json           # JSON for parsing
```

Use the `[ID]` numbers from the output in subsequent `object` and `prefab` commands. Treat instance IDs as short-lived editor handles and refresh them with `ucp scene snapshot` after compilation, domain reloads, package refreshes, scene loads, or test runs.

## Editor control

```bash
ucp editor status      # Show editor runtime state and resolved Unity path
ucp editor logs        # Tail the stored Unity editor log
ucp editor ps          # List discovered Unity editor processes
ucp play              # Enter play mode
ucp stop              # Exit play mode
ucp pause             # Toggle pause
ucp compile           # Trigger script recompilation (blocks until done)
ucp compile --no-wait # Fire and forget
```

Prefer normal project-local file edits when the agent already has workspace access. Use `ucp compile` to import those changes. Use `ucp files ...` when you intentionally want the bridge to perform sandboxed project file I/O.

Use `ucp scene focus` plus `ucp screenshot --view scene` for spatial iteration loops, then apply `object set-property`, `material set-property`, or `settings set-lighting` changes and capture again.

## GameObjects & components

All commands require `--id <instanceID>` from `ucp scene snapshot`.

```bash
# Inspect
ucp object get-fields --id 46894 --component Transform
ucp object get-property --id 46894 --component Camera --property m_Depth

# Modify (values are JSON)
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true

# Enable/disable
ucp object set-active --id 46894 --active false

# Rename
ucp object set-name --id 46894 --name "Main Camera"

# Create & delete
ucp object create "MyObject"
ucp object create "Child" --parent 46894
ucp object delete --id -15774

# Reparent
ucp object reparent --id -15774 --parent 46894
ucp object reparent --id -15774 --parent 46894 --sibling-index 0

# Instantiate prefab
ucp object instantiate "Assets/Prefabs/Enemy.prefab" --name "Enemy1"

# Components
ucp object add-component --id -15774 --component Rigidbody
ucp object remove-component --id -15774 --component BoxCollider
```

Newly created objects get negative instance IDs. All modifications register with Unity's Undo system.

`ucp object get-fields` in human mode intentionally prints only a bounded field list. Use `ucp object get-property` or `--json` when you need deeper inspection.

## Assets

```bash
ucp asset search -t Material                          # By type
ucp asset search -n "Player" -p "Assets/Prefabs"      # By name in folder
ucp asset search -t Texture2D --max 10                 # Limit results

ucp asset info "Assets/Materials/Agent.mat"            # Metadata
ucp asset read "Assets/Materials/Agent.mat"            # Full dump
ucp asset read "Assets/Materials/Agent.mat" --field m_Shader  # Specific field

ucp asset write "Assets/Configs/GameConfig.asset" --field maxPlayers --value "8"
ucp asset write "Assets/Configs/GameConfig.asset" --field icon --value '{"path":"Assets/UI/GameIcon.png"}'
ucp asset write-batch "Assets/Configs/GameConfig.asset" --values '{"maxPlayers":8,"spawnDelay":1.5}'
ucp asset create-so -t GameConfig "Assets/Configs/NewConfig.asset"
```

Object reference writes accept `instanceId`, asset `path`, or asset `guid`. Invalid references now fail explicitly instead of silently succeeding.

## Materials

Use asset path (not instance ID).

```bash
ucp material get-properties --path "Assets/Materials/Agent.mat"
ucp material get-property --path "Assets/Materials/Agent.mat" --property _BaseColor
ucp material set-property --path "Assets/Materials/Agent.mat" --property _Metallic --value "0.5"
ucp material set-property --path "Assets/Materials/Agent.mat" --property _BaseColor --value "[1,0,0,1]"

ucp material keywords --path "Assets/Materials/Agent.mat"
ucp material set-keyword --path "Assets/Materials/Agent.mat" --keyword _EMISSION --enabled true
ucp material set-shader --path "Assets/Materials/Agent.mat" --shader "Standard"
```

Common properties: `_BaseColor`, `_Color`, `_Metallic`, `_Smoothness`, `_BumpMap`, `_EmissionColor`, `_Cutoff`.

## Prefabs

```bash
ucp prefab status --id -136722                 # Check if prefab instance
ucp prefab overrides --id -136722              # View overrides
ucp prefab apply --id -136722                  # Apply overrides to asset
ucp prefab revert --id -136722                 # Revert to source
ucp prefab unpack --id -136722                 # Unpack one level
ucp prefab unpack --id -136722 --completely true  # Full recursive unpack
ucp prefab create --id -136722 --path "Assets/Prefabs/New.prefab"
```

## Project settings

```bash
ucp settings player                            # Read PlayerSettings
ucp settings set-player --key companyName --value '"Studio"'
ucp settings set-player --key productName --value '"Game"'
ucp settings set-player --key bundleVersion --value '"1.0"'
ucp settings set-player --key runInBackground --value true
ucp settings set-player --key defaultScreenWidth --value 1920
ucp settings set-player --key defaultScreenHeight --value 1080
ucp settings set-player --key defaultIsNativeResolution --value false

ucp settings quality                           # Read QualitySettings
ucp settings set-quality --key vSyncCount --value 0

ucp settings physics                           # Read Physics settings
ucp settings set-physics --key gravity --value "[0,-9.81,0]"

ucp settings lighting                          # Active scene lighting
ucp settings set-lighting --key ambientIntensity --value 1.2
ucp settings set-lighting --key fog --value true

ucp settings tags-layers                       # List tags and layers
ucp settings add-tag "Interactable"
ucp settings add-layer "VFX"
```

## Build pipeline

```bash
ucp build targets                              # Available targets
ucp build active-target                        # Current target
ucp build set-target Android                   # Switch (triggers reimport)

ucp build scenes                               # Build Settings scene list
ucp build set-scenes "Assets/Scenes/Boot.unity,Assets/Scenes/Game.unity"

ucp build defines                              # Scripting defines
ucp build set-defines "RELEASE;ENABLE_ANALYTICS"

ucp build start --output "Builds/Game.exe"    # Trigger build (blocks until done)
```

## Media & logs

```bash
ucp screenshot                                 # Capture game view
ucp screenshot --view scene                    # Capture scene view
ucp screenshot --output capture.png            # Save to file

ucp logs --follow                              # Stream all new logs
ucp logs --follow --level error               # Stream only new errors
ucp logs --count 10                           # Read the latest buffered logs
ucp logs --pattern "NullReference|Exception" --count 100
ucp logs --pattern "failed" --before-id 200 --after-id 100
ucp logs --id 42                              # Inspect one buffered log entry in full
```

Buffered searches filter first and then apply `--count`, so older matching entries are no longer missed just because newer non-matching noise filled the recent window.

## Testing

```bash
ucp run-tests --mode edit                      # Edit mode tests
ucp run-tests --mode play                      # Play mode tests
ucp run-tests --mode edit --filter "MyTest"    # Filter using a Unity Test Runner name or fully qualified name
ucp run-tests --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_ReturnsRequestedBufferedCount"
```

`--filter` uses Unity Test Runner semantics rather than a UCP-defined regex engine. Prefer fully qualified test names when you need precise selection.

## Lifecycle guidance for agents

- Prefer `ucp open` when you need an explicit editor bootstrap step before a larger workflow.
- Use `ucp connect` when you want UCP to both start Unity if needed and verify that the bridge handshake is live.
- Use `ucp doctor` first when you suspect the project is pinned to an outdated bridge package.
- Use `ucp close` when you need a clean editor shutdown after automation.

## Editor scripting

Define `IUCPScript` classes in the Unity project, then run them remotely.

```bash
ucp exec list                                 # List available scripts
ucp exec run SetupScene                        # Run a script
ucp exec run CreatePrefabs --params '{"count": 10}'  # With parameters
```

## Version control (Plastic SCM / Unity VCS)

```bash
ucp vcs status
ucp vcs diff
ucp vcs commit --message "Add player animations"
ucp vcs checkout Assets/Scenes/Level1.unity
ucp vcs history
ucp vcs lock Assets/Art/Hero.fbx
ucp vcs unlock Assets/Art/Hero.fbx
ucp vcs branch list
ucp vcs branch create --name "feature/new-ui"
ucp vcs branch switch --name "main"
```

## Common workflows

### Inspect and tweak a GameObject

```bash
ucp scene snapshot --filter "Player"
ucp object get-fields --id 46900 --component Rigidbody
ucp object set-property --id 46900 --component Rigidbody --property m_Mass --value "2.5"
```

### Iterate on a material

```bash
ucp material get-properties --path "Assets/Materials/Hero.mat"
ucp material set-property --path "Assets/Materials/Hero.mat" --property _Metallic --value "0.8"
ucp screenshot --output after.png
```

### CI build

```bash
ucp connect || exit 1
ucp run-tests --mode edit
ucp build set-defines "CI;RELEASE"
ucp build start --output "Builds/Game.exe"
```

### Quick scene audit

```bash
ucp scene snapshot --json > hierarchy.json
ucp logs --level error
ucp screenshot
```
