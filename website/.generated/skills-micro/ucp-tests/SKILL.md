---
name: ucp-tests
description: >-
  Run tests and named editor scripts with `ucp run-tests`, `ucp exec`
  (list/run), and `ucp script doctor`. Use when the user wants to run
  edit/play-mode tests, execute a named editor script, or diagnose a script.
  For broad multi-surface Unity automation, use the unity-control-protocol
  skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Tests & Editor Scripts

Focused micro-skill for the `ucp run-tests` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp run-tests --mode edit
ucp run-tests --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_ReturnsRequestedBufferedCount"
ucp exec list
ucp exec run SetupScene
ucp script doctor
```

## When to use

Use to run the Unity Test Runner (edit/play mode, optionally filtered by fully qualified name), list/run registered editor scripts, or run the script doctor.

## When NOT to use (use the omni skill instead)

Prefer fully qualified test names when filtering and `--json` for structured consumption. For raw play/stop and compile use `ucp-scene`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
