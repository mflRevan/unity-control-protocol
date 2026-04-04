# Assets

Search, inspect, and manage project assets. Works with materials, textures, ScriptableObjects, and any asset type Unity recognizes.

For imported assets such as textures, models, audio, and similar Unity-managed files, use the importer-settings commands instead of hand-editing `.meta` files. Importer writes reimport automatically by default so the changes are applied immediately.

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

| Flag              | Description                      |
| ----------------- | -------------------------------- |
| `--values <json>` | JSON object of field/value pairs |

### `ucp asset create-so`

Create a new ScriptableObject asset.

```bash
ucp asset create-so -t GameConfig "Assets/Configs/NewConfig.asset"
```

| Flag                | Description                 |
| ------------------- | --------------------------- |
| `-t, --type <type>` | ScriptableObject class name |

### `ucp asset delete <path>`

Delete an asset or folder through Unity's asset database.

```bash
ucp asset delete "Assets/UcpTemp/UcpPrefabVariantSmoke.prefab"
ucp asset delete "Assets/UcpTemp"
```

Use this instead of deleting Unity-managed assets directly on disk when the asset is already known to the editor. That keeps the asset database, meta handling, and import lifecycle coherent.

### `ucp asset move <path> <destination>`

Move or rename an asset or folder through Unity's asset database. This preserves the existing `.meta` file and GUID, so scene references, prefab references, ScriptableObject references, and other serialized links stay intact.

```bash
# Rename an asset
ucp asset move "Assets/Configs/GameConfig.asset" "Assets/Configs/GameConfig.Legacy.asset"

# Move an asset into a different folder (folders are created automatically)
ucp asset move "Assets/Textures/HUD.png" "Assets/UI/Textures/HUD.png"

# Move into an existing folder while keeping the same file name
ucp asset move "Assets/Prefabs/Enemy.prefab" "Assets/Archive/"

# Move a whole folder
ucp asset move "Assets/OldEnvironment" "Assets/Archive/OldEnvironment"
```

**Notes:**

- Moves are currently supported for paths under `Assets/`.
- If the destination folder does not exist, UCP creates it automatically.
- If the destination already exists, the move fails explicitly.
- UCP waits for Unity to finish processing the move before the command returns.

### `ucp asset bulk-move`

Move multiple assets or folders in one ordered batch. This is useful for cleanup, renames, and larger refactors where you want Unity to preserve GUIDs and keep references intact across all moved assets.

```bash
# Ordered array form
ucp asset bulk-move --moves '[
  {"from":"Assets/Legacy/Player.prefab","to":"Assets/Characters/Player.prefab"},
  {"from":"Assets/Legacy/Player.mat","to":"Assets/Characters/Materials/Player.mat"}
]'

# Object map form
ucp asset bulk-move --moves '{
  "Assets/Legacy/Enemy.prefab":"Assets/Characters/Enemy.prefab",
  "Assets/Legacy/Enemy.mat":"Assets/Characters/Materials/Enemy.mat"
}'

# Best-effort cleanup pass
ucp asset bulk-move --moves '[
  {"from":"Assets/Temp/A.asset","to":"Assets/Archive/A.asset"},
  {"from":"Assets/Temp/B.asset","to":"Assets/Archive/B.asset"}
]' --continue-on-error
```

| Flag                    | Description                                                   |
| ----------------------- | ------------------------------------------------------------- |
| `--moves <json>`        | JSON array of `{from,to}` entries or an object map            |
| `--continue-on-error`   | Keep processing later entries after an individual move fails  |

**Notes:**

- Bulk moves execute in the order provided.
- Without `--continue-on-error`, UCP stops on the first failed move.
- Earlier successful moves are not rolled back automatically.
- Use `--json` when you want per-entry success/error details for larger refactors.

### `ucp asset reimport <path>`

Force Unity to reimport a specific asset. The path may point to either the asset itself or its `.meta` file.

```bash
ucp asset reimport "Assets/Models/Agent.fbx"
ucp asset reimport "Assets/Textures/HUD.png.meta"
```

Use this when you intentionally skipped an automatic reimport, or when you updated an asset on disk outside the importer-settings workflow and want Unity to apply it immediately. UCP waits for Unity to finish the resulting asset-processing work before the command returns.

### `ucp asset import-settings read <path>`

Read importer settings from an imported asset. The path may point to either the asset or its `.meta` file.

```bash
# Read all visible importer settings
ucp asset import-settings read "Assets/Models/Agent.fbx"

# Read one specific importer property
ucp asset import-settings read "Assets/Textures/HUD.png" --field m_IsReadable
```

| Flag             | Description                                                  |
| ---------------- | ------------------------------------------------------------ |
| `--field <name>` | Specific importer field/property path (reads all if omitted) |

### `ucp asset import-settings write <path>`

Modify one importer setting on an imported asset.

```bash
ucp asset import-settings write "Assets/Textures/HUD.png" --field m_IsReadable --value true
ucp asset import-settings write "Assets/Models/Agent.fbx" --field m_GlobalScale --value 0.5
```

Importer writes reimport the asset automatically by default so Unity applies the updated import settings immediately. By default, UCP also waits for Unity to finish the resulting import work so the editor is ready for follow-up commands right away.

| Flag                  | Description                                                     |
| --------------------- | --------------------------------------------------------------- |
| `--field <name>`      | Importer field/property path                                    |
| `--value <json>`      | Value as JSON                                                   |
| `--no-reimport`       | Update importer settings without immediately reimporting        |

### `ucp asset import-settings write-batch <path>`

Modify multiple importer settings in one request.

```bash
ucp asset import-settings write-batch "Assets/Textures/HUD.png" --values '{"m_IsReadable":true,"m_TextureType":8}'
ucp asset import-settings write-batch "Assets/Models/Agent.fbx" --values '{"m_GlobalScale":0.5,"m_ImportBlendShapes":false}'
```

| Flag                  | Description                                              |
| --------------------- | -------------------------------------------------------- |
| `--values <json>`     | JSON object of importer field/value pairs                |
| `--no-reimport`       | Update importer settings without immediately reimporting |

Batch importer writes follow the same settle behavior: unless you pass `--no-reimport`, UCP waits for Unity to finish applying the importer changes before returning.
