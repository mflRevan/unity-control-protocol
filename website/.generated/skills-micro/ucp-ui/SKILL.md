---
name: ucp-ui
description: >-
  Author and verify UI Toolkit with `ucp ui`
  (list/lint/inspect/screenshot/check) using UXML, USS, and strict
  dynamic-data scenarios. Use when the user wants to lint, inspect,
  populate, screenshot, or verify UI Toolkit UXML and USS. For broad
  multi-surface Unity automation, use the unity-control-protocol skill
  instead.
homepage: https://github.com/mflRevan/unity-control-protocol
compatibility: Requires the `ucp` CLI, the UCP Bridge package, and Unity 6.0+ in the target project.
metadata:
  author: mflRevan
  version: '0.6.2'
---

# UCP UI Toolkit

Focused micro-skill for the `ucp ui` command surface of the
Unity Control Protocol (`ucp`) CLI. Always confirm the live surface with
`ucp <cmd> --help` and see the docs at https://unityctl.dev.

## Examples

```bash
ucp ui list --root Assets/UI
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --query '#cards' --json
ucp ui screenshot Assets/UI/Inventory.ucp-ui.json --state populated -o artifacts/inventory.png --force
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --force --json
```

## When to use

Use on Unity 6+ after editing UXML/USS: lint for importer/schema feedback, inspect for resolved geometry and bindings, screenshot for visual judgment, and check for the full multi-state pass.

## When NOT to use (use the omni skill instead)

Do not use UI capture commands in batchmode or with the Null graphics device; `ucp ui lint` remains available there. Omit width/height to preserve a scenario viewport.

For broad, multi-surface Unity automation that spans several of these
command groups at once, use the `unity-control-protocol` omni skill instead
of this focused micro-skill.
