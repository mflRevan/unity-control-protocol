---
name: ucp-scene
description: >-
  Manage scenes and the editor lifecycle with `ucp scene`
  (list/load/active/save/focus/snapshot/query), `ucp editor`, and `ucp
  play|stop|pause|compile|screenshot`. Use when the user wants to
  open/save/snapshot scenes, enter or exit play mode, recompile, or
  screenshot the editor. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Scene & Editor Lifecycle

Focused micro-skill for the `ucp scene` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp scene snapshot --filter "Player"        # live instance IDs for object work
ucp scene save                              # save active scene before loading another
ucp scene load Assets/Scenes/Level1.unity
ucp scene load Assets/Scenes/Lighting.unity --additive
ucp scene focus --id 46894 --axis 1 0 0
ucp play && ucp pause && ucp stop
ucp compile
```

## When to use

Use to list/load/save scenes, snapshot the hierarchy for fresh instance IDs, frame the scene view, drive play/stop/pause, force a recompile, or grab an editor screenshot.

## When NOT to use (use the omni skill instead)

Treat instance IDs from `scene snapshot` as short-lived; refresh after compile, reloads, or scene loads. For object edits use `ucp-objects`; for render captures use `ucp-view`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
