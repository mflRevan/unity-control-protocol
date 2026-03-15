# Commands Overview

UCP provides a comprehensive set of commands organized by domain. All commands support `--json` for machine-readable output and `--project` to target a specific Unity project directory.

Without `--json`, commands use human mode: terminal-friendly summaries optimized for interactive use. Human mode intentionally truncates broad reads so scenes, settings, and logs stay inspectable without overwhelming the terminal or an agent context window.

## Global Flags

| Flag                            | Description                                               |
| ------------------------------- | --------------------------------------------------------- |
| `--json`                        | Output results as JSON                                    |
| `--project <path>`              | Path to Unity project (defaults to current directory)     |
| `--unity <path>`                | Override the Unity Editor executable UCP should launch    |
| `--force-unity-version <ver>`   | Force a specific installed Unity editor version           |
| `--bridge-update-policy <mode>` | Handle outdated bridge refs with `auto`, `warn`, or `off` |
| `--dialog-policy <mode>`        | Handle Unity startup dialogs during launch waits          |
| `--timeout <s>`                 | Request timeout in seconds (default: 30)                  |
| `--verbose`                     | Enable verbose output                                     |

## Command Groups

### Connection & Setup

| Command              | Description                                                          |
| -------------------- | -------------------------------------------------------------------- |
| `ucp install [path]` | Install the bridge package into a Unity project                      |
| `ucp uninstall`      | Remove the bridge package                                            |
| `ucp doctor`         | Check CLI, bridge package, editor runtime, and connection            |
| `ucp connect`        | Ensure Unity is running and test the live bridge                     |
| `ucp bridge status`  | Show bridge dependency source, version, and drift status             |
| `ucp bridge update`  | Re-pin the project to the bridge git dependency for this CLI version |

`ucp install` is manifest-first by default and writes a tracked dependency in `Packages/manifest.json`.

`ucp install --dev`, `ucp install --embedded`, and `ucp install --bridge-path <path>` mount local embedded bridge packages for live development and smoke testing.

`ucp connect` and other bridge-backed commands now auto-start Unity when the target project can be resolved and a Unity executable is available. By default, UCP also auto-updates stale tracked bridge refs before connecting; use `--bridge-update-policy warn` to downgrade that to warnings only.

When a project declares a Unity version that is not installed, UCP reports the installed versions it found and requires an explicit `--force-unity-version <ver>` override before launching a different editor.

### Editor Lifecycle

| Command              | Description                                                                    |
| -------------------- | ------------------------------------------------------------------------------ |
| `ucp open`           | Alias for `ucp editor open`                                                    |
| `ucp close`          | Alias for `ucp editor close`                                                   |
| `ucp editor open`    | Launch Unity for the target project and wait for the bridge                    |
| `ucp editor close`   | Gracefully close the editor, with optional force fallback                      |
| `ucp editor restart` | Restart the editor for the target project                                      |
| `ucp editor status`  | Show runtime state, resolved Unity path, version diagnostics, and log location |
| `ucp editor logs`    | Read the editor log captured under `.ucp/logs/`                                |
| `ucp editor ps`      | List Unity editor processes discovered by UCP                                  |

### Editor Control

| Command                   | Description                  |
| ------------------------- | ---------------------------- |
| `ucp play`                | Enter play mode              |
| `ucp stop`                | Exit play mode               |
| `ucp pause`               | Toggle pause                 |
| `ucp compile [--no-wait]` | Trigger script recompilation |

### Scene Management

| Command                                   | Description                                                    |
| ----------------------------------------- | -------------------------------------------------------------- |
| `ucp scene list`                          | List all scenes in the project                                 |
| `ucp scene active`                        | Get the active scene                                           |
| `ucp scene focus --id <id>`               | Focus the Scene view camera on a GameObject                    |
| `ucp scene load <path>`                   | Load a scene                                                   |
| `ucp scene snapshot [--filter] [--depth]` | Capture a shallow hierarchy snapshot (root objects by default) |

### File Operations

| Command                  | Description                          |
| ------------------------ | ------------------------------------ |
| `ucp files read <path>`  | Read a project file through the bridge |
| `ucp files write <path>` | Write a project file through the bridge |
| `ucp files patch <path>` | Patch a project file through the bridge |

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
