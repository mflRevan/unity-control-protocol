# Settings

Read and modify Unity project settings: PlayerSettings, QualitySettings, Physics, Lighting, Tags, and Layers.

## Commands

### `ucp settings player`

Read PlayerSettings values. Returns company name, product name, default icon, and all `#define` symbols.

```bash
ucp settings player
```

**Output:**

```
[OK] PlayerSettings
  Company: DefaultCompany
  Product: Flux
  Version: 0.2.0
  Defines: UNITY_POST_PROCESSING, ODIN_INSPECTOR
```

### `ucp settings set-player`

Modify a single PlayerSettings field.

```bash
ucp settings set-player --key companyName --value '"MyStudio"'
ucp settings set-player --key productName --value '"MyGame"'
ucp settings set-player --key bundleVersion --value '"1.0.0"'
```

| Flag            | Description                         |
| --------------- | ----------------------------------- |
| `--key <name>`  | Setting key to modify               |
| `--value <json>`| New value serialized as JSON        |

### `ucp settings quality`

Read QualitySettings for the active quality level.

```bash
ucp settings quality
```

**Output:**

```
[OK] QualitySettings (level 5: Ultra)
  VSyncCount: 1
  AntiAliasing: 4
  ShadowDistance: 150
  ...
```

### `ucp settings set-quality`

Modify quality settings.

```bash
ucp settings set-quality --key vSyncCount --value 0
ucp settings set-quality --key shadowDistance --value 100
```

### `ucp settings physics`

Read Physics settings (gravity, timestep, layer collision matrix, etc.).

```bash
ucp settings physics
```

### `ucp settings set-physics`

Modify physics settings.

```bash
ucp settings set-physics --key gravity --value "[0,-9.81,0]"
ucp settings set-physics --key defaultSolverIterations --value 12
```

### `ucp settings lighting`

Read the active scene's lighting/render settings (ambient, fog, skybox, etc.).

```bash
ucp settings lighting
```

### `ucp settings set-lighting`

Modify lighting settings for the active scene.

```bash
ucp settings set-lighting --key ambientIntensity --value 1.2
ucp settings set-lighting --key fog --value true
```

### `ucp settings tags-layers`

List all tags and sorting layers in the project.

```bash
ucp settings tags-layers
```

**Output:**

```
[OK] Tags & Layers
  Tags: Untagged, Respawn, Finish, EditorOnly, Player, Enemy, Pickup
  Sorting Layers: Default, Background, Foreground, UI
```

### `ucp settings add-tag`

Add a new tag to the project.

```bash
ucp settings add-tag "Interactable"
```

### `ucp settings add-layer`

Add a Unity layer to the project.

```bash
ucp settings add-layer "VFX"
ucp settings add-layer "Gameplay" --index 12
```

## Notes

- `player` and `quality` read project-wide settings, not per-scene
- `lighting` reads the active scene's render settings
- Tags and layers are project-wide and persist across scenes
- Changes made via `set-*` commands take effect immediately in the editor
