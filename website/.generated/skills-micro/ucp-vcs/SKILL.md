---
name: ucp-vcs
description: >-
  Access Unity Version Control (Plastic SCM) as a lightweight fallback with
  `ucp vcs`. Use when the user wants bridge-backed version-control actions
  and the native `cm` CLI is unavailable. For broad multi-surface Unity
  automation, use the unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.0'
---

# UCP Version Control

Focused micro-skill for the `ucp vcs` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp vcs                                      # list available bridge VCS commands
cm status                                    # prefer the native Plastic/Unity VCS CLI
cm checkin -c "message"                      # prefer cm when available
```

## When to use

Use `ucp vcs` only as a lightweight fallback to discover and run bridge-backed Unity VCS commands when the native `cm` CLI is not available.

## When NOT to use (use the omni skill instead)

Prefer the native `cm` CLI for normal Unity Version Control work; reach for `ucp vcs` only when `cm` is missing.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
