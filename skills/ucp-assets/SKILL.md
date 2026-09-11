---
name: ucp-assets
description: >-
  Work with a Unity project's assets and files through the editor with `ucp asset`, `ucp files`,
  `ucp material`, `ucp references`, `ucp shader`, and `ucp script`: search and inspect assets,
  read and write ScriptableObject and material fields, move or rename assets without breaking
  GUID references, edit importer settings instead of .meta files, find every reference to an
  asset, and check shader and project-file health. Use when the task touches files under Assets/
  or Packages/. For scene objects use ucp-scene-authoring; for UXML/USS use ucp-ui-toolkit.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer. `references` runs natively without an editor on projects using Force Text serialization and visible meta files.
metadata:
  author: mflRevan
  version: '0.6.3'
  homepage: https://unityctl.dev/skills/ucp-assets
---

# Assets, files, materials, references

Unity tracks assets by GUID in `.meta` files, and scenes, prefabs, and settings serialize
references as GUIDs. Renaming or moving through the filesystem breaks those references; moving
through the editor keeps them. This skill routes file work through the editor where that matters
and stays on the filesystem where it does not.

## Ground rules

- You usually have direct filesystem access. Edit scripts and text assets locally, then run
  `ucp compile` (scripts) or let `ucp files write` / `ucp asset reimport` trigger the import.
- Use `ucp asset move` / `bulk-move` for renames and folder cleanup, never `mv`.
- Use `ucp asset import-settings` for FBX, texture, audio import options, never hand-edited
  `.meta` files.
- Prefer `--json` and `--detail summary` in reference searches to keep payloads small.

## Search and inspect

```bash
ucp asset search -t Material --max 20
ucp asset search -t Prefab -p Assets/Prefabs
ucp asset search -n '^SCN_[0-9]+$' --regex
ucp asset info Assets/Materials/Crate.mat            # type, guid, size, importer
ucp asset inspect Assets/Prefabs/Enemy.prefab        # type-aware: renderers, materials, components
ucp asset inspect Assets/Materials/Crate.mat --max-fields 40
```

`-t` takes Unity type names (`Texture2D`, `Material`, `Prefab`, `AudioClip`, `ScriptableObject`
subclasses by name). Results are capped at `--max` (default 50).

## Read and write serialized fields

```bash
ucp asset read Assets/Config/EnemyConfig.asset
ucp asset read Assets/Config/EnemyConfig.asset --field maxHealth
ucp asset write Assets/Config/EnemyConfig.asset --field maxHealth --value 120
ucp asset write-batch Assets/Config/EnemyConfig.asset --values '{"maxHealth":120,"speed":3.5,"loot":{"path":"Assets/Items/Gold.asset"}}'
ucp asset create-so Assets/Config/BossConfig.asset --type EnemyConfig
ucp asset delete Assets/Config/Old.asset
```

Values are JSON. Object references accept `{"path": ...}`, `{"guid": ...}`, or
`{"instanceId": ...}`, and fail explicitly when unresolved.

## Move and rename safely

```bash
ucp asset move Assets/Legacy/Enemy.prefab Assets/Characters/Enemy.prefab
ucp asset move Assets/Legacy/Textures Assets/Art/Textures            # whole folder, GUIDs kept
ucp asset bulk-move --moves '[{"from":"Assets/A.mat","to":"Assets/Materials/A.mat"},{"from":"Assets/B.mat","to":"Assets/Materials/B.mat"}]' --dry-run
ucp asset bulk-move --moves '{"Assets/A.mat":"Assets/Materials/A.mat"}' --continue-on-error
ucp references check Assets/Characters                              # any unresolved outgoing references after the move?
```

Build-settings scene entries, prefab references, and material slots keep resolving because the
GUID never changes. `--dry-run` validates the whole batch (collisions, missing sources) first.

## Project files

```bash
ucp files read Assets/Scripts/Enemy.cs
ucp files write Assets/Scripts/Enemy.cs --content "..."          # reimports; --compile waits for the recompile
ucp files write Assets/Data/table.json < table.json               # content from stdin
ucp files patch Assets/Scripts/Enemy.cs --find "speed = 3f" --replace "speed = 5f"
ucp files write Assets/Tuning.txt --content "..." --no-reimport
```

Paths are relative to the project root and sandboxed inside it. Writes under `Assets/` and
`Packages/` reimport automatically, including `.meta` files (which reimport their owning asset).

## Importer settings

```bash
ucp asset import-settings read Assets/Textures/HUD.png
ucp asset import-settings read Assets/Models/Enemy.fbx --field globalScale
ucp asset import-settings write Assets/Textures/HUD.png --field isReadable --value true
ucp asset import-settings write Assets/Textures/HUD.png --field textureType --value "Sprite"
ucp asset import-settings write-batch Assets/Textures/HUD.png --values '{"isReadable":true,"maxTextureSize":2048}' --no-reimport
ucp asset reimport Assets/Textures/HUD.png
ucp asset reimport Assets/Generated --recursive
```

Field names are the importer's serialized or public names as shown by `import-settings read`.
Batch several writes with `--no-reimport`, then reimport once.

## Materials

```bash
ucp material create Assets/Materials/Crate.mat --shader "Universal Render Pipeline/Lit"
ucp material get-properties --path Assets/Materials/Crate.mat
ucp material get-property --path Assets/Materials/Crate.mat --property _BaseColor
ucp material set-property --path Assets/Materials/Crate.mat --property _BaseColor --value [0.8,0.3,0.1,1]
ucp material set-property --path Assets/Materials/Crate.mat --property _Metallic --value 0.4
ucp material set-property --path Assets/Materials/Crate.mat --property _BaseMap --value '{"path":"Assets/Textures/Crate.png"}'
ucp material keywords --path Assets/Materials/Crate.mat
ucp material set-keyword --path Assets/Materials/Crate.mat --keyword _EMISSION --enabled true
ucp material set-shader --path Assets/Materials/Crate.mat --shader Standard
ucp shader errors --errors-only                     # shader compile problems the editor knows about
```

Property names are the shader's (`_BaseColor` in URP, `_Color` in Built-in). `get-properties`
lists what the current shader exposes.

## References

```bash
ucp references check                                # can this project be indexed natively? (Force Text + visible meta)
ucp references find --asset Assets/Materials/Crate.mat --detail summary
ucp references find --asset 933532a4fcc9baf4fa0491de14d08ed7 --json
ucp references find --asset Assets/Prefabs/Enemy.prefab --object 3cb6...:11400000
ucp references find-strings --pattern "SCN_Menu"                 # string ids Unity will not migrate
ucp references find-strings --pattern 'Level_[0-9]+' --regex -p Assets/Scenes
ucp references index build && ucp references index status
ucp references find --asset Assets/Materials/Crate.mat --approach bridge   # force the in-editor path
```

Native search reads Unity's YAML from disk in parallel and needs no running editor; `--detail
summary` collapses repetitive hits (200 renderers using one material become one line).

## Scripts and project files

```bash
ucp script doctor                                   # stale .csproj / project files?
ucp script doctor --fix                             # delete stale generated files and regenerate
ucp compile                                         # after local script edits
```

## Workflows

Retexture a prop end to end:

```bash
ucp asset search -n crate -t Texture2D
ucp asset import-settings write Assets/Textures/Crate.png --field textureType --value "Default"
ucp material create Assets/Materials/Crate.mat --shader "Universal Render Pipeline/Lit"
ucp material set-property --path Assets/Materials/Crate.mat --property _BaseMap --value '{"path":"Assets/Textures/Crate.png"}'
ucp object set-property --id 46894 --component MeshRenderer --property m_Materials --value '[{"path":"Assets/Materials/Crate.mat"}]' --save
```

Reorganize a folder without breaking anything:

```bash
ucp references find --asset Assets/Legacy/Enemy.prefab --detail summary   # who uses it
ucp asset bulk-move --moves '{"Assets/Legacy":"Assets/Characters/Legacy"}' --dry-run
ucp asset bulk-move --moves '{"Assets/Legacy":"Assets/Characters/Legacy"}'
ucp references check Assets/Characters                                    # nothing unresolved
```

## Pitfalls

- `asset delete` and `asset move` are real Unity operations: they update the asset database and
  can trigger reimports and recompiles. Expect the `[editor]` line to show `importing assets` or
  `compiling` afterwards; the next command waits for it.
- Writing a `.cs` file starts a compile. Run `ucp compile` to get the `CS####` diagnostics
  instead of discovering them three commands later.
- `references find` on a project that is not Force Text falls back to the editor bridge, which is
  slower and requires the editor to be open.
