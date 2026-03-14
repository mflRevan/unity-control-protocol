# Assets

Search, inspect, and manage project assets. Works with materials, textures, ScriptableObjects, and any asset type Unity recognizes.

## Commands

### `ucp asset search`

Search for assets by type and/or name.

```bash
# Find all materials
ucp asset search -t Material

# Find by name
ucp asset search -n "Player"

# Filter by folder
ucp asset search -t Prefab -p "Assets/Prefabs"

# Limit results
ucp asset search -t Texture2D --max 10
```

| Flag                | Description                                    |
| ------------------- | ---------------------------------------------- |
| `-t, --type <type>` | Asset type (Material, Texture2D, Prefab, etc.) |
| `-n, --name <name>` | Name filter                                    |
| `-p, --path <path>` | Folder path filter                             |
| `--max <n>`         | Maximum results (default: 50)                  |

### `ucp asset info <path>`

Get metadata about an asset.

```bash
ucp asset info "Assets/Materials/Agent.mat"
```

**Output:**

```
[OK] Agent (Material)
  Path: Assets/Materials/Agent.mat
  GUID: adbf4a7415ede7c42ada304e953520f6
```

### `ucp asset read <path>`

Read serialized fields from an asset.

```bash
# Read all fields
ucp asset read "Assets/Materials/Agent.mat"

# Read a specific field
ucp asset read "Assets/Materials/Agent.mat" --field m_Shader
```

| Flag             | Description            |
| ---------------- | ---------------------- |
| `--field <name>` | Specific field to read |

### `ucp asset write <path>`

Modify a field on an asset.

```bash
ucp asset write "Assets/Configs/GameConfig.asset" --field maxPlayers --value "8"
ucp asset write "Assets/Configs/GameConfig.asset" --field icon --value '{"path":"Assets/UI/GameIcon.png"}'
```

Object reference fields accept:

- `null`
- an `instanceId`
- an asset `path`
- an asset `guid`

Invalid references now fail explicitly instead of silently no-oping.

### `ucp asset write-batch <path>`

Modify multiple serialized fields on an asset in one request.

```bash
ucp asset write-batch "Assets/Configs/GameConfig.asset" --values '{"maxPlayers":8,"spawnDelay":1.5}'
ucp asset write-batch "Assets/Configs/GameConfig.asset" --values '{"icon":{"path":"Assets/UI/GameIcon.png"}}'
```

| Flag              | Description                          |
| ----------------- | ------------------------------------ |
| `--values <json>` | JSON object of field/value pairs     |

### `ucp asset create-so`

Create a new ScriptableObject asset.

```bash
ucp asset create-so -t GameConfig "Assets/Configs/NewConfig.asset"
```

| Flag                | Description                 |
| ------------------- | --------------------------- |
| `-t, --type <type>` | ScriptableObject class name |
