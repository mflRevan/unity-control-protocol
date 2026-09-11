---
name: ucp-visual-feedback
description: >-
  See what the Unity scene looks like and how it moves, from the terminal: `ucp screenshot` for
  the Game or Scene view, `ucp view capture|isolate|orbit` for framed, isolated, and multi-angle
  renders a vision model can read, and `ucp record capture|start|stop|arm|signal` for short video
  clips, including `--slowdown` so a video model samples enough frames to judge motion. Use
  whenever a judgment depends on appearance, layout, timing, or motion rather than on hierarchy
  data or logs. For UI Toolkit panels use ucp-ui-toolkit.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer; video recording needs an interactive editor with a graphics device.
metadata:
  author: mflRevan
  version: '0.6.4'
  homepage: https://unityctl.dev/skills/ucp-visual-feedback
---

# Visual feedback: screenshots, composed views, recordings

Hierarchy dumps and logs tell you what exists; they do not tell you whether the crate floats,
whether the camera overshoots, or whether the character's feet slide. Capture, look, decide.

## Ground rules

- Frame what you want to judge. A raw Game view screenshot shows whatever the player camera sees;
  `view capture --target-id` and `view isolate` frame a specific object from its bounds.
- Keep images small for model consumption: `--max-edge 768` is plenty for a decision, and
  composites (`isolate`, `orbit`) put several angles into one image.
- Use a recording, not a burst of screenshots, when the question is about motion or transient
  state. Add `--slowdown` when a model, not a person, will watch it.
- Outputs go where you say (`-o path.png`); without `-o`, screenshots print base64 to stdout and
  recordings land under `.ucp/recordings/`.

## Screenshots

```bash
ucp screenshot -o game.png                          # Game view (Camera.main), 1920x1080 default
ucp screenshot --view scene -o scene.png            # the Scene view as currently framed
ucp screenshot --width 1280 --height 720 -o small.png
ucp scene focus --id 46894 --axis 1 0 0 && ucp screenshot --view scene -o side.png
```

`scene focus` aims the Scene view camera at an object (optionally along an axis), which makes
Scene-view screenshots repeatable across a before/after pair.

## Composed views

```bash
ucp view capture -o main.png                                  # main camera, the whole scene
ucp view capture --target-name Crate --max-edge 768 -o crate.png   # temporary camera framed on the object, scene still visible
ucp view capture --camera 47010 -o cinematic.png              # render from a specific camera object
ucp view isolate --id 46894 -o crate-grid.png                 # Front/Right/Back/Top composite, object alone
ucp view isolate --name Crate --views front,right --max-edge 512 -o crate.png   # writes crate-front.png, crate-right.png
ucp view isolate --path "Level/Props/Crate" --background transparent -o crate.png
ucp view orbit --id 46894 --count 8 --elevation 25 --max-edge 384 -o orbit.png
```

- `isolate` renders one object in isolation, auto-framed from its bounds; the composite grid is
  the fastest way for a vision model to read 3D shape from one image.
- `orbit` renders a ring of evenly spaced angles (1 to 12) as one grid.
- `--background transparent` produces an alpha PNG for compositing.

## Recordings

```bash
ucp record capture --duration 5 -o clip.mp4                    # block until the file is final
ucp record capture --view scene --duration 8 --max-edge 640 -o scene.webm
ucp record capture --duration 6 --slowdown 6 -o for-the-model.mp4
ucp record start --duration 30 -o session.mp4 --max-duration 120   # detached; survives this CLI call
ucp play && ucp stop
ucp record stop                                                 # finalize (or cancel an armed trigger)
ucp record status
ucp record arm --on play-enter --duration 6 -o enter.mp4        # event-driven
ucp record arm --on 'log:Level loaded' --duration 4 -o loaded.mp4 --wait-timeout 120
ucp record arm --on signal:checkpoint --duration 3 -o cp.mp4 && ucp record signal checkpoint
ucp exec run demo-autopilot --record run.mp4 --record-lead 0.5 --record-tail 1
```

- Defaults: silent video, 960 px longest edge with the source aspect preserved, 15 fps, 2 Mbps,
  H.264 MP4 (or VP8 WebM with `--format webm`). No objects or scripts are injected into the scene.
- `--view game` records `Camera.main`, not the Game view's camera stack. `--view scene` records
  a fixed vantage that does not follow the player, which is often what you want for judging
  motion.
- `capture` blocks and returns the finalized path. `start`/`stop` bracket a sequence of commands
  that do not reload the domain (a recompile ends the recording). `arm` waits for `play-enter`,
  `play-exit`, a `log:<regex>` match, or a named `signal`, then records for `--duration`.
- `exec run --record` wraps a script run with lead and tail seconds so a model sees the before
  and after.

### `--slowdown`, and why it exists

Video-understanding models do not watch a file; they sample it, typically at about one frame per
second regardless of its frame rate. A six-second clip reaches the model as roughly six frames,
and everything between samples (foot sliding, a camera settling, a one-frame pop) is invisible.
Raising `--fps` does not help because the sampler ignores it. `--slowdown N` keeps every captured
frame and divides only the container's declared playback rate, so one gameplay second becomes N
seconds of file and the sampler receives about N frames of it. Use it when the judgment is about
contact, timing, smoothness, or settling. Leave it at 1 for clips a person will watch.

## Getting a useful answer from a model

- Ask about one axis at a time: translation, rotation, bobbing, contact. "Is it moving?" invites
  a wrong answer when an object rotates in place.
- Compare, don't describe: capture before and after the change with the same framing (`scene
  focus` + `--view scene`, or the same `view capture --target-id`), then ask which is correct
  and why.
- For "which of these two clips is right", a forced choice with both clips is far more reliable
  than a single-clip verdict.

## Workflows

Verify a placement:

```bash
ucp transform move --name Crate --to 2 3 0
ucp spatial ground --name Crate
ucp view capture --target-name Crate --max-edge 768 -o crate.png
```

Judge a camera-follow tweak:

```bash
ucp record capture --view scene --duration 6 --slowdown 4 -o before.mp4
ucp object set-property --id 47010 --component CinemachineThirdPersonFollow --property Damping --value [0.1,0.25,0.3]
ucp record capture --view scene --duration 6 --slowdown 4 -o after.mp4
```

Capture a scripted moment without babysitting the timing:

```bash
ucp record arm --on 'log:Boss spawned' --duration 5 -o boss.mp4 --wait-timeout 300
ucp play
ucp record status
```

## Pitfalls

- A recording that spans a domain reload (recompile, entering play mode on some setups) is
  finalized at the reload; check `record status` and record after the reload instead.
- `--view game` needs a `Camera.main` (tagged MainCamera); an empty scene records black.
- Disabling the main camera to "hide" it breaks scripts that resolve the camera by tag
  (StarterAssets does); prefer `view capture --camera` to render from another camera.
