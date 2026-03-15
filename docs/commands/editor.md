# Editor Lifecycle

UCP now manages the Unity Editor process directly instead of assuming Unity is already open. Any bridge-backed command can auto-start the editor when a project is detected and a Unity executable is available.

## Commands

### `ucp open`

Alias for `ucp editor open`.

```bash
ucp open
ucp --project /path/to/MyProject open
```

This launches Unity for the target project, waits for `.ucp/bridge.lock`, and then waits for the bridge handshake to succeed. If UCP detects a Unity process for the project without a live bridge, it waits for that instance to either finish starting or exit before launching another one.

### `ucp close`

Alias for `ucp editor close`.

```bash
ucp close
ucp editor close --force
```

UCP first requests a graceful shutdown through the bridge, then falls back to a window-close request, and finally uses forced termination when `--force` is supplied or graceful shutdown times out. If shutdown is still in progress when the timeout expires, the command now reports that the process is still closing instead of claiming success.

### `ucp editor restart`

```bash
ucp editor restart
ucp editor restart --force
```

### `ucp editor status`

Show whether Unity is running for the target project, the detected PID, the resolved Unity executable path, and the editor log path.

```bash
ucp editor status
```

### `ucp editor logs`

Print the Unity editor log captured at `.ucp/logs/editor.log`.

```bash
ucp editor logs
ucp editor logs --lines 400
```

### `ucp editor ps`

List Unity editor processes discovered by UCP, including PID, project path, and executable path when available.

```bash
ucp editor ps
```

## Unity executable resolution

UCP resolves the Unity executable in this order:

1. `--unity <path>`
2. `UCP_UNITY`
3. Persistent CLI settings at the platform config path
4. `--force-unity-version <version>` when supplied
5. `ProjectSettings/ProjectVersion.txt`
6. Unity Hub `projects-v1.json` project metadata
7. Installed editor roots from standard Hub locations plus Unity Hub secondary install paths
8. `Unity.exe` on `PATH`

If the project's configured Unity version is known but not installed, UCP now fails instead of silently falling back to a different editor. The error includes the installed versions it found and points to `--force-unity-version` as an explicit override.

### Forcing a different Unity version

```bash
ucp --force-unity-version 6000.3.1f1 open
ucp --force-unity-version 2023.1.7f1 editor status
```

This is a dangerous escape hatch. Opening a project in a different Unity version can upgrade project metadata or assets. Make a backup or commit your work before using it.

### Startup dialog policy

Use `--dialog-policy` when Unity shows startup prompts such as Safe Mode or recovery dialogs.

```bash
ucp --dialog-policy auto start
ucp --dialog-policy recover start
ucp --dialog-policy safe-mode start
ucp --dialog-policy manual start
```

Policies:

- `auto`: best-effort automatic choice based on detected button labels
- `manual`: do not auto-click dialogs; wait for the operator
- `ignore`: prefer buttons like `Ignore` when available
- `recover`: prefer recovery / continue options when available
- `safe-mode`: prefer Safe Mode when available
- `cancel`: prefer cancel / close options when available

Unity does not document a general command-line flag to skip these prompts, so UCP handles them as a best-effort runtime policy during startup.

## Bridge package drift handling

Before UCP launches Unity for bridge-backed commands, it checks whether the tracked `com.ucp.bridge` git dependency is behind the current CLI version.

Policies:

- `auto`: update the tracked git dependency automatically before launch or connection
- `warn`: report the drift but leave the project unchanged
- `off`: skip drift handling entirely

Set the policy per command:

```bash
ucp --bridge-update-policy warn connect
```

Or update the bridge explicitly:

```bash
ucp bridge update
```
