# Build Pipeline

Configure build targets, scene lists, scripting defines, and trigger builds from the CLI.

## Commands

### `ucp build targets`

List all available build targets for the current platform.

```bash
ucp build targets
```

**Output:**

```
[OK] Available build targets
  StandaloneWindows64
  StandaloneOSX
  StandaloneLinux64
  Android
  iOS
  WebGL
```

### `ucp build active-target`

Show the currently active build target.

```bash
ucp build active-target
```

**Output:**

```
[OK] Active build target: StandaloneWindows64
```

### `ucp build set-target`

Switch the active build target. This triggers a script recompilation.

```bash
ucp build set-target Android
```

> **Note:** Switching build targets can take significant time as Unity reimports assets for the new platform.

UCP waits for the resulting platform-switch/domain-reload work to finish before `set-target` reports success.
If the active scene is dirty, `set-target` fails first with a concise unsaved-scene summary instead of letting Unity prompt for a save.

### `ucp build scenes`

List all scenes in the Build Settings, including their enabled status and index.

```bash
ucp build scenes
```

**Output:**

```
[OK] Build scenes
  [0] Assets/Scenes/MainMenu.unity (enabled)
  [1] Assets/Scenes/World.unity (enabled)
  [2] Assets/Scenes/TestScene.unity (disabled)
```

### `ucp build set-scenes`

Set the build scene list. Provide scene paths as a comma-separated string.

```bash
ucp build set-scenes "Assets/Scenes/MainMenu.unity,Assets/Scenes/World.unity"
```

All listed scenes will be enabled by default.

`set-scenes` waits for Unity to finish applying the updated build-settings asset before returning.

### `ucp build start`

Start a build with the current settings. Specify an output path for the built artifact.

```bash
ucp build start --output "Builds/MyGame.exe"
```

| Flag              | Description                                |
| ----------------- | ------------------------------------------ |
| `--output <path>` | Output path for the build artifact         |
| `--development`   | Build with Unity development build options |

**Output:**

```
[OK] Build completed: Builds/MyGame.exe (45.2 MB, 32.5s)
```

### `ucp build defines`

List scripting define symbols for the active build target.

```bash
ucp build defines
```

**Output:**

```
[OK] Scripting defines (StandaloneWindows64)
  UNITY_POST_PROCESSING
  ODIN_INSPECTOR
  ENABLE_LOGGING
```

### `ucp build set-defines`

Set scripting define symbols. Provide defines as a semicolon-separated string.

```bash
ucp build set-defines "UNITY_POST_PROCESSING;ENABLE_LOGGING;MY_CUSTOM_DEFINE"
```

> **Note:** Changing defines triggers a full script recompilation.

`set-defines` follows the restart-then-settle policy: it waits through the compilation/domain-reload cycle and only reports success once the editor is ready again.
If the active scene is dirty, `set-defines` blocks first and asks you to save explicitly.

## Typical Workflow

```bash
# 1. Check current target
ucp build active-target

# 2. Configure scenes
ucp build set-scenes "Assets/Scenes/Boot.unity,Assets/Scenes/Game.unity"

# 3. Set defines
ucp build set-defines "RELEASE;ENABLE_ANALYTICS"

# 4. Build
ucp build start --output "Builds/Windows/Game.exe"
```

## Notes

- Build target switching reimports all assets for the new platform - this can be slow
- `set-defines` replaces all defines, so include existing ones you want to keep
- `start` blocks until the build completes (or fails)
- Build output path is relative to the Unity project root
- `set-target`, `set-scenes`, and `set-defines` all use lifecycle-aware waits before reporting success
