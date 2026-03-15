# Objects & Components

Inspect and modify GameObjects, components, and their properties in the active scene. Most commands require an `--id` flag with the instance ID of the target GameObject (use `ucp scene snapshot` to discover IDs). The snapshot command is intentionally shallow by default; detailed component decomposition lives here.

## Commands

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

### `ucp object set-property`

Write a property value. Values are provided as JSON.

```bash
# Set a boolean
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true

# Set a number
ucp object set-property --id 46894 --component Camera --property m_Depth --value "2"
```

| Flag                 | Description         |
| -------------------- | ------------------- |
| `--id <instanceId>`  | Target instance ID  |
| `--component <type>` | Component type      |
| `--property <name>`  | Property/field name |
| `--value <json>`     | Value as JSON       |

### `ucp object set-active`

Enable or disable a GameObject.

```bash
ucp object set-active --id 46894 --active false
ucp object set-active --id 46894 --active true
```

### `ucp object set-name`

Rename a GameObject.

```bash
ucp object set-name --id 46894 --name "Player Camera"
```

### `ucp object create`

Create a new empty GameObject.

```bash
# Create at root
ucp object create "MyObject"

# Create as child
ucp object create "Child" --parent 46894
```

### `ucp object delete`

Delete a GameObject and all its children.

```bash
ucp object delete --id -15774
```

### `ucp object reparent`

Move a GameObject in the hierarchy.

```bash
# Move under a parent
ucp object reparent --id -15774 --parent 46894

# Move to root
ucp object reparent --id -15774

# Set sibling index
ucp object reparent --id -15774 --parent 46894 --sibling-index 0
```

### `ucp object instantiate`

Instantiate a prefab or clone a scene object.

```bash
# From prefab asset
ucp object instantiate "Assets/Prefabs/Enemy.prefab" --name "Enemy1"

# With parent
ucp object instantiate "Assets/Prefabs/UI/Button.prefab" --parent 46900
```

### `ucp object add-component`

Add a component to a GameObject.

```bash
ucp object add-component --id -15774 --component BoxCollider
ucp object add-component --id -15774 --component Rigidbody
```

### `ucp object remove-component`

Remove a component from a GameObject.

```bash
ucp object remove-component --id -15774 --component BoxCollider
```

## Notes

- Instance IDs for newly created objects are negative numbers. UCP handles these correctly.
- All modifications are registered with Unity's Undo system.
- Use `ucp scene snapshot` to discover instance IDs for existing scene objects.
- Treat instance IDs as short-lived editor handles. Re-run `ucp scene snapshot` after compilation, domain reloads, package refreshes, scene loads, or test runs before issuing object-level commands.
- `ucp object get-fields` in human mode intentionally prints only a bounded field list. Use `ucp object get-property` or `--json` when you need deeper inspection.
