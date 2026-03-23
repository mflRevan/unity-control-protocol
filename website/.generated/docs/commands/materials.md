# Materials

Inspect and modify material properties, shader keywords, and shaders.

## Commands

### `ucp material create <path>`

Create a new material asset.

```bash
ucp material create "Assets/Materials/Agent.mat"
ucp material create "Assets/Materials/FXGlow.mat" --shader "Universal Render Pipeline/Lit"
```

If `--shader` is omitted, UCP prefers a common lit shader available in the project and still waits for Unity to finish the resulting asset import before returning.

### `ucp material get-properties`

List all properties on a material, including their types and current values.

```bash
ucp material get-properties --path "Assets/Materials/Agent.mat"
```

**Output:**

```
[OK] Agent (Universal Render Pipeline/Lit): 50 properties
  _BaseColor (Color): [1,0.44,0.27,1]
  _Metallic (Range): 0.091
  _Smoothness (Range): 0
  _BumpScale (Float): 1
  ...
```

### `ucp material get-property`

Read a specific material property.

```bash
ucp material get-property --path "Assets/Materials/Agent.mat" --property _BaseColor
```

### `ucp material set-property`

Modify a material property value.

```bash
# Set a float
ucp material set-property --path "Assets/Materials/Agent.mat" --property _Metallic --value "0.5"

# Set a color (RGBA)
ucp material set-property --path "Assets/Materials/Agent.mat" --property _BaseColor --value "[1,0,0,1]"
```

| Flag                 | Description                                  |
| -------------------- | -------------------------------------------- |
| `--path <assetPath>` | Path to the material asset                   |
| `--property <name>`  | Property name (e.g. \_BaseColor, \_Metallic) |
| `--value <json>`     | New value as JSON                            |

Mutating material commands wait for Unity to finish applying the material/shader-side change before returning.

### `ucp material keywords`

List enabled shader keywords on a material.

```bash
ucp material keywords --path "Assets/Materials/Agent.mat"
```

**Output:**

```
[OK] 1 keyword(s) enabled
  _SPECULAR_SETUP
```

### `ucp material set-keyword`

Enable or disable a shader keyword.

```bash
ucp material set-keyword --path "Assets/Materials/Agent.mat" --keyword _EMISSION --enabled true
```

### `ucp material set-shader`

Change the shader used by a material.

```bash
ucp material set-shader --path "Assets/Materials/Agent.mat" --shader "Standard"
```

## Common Property Names

| Property                      | Type    | Description          |
| ----------------------------- | ------- | -------------------- |
| `_BaseColor` / `_Color`       | Color   | Main color           |
| `_MainTex` / `_BaseMap`       | Texture | Main texture         |
| `_Metallic`                   | Range   | Metallic value (0-1) |
| `_Smoothness` / `_Glossiness` | Range   | Smoothness (0-1)     |
| `_BumpMap`                    | Texture | Normal map           |
| `_EmissionColor`              | Color   | Emission color       |
| `_Cutoff`                     | Range   | Alpha cutoff         |
