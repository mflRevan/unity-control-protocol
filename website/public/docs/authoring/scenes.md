# Scenes

Manage scenes and capture hierarchy snapshots.

## Commands

### `ucp scene list`

List all scenes in the project.

```bash
ucp scene list
```

### `ucp scene active`

Get the currently active scene.

```bash
ucp scene active
```

### `ucp scene save`

Save the active scene explicitly.

```bash
ucp scene save
```

Use this after a series of scene edits when you want later disruptive commands such as `ucp compile`, `ucp run-tests`, `ucp editor restart`, `ucp install`, or package/build-target/define changes to proceed without dirty-scene blocking (`ucp play` and `ucp scene load` save a dirty scene themselves).

### `ucp scene focus`

Focus the Scene view camera on a GameObject. This is the recommended visual iteration loop for autonomous in-scene work: focus a target, capture a scene screenshot, adjust transforms or lighting, then focus and capture again.

```bash
# Frame the object with the current Scene view orientation
ucp scene focus --path "Level/Player"

# Align the Scene view to look from the positive X side
ucp scene focus --name Player --axis 1 0 0

# Negative axes are supported too; --id works as on every object command
ucp scene focus --id 46894 --axis 0 0 -1
```

| Flag           | Description                                               |
| -------------- | --------------------------------------------------------- |
| `--path <path>` | Target by hierarchy path (stable across reloads)         |
| `--name <name>` | Target by GameObject name (first match)                  |
| `--id <id>`    | Target GameObject instance ID                             |
| `--axis X Y Z` | Optional Scene view alignment direction toward the target |

### `ucp scene load <path>`

Load a scene by path.

```bash
ucp scene load Assets/Scenes/Level1.unity

# Keep the current scene setup loaded and add another scene
ucp scene load Assets/Scenes/Lighting.unity --additive
```

After `scene load`, UCP waits for Unity's scene-processing work to settle before returning so the newly loaded scene is ready for immediate inspection or follow-up edits.

If the active scene has unsaved changes, `scene load` saves it first; a dirty untitled scene is discarded unless `--keep-untitled` is passed. With `--no-save` the command refuses instead, with a concise dirty-scene summary and a non-zero exit code, so nothing is written behind your back.

| Flag              | Description                                                     |
| ----------------- | --------------------------------------------------------------- |
| `--additive`      | Load the scene additively instead of replacing the current setup |
| `--no-save`       | Do not auto-save dirty scenes before loading                    |
| `--keep-untitled` | Keep dirty untitled scenes instead of discarding them           |

### `ucp scene snapshot`

Capture a lean hierarchy snapshot of the active scene as an indented tree: one line per object with its name, instance id, component type names, and anything that is not the default (an inactive object, a tag, a non-Default layer, the number of children a collapsed subtree hides). By default only root objects are listed; `--depth` expands children and `--filter` searches the whole hierarchy. Use object-specific commands for full component/property inspection.

```bash
# Root objects only (default depth 0)
ucp scene snapshot

# Filter by name
ucp scene snapshot --filter "Player"

# Limit depth
ucp scene snapshot --depth 2

# JSON output for programmatic use
ucp scene snapshot --json
```

| Flag                 | Description                            |
| -------------------- | -------------------------------------- |
| `--filter <pattern>` | Case-insensitive name substring; searches the whole hierarchy and prints each match's `path` |
| `--depth <n>`        | Maximum hierarchy depth without a filter (default: `0`, roots only) |
| `--json`             | Output as JSON; entries carry `instanceId`, `name`, `path`, `components`, and `active`/`tag`/`layer`/`childCount` only when not the default |

**Example output:**

```
[OK] SampleScene: 3 root object(s); --depth N expands children, --filter NAME searches
  Main Camera #46894 tag:MainCamera · Camera, AudioListener
  Directional Light #46896 · Light
  Player #46900 tag:Player [4 children] · Rigidbody, PlayerController
```

`Transform` is implied on every object and left out of the component list.

### `ucp scene query`

Query the active scene hierarchy without piping the full snapshot through an external JSON tool.

```bash
# Find a camera by name and return only the fields an agent needs
ucp scene query "name=XRCamera" --fields active,components

# Find all active Camera objects under the first 8 hierarchy levels
ucp scene query "component=Camera and active=true" --fields instanceId,name,active,components --depth 8
```

Supported query keys are `name`, `component`, `active`, `tag`, and `layer`. Multiple terms can be joined with `and` or commas. `--fields` is a comma-separated selector; when omitted, UCP returns `instanceId,name,active,components`.
