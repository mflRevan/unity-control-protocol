---
name: ucp-ui-toolkit
description: >-
  Author and verify Unity UI Toolkit interfaces (UXML, USS, TSS) with `ucp ui`: discover
  documents and scenarios, lint with Unity's own importers, instantiate a document in an isolated
  editor panel, inspect its resolved layout and bindings, populate lists and repeaters from JSON
  scenarios, and capture deterministic PNGs. Use after editing UXML/USS or when the user wants a
  UI checked or screenshotted without loading a scene. Requires Unity 6 or newer.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`), the UCP bridge package, and Unity 6000.0 or newer in the target project. inspect/screenshot/check need an interactive editor with a graphics device; lint also works headless.
metadata:
  author: mflRevan
  version: '0.6.4'
  homepage: https://unityctl.dev/skills/ucp-ui-toolkit
---

# UI Toolkit authoring loop

The loop is: edit UXML/USS on disk, `lint` (fast importer and schema pass), `inspect` (resolved
tree, layout, bindings), `screenshot` (visual evidence), `check` (all of it across every state,
with cleanup). Nothing here loads or dirties a scene; the harness opens a transient editor panel,
renders, and closes it.

## Discover

```bash
ucp ui list --root Assets/UI
ucp ui list --include-packages --limit 50 --json
```

Lists `.uxml` documents and `.ucp-ui.json` scenarios. Invalid scenarios stay in the list with
`valid: false` and a diagnostic, so discovery works while you are still authoring one.

## Lint

```bash
ucp ui lint Assets/UI
ucp ui lint Assets/UI/Inventory.uxml Assets/UI/Inventory.uss
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings --max-diagnostics 200
```

Runs Unity's synchronous UXML, USS, and TSS importers, reads their logs and flags, follows
dependencies, and clone-instantiates each document under a scoped log capture, which is what
catches an unknown element that imports cleanly and only fails when instantiated. Errors fail the
command; warnings fail it only with `--fail-on-warnings`. Diagnostics carry the asset path and
line. Assets in immutable packages are inspected from their existing import without a reimport.

## Inspect

```bash
ucp ui inspect Assets/UI/Inventory.uxml --query '#toolbar' --depth 4 --max-elements 100
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --query '#cards' --json
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state empty --detail verbose
```

- Instantiates the target in an isolated panel, waits for stable finite layout, and returns a
  bounded snapshot: names, classes, text/value, layout, world bounds, a fixed resolved-style
  allowlist, binding results, and dynamic-collection counts.
- `--query` takes one simple selector: `#name`, `.class`, or an element type. Compound selectors
  such as `.a.b` are rejected rather than silently matching nothing.
- Traversal includes the physical children of controls, including realized `ListView` rows.
  Internal containers consume depth, so row labels can sit eight levels deep; use `--query
  '#row-label'` or a deeper `--depth`. `truncated: true` means a depth or element cap was hit.

## Screenshot

```bash
ucp ui screenshot Assets/UI/Inventory.uxml --width 960 --height 640 -o artifacts/inventory.png
ucp ui screenshot Assets/UI/Inventory.ucp-ui.json --state populated -o artifacts/populated.png --force
```

Captures after three identical geometry samples and two identical pixel samples (a scenario can
override with `settle`). Without `-o` the bridge keeps the PNG under `Library/UCP/UiCaptures` and
returns its path and pixel hash. `--width` and `--height` go together; omit both to keep a
scenario's viewport (direct UXML defaults to 960x640).

## Check

```bash
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --force --json
ucp ui check Assets/UI/Inventory.ucp-ui.json --state populated --fail-on-warnings
```

Lint, instantiate, inspect, audit, and capture in one bridge-owned operation. The audit reports
binding failures as errors and, as warnings, focusable elements with zero size or outside the
viewport and duplicated static names (rows inside managed collections are exempt). Counts are
complete even when the returned detail list is capped at 200. Exit code is non-zero when
`passed` is false.

## Scenarios (`.ucp-ui.json`)

```json
{
  "schemaVersion": 0,
  "document": "./Inventory.uxml",
  "defaultState": "populated",
  "viewport": { "width": 960, "height": 640 },
  "data": { "title": "Inventory" },
  "states": {
    "empty": {
      "data": { "items": [] },
      "set": [ { "target": "#empty-message", "property": "display", "value": "flex" } ]
    },
    "populated": {
      "data": { "items": [ { "name": "Potion", "countLabel": "x3" }, { "name": "Key", "countLabel": "x1" } ] },
      "collections": [
        { "target": "#cards", "mode": "repeat", "source": "/items", "template": "./InventoryCard.uxml" },
        { "target": "#rows", "mode": "list-view", "source": "/items", "template": "./InventoryRow.uxml", "itemHeight": 28 }
      ]
    }
  }
}
```

- Unknown fields, ambiguous selectors, and out-of-range values are rejected with a location such
  as `Inventory.ucp-ui.json#states.populated.set[1].value`.
- `set` allows only `text`, `value`, `enabled`, `display` (`flex`/`none`), `visibility`
  (`visible`/`hidden`), `tooltip`, and `class:<name>` (boolean). Targets are `:root`, `#name`, or
  `.class` and must match exactly one element.
- `collections`: `repeat` clones the template per item (small grids); `list-view` assigns
  `itemsSource` and owns `makeItem`/`bindItem` for a real virtualized `ListView` (`itemHeight`
  selects fixed-height virtualization). `source` is a JSON pointer such as `/items`.
- Bindings are ordinary UXML `DataBinding` elements; the harness rewrites their paths to the
  JSON dictionary keys after cloning. Each item receives one array element as its data source.
- `--data-json '{"title":"Shop"}'` or `--data-file data.json` overlay data for one run. Top-level
  data is deep-merged with state data, then with the overlay.

## Workflow

```bash
# edit Assets/UI/Inventory.uxml and .uss locally
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --query '#cards' --json
ucp ui screenshot Assets/UI/Inventory.ucp-ui.json --state populated -o artifacts/populated.png --force
# look at the PNG, adjust, repeat; before handing off:
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --force --json
```

## Pitfalls

- Render commands briefly open and focus a transient utility window. They refuse to run in batch
  mode or on the Null graphics device; `lint` still works there.
- Operations are asynchronous in the bridge with a 300 s overall ceiling; the CLI timeout for
  `inspect`, `screenshot`, and `check` defaults to 310 s. A domain reload mid-operation returns a
  structured `editor_shutdown` error, and a lost completion is recovered through `ui/status`.
- JSON integers within 32 bits are normalized for controls such as `IntegerField`; wider ones
  are kept as 64-bit and flagged with a warning.
