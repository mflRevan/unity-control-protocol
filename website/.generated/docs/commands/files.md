# Files

Read, write, and patch project files. All file paths are relative to the project root and sandboxed within the project directory for safety.

When you already have normal workspace access inside the Unity project, direct filesystem edits plus `ucp compile` are usually the fastest iteration path. Use `ucp files ...` when you want the bridge to perform sandboxed project file I/O directly.

## Commands

### `ucp files read <path>`

Read the contents of a project file.

```bash
ucp files read Assets/Scripts/Player.cs
```

### `ucp files write <path>`

Write content to a project file. Creates the file if it doesn't exist.

```bash
# Write from flag
ucp files write Assets/Scripts/Config.cs --content "public class Config { public int maxHP = 100; }"

# Read from stdin
echo "using UnityEngine;" | ucp files write Assets/Scripts/Header.cs

# Write and trigger recompilation
ucp files write Assets/Scripts/Player.cs --content "..." --compile

# Skip the automatic reimport if you plan to reimport manually later
ucp files write Assets/Textures/HUD.png.meta --content "..." --no-reimport
```

| Flag               | Description                                |
| ------------------ | ------------------------------------------ |
| `--content <text>` | File content (reads from stdin if omitted) |
| `--no-reimport`    | Skip the automatic Unity reimport          |
| `--compile`        | Trigger recompilation after write          |

Writes and patches now reimport the edited Unity asset automatically for `Assets/` and `Packages/` paths, including `.meta` file edits. This keeps imported assets and importer changes applied immediately without a separate manual step.

### `ucp files patch <path>`

Apply a find/replace patch to a file.

```bash
ucp files patch Assets/Scripts/Player.cs --find "maxHP = 100" --replace "maxHP = 200"
ucp files patch Assets/Textures/HUD.png.meta --find "isReadable: 0" --replace "isReadable: 1" --no-reimport
```

| Flag               | Description        |
| ------------------ | ------------------ |
| `--find <text>`    | Text to search for |
| `--replace <text>` | Replacement text   |
| `--no-reimport`    | Skip the automatic Unity reimport |

## Security

File operations are sandboxed to the Unity project directory. Attempting to read or write files outside the project root will be rejected.
