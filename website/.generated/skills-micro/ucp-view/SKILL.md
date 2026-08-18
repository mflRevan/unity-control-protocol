---
name: ucp-view
description: >-
  Render objects and the scene for vision models with `ucp view`
  (capture/isolate/orbit) and `ucp screenshot`. Use when the user wants a
  rendered image of an object or the scene to inspect 3D shape or state. For
  broad multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP View & Capture

Focused micro-skill for the `ucp view` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp view capture --target-id 1234 --max-edge 768 --output framed.png
ucp view isolate --id 1234 --output hero.png        # Front/Right/Back/Top grid
ucp view orbit --id 1234 --count 6 --output orbit.png
ucp screenshot --view scene --output before.png
ucp screenshot --view game --output game.png
```

## When to use

Use to frame a single object (`capture`), composite an orthographic grid (`isolate`), spin an object for a turntable (`orbit`), or grab a scene/game screenshot.

## When NOT to use (use the omni skill instead)

For aligning the scene view before a screenshot use `ucp scene focus` (the `ucp-scene` skill); for transforms use `ucp-transform`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
