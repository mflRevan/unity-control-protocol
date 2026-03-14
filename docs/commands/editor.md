# Editor Lifecycle

UCP now manages the Unity Editor process directly instead of assuming Unity is already open. Any bridge-backed command can auto-start the editor when a project is detected and a Unity executable is available.

## Commands

### `ucp start`

Alias for `ucp editor start`.

```bash
ucp start
ucp --project /path/to/MyProject start
```

This launches Unity for the target project, waits for `.ucp/bridge.lock`, and then waits for the bridge handshake to succeed.

### `ucp close`

Alias for `ucp editor close`.

```bash
ucp close
ucp editor close --force
```

UCP first requests a graceful shutdown through the bridge, then falls back to a window-close request, and finally uses forced termination when `--force` is supplied or graceful shutdown times out.

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
4. Standard Unity Hub install locations derived from `ProjectSettings/ProjectVersion.txt`
5. `Unity.exe` on `PATH`

If no executable is found, `ucp start`, `ucp connect`, and other bridge-backed commands fail with a resolution error that includes the searched paths.

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