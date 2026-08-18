---
name: ucp-settings
description: >-
  Read and write project settings with `ucp settings`
  (player/quality/physics/lighting/tags-layers and their set-* plus
  add-tag/add-layer). Use when the user wants to inspect or change player,
  quality, physics, lighting, or tags/layers settings. For broad
  multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Project Settings

Focused micro-skill for the `ucp settings` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp settings player
ucp settings set-player --key runInBackground --value true
ucp settings set-quality --key shadowDistance --value 150
ucp settings set-physics --key gravity --value "0 -9.81 0"
ucp settings set-lighting --key fog --value true
ucp settings add-tag --name Interactable
ucp settings add-layer --name Water --index 8
```

## When to use

Use to read settings groups, set individual player/quality/physics/lighting keys, or add tags and layers.

## When NOT to use (use the omni skill instead)

For build target/scenes/defines use `ucp-build`. For package registries use `ucp-packages`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
