# Commands Overview

UCP provides a comprehensive set of commands organized by domain. All commands support `--json` for machine-readable output and `--project` to target a specific Unity project directory.

Without `--json`, commands use human mode: terminal-friendly summaries optimized for interactive use. Human mode intentionally truncates broad reads so scenes, settings, and logs stay inspectable without overwhelming the terminal or an agent context window.

## Global Flags

| Flag               | Description                                           |
| ------------------ | ----------------------------------------------------- |
| `--json`           | Output results as JSON                                |
| `--project <path>` | Path to Unity project (defaults to current directory) |
| `--timeout <s>`    | Request timeout in seconds (default: 30)              |
| `--verbose`        | Enable verbose output                                 |

## Command Groups

### Connection & Setup

| Command              | Description                                     |
| -------------------- | ----------------------------------------------- |
| `ucp install [path]` | Install the bridge package into a Unity project |
| `ucp uninstall`      | Remove the bridge package                       |
| `ucp doctor`         | Check CLI and bridge health                     |
| `ucp connect`        | Test connection to the running bridge           |

`ucp install --dev` mounts the repo-local bridge package into `Packages/com.ucp.bridge` as a local-only embedded package for live development and smoke testing.

### Editor Control

| Command                   | Description                  |
| ------------------------- | ---------------------------- |
| `ucp play`                | Enter play mode              |
| `ucp stop`                | Exit play mode               |
| `ucp pause`               | Toggle pause                 |
| `ucp compile [--no-wait]` | Trigger script recompilation |

### Scene Management

| Command                             | Description                                                    |
| ----------------------------------- | -------------------------------------------------------------- |
| `ucp scene list`                    | List all scenes in the project                                 |
| `ucp scene active`                  | Get the active scene                                           |
| `ucp scene load <path>`             | Load a scene                                                   |
| `ucp snapshot [--filter] [--depth]` | Capture a shallow hierarchy snapshot (root objects by default) |

### File Operations

| Command                 | Description                     |
| ----------------------- | ------------------------------- |
| `ucp read-file <path>`  | Read a project file             |
| `ucp write-file <path>` | Write content to a project file |
| `ucp patch-file <path>` | Find and replace in a file      |

### Media

| Command                                  | Description                                    |
| ---------------------------------------- | ---------------------------------------------- |
| `ucp screenshot [--view] [--output]`     | Capture game or scene view screenshot          |
| `ucp logs [--follow] [--pattern] [--id]` | Follow live logs or query buffered log history |

### Testing

| Command                             | Description                    |
| ----------------------------------- | ------------------------------ |
| `ucp run-tests [--mode] [--filter]` | Run tests in edit or play mode |

### Scripting

| Command                          | Description                |
| -------------------------------- | -------------------------- |
| `ucp exec list`                  | List available UCP scripts |
| `ucp exec run <name> [--params]` | Execute a named script     |

### Objects & Components

| Command                       | Description                             |
| ----------------------------- | --------------------------------------- |
| `ucp object get-fields`       | List fields on a component              |
| `ucp object get-property`     | Read a property value                   |
| `ucp object set-property`     | Write a property value                  |
| `ucp object set-active`       | Enable/disable a GameObject             |
| `ucp object set-name`         | Rename a GameObject                     |
| `ucp object create`           | Create a new GameObject                 |
| `ucp object delete`           | Delete a GameObject                     |
| `ucp object reparent`         | Move a GameObject in the hierarchy      |
| `ucp object instantiate`      | Instantiate a prefab or clone an object |
| `ucp object add-component`    | Add a component                         |
| `ucp object remove-component` | Remove a component                      |

### Assets

| Command                  | Description                    |
| ------------------------ | ------------------------------ |
| `ucp asset search`       | Search for assets by type/name |
| `ucp asset info <path>`  | Get asset metadata             |
| `ucp asset read <path>`  | Read asset fields              |
| `ucp asset write <path>` | Write an asset field           |
| `ucp asset create-so`    | Create a ScriptableObject      |

### Materials

| Command                       | Description                  |
| ----------------------------- | ---------------------------- |
| `ucp material get-properties` | List all material properties |
| `ucp material get-property`   | Read a material property     |
| `ucp material set-property`   | Set a material property      |
| `ucp material keywords`       | List enabled shader keywords |
| `ucp material set-keyword`    | Toggle a shader keyword      |
| `ucp material set-shader`     | Change material shader       |

### Prefabs

| Command                | Description                          |
| ---------------------- | ------------------------------------ |
| `ucp prefab status`    | Check if object is a prefab instance |
| `ucp prefab apply`     | Apply overrides to prefab asset      |
| `ucp prefab revert`    | Revert to prefab asset state         |
| `ucp prefab unpack`    | Unpack a prefab instance             |
| `ucp prefab create`    | Save a scene object as a new prefab  |
| `ucp prefab overrides` | List property modifications          |

### Settings

| Command                     | Description                   |
| --------------------------- | ----------------------------- |
| `ucp settings player`       | Read player settings          |
| `ucp settings set-player`   | Modify a player setting       |
| `ucp settings quality`      | Read quality settings         |
| `ucp settings set-quality`  | Modify a quality setting      |
| `ucp settings physics`      | Read physics settings         |
| `ucp settings set-physics`  | Modify a physics setting      |
| `ucp settings lighting`     | Read lighting/render settings |
| `ucp settings set-lighting` | Modify a lighting setting     |
| `ucp settings tags-layers`  | List tags and layers          |
| `ucp settings add-tag`      | Add a custom tag              |
| `ucp settings add-layer`    | Add a custom layer            |

### Build Pipeline

| Command                   | Description                   |
| ------------------------- | ----------------------------- |
| `ucp build targets`       | List installed build targets  |
| `ucp build active-target` | Get active build target       |
| `ucp build set-target`    | Switch build target           |
| `ucp build scenes`        | List scenes in build settings |
| `ucp build set-scenes`    | Set build scenes              |
| `ucp build start`         | Start a build                 |
| `ucp build defines`       | List scripting defines        |
| `ucp build set-defines`   | Set scripting defines         |

### Version Control

| Command                 | Description            |
| ----------------------- | ---------------------- |
| `ucp vcs status`        | Working copy status    |
| `ucp vcs commit`        | Commit changes         |
| `ucp vcs checkout`      | Checkout files         |
| `ucp vcs diff`          | Show differences       |
| `ucp vcs history`       | View changeset history |
| `ucp vcs lock`          | Lock files             |
| `ucp vcs unlock`        | Unlock files           |
| `ucp vcs branch list`   | List branches          |
| `ucp vcs branch create` | Create a branch        |
| `ucp vcs branch switch` | Switch branches        |
