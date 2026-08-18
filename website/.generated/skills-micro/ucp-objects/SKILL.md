---
name: ucp-objects
description: >-
  Create and edit GameObjects and components with `ucp object` (create incl.
  `--primitive`, get/set-property, get-fields, add/remove-component,
  reparent, instantiate, delete, set-active, set-name). Use when the user
  wants to spawn, inspect, or modify GameObjects and their components. For
  broad multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.
metadata:
  author: mflRevan
  version: '0.6.1'
---

# UCP Objects

Focused micro-skill for the `ucp object` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
# A plain `object create` makes an EMPTY object (no mesh, will not render).
# `--primitive` is THE way to make a visible built-in mesh from the CLI.
ucp object create Crate --primitive Cube
ucp object create EnemyRoot                 # empty container, no renderer
ucp object add-component --id -15774 --component Rigidbody
ucp object get-fields --id 46894 --component Transform
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true
ucp object reparent --id -15775 --parent -15774
```

## When to use

Use when creating primitives/empties, attaching or removing components, reading/writing serialized fields, reparenting, instantiating prefab assets, or toggling active/name.

## When NOT to use (use the omni skill instead)

Use `object instantiate` only for prefab assets under `Assets/`, not built-in primitives. For moving/rotating/scaling, prefer the `ucp-transform` skill.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
