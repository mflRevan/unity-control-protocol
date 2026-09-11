---
name: ucp-scene-authoring
description: >-
  Build and inspect scene content in the live Unity Editor with `ucp scene`, `ucp object`,
  `ucp transform`, `ucp spatial`, and `ucp prefab`: snapshot and query the hierarchy, create
  visible primitives, add components, set serialized properties, move/rotate/scale/look-at
  objects, raycast and ground them, and turn hierarchies into prefabs. Use when the user wants
  something placed, arranged, wired, or measured in a scene. For assets on disk use ucp-assets;
  for what the scene looks like use ucp-visual-feedback.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer.
metadata:
  author: mflRevan
  version: '0.6.3'
  homepage: https://unityctl.dev/skills/ucp-scene-authoring
---

# Scene authoring: hierarchy, objects, transforms, space, prefabs

Everything here happens in the open scene of the running editor and registers with Unity's Undo.
Changes are in memory until saved: pass `--save` on a mutating command, or run `ucp scene save`.

## Find things first

```bash
ucp scene active                                  # name, path, dirty, root count
ucp scene list                                    # scenes in build settings
ucp scene snapshot                                # root objects with instance ids (lean on purpose)
ucp scene snapshot --filter Player --depth 3      # substring filter, deeper hierarchy
ucp scene query 'component=Camera' --fields instanceId,name,active
ucp scene query 'name=Enemy' --depth 8
ucp object get-children --id 46894 --depth 2
```

- The snapshot is shallow by default (`--depth 0` = roots) to keep payloads small; deepen only
  where you need to.
- Ids change after recompiles, reloads, scene loads, and test runs. Re-snapshot rather than
  reuse. `transform`, `spatial`, and `view` commands also accept `--path "Root/Child/Leaf"` and
  `--name`, which survive reloads (`--name` is the first match; ambiguous when names repeat).

## Load and save scenes

```bash
ucp scene load Assets/Scenes/Level1.unity            # saves dirty titled scenes first
ucp scene load Assets/Scenes/Lighting.unity --additive
ucp scene save
ucp scene focus --id 46894 --axis 1 0 0              # aim the Scene view at an object (for screenshots)
```

A dirty *untitled* scene blocks `load` on purpose. Save it under a path or pass `--keep-untitled`
knowing the change is discarded when Unity switches scenes.

## Create objects that render

```bash
ucp object create Crate --primitive Cube               # mesh + collider in one call
ucp object create Floor --primitive Plane
ucp object create Enemies                              # EMPTY container, nothing to see
ucp object create Head --primitive Sphere --parent -15774
ucp object instantiate Assets/Prefabs/Enemy.prefab --name Enemy_01 --parent -15774
ucp object instantiate -4231 --name Copy                # a bare integer clones a scene object
```

`--primitive Cube|Sphere|Capsule|Cylinder|Plane|Quad` is the only way to get a built-in mesh from
the CLI. A plain `object create` makes an empty GameObject; adding MeshFilter and MeshRenderer by
hand cannot reference Unity's built-in meshes and will not render.

## Components and properties

```bash
ucp object add-component --id 46894 --component Rigidbody
ucp object remove-component --id 46894 --component BoxCollider
ucp object get-fields --id 46894 --component Rigidbody
ucp object get-property --id 46894 --component Transform --property m_LocalPosition
ucp object set-property --id 46894 --component Rigidbody --property m_Mass --value 2.5
ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true
ucp object set-property --id 46894 --component MeshRenderer --property m_Materials --value '[{"path":"Assets/Materials/Crate.mat"}]'
ucp object set-property --id 46894 --component Light --property enabled --value false
ucp object set-active --id 46894 --active false
ucp object set-name --id 46894 --name "Crate_A"
ucp object reparent --id 46894 --parent -15774 --sibling-index 0
ucp object delete --id 46894
```

- `get-fields` lists the serialized names to use with `set-property` (`m_LocalPosition`,
  `m_Mass`, ...); public aliases like `position` also work through reflection.
- Values are JSON: `true`, `5`, `[1,2,3]`, `"text"`, `[0.2,0.2,0.2,1]` for a color. Negative
  numbers and object ids like `-55730` are accepted directly.
- Object references accept `{"instanceId": ...}`, `{"path": "Assets/..."}`, or `{"guid": ...}`.
  Unresolved references fail explicitly instead of silently clearing the field.
- Composite values come back typed: a `Vector3` is `[x, y, z]`, a `Color` is `[r, g, b, a]`.

## Transforms

```bash
ucp transform get --id 46894                       # position, rotation, scale, bounds
ucp transform get --ids 46894,46910,46911          # bulk read
ucp transform move --id 46894 --to 3 0 -2           # world space, absolute
ucp transform move --path "Level/Crates/Crate_A" --to 0 1 0 --relative --space local
ucp transform rotate --id 46894 --euler 0 45 0
ucp transform rotate --id 46894 --euler 0 90 0 --relative
ucp transform scale --id 46894 --uniform 2
ucp transform scale --id 46894 --scale 1 2 1 --relative
ucp transform look-at --id 46894 --at 0 0 0          # world point
ucp transform look-at --id 46894 --target-id 46910 --up 0 1 0
```

Prefer these over `set-property m_LocalPosition`: they handle world/local, relative offsets, and
Euler rotation for you.

## Spatial queries

```bash
ucp spatial bounds --id 46894                        # world AABB: center, size, min, max
ucp spatial bounds --id 46894 --no-children
ucp spatial raycast --origin 0 10 0 --direction 0 -1 0 --max-distance 50 --layer-mask Ground
ucp spatial overlap --shape sphere --center 0 1 0 --radius 2
ucp spatial overlap --shape box --center 0 1 0 --half-extents 1 1 1 --query-triggers
ucp spatial ground --id 46894                        # drop onto the first surface below and move it there
ucp spatial ground --point 4 10 4 --no-apply         # just report the hit
ucp spatial nearest --id 46894 --max 5 --component Light
ucp spatial nearest --point 0 0 0 --tag Enemy
```

Queries use colliders (`raycast`, `overlap`, `ground`) or renderers/colliders (`bounds`). An
object without a collider is invisible to a raycast; `--primitive` objects have one.

## Prefabs

```bash
ucp prefab status --id 46894                         # instance? asset path? overrides?
ucp prefab overrides --id 46894
ucp prefab create --id -15774 --path Assets/Prefabs/EnemyRoot.prefab   # scene object -> asset, connected
ucp prefab apply --id -15774                         # push instance overrides to the asset
ucp prefab revert --id -15774
ucp prefab unpack --id -15774 --completely false
```

## Workflows

Arrange a set piece and verify it:

```bash
ucp object create Floor --primitive Plane
ucp transform scale --name Floor --uniform 4
ucp object create Crate --primitive Cube
ucp transform move --name Crate --to 2 5 0
ucp spatial ground --name Crate                       # rests on the floor now
ucp transform rotate --name Crate --euler 0 30 0
ucp spatial bounds --name Crate
ucp view capture --target-name Crate --max-edge 768 -o crate.png   # see it (ucp-visual-feedback)
ucp scene save
```

Assemble a hierarchy and persist it as a prefab:

```bash
ucp object create EnemyRoot                                   # empty root is fine here
ucp scene snapshot --filter EnemyRoot                         # take its id
ucp object add-component --id -15774 --component Rigidbody
ucp object create Body --primitive Capsule --parent -15774
ucp prefab create --id -15774 --path Assets/Prefabs/EnemyRoot.prefab --save
```

Read a property, change it, prove it changed:

```bash
ucp object get-property --id 46894 --component Rigidbody --property m_Mass
ucp object set-property --id 46894 --component Rigidbody --property m_Mass --value 2.5 --save
ucp object get-property --id 46894 --component Rigidbody --property m_Mass
```

## Pitfalls

- A `[editor] ... prefab stage Assets/X.prefab` line means the editor is in prefab isolation; scene
  commands then operate inside that prefab. Exit prefab mode in the editor or open a scene first.
- Ids in the `[editor]` line's "Modified objects" list and in error messages are live; the ones in
  your notes from before a compile are not.
- `object instantiate` expects a prefab path or a scene-object id. `"PrimitiveType.Cube"` is not
  an asset; use `object create --primitive`.
