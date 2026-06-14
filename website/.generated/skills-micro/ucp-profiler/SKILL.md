---
name: ucp-profiler
description: >-
  Profile play mode with `ucp profiler`
  (status/config/session/capture/frames/hierarchy/timeline/callstacks/summary),
  plus `ucp profile` and `ucp frame capture`. Use when the user wants to
  capture profiler frames and analyze performance, spikes, or hot paths. For
  broad multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Profiler

Focused micro-skill for the `ucp profiler` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp profiler status
ucp profiler session start --mode play
ucp profiler frames list --limit 1 --json     # grab a FRESH frame id
ucp profiler timeline --frame <fresh-frame> --thread 0 --limit 20
ucp profiler hierarchy --frame <fresh-frame> --thread 0 --limit 20
ucp profiler summary --limit 10
ucp profiler capture save --output ProfilerCaptures/session.json
```

## When to use

Use to start/stop a profiler session, list captured frames, drill into timeline/hierarchy/callstacks for a specific frame, get a bounded summary, or export a capture.

## When NOT to use (use the omni skill instead)

Live frame ids churn fast: pull a fresh id from `frames list` immediately before `timeline`/`hierarchy`/`callstacks`. For play/stop control use `ucp-scene`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
