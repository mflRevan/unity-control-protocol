---
name: ucp-transform
description: >-
  Author object transforms with `ucp transform`
  (move/rotate/scale/look-at/get) using Euler degrees, world|local space,
  and `--relative` offsets. Use when the user wants to position, orient, or
  scale objects in the scene. For broad multi-surface Unity automation, use
  the unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Transform

Focused micro-skill for the `ucp transform` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp transform move --id 1234 --to 3 0 0
ucp transform move --id 1234 --to 0 1 0 --relative --space local
ucp transform rotate --id 1234 --euler 0 45 0       # Euler X Y Z degrees
ucp transform scale --id 1234 --uniform 2
ucp transform look-at --id 1234 --target 0 0 0      # or --target-id <id>
ucp transform get --id 1234 --space world
```

## When to use

Use to move/rotate/scale by absolute value or `--relative` offset, aim an object with look-at, or read a transform in world or local space.

## When NOT to use (use the omni skill instead)

Do not write transform values through raw serialized properties. For surface placement (ground/raycast) use `ucp-spatial`; for hierarchy reparenting use `ucp-objects`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
