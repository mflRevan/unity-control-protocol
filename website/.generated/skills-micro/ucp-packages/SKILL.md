---
name: ucp-packages
description: >-
  Manage Unity packages with `ucp packages`
  (search/add/remove/info/dependency, scoped registries, and selective
  `.unitypackage` import). Use when the user wants to install/remove UPM
  packages, manage scoped registries, or selectively import a
  `.unitypackage`. For broad multi-surface Unity automation, use the
  unity-control-protocol skill instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Packages

Focused micro-skill for the `ucp packages` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp packages search com.unity.cinemachine
ucp packages add com.unity.cinemachine
ucp packages info com.unity.cinemachine
ucp packages dependency set com.company.tooling file:../tooling-package
ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company
ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees
```

## When to use

Use for normal UPM add/remove, package info, manifest `file:` dependencies, scoped registry setup, and machine-friendly selective import of `.unitypackage` archives.

## When NOT to use (use the omni skill instead)

A new scoped registry may trigger Unity's own security popup the first time. For scripting defines or build scenes use `ucp-build`; for project settings use `ucp-settings`.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
