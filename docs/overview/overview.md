# CLI Overview

This page covers the stable cross-cutting behavior of UCP itself: global flags, output modes, editor/bridge lifecycle, and recommended workflows. Detailed command surfaces now live under workflow-oriented sections instead of one flat command bucket.

Use `ucp --help` and `ucp <command> --help` for the current exhaustive surface. The docs should explain **how to approach work**, not duplicate every subcommand list in a page that goes stale.

## Global Flags

| Flag                            | Description                                               |
| ------------------------------- | --------------------------------------------------------- |
| `--json`                        | Output results as JSON (compact; `UCP_JSON_PRETTY=1` indents) |
| `--project <path>`              | Path to a Unity project (defaults to current directory)   |
| `--unity <path>`                | Override the Unity Editor executable UCP should launch    |
| `--force-unity-version <ver>`   | Force a specific installed Unity editor version           |
| `--bridge-update-policy <mode>` | Handle outdated bridge refs with `auto`, `warn`, or `off` |
| `--dialog-policy <mode>`        | Handle Unity startup dialogs during launch waits          |
| `--timeout <s>`                 | Request timeout in seconds (default: 30)                  |
| `--verbose`                     | Enable verbose output                                     |

The global flags are listed once, at the end of `ucp --help`; subcommand help screens show only
their own arguments. `UCP_EDITOR_STATE=0` drops the trailing `[editor]` state line (and the
`editor` JSON member) described in [Editor State](/docs/runtime/editor-state).

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

Human mode is intentionally curated, and tuned for the token budget of an agent reading it:

- every result is the shortest text that still carries the handles for the next command: an
  object is `Name #id` on one line, a transform is one line of rounded numbers, a hierarchy is an
  indented tree, a hit is `hit Name #id at (x, y, z) distance d`
- defaults are implied, not printed: active objects, `Untagged`, the `Default` layer, built-in
  engine modules in `packages list`, package shaders in `shader errors`, platforms that are not
  installed in `build targets`
- broad reads such as logs, snapshots, settings, and references are bounded (newest 20 logs,
  root objects only, 50 assets) and say how to widen the read (`--count`, `--depth`, `--all`)
- progress notices (`[*] Waiting for ...`) only appear when a wait takes longer than about
  1.5 seconds
- images are never written to stdout: `screenshot` and `view` save a PNG (to `--output`, or
  `.ucp/captures/` in the project) and print its path; `--base64` opts into the old behaviour
- every command that touched the bridge ends with one `[editor]` line describing the state it left
  the editor in, so the console, play mode, and scene dirtiness never need a second request

With `--json`, the CLI returns structured output suitable for scripts, agents, and CI automation.
It is compact (one line) because an agent reads it just as well as the indented form and pays
about 40% fewer tokens for it; set `UCP_JSON_PRETTY=1` for a human. The same default-elision
applies: a snapshot entry lists `active`, `tag`, `layer`, and `childCount` only when they are not
the default, package entries carry descriptions and resolved paths only with `--full`, and test
results list the tests that did not pass.

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
