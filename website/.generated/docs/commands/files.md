# Files

Read, write, and patch project files. All file paths are relative to the project root and sandboxed within the project directory for safety.

## Commands

### `ucp read-file <path>`

Read the contents of a project file.

```bash
ucp read-file Assets/Scripts/Player.cs
```

### `ucp write-file <path>`

Write content to a project file. Creates the file if it doesn't exist.

```bash
# Write from flag
ucp write-file Assets/Scripts/Config.cs --content "public class Config { public int maxHP = 100; }"

# Read from stdin
echo "using UnityEngine;" | ucp write-file Assets/Scripts/Header.cs

# Write and trigger recompilation
ucp write-file Assets/Scripts/Player.cs --content "..." --compile
```

| Flag               | Description                                |
| ------------------ | ------------------------------------------ |
| `--content <text>` | File content (reads from stdin if omitted) |
| `--compile`        | Trigger recompilation after write          |

### `ucp patch-file <path>`

Apply a find/replace patch to a file.

```bash
ucp patch-file Assets/Scripts/Player.cs --find "maxHP = 100" --replace "maxHP = 200"
```

| Flag               | Description        |
| ------------------ | ------------------ |
| `--find <text>`    | Text to search for |
| `--replace <text>` | Replacement text   |

## Security

File operations are sandboxed to the Unity project directory. Attempting to read or write files outside the project root will be rejected.
