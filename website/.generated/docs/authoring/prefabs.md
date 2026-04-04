# Prefabs

Inspect and manage prefab instances and overrides in the scene.

## Commands

### `ucp prefab status`

Check whether a GameObject is a prefab instance and get its prefab asset path.

```bash
ucp prefab status --id -136722
```

**Output:**

```
[OK] Prefab instance of Assets/Prefabs/Agent.prefab (Connected)
```

### `ucp prefab apply`

Apply overrides from a prefab instance back to the prefab asset.

```bash
ucp prefab apply --id -136722 --save
```

This saves any modifications you've made on the instance (component values, added components, etc.) to the source prefab asset.

Mutating prefab commands wait for Unity to finish applying the prefab/scene/asset changes before returning.

### `ucp prefab revert`

Revert a prefab instance to match its source prefab, discarding any overrides.

```bash
ucp prefab revert --id -136722 --save
```

### `ucp prefab unpack`

Unpack a prefab instance, converting it into a regular GameObject. Use `--completely true` to fully unpack nested prefabs as well.

```bash
# Unpack one level
ucp prefab unpack --id -136722 --save

# Fully unpack (including nested prefabs)
ucp prefab unpack --id -136722 --completely true --save
```

### `ucp prefab create`

Create a new prefab asset from an existing GameObject in the scene.

```bash
ucp prefab create --id -136722 --path "Assets/Prefabs/NewPrefab.prefab" --save
```

| Flag                 | Description                          |
| -------------------- | ------------------------------------ |
| `--id <instanceId>`  | Instance ID of the source GameObject |
| `--path <assetPath>` | Where to save the new prefab asset   |

### `ucp prefab overrides`

List all property overrides on a prefab instance compared to its source prefab.

```bash
ucp prefab overrides --id -136722
```

**Output:**

```
[OK] 3 override(s) on Agent
  MeshRenderer.m_Enabled: True → False
  Transform.m_LocalPosition.x: 0 → 2.5
  Transform.m_LocalPosition.z: 0 → -1.3
```

## Notes

- Instance IDs can be negative (use quotes or `--` before negative values if needed)
- `apply` and `revert` only work on connected prefab instances
- `unpack` with `--completely true` recursively unpacks all nested prefabs
- `create` will overwrite an existing prefab at the target path
- `apply`, `revert`, `unpack`, and `create` follow the editor-settle policy before reporting success
- Add `--save` when the prefab operation should also persist the active scene immediately; otherwise the scene stays dirty until `ucp scene save`
