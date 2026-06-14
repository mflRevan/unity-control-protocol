---
name: ucp-materials
description: >-
  Create and edit materials with `ucp material`
  (create/get-properties/get-property/set-property/keywords/set-keyword/set-shader).
  Use when the user wants to create a material or read/write its properties,
  keywords, or shader. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Materials

Focused micro-skill for the `ucp material` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp material create "Assets/Materials/Agent.mat" --shader "Standard"
ucp material get-properties --path "Assets/Materials/Agent.mat"
ucp material set-property --path "Assets/Materials/Agent.mat" --property _Metallic --value "0.5"
ucp material set-property --path "Assets/Materials/Agent.mat" --property _Color --value "1 0 0 1"
ucp material set-keyword --path "Assets/Materials/Agent.mat" --keyword _EMISSION --enabled true
ucp material set-shader --path "Assets/Materials/Agent.mat" --shader "Universal Render Pipeline/Lit"
```

## When to use

Use to create a `.mat`, enumerate or read a single property, set scalar/color/vector properties, toggle shader keywords, or swap the shader.

## When NOT to use (use the omni skill instead)

For moving/renaming `.mat` files use `ucp asset move` (`ucp-assets`); to find every object using a material use `ucp references find` (`ucp-references`).

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
