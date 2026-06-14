---
name: ucp-assets
description: >-
  Manage project assets with `ucp asset`
  (search/move/bulk-move/import-settings/reimport/info/inspect),
  bridge-mediated file I/O via `ucp files` (read/write/patch), and shader
  diagnostics via `ucp shader errors`. Use when the user wants to find/move
  assets, edit importer settings, reimport, or read/write project files. For
  broad multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Assets & Files

Focused micro-skill for the `ucp asset` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp asset search -t Material --max 10
ucp asset search -n '^SCN_[0-9]+$' --regex
ucp asset move "Assets/Legacy/Enemy.prefab" "Assets/Characters/Enemy.prefab"
ucp asset import-settings write "Assets/Textures/HUD.png" --field m_IsReadable --value true
ucp asset reimport "Assets/Generated" --recursive
ucp files write Assets/Scripts/EnemyAI.cs --content "..."   # auto-reimports
ucp shader errors "Assets/Shaders/Water.shader"
```

## When to use

Use to search assets, do Unity-aware moves/bulk-moves that preserve `.meta`/GUIDs, edit importer settings instead of raw `.meta`, reimport, or do sandboxed file read/write/patch.

## When NOT to use (use the omni skill instead)

Prefer direct workspace edits + `ucp compile` when you have filesystem access; use `ucp files` as a fallback. For material property edits use `ucp-materials`; for cross-project reference lookups use `ucp-references`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
