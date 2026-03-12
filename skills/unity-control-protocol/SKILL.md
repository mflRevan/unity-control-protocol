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
  version: '0.2.3'
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

Always verify the bridge before issuing commands.

```bash
ucp doctor                  # Health check
ucp connect                 # Verify live connection
ucp install                 # Install bridge into current Unity project (local-only by default)
ucp install --dev           # Mount the repo-local bridge package as a local-only embedded package
ucp install --manifest      # Force a tracked manifest dependency install
ucp uninstall               # Remove bridge
```

If `ucp connect` fails, either Unity is not open or the bridge is not installed. Run `ucp install` from the project root and reopen Unity.

`ucp install` prefers a local embedded bridge mount when the CLI can resolve a local bridge payload. `ucp install --dev` leaves the target project's manifest alone and forces the repo-local bridge source. `ucp install --manifest` opts back into a tracked dependency.

Without `--json`, commands use human mode: concise terminal-oriented summaries meant for people and agent review loops. Broad read commands intentionally truncate in human mode so scenes, settings, and log searches do not flood the terminal.

## Global flags

| Flag               | Purpose                                           |
| ------------------ | ------------------------------------------------- |
| `--json`           | Machine-readable JSON output                      |
| `--project <path>` | Target a specific Unity project (defaults to cwd) |
| `--timeout <s>`    | Timeout in seconds (default 30)                   |
| `--verbose`        | Extra diagnostic output                           |

## Scene management

```bash
ucp scene list                              # All scenes in the project
ucp scene active                            # Currently loaded scene
ucp scene load Assets/Scenes/Level1.unity   # Load a scene
```

### Hierarchy snapshot

Run a snapshot before any GameObject command to discover instance IDs.

```bash
ucp snapshot                        # Root objects only, with lean metadata
ucp snapshot --filter "Player"      # Filter by name
ucp snapshot --depth 2              # Limit depth
ucp snapshot --json                 # JSON for parsing
```

Use the `[ID]` numbers from the output in subsequent `object` and `prefab` commands. Treat instance IDs as short-lived editor handles and refresh them with `ucp snapshot` after compilation, domain reloads, package refreshes, scene loads, or test runs.

## Editor control

```bash
ucp play              # Enter play mode
ucp stop              # Exit play mode
ucp pause             # Toggle pause
ucp compile           # Trigger script recompilation (blocks until done)
ucp compile --no-wait # Fire and forget
```

## File operations

```bash
ucp read-file Assets/Scripts/Player.cs
ucp write-file Assets/Scripts/Config.cs --content "public class Config {}"
ucp patch-file Assets/Scripts/Player.cs --find "maxHealth = 100" --replace "maxHealth = 200"
```

## GameObjects & components

All commands require `--id <instanceID>` from `ucp snapshot`.

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
ucp asset create-so -t GameConfig "Assets/Configs/NewConfig.asset"
```

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

Bulk history reads are intentionally capped to `10` returned entries in human mode. Use the returned IDs with `ucp logs --id <logId>` or narrow the search space further.

## Testing

```bash
ucp run-tests --mode edit                      # Edit mode tests
ucp run-tests --mode play                      # Play mode tests
ucp run-tests --mode edit --filter "MyTest"    # Filter using a Unity Test Runner name or fully qualified name
ucp run-tests --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_TruncatesBulkResultsToTenEntries"
```

`--filter` uses Unity Test Runner semantics rather than a UCP-defined regex engine. Prefer fully qualified test names when you need precise selection.

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
ucp snapshot --filter "Player"
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
ucp snapshot --json > hierarchy.json
ucp logs --level error
ucp screenshot
```
