# Connection

## How Discovery Works

When you run any UCP command, the CLI automatically discovers the running Unity Editor instance:

1. Resolves a Unity project from `--project` or the current working directory
2. Checks the tracked `com.ucp.bridge` dependency and optionally auto-updates it when it is behind the CLI version
3. Starts Unity automatically when the command requires a live bridge and the editor is not already running
4. Reads `.ucp/bridge.lock` to discover the WebSocket port and session token
5. Establishes a WebSocket connection and performs a handshake to verify protocol compatibility

The bridge writes the lock file when Unity opens the project and removes it on exit.

If UCP cannot find a Unity executable automatically, pass `--unity <path>` or set `UCP_UNITY`. If the project's configured Unity version is known but not installed, UCP reports the installed versions it found and requires an explicit `--force-unity-version <version>` override before launching a different editor.

## Commands

### `ucp connect`

Ensure the editor is running, wait for the bridge, and report the Unity version, project name, and protocol version.

```bash
ucp connect

# Override the Unity executable explicitly
ucp --unity "C:/Program Files/Unity/Hub/Editor/6000.3.1f1/Editor/Unity.exe" connect
```

**Output:**

```
[OK] Connected to Unity bridge
  | Unity 6000.3.1f1
  | Project: MyProject
  | Protocol: 0.4.4
```

By default, `ucp connect` auto-updates stale tracked bridge refs before launching Unity. To warn without mutating the project, use `--bridge-update-policy warn`.

If Unity opens into a startup prompt, use `--dialog-policy <mode>` to control best-effort handling for Safe Mode or recovery dialogs during the bridge wait.

### `ucp install [path]`

Install the UCP bridge package into a Unity project.

```bash
# Install in current directory
ucp install

# Install in a specific project
ucp install /path/to/MyUnityProject

# Explicit local embedded install modes
ucp install --dev
ucp install --embedded
```

By default, `ucp install` writes a tracked git dependency into `Packages/manifest.json`, pinned to the CLI version.

Default install does not add a local `file:` dependency. Use `--dev`, `--embedded`, or `--bridge-path` for explicit local embedded workflows.

### `ucp bridge status`

Inspect the installed bridge dependency source, version, and whether it matches the current CLI release.

```bash
ucp bridge status
```

### `ucp bridge update`

Update the project to the tracked bridge git dependency for the current CLI version.

```bash
ucp bridge update
ucp bridge update --no-wait
```

### `ucp uninstall`

Remove the UCP bridge package from the current Unity project.

```bash
ucp uninstall
```

### `ucp doctor`

Run diagnostic checks on the CLI installation, bridge package drift, Unity executable resolution, editor runtime, and bridge connection status.

```bash
ucp doctor
```

`ucp doctor` also applies the configured bridge update policy. With the default `auto` policy, it will re-pin stale tracked git dependencies before reporting status.

## Related lifecycle commands

Connection commands now integrate with the editor lifecycle surface documented in `docs/commands/editor.md`.

```bash
ucp open
ucp close
ucp editor status
ucp editor logs --lines 200
```

## Connection Troubleshooting

| Issue                           | Solution                                                                                                |
| ------------------------------- | ------------------------------------------------------------------------------------------------------- |
| "No lock file found"            | Use `ucp open` or `ucp connect`; UCP now launches Unity automatically when it can                       |
| "Unity executable"              | Pass `--unity <path>` or set `UCP_UNITY`                                                                |
| "Project version not installed" | Inspect `ucp editor status`, then use `--force-unity-version <ver>` only if you accept the upgrade risk |
| "Bridge package is behind"      | Use `ucp bridge update` or keep the default `--bridge-update-policy auto`                               |
| "Startup dialog blocked launch" | Retry with `--dialog-policy recover`, `safe-mode`, or `manual` depending on the prompt                  |
| "Connection refused"            | Unity might still be importing or compiling - wait and retry                                            |
| "Protocol mismatch"             | Update CLI and bridge to matching versions                                                              |
| "Token mismatch"                | Restart Unity to regenerate the lock file                                                               |
