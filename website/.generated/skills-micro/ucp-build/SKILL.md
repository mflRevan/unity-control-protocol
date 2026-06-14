---
name: ucp-build
description: >-
  Drive the build pipeline with `ucp build`
  (targets/active-target/set-target/scenes/set-scenes/start/defines/set-defines).
  Use when the user wants to inspect or change the build target, scene list,
  scripting defines, or run a build. For broad multi-surface Unity
  automation, use the unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Build Pipeline

Focused micro-skill for the `ucp build` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp build targets
ucp build active-target
ucp build set-target StandaloneWindows64
ucp build set-scenes "Assets/Scenes/Menu.unity;Assets/Scenes/Level1.unity"
ucp build set-defines "CI;RELEASE"
ucp build start --output "Builds/Game.exe"
```

## When to use

Use to list/switch build targets, read or set the scenes-in-build list, manage scripting define symbols, or kick off a player build.

## When NOT to use (use the omni skill instead)

For player/quality/physics project settings use `ucp-settings`. For package/manifest changes that affect a build use `ucp-packages`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
