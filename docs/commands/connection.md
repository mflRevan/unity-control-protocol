# Connection

## How Discovery Works

When you run any UCP command, the CLI automatically discovers the running Unity Editor instance:

1. Looks for a `.ucp-lock` file in the project directory
2. Reads the WebSocket port and authentication token from the lock file
3. Establishes a WebSocket connection
4. Performs a handshake to verify protocol version compatibility

The bridge writes the lock file when Unity opens the project and removes it on exit.

## Commands

### `ucp connect`

Test the connection to the Unity bridge. Reports the Unity version, project name, and protocol version.

```bash
ucp connect
```

**Output:**

```
[OK] Connected to Unity bridge
  | Unity 6000.3.1f1
  | Project: MyProject
  | Protocol: 0.3.1
```

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

### `ucp uninstall`

Remove the UCP bridge package from the current Unity project.

```bash
ucp uninstall
```

### `ucp doctor`

Run diagnostic checks on the CLI installation, bridge package, and connection status.

```bash
ucp doctor
```

## Connection Troubleshooting

| Issue                | Solution                                                      |
| -------------------- | ------------------------------------------------------------- |
| "No lock file found" | Ensure Unity Editor is open with the bridge package installed |
| "Connection refused" | Unity might be busy compiling — wait and retry                |
| "Protocol mismatch"  | Update CLI and bridge to matching versions                    |
| "Token mismatch"     | Restart Unity to regenerate the lock file                     |
