---
name: ucp-spatial
description: >-
  Reason about scene geometry with `ucp spatial`
  (raycast/overlap/bounds/ground/nearest). Use when the user wants to place
  objects on surfaces, cast rays, or query bounds and nearby objects. For
  broad multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Spatial Queries

Focused micro-skill for the `ucp spatial` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp spatial ground --id 1234                        # drop onto the surface below
ucp spatial raycast --origin 0 10 0 --direction 0 -1 0
ucp spatial overlap --center 0 0 0 --radius 5
ucp spatial bounds --id 1234                        # world AABB center/size/min/max
ucp spatial nearest --point 0 0 0 --max 5
```

## When to use

Use to snap objects onto the surface beneath them, cast rays for hit tests, query an object world AABB, find overlapping colliders, or list nearest objects to a point.

## When NOT to use (use the omni skill instead)

Spatial queries report geometry and place via `ground`; to set an exact position/rotation/scale use `ucp-transform`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
