---
name: ucp-prefabs
description: >-
  Work with prefabs via `ucp prefab`
  (status/apply/revert/unpack/create/overrides). Use when the user wants to
  create prefabs, apply or revert overrides, unpack, or inspect prefab
  status. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Prefabs

Focused micro-skill for the `ucp prefab` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp prefab create --id -15774 --path "Assets/Prefabs/EnemyRoot.prefab"
ucp prefab status --id -136722
ucp prefab overrides --id -136722
ucp prefab apply --id -136722
ucp prefab revert --id -136722
ucp prefab unpack --id -136722
```

## When to use

Use to persist a scene hierarchy as a prefab (`create`), inspect/apply/revert instance overrides, or unpack a prefab instance.

## When NOT to use (use the omni skill instead)

To spawn a prefab asset into the scene use `ucp object instantiate` (`ucp-objects`). To assemble the hierarchy first, use `ucp-objects` + `ucp-transform`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
