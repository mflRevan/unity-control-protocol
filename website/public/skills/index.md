# Unity Control Protocol agent skills

Version 0.6.3. Each skill follows the Agent Skills specification (https://agentskills.io/specification).
Download the raw URL into `<skills dir>/<name>/SKILL.md` to install by hand.

| skill | kind | commands | raw |
|---|---|---|---|
| ucp-assets | surface | `asset`, `compile`, `files`, `material`, `object`, `references`, `script`, `shader` | https://unityctl.dev/skills/ucp-assets.md |
| ucp-editor-lifecycle | surface | `bridge`, `compile`, `connect`, `doctor`, `editor`, `exec`, `install`, `logs`, `open`, `pause`, `play`, `scene`, `stop` | https://unityctl.dev/skills/ucp-editor-lifecycle.md |
| ucp-project-config | surface | `build`, `compile`, `connect`, `install`, `logs`, `packages`, `references`, `run-tests`, `settings` | https://unityctl.dev/skills/ucp-project-config.md |
| ucp-runtime-debugging | surface | `compile`, `exec`, `frame`, `log`, `logs`, `play`, `profile`, `profiler`, `run-tests`, `shader`, `stop` | https://unityctl.dev/skills/ucp-runtime-debugging.md |
| ucp-scene-authoring | surface | `object`, `prefab`, `scene`, `spatial`, `transform`, `view` | https://unityctl.dev/skills/ucp-scene-authoring.md |
| ucp-ui-toolkit | surface | `ui` | https://unityctl.dev/skills/ucp-ui-toolkit.md |
| ucp-version-control | surface | `files`, `vcs` | https://unityctl.dev/skills/ucp-version-control.md |
| ucp-visual-feedback | surface | `exec`, `object`, `play`, `record`, `scene`, `screenshot`, `spatial`, `transform`, `view` | https://unityctl.dev/skills/ucp-visual-feedback.md |
| unity-control-protocol | omni | `asset`, `bridge`, `build`, `compile`, `connect`, `doctor`, `editor`, `exec`, `files`, `install`, `logs`, `material`, `object`, `open`, `packages`, `play`, `prefab`, `profiler`, `record`, `references`, `run-tests`, `scene`, `screenshot`, `settings`, `spatial`, `stop`, `transform`, `ui`, `vcs`, `view` | https://unityctl.dev/skills/unity-control-protocol.md |

## Install

```text
/plugin marketplace add mflRevan/unity-control-protocol
/plugin install ucp@unity-control-protocol
/plugin install ucp-surfaces@unity-control-protocol
```

```bash
npx skills add mflRevan/unity-control-protocol
gh skill install mflRevan/unity-control-protocol <skill-name>
curl -fsSL https://unityctl.dev/skills/<skill-name>.md -o .agents/skills/<skill-name>/SKILL.md
```

## ucp-assets

Work with a Unity project's assets and files through the editor with `ucp asset`, `ucp files`, `ucp material`, `ucp references`, `ucp shader`, and `ucp script`: search and inspect assets, read and write ScriptableObject and material fields, move or rename assets without breaking GUID references, edit importer settings instead of .meta files, find every reference to an asset, and check shader and project-file health. Use when the task touches files under Assets/ or Packages/. For scene objects use ucp-scene-authoring; for UXML/USS use ucp-ui-toolkit.

- page: https://unityctl.dev/skills/ucp-assets
- raw: https://unityctl.dev/skills/ucp-assets.md

## ucp-editor-lifecycle

Bring a Unity project under control with the `ucp` CLI: install the bridge, open or adopt the editor, read the `[editor]` state line every command prints, recompile, enter and leave play mode, and recover from modal dialogs. Use when a task starts (is Unity running? is the console red? is the scene dirty?), when a command reports COMPILE ERRORS or a MODAL, or when the editor must be opened, restarted, or closed. For scene content, assets, UI, capture, debugging, or project configuration, use the matching ucp-* skill or the unity-control-protocol omni skill.

- page: https://unityctl.dev/skills/ucp-editor-lifecycle
- raw: https://unityctl.dev/skills/ucp-editor-lifecycle.md

## ucp-project-config

Configure a Unity project from the terminal with `ucp packages`, `ucp settings`, and `ucp build`: search, add, and remove UPM packages, manage scoped registries and manifest dependencies, inspect and selectively import .unitypackage archives, read and set player, quality, physics, and lighting settings, tags and layers, and drive the build pipeline (targets, scenes, scripting defines, builds). Use for project setup, dependency work, and release configuration. For scene content use ucp-scene-authoring; for assets use ucp-assets.

- page: https://unityctl.dev/skills/ucp-project-config
- raw: https://unityctl.dev/skills/ucp-project-config.md

## ucp-runtime-debugging

Find out what the running Unity project is doing and why with `ucp logs`, `ucp run-tests`, `ucp exec`, `ucp profiler`, `ucp profile`, and `ucp frame capture`: read and follow console logs with filters, run edit-mode or play-mode tests by name pattern, execute registered editor scripts with parameters, profile frames and read hierarchies sorted by self time, and export structured captures. Use for playtesting loops, failure triage, test runs, and performance work. For compile errors and editor state use ucp-editor-lifecycle.

- page: https://unityctl.dev/skills/ucp-runtime-debugging
- raw: https://unityctl.dev/skills/ucp-runtime-debugging.md

## ucp-scene-authoring

Build and inspect scene content in the live Unity Editor with `ucp scene`, `ucp object`, `ucp transform`, `ucp spatial`, and `ucp prefab`: snapshot and query the hierarchy, create visible primitives, add components, set serialized properties, move/rotate/scale/look-at objects, raycast and ground them, and turn hierarchies into prefabs. Use when the user wants something placed, arranged, wired, or measured in a scene. For assets on disk use ucp-assets; for what the scene looks like use ucp-visual-feedback.

- page: https://unityctl.dev/skills/ucp-scene-authoring
- raw: https://unityctl.dev/skills/ucp-scene-authoring.md

## ucp-ui-toolkit

Author and verify Unity UI Toolkit interfaces (UXML, USS, TSS) with `ucp ui`: discover documents and scenarios, lint with Unity's own importers, instantiate a document in an isolated editor panel, inspect its resolved layout and bindings, populate lists and repeaters from JSON scenarios, and capture deterministic PNGs. Use after editing UXML/USS or when the user wants a UI checked or screenshotted without loading a scene. Requires Unity 6 or newer.

- page: https://unityctl.dev/skills/ucp-ui-toolkit
- raw: https://unityctl.dev/skills/ucp-ui-toolkit.md

## ucp-version-control

Lightweight Unity Version Control (Plastic SCM) operations through the editor with `ucp vcs`: provider status, pending changes, checkout, revert, commit, diff, incoming, update, lock, unlock, history, and conflict resolution. Use when a project is under Unity VCS and the native `cm` CLI is unavailable or you want the editor's own view of pending changes. Prefer `cm` (or git) for everything else.

- page: https://unityctl.dev/skills/ucp-version-control
- raw: https://unityctl.dev/skills/ucp-version-control.md

## ucp-visual-feedback

See what the Unity scene looks like and how it moves, from the terminal: `ucp screenshot` for the Game or Scene view, `ucp view capture|isolate|orbit` for framed, isolated, and multi-angle renders a vision model can read, and `ucp record capture|start|stop|arm|signal` for short video clips, including `--slowdown` so a video model samples enough frames to judge motion. Use whenever a judgment depends on appearance, layout, timing, or motion rather than on hierarchy data or logs. For UI Toolkit panels use ucp-ui-toolkit.

- page: https://unityctl.dev/skills/ucp-visual-feedback
- raw: https://unityctl.dev/skills/ucp-visual-feedback.md

## unity-control-protocol

Programmatic control of the Unity Editor from the terminal via the `ucp` CLI. Automate scenes, GameObjects, components, assets, materials, prefabs, build pipelines, UI Toolkit UXML/USS, settings, tests, packages, selective `.unitypackage` import, debugging and profiling over a WebSocket/JSON-RPC 2.0 bridge. Use when the user asks to inspect, create, modify, or automate anything inside a Unity project without opening the Editor UI.

- page: https://unityctl.dev/skills/unity-control-protocol
- raw: https://unityctl.dev/skills/unity-control-protocol.md
