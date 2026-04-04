# CLI Overview

This page covers the stable cross-cutting behavior of UCP itself: global flags, output modes, editor/bridge lifecycle, and recommended workflows. Detailed command surfaces now live under workflow-oriented sections instead of one flat command bucket.

Use `ucp --help` and `ucp <command> --help` for the current exhaustive surface. The docs should explain **how to approach work**, not duplicate every subcommand list in a page that goes stale.

## Global Flags

| Flag                            | Description                                               |
| ------------------------------- | --------------------------------------------------------- |
| `--json`                        | Output results as JSON                                    |
| `--project <path>`              | Path to a Unity project (defaults to current directory)   |
| `--unity <path>`                | Override the Unity Editor executable UCP should launch    |
| `--force-unity-version <ver>`   | Force a specific installed Unity editor version           |
| `--bridge-update-policy <mode>` | Handle outdated bridge refs with `auto`, `warn`, or `off` |
| `--dialog-policy <mode>`        | Handle Unity startup dialogs during launch waits          |
| `--timeout <s>`                 | Request timeout in seconds (default: 30)                  |
| `--verbose`                     | Enable verbose output                                     |

## Execution Model

For most bridge-backed commands, UCP follows the same lifecycle:

1. Resolve the Unity project from `--project` or the current working directory.
2. Resolve the bridge dependency and apply the configured bridge update policy.
3. Resolve the Unity executable and auto-start the editor if the command needs a live bridge.
4. Discover `.ucp/bridge.lock`, connect over WebSocket, and verify protocol compatibility.
5. Execute the command and wait for Unity settle points when the operation mutates assets, scenes, compilation state, or the editor lifecycle.
6. Return bounded human output or machine-readable JSON.

That lifecycle is what makes UCP useful in agent and CI workflows: the caller can stay at the CLI layer while UCP handles bridge state, editor startup, and Unity-specific settle behavior.

## Output Modes

Without `--json`, commands use human mode: terminal-friendly summaries optimized for interactive use and bounded enough to avoid overwhelming a terminal or an agent context window.

Human mode is intentionally curated:

- broad reads such as logs, snapshots, settings, and references are truncated or grouped
- long-running mutation commands append the same curated `ucp logs status` summary when it is useful
- repetitive results are collapsed into patterns where possible instead of printing hundreds of nearly identical lines

With `--json`, the CLI returns structured output suitable for scripts, agents, and CI automation.

## Recommended Workflows

### First-time project setup

```bash
ucp install
ucp doctor
ucp open
ucp connect
ucp bridge status
```

Start with the [Project Setup & Bridge](/docs/overview/project-setup) and [Editor Lifecycle](/docs/overview/editor-lifecycle) pages whenever bridge state or editor startup behavior is unknown.

### Normal day-to-day authoring

```bash
# Edit code/files locally in the workspace
ucp compile
ucp scene snapshot
ucp object get-fields --id 46894 --component Transform
ucp asset move "Assets/Legacy/Enemy.prefab" "Assets/Characters/Enemy.prefab"
ucp references find --asset "Assets/Characters/Enemy.prefab" --detail summary
```

Prefer normal workspace edits plus `ucp compile` when you already have filesystem access. Use UCP for Unity-aware actions: scene/object inspection, importer updates, GUID-safe asset moves, prefab operations, and reference discovery.

### Agent and CI automation

```bash
ucp connect
ucp run-tests --mode edit --json
ucp build start --output "Builds/Game.exe" --json
```

Prefer `--json`, fully qualified test filters, and bounded queries such as `ucp references find --detail summary` when an agent or automation job needs structured output without context bloat.

## Documentation Map

| Section | Focus |
| ------- | ----- |
| [Overview](/docs/overview) | CLI lifecycle, setup, bridge injection, and editor process behavior |
| [Authoring](/docs/authoring/scenes) | Scenes, objects, prefabs, assets, references, files, and scripting |
| [Runtime & Diagnostics](/docs/runtime/play-mode) | Play mode, logs, screenshots, testing, and profiler workflows |
| [Project Operations](/docs/project/packages) | Packages, settings, build pipeline, and version-control guidance |
| [Agent Skills](/docs/agents/skills) | How the UCP skill is packaged and consumed by agent tooling |
