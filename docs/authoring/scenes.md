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

Use this after a series of scene edits when you want later disruptive commands such as `ucp play`, `ucp compile`, `ucp scene load`, `ucp editor restart`, or package/build-target/define changes to proceed without dirty-scene blocking.

### `ucp scene focus`

Focus the Scene view camera on a GameObject. This is the recommended visual iteration loop for autonomous in-scene work: focus a target, capture a scene screenshot, adjust transforms or lighting, then focus and capture again.

```bash
# Frame the object with the current Scene view orientation
ucp scene focus --id 46894

# Align the Scene view to look from the positive X side
ucp scene focus --id 46894 --axis 1 0 0

# Negative axes are supported too
ucp scene focus --id 46894 --axis 0 0 -1
```

| Flag           | Description                                               |
| -------------- | --------------------------------------------------------- |
| `--id <id>`    | Target GameObject instance ID                             |
| `--axis X Y Z` | Optional Scene view alignment direction toward the target |

### `ucp scene load <path>`

Load a scene by path.

```bash
ucp scene load Assets/Scenes/Level1.unity
```

After `scene load`, UCP waits for Unity's scene-processing work to settle before returning so the newly loaded scene is ready for immediate inspection or follow-up edits.

If the active scene has unsaved changes, `scene load` now fails before the transition and reports a concise dirty-scene summary. Save first with `ucp scene save`, or rerun your scene-editing command with `--save`.

### `ucp scene snapshot`

Capture a lean hierarchy snapshot of the active scene. By default this returns only root objects with lightweight metadata such as instance ID, name, active state, tags, layers, child counts, and component type names. Use `--depth` to expand into children. Use object-specific commands for full component/property inspection.

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
| `--filter <pattern>` | Filter objects by name                 |
| `--depth <n>`        | Maximum hierarchy depth (default: `0`) |
| `--json`             | Output as JSON                         |

**Example output:**

```
[OK] Active scene: SampleScene (3 root objects)
  Main Camera [46894] children=0 tag=Untagged layer=Default components=Transform, Camera, AudioListener
  Directional Light [46896] children=0 tag=Untagged layer=Default components=Transform, Light
  Player [46900] children=4 tag=Player layer=Default components=Transform, Rigidbody, PlayerController
```
