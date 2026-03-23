---
name: unity-control-protocol
description: >-
  Programmatic control of the Unity Editor from the terminal via the `ucp` CLI.
  Automate scenes, GameObjects, components, assets, materials, prefabs, build
  pipelines, settings, tests, packages, selective `.unitypackage` import, debugging and profiling 
  over a WebSocket/JSON-RPC 2.0
  bridge. Use when the user asks to inspect, create, modify, or automate anything
  inside a Unity project without opening the Editor UI.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI (install via npm, cargo, or binary) and the UCP Bridge package installed in the target Unity project. Unity 2021.3+ required.
metadata:
  author: mflRevan
  version: '0.4.3'
  clawdbot:
    emoji: '🎮'
---

# Unity Control Protocol (UCP)

UCP is a cross-platform CLI + Unity Editor bridge. The CLI (`ucp`) sends commands over WebSocket to a bridge package running inside the Unity Editor, which executes them via JSON-RPC 2.0 and returns results. Every command supports `--json` for machine-readable output.

UCP and Unity expose a broad command surface. If you are unsure what is available in any area, prefer `ucp --help` and `ucp <command> --help` first to discover the current control surface before guessing.

## When to use this skill

- The user wants to inspect or modify GameObjects, components, materials, prefabs, or assets
- The user asks to enter/exit play mode, run tests, capture screenshots, or read logs
- The user needs to manage scenes, project settings, build pipelines, or scripting defines
- The user needs to browse/install Unity packages, manage scoped registries, or selectively import `.unitypackage` content
- The user wants to automate Unity Editor workflows from CI/CD, scripts, or agents
- The user mentions Plastic SCM / Unity VCS operations and needs a bridge-backed fallback rather than the native `cm` CLI

## When NOT to use this skill

- The user is writing C# game code without needing editor automation
- The user is working in a non-Unity project
- UCP is not installed (`ucp doctor` will confirm)

## Setup & connection

Start here whenever bridge state is unknown:

```bash
ucp doctor
ucp open
ucp connect
ucp install
ucp bridge status
```

Use `ucp <command> --help` for flags such as `--project`, `--json`, `--unity`, `--timeout`, `--dialog-policy`, and bridge update controls.

`ucp connect` and other bridge-backed commands can auto-start Unity when the project and editor path are resolvable. If startup stalls on dialogs, use `--dialog-policy ...`. If you need machine-readable output, prefer `--json`.

## Core agent guidance

- Prefer direct workspace edits plus `ucp compile` when you already have normal filesystem access.
- Use `ucp files ...` as a sandboxed fallback for bridge-mediated project file I/O.
- Run `ucp scene snapshot` before object or prefab work to discover instance IDs.
- Treat instance IDs as short-lived handles; refresh them after compilation, reloads, package changes, scene loads, or test runs.
- For imported assets such as FBX, textures, and audio, prefer `ucp asset import-settings ...` over raw `.meta` file edits.
- `ucp files write` and `ucp files patch` automatically reimport edited Unity assets and `.meta` files under `Assets/` and `Packages/` unless you pass `--no-reimport`.
- Prefer `ucp packages add|remove` for normal Package Manager installs and `ucp packages dependency ...` for explicit manifest-driven local `file:` references.
- `ucp packages unitypackage inspect|import` gives a deterministic, agent-friendly path for Asset Store-style archives, including selective folder/asset import.
- `ucp play` can fail when Unity is blocked by compile errors; fix the errors first, then retry.
- Prefer `cm` for normal Unity Version Control work; use `ucp vcs` only as a lightweight fallback.
- Adding a brand-new scoped registry can trigger Unity's own Package Manager security popup (blocking)

## External Endpoints

This ClawHub bundle ships documentation only. It does not include helper scripts and it does not make any automatic network calls by itself. When an agent follows this skill, the surrounding `ucp` toolchain can reach the following endpoints as part of explicit user-requested workflows:

| Endpoint                                                                 | Used by                                                      | Data sent                                                                                                                                   |
| ------------------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `ws://127.0.0.1:<dynamic-port>`                                          | `ucp` CLI talking to the local Unity bridge                  | JSON-RPC requests and responses describing the target Unity project's scenes, objects, assets, settings, tests, builds, and profiler state. |
| `https://github.com/mflRevan/unity-control-protocol.git`                 | `ucp install` tracked bridge dependency installs and updates | Normal git package fetch metadata for the bridge package version requested by the operator.                                                 |
| `https://packages.unity.com` and any user-configured scoped registry URL | `ucp packages ...` workflows                                 | Package names, versions, registry URLs, and standard Unity Package Manager traffic needed for explicit package search/install/update work.  |

## Security & Privacy

- This skill bundle contains only markdown documentation. It does not ship shell scripts, binaries, auto-downloaders, or persistence hooks.
- The primary runtime path is the local `ucp` CLI talking to a Unity bridge over localhost WebSocket; that traffic stays on the machine unless the operator explicitly invokes package or install commands that contact remote registries.
- When the skill is used, an agent may inspect or mutate the local Unity project through `ucp`, which means Unity scene names, asset paths, component data, logs, test results, and build settings can move between the local editor and the local CLI process.
- Remote traffic is workflow-dependent and opt-in: package installs, bridge dependency installs, and user-chosen registries can receive package names, versions, and repository refs needed to fulfill the requested action.
- If the project contains secrets or sensitive assets, prefer `--json` plus narrow commands, review intended commands before execution, and avoid registry/install operations you do not trust.

## Model Invocation Note

Autonomous invocation of this skill is expected in OpenClaw/ClawHub-style environments when the user asks for Unity Editor automation. If the user wants filesystem-only edits or does not want bridge-backed automation, they should explicitly say not to use UCP and the agent should stay within normal workspace tools.

## Trust Statement

By using this skill, you are trusting the `ucp` CLI, the local Unity project it operates on, and any package registries or git remotes you intentionally invoke through UCP. Only install or enable this skill if you trust this repository, the local machine context, and the remote package sources involved in your workflow.

## Scene & editor basics

If unsure, inspect the full surface with `ucp scene --help` and `ucp editor --help`.

```bash
ucp scene snapshot --filter "Player"
ucp scene load Assets/Scenes/Level1.unity
ucp scene focus --id 46894 --axis 1 0 0

ucp editor status
ucp play
ucp compile
```

## Objects, assets, materials, and prefabs

Use `ucp object --help`, `ucp asset --help`, `ucp material --help`, and `ucp prefab --help` for the full command set.

```bash
ucp object get-fields --id 46894 --component Transform
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true

ucp asset search -t Material --max 10
ucp asset import-settings write "Assets/Textures/HUD.png" --field m_IsReadable --value true

ucp material set-property --path "Assets/Materials/Agent.mat" --property _Metallic --value "0.5"
ucp prefab apply --id -136722
```

Object reference writes accept `instanceId`, asset `path`, or asset `guid`, and unresolved references fail explicitly.

## Packages, settings, build, logs, tests, profiler, and exec

Use `ucp packages --help`, `ucp settings --help`, `ucp build --help`, `ucp logs --help`, `ucp run-tests --help`, `ucp profiler --help`, and `ucp exec --help` when you need the full surface.

```bash
ucp packages add com.unity.cinemachine
ucp packages dependency set com.company.tooling file:../tooling-package
ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage

ucp settings player
ucp settings set-player --key runInBackground --value true
ucp settings set-lighting --key fog --value true

ucp build targets
ucp build start --output "Builds/Game.exe"

ucp logs --pattern "NullReference|Exception" --count 100
ucp run-tests --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_ReturnsRequestedBufferedCount"
ucp profiler summary --limit 5
ucp exec run SetupScene
```

Prefer fully qualified test names when filtering, and use `--json` for structured log or test consumption.

## Version control (Plastic SCM / Unity VCS)

```bash
ucp vcs
```

Prefer the native `cm` CLI for normal Unity Version Control work when it is available. Use `ucp vcs` as a lightweight fallback that prints the currently available bridge-backed VCS commands and flags.

## Common workflows

### Spatial scene iteration loop

```bash
ucp scene snapshot --filter "Player"
ucp scene focus --id 46900 --axis 1 0 0
ucp screenshot --view scene --output before.png
ucp object get-fields --id 46900 --component Rigidbody
ucp object set-property --id 46900 --component Rigidbody --property m_Mass --value "2.5"
ucp screenshot --view scene --output after.png
```

Use this when an agent needs scene-aware iteration, not just raw file editing. Snapshot gives live instance IDs, focus aligns the Scene view for repeatable screenshots, and property changes apply through Unity immediately.

### Build a scene object into a prefab workflow

```bash
ucp object create "EnemyRoot"
ucp object add-component --id -15774 --component Rigidbody
ucp object create "Visual" --parent -15774
ucp object add-component --id -15775 --component MeshRenderer
ucp prefab create --id -15774 --path "Assets/Prefabs/EnemyRoot.prefab"
ucp prefab apply --id -15774
```

Use this for bridge-native authoring loops: assemble hierarchy in-scene, attach components, then persist it as a prefab. Refresh IDs with `ucp scene snapshot` if compilation, reloads, or other editor events invalidate handles.

### Asset and importer iteration

```bash
# Preferred when you already have workspace access
<edit file locally>
ucp compile

# Fallback when you want bridge-mediated writes
ucp files write Assets/Scripts/EnemyAI.cs --content "..."

# Imported assets: update importer settings instead of hand-editing .meta
ucp asset import-settings write "Assets/Models/Enemy.fbx" --field m_GlobalScale --value 0.5
ucp asset reimport "Assets/Models/Enemy.fbx"
```

UCP is unique here because it can bridge Unity-aware apply steps: `ucp compile` handles recompilation after local edits, while `ucp files write` / `patch` automatically reimport eligible assets and `.meta` files unless you intentionally defer with `--no-reimport`.

### Package install and selective import iteration

```bash
ucp packages search com.unity.cinemachine
ucp packages add com.unity.cinemachine
ucp packages info com.unity.cinemachine

ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company
ucp packages dependency set com.company.tooling file:../tooling-package

ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees
```

Use `packages add|remove` for normal UPM installs, `packages dependency ...` for explicit manifest references, and `packages unitypackage ...` when you need machine-friendly inspection plus selective import for archive-based content. Scoped registry adds may surface Unity's own security popup the first time a new registry is introduced.

### Playtest, logging, and failure triage

```bash
ucp compile
ucp play
ucp screenshot --output playtest.png
ucp logs --pattern "Exception|Error" --count 50
ucp stop
```

Use this loop for autonomous playtesting. If `ucp play` fails, fix compile or console-blocking errors first, then retry. Use logs for buffered inspection without needing to stream the entire editor log.

### Profiling and debugging

```bash
ucp profiler status
ucp profiler session start --mode play
ucp play
ucp profiler frames list --limit 1 --json
ucp profiler summary --limit 10
ucp profiler timeline --frame <fresh-frame> --thread 0 --limit 20
ucp profiler hierarchy --frame <fresh-frame> --thread 0 --limit 20
ucp profiler capture save --output ProfilerCaptures/session.json
ucp profiler session stop
ucp stop
```

Use profiler commands when debugging performance, spikes, or hot paths. Prefer grabbing a fresh frame id from `ucp profiler frames list` immediately before `timeline`, `hierarchy`, or `callstacks`, because live editor frame ids churn quickly. `summary` is intentionally bounded to recent frames by default, and `capture save --output *.json` exports a structured snapshot for agents/scripts without relying on unsupported live editor raw-binary logging.

### CI / validation pass

```bash
ucp connect || exit 1
ucp run-tests --mode edit
ucp build set-defines "CI;RELEASE"
ucp build start --output "Builds/Game.exe"
```

### Quick scene audit and debug snapshot

```bash
ucp scene snapshot --json > hierarchy.json
ucp logs --level error
ucp screenshot
```

This is a compact handoff workflow for agents: capture hierarchy state, inspect current errors, and grab a visual snapshot before deciding on the next action.
