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

### `ucp scene load <path>`

Load a scene by path.

```bash
ucp scene load Assets/Scenes/Level1.unity
```

### `ucp snapshot`

Capture a complete hierarchy snapshot of the active scene. Returns all GameObjects with their instance IDs, names, positions, components, and parent-child relationships.

```bash
# Full snapshot
ucp snapshot

# Filter by name
ucp snapshot --filter "Player"

# Limit depth
ucp snapshot --depth 2

# JSON output for programmatic use
ucp snapshot --json
```

| Flag                 | Description             |
| -------------------- | ----------------------- |
| `--filter <pattern>` | Filter objects by name  |
| `--depth <n>`        | Maximum hierarchy depth |
| `--json`             | Output as JSON          |

**Example output:**

```
[OK] Active scene: SampleScene (3 root objects)
  Main Camera [46894] @ (0.0, 1.0, -10.0)
    Components: Transform, Camera, AudioListener
  Directional Light [46896] @ (0.0, 3.0, 0.0)
    Components: Transform, Light
  Player [46900] @ (0.0, 0.0, 0.0)
    Components: Transform, Rigidbody, PlayerController
```
