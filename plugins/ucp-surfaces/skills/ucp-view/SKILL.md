---
name: ucp-view
description: >-
  Render objects and the scene for vision models with `ucp view`
  (capture/isolate/orbit), `ucp screenshot`, and `ucp record`. Use when the
  user wants a rendered image or lightweight screen recording to inspect 3D
  shape, motion, or state. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.3'
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
ucp record capture --duration 5 --view game --output playtest.mp4
ucp record start --view scene --output sequence.mp4  # run non-reloading commands, then stop
ucp record arm --on signal:impact --duration 5 --output impact.mp4
ucp record capture --duration 3 --fps 30 --slowdown 6 --output analysis.mp4  # for a model to watch
```

## When to use

Use to frame a single object (`capture`), composite an orthographic grid (`isolate`), spin an object for a turntable (`orbit`), grab a screenshot, or record short game/scene motion for a vision model. When the clip is for a model rather than a person, add `--slowdown <factor>`: multimodal models sample video at roughly one frame per second, so sub-second motion is otherwise invisible; `--slowdown` stretches playback only, with the same captured frames and no re-encode. `--view game` records `Camera.main`, not the Game view camera stack -- use `--view scene` for a fixed vantage point that does not follow the player.

## When NOT to use (use the omni skill instead)

Use `record capture` for one bounded clip, `start`/`stop` around sequences, and `arm` for play/log/signal events. For transforms use `ucp-transform`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
