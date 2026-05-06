# Objects & Components

Inspect and modify GameObjects, components, and their properties in the active scene. Most commands require an `--id` flag with the instance ID of the target GameObject (use `ucp scene snapshot` to discover IDs). The snapshot command is intentionally shallow by default; detailed component decomposition lives here.

Use the object command family when you already know which object you want to inspect or mutate and want a narrow, predictable payload. The common workflow is:

1. Discover an instance ID with `ucp scene snapshot`.
2. Inspect the target with `ucp object get-children`, `get-fields`, or `get-property`.
3. Apply a change with one of the mutating commands.
4. Add `--save` if the scene edit should persist immediately.

## Commands

### `ucp object get-children`

List a GameObject's direct children, or include deeper descendants with `--depth`.

```bash
# Direct children only
ucp object get-children --id 46894

# Include grandchildren too
ucp object get-children --id 46894 --depth 2
```

| Flag                | Description                                  |
| ------------------- | -------------------------------------------- |
| `--id <instanceId>` | Instance ID of the target GameObject         |
| `--depth <levels>`  | Child hierarchy depth to include (default 1) |

This returns the same child metadata shape used by `ucp scene snapshot`, but scoped to one object so scripts do not need to crawl the whole active scene just to inspect a subtree.

**Human output:**

```text
[OK] PlayerRoot (46894): 2 child(ren)
  Showing hierarchy depth: 2
  - Body (id: 46910, children: 1, components: Transform, MeshRenderer)
    - WeaponSocket (id: 46911, children: 0, components: Transform)
  - CameraRig (id: 46920, children: 0, components: Transform, Camera)
```

**JSON shape:**

```json
{
  "success": true,
  "data": {
    "instanceId": 46894,
    "name": "PlayerRoot",
    "childCount": 2,
    "requestedDepth": 2,
    "children": [
      {
        "instanceId": 46910,
        "name": "Body",
        "depth": 1,
        "childCount": 1,
        "components": ["Transform", "MeshRenderer"],
        "children": [
          {
            "instanceId": 46911,
            "name": "WeaponSocket",
            "depth": 2,
            "childCount": 0,
            "components": ["Transform"]
          }
        ]
      }
    ],
    "stats": {
      "objectCount": 2,
      "componentCount": 3
    }
  }
}
```

`childCount` reports the target object's direct-child count even when `children` is empty because you asked for a shallower depth or the object currently has no children. `stats.objectCount` and `stats.componentCount` cover only the returned subtree beneath the target, not the target object itself.

### `ucp object get-fields`

List all serialized fields on a component.

```bash
ucp object get-fields --id 46894 --component Transform
```

**Output:**

```
[OK] Main Camera.Transform: 4 field(s)
  m_LocalRotation (Quaternion): [0,0,0,1]
  m_LocalPosition (Vector3): [0,1,-10]
  m_LocalScale (Vector3): [1,1,1]
  m_ConstrainProportionsScale (Boolean): false
```

| Flag                 | Description                                             |
| -------------------- | ------------------------------------------------------- |
| `--id <instanceId>`  | Instance ID of the target GameObject                    |
| `--component <type>` | Component type name (e.g. Transform, Camera, Rigidbody) |

### `ucp object get-property`

Read a single property value.

```bash
ucp object get-property --id 46894 --component Camera --property m_Depth
```

Use `get-property` when you already know the exact serialized field/property name and want the smallest possible payload. Use `get-fields` first when you need to discover available serialized members on a component.

### `ucp object set-property`

Write a property value. Values are provided as JSON.

```bash
# Set a boolean
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true --save

# Set a number
ucp object set-property --id 46894 --component Camera --property m_Depth --value "2"
```

| Flag                 | Description         |
| -------------------- | ------------------- |
| `--id <instanceId>`  | Target instance ID  |
| `--component <type>` | Component type      |
| `--property <name>`  | Property/field name |
| `--value <json>`     | Value as JSON       |

Common value forms:

- Scalars: `true`, `false`, `3`, `3.5`, `"Player Camera"`
- Arrays / vectors: `[0,1,-10]`
- Structured values: `{"x":0,"y":1,"z":2}` when a property expects an object-like payload

If the value is not valid JSON, the CLI falls back to passing it as a string.

Mutating object commands now wait for Unity to finish applying the scene/object change before returning, so follow-up automation sees a settled editor instead of deferred hierarchy or serialization work.

Add `--save` to any mutating object command when you want the active scene persisted immediately instead of left dirty.

### `ucp object set-active`

Enable or disable a GameObject.

```bash
ucp object set-active --id 46894 --active false --save
ucp object set-active --id 46894 --active true
```

### `ucp object set-name`

Rename a GameObject.

```bash
ucp object set-name --id 46894 --name "Player Camera" --save
```

### `ucp object create`

Create a new empty GameObject.

```bash
# Create at root
ucp object create "MyObject"

# Create as child
ucp object create "Child" --parent 46894 --save
```

New objects are created with a Transform component and become part of the active scene immediately.

### `ucp object delete`

Delete a GameObject and all its children.

```bash
ucp object delete --id -15774 --save
```

### `ucp object reparent`

Move a GameObject in the hierarchy.

```bash
# Move under a parent
ucp object reparent --id -15774 --parent 46894 --save

# Move to root
ucp object reparent --id -15774

# Set sibling index
ucp object reparent --id -15774 --parent 46894 --sibling-index 0
```

`--sibling-index` is optional and lets you control ordering among the new parent's existing children. Omit `--parent` to move the object back to the scene root.

### `ucp object instantiate`

Instantiate a prefab or clone a scene object.

```bash
# From prefab asset
ucp object instantiate "Assets/Prefabs/Enemy.prefab" --name "Enemy1"

# With parent
ucp object instantiate "Assets/Prefabs/UI/Button.prefab" --parent 46900 --save
```

`source` accepts either a prefab asset path or an existing scene object instance ID to clone.

### `ucp object add-component`

Add a component to a GameObject.

```bash
ucp object add-component --id -15774 --component BoxCollider --save
ucp object add-component --id -15774 --component Rigidbody
```

### `ucp object remove-component`

Remove a component from a GameObject.

```bash
ucp object remove-component --id -15774 --component BoxCollider --save
```

## Notes

- Instance IDs for newly created objects are negative numbers. UCP handles these correctly.
- All modifications are registered with Unity's Undo system.
- Use `ucp scene snapshot` to discover instance IDs for existing scene objects.
- Treat instance IDs as short-lived editor handles. Re-run `ucp scene snapshot` after compilation, domain reloads, package refreshes, scene loads, or test runs before issuing object-level commands.
- Use `ucp object get-children` when you already have an instance ID and want a targeted subtree read instead of a scene-wide snapshot.
- `ucp object get-children --depth 1` is the direct-child equivalent of a focused hierarchy probe; increase depth only when you need nested descendants, since larger subtrees produce correspondingly larger JSON payloads.
- `ucp object get-fields` in human mode intentionally prints only a bounded field list. Use `ucp object get-property` or `--json` when you need deeper inspection.
- `get-children`, `get-fields`, and `get-property` are read-only and return immediately.
- `set-property`, `set-active`, `set-name`, `create`, `delete`, `reparent`, `instantiate`, `add-component`, and `remove-component` all follow the editor-settle policy before reporting success.
- Use `--save` when the object edit should persist immediately; otherwise the active scene remains dirty until you run `ucp scene save`.
