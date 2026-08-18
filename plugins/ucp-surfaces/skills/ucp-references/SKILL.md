---
name: ucp-references
description: >-
  Find references across the project with `ucp references`
  (find/index/check/find-strings) using native Rust indexing — read-only.
  Use when the user wants to find every place an asset, script, material, or
  prefab is referenced. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Reference Search

Focused micro-skill for the `ucp references` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp references check                        # native indexing compatibility
ucp references find --asset "Assets/Materials/Agent.mat" --detail summary
ucp references find --asset 933532a4fcc9baf4fa0491de14d08ed7
ucp references check Assets/Prefabs          # missing-target verification
ucp references find-strings --pattern "SCN_Menu"
ucp references find --asset "Assets/Prefabs/Enemy.prefab" --json --detail normal
```

## When to use

Use to locate all references to an asset by path or GUID, verify references after a move/refactor, or find string-based references Unity will not migrate automatically.

## When NOT to use (use the omni skill instead)

Native indexing needs Force Text serialization + Visible Meta Files and does not require a running editor; pass `--approach bridge` only as a fallback. Use `--detail summary` to avoid context bloat.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
