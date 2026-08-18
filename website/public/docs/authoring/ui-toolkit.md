# UI Toolkit

`ucp ui` gives agents a tight authoring loop for UI Toolkit UXML and USS: discover targets, lint with Unity's importers, inspect a live resolved tree, capture a PNG, or run the full check in one command.

The harness requires Unity 6 or newer. `ui lint` works in batch mode and with the Null graphics device; `ui inspect`, `ui screenshot`, and `ui check` require an interactive Editor with a graphics device.

## Commands

```bash
ucp ui list
ucp ui lint Assets/UI/Inventory.uxml Assets/UI/Inventory.uss
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --json
ucp ui screenshot Assets/UI/Inventory.uxml --width 960 --height 640 -o inventory.png
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui
```

Every path must resolve to a location under `Assets/` or `Packages/`; absolute and scenario-relative paths are accepted as long as they land there. A render target can be either a `.uxml` asset or a strict `.ucp-ui.json` scenario.

### `ucp ui list`

Discovers UXML documents and scenarios without instantiating them.

```bash
ucp ui list --root Assets/UI --limit 50
ucp ui list --include-packages --json
```

Invalid scenarios remain in the result with `valid: false` and a structured diagnostic, which makes discovery useful while a fixture is still being authored.

### `ucp ui lint <paths...>`

Runs Unity's synchronous UXML and USS importers, reads their import logs and flags, resolves dependencies, and clones each UXML tree while capturing scoped Unity diagnostics. The clone step matters because an unknown UXML element can import without an error and fail only when Unity tries to instantiate it.

```bash
ucp ui lint Assets/UI
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings --max-diagnostics 200
```

A scenario lint also validates its schema and lints its document, USS dependencies, and collection templates. Errors fail the command. Warnings fail only with `--fail-on-warnings`.

### `ucp ui inspect <target>`

Instantiates the target in an isolated Editor panel, waits for finite stable layout, and returns a bounded semantic snapshot. It includes names, classes, text/value, layout, world bounds, a fixed resolved-style allowlist, binding results, and dynamic-collection counts.

```bash
ucp ui inspect Assets/UI/Inventory.uxml --query '#toolbar' --depth 4 --max-elements 100
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state empty --detail verbose --json
```

`--query` accepts one simple `#name`, `.class`, or element-type selector. Detail is `summary`, `normal`, or `verbose`. The snapshot reports actual UI Toolkit state; it does not claim matched-USS-rule provenance or an accessibility tree.

### `ucp ui screenshot <target>`

Captures the resolved Editor panel after stable geometry and pixel samples. The defaults are three identical finite geometry samples and two identical pixel samples; a scenario can override them with `settle`.

```bash
ucp ui screenshot Assets/UI/Inventory.ucp-ui.json --state populated -o artifacts/inventory.png
ucp ui screenshot Assets/UI/Inventory.uxml --width 1280 --height 720 -o artifacts/wide.png --force
```

Without `--out`, the bridge retains the PNG under `Library/UCP/UiCaptures` and returns its path and pixel hash. `--out` copies that artifact without overwriting an existing file unless `--force` is explicit. Width and height must be supplied together; if omitted, a scenario's viewport is preserved and direct UXML uses `960x640`.

The capture backend briefly opens and focuses a transient utility window, then closes it and restores the previously focused window. It does not load or dirty a scene. Use a longer global `--timeout` for scenarios with many states or deliberately slow deferred UI updates.

### `ucp ui check <target>`

Runs fixture validation and lint, then instantiate, settle, inspect, conservative audits, capture, and cleanup as one bridge-owned operation.

```bash
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --json
ucp ui check Assets/UI/Inventory.ucp-ui.json --state populated --fail-on-warnings
```

The v0 audits treat binding/application failures as errors. They warn for focusable zero-size controls, focusable controls outside the viewport, and duplicate element names outside harness-managed collections. They intentionally avoid speculative claims such as unused selectors or contrast failures. `--fail-on-warnings` makes both lint and audit warnings fail the command.

## Scenario format

A scenario makes dynamic UI states reproducible without requiring a compiled code-behind type:

```json
{
  "schemaVersion": 0,
  "document": "./Inventory.uxml",
  "defaultState": "populated",
  "viewport": { "width": 960, "height": 640 },
  "settle": {
    "stableFrames": 3,
    "pixelStableFrames": 2,
    "timeoutSeconds": 15
  },
  "data": { "title": "Inventory" },
  "states": {
    "empty": {
      "data": { "items": [] },
      "set": [
        { "target": "#empty-message", "property": "display", "value": "flex" }
      ]
    },
    "populated": {
      "data": {
        "items": [
          { "name": "Potion", "countLabel": "3" },
          { "name": "Key", "countLabel": "1" }
        ]
      },
      "collections": [
        {
          "target": "#cards",
          "mode": "repeat",
          "source": "/items",
          "template": "./InventoryCard.uxml"
        },
        {
          "target": "#rows",
          "mode": "list-view",
          "source": "/items",
          "template": "./InventoryRow.uxml",
          "itemHeight": 32
        }
      ]
    }
  }
}
```

The schema rejects unknown fields and paths outside the project. `states` is required and must contain 1–64 states. `defaultState` is optional only when a state is named `default` or the fixture has exactly one state; otherwise it is required. Viewports allow 1–8192 pixels per axis and at most 8,388,608 total pixels. A render operation has a 300-second enqueue-to-completion ceiling across all selected states. `document` and collection `template` paths are relative to the scenario unless they begin with `Assets/` or `Packages/`. Top-level data is deeply overlaid by state data and then by `--data-json` or `--data-file`.

`set` supports the deliberately small property allowlist `text`, `value`, `enabled`, `display`, `visibility`, `tooltip`, and `class:<class-name>`. Targets are `:root`, `#name`, or `.class` and must resolve to one element.

## Data binding and collections

Author bindings as ordinary UI Toolkit paths. The harness adapts those paths to its JSON dictionary data source after cloning:

```xml
<ui:Label name="row-name">
  <Bindings>
    <ui:DataBinding property="text"
                    data-source-path="name"
                    binding-mode="ToTarget" />
  </Bindings>
</ui:Label>
```

Each collection `source` is an RFC 6901-style JSON pointer into the resolved data, such as `/items` or `/inventory/rows`, and must resolve to an array. Each `repeat` or `list-view` item receives one array value as its data source, so the same row UXML works in either mode.

- `repeat` eagerly clones every row. Use it for small flex-wrap grids and layouts where every item must exist at once.
- `list-view` assigns `itemsSource` and owns `makeItem`, `bindItem`, and `unbindItem`. It uses dynamic-height virtualization by default; setting `itemHeight` selects fixed-height virtualization. It reports logical, realized, bound, and currently visible row counts.

JSON integers that fit in 32 bits are normalized for controls such as `IntegerField`. Larger integers remain 64-bit and produce a warning because some UI Toolkit controls cannot bind them automatically.

## Recommended agent loop

```bash
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --query '#cards' --json
ucp ui screenshot Assets/UI/Inventory.ucp-ui.json --state populated -o artifacts/inventory.png --force
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --force --json
```

Use lint for the fast import/schema pass, inspect to reason about resolved geometry and bindings, and screenshot for visual judgment. Run check before handing off the UI.
