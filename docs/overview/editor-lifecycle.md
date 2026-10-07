# Editor Lifecycle

UCP manages the Unity Editor process directly instead of assuming Unity is already open. Any
bridge-backed command can auto-start the editor when a project is detected and a Unity executable
is available, and every command attaches to an editor that is already running on the project,
whichever way it was started.

## Which editor ucp talks to

ucp finds the editor for a project by its command line (`-projectPath` or Unity Hub's
`-createProject`), and failing that by the pid in the bridge's own lock file. An editor opened
from the Hub, from a file association, or by hand is adopted exactly like one `ucp open` launched:
`editor status` says how it was started, `editor close` and `editor restart` work on it, and
`editor logs` reads Unity's per-user `Editor.log` when the editor has no `-logFile` of its own.
If Unity holds the project (`Temp/UnityLockfile`) but ucp cannot identify the process, launching
is refused rather than producing a second editor on the same project.

## When ucp launches an editor

- One invocation launches at most one editor, and only before it has attached to one. A command
  that is bound to an editor which then exits (a crash during `logs --follow`, a kill during a
  play-mode poll) fails with "the editor exited" instead of starting a replacement. This is what
  stops a forgotten follower from relaunching the editor after every crash.
- An editor ucp launched that dies within two minutes is treated as a crash on startup: ordinary
  commands stop auto-launching and point at `ucp editor log`; `ucp open` and `ucp editor restart`
  always proceed.
- `ucp open` rotates a non-empty `editor.log` to `editor.prev.log`, so the log of the editor that
  just crashed survives the relaunch.
- ucp never brings the editor window to the foreground. The bridge keeps an unfocused editor
  compiling, importing, and reloading by queueing player-loop updates; set `UCP_FOCUS_EDITOR=1`
  only if you want the old nudge-to-front behaviour back.

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
ucp editor close --discard-changes
```

UCP requests a graceful shutdown through the bridge (`EditorApplication.Exit`, which raises no
save prompt) and uses forced termination when `--force` is supplied or the graceful shutdown does
not return. It never sends an OS window-close, which on a dirty scene would raise Unity's native
save dialog with nobody to answer it. If shutdown is still in progress when the timeout expires,
the command reports that the process is still closing instead of claiming success.

If the active scene has unsaved changes, `close` blocks first, with or without `--force`, and
asks you to save. `--discard-changes` is the explicit way to close anyway and lose them.

### `ucp editor restart`

```bash
ucp editor restart
ucp editor restart --force
ucp editor restart --discard-changes
```

Like `close`, `restart` refuses to proceed while the active scene is dirty unless
`--discard-changes` is given.

### `ucp editor status`

Show whether Unity is running for the target project, how it was launched (by ucp, by Unity Hub,
or outside ucp), the detected PID, the resolved Unity executable path, and the editor log path.

```bash
ucp editor status
```

### `ucp editor logs`

Print the Unity editor log: `.ucp/logs/editor.log` for editors ucp launched, the editor's own
`-logFile` when it was started with one, and Unity's per-user `Editor.log` for Hub and manual
launches. The JSON output names the file that was read.

```bash
ucp editor logs
ucp editor logs --lines 400
```

### `ucp editor ps`

List Unity editor processes discovered by UCP, including PID, project path, executable path, and
whether Unity Hub launched them, followed by any other `ucp` processes on the machine with their
age and command line. A `ucp` that outlives its task (a forgotten `logs --follow`, a hung
command) keeps acting on the editor; `ucp doctor` warns about ones older than ten minutes.

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
