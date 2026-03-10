# @mflrevan/ucp

CLI for programmatic control of Unity Editor over WebSocket. Enables AI agents, CI/CD pipelines, and automation tools to interact with Unity projects.

## Install

```bash
npm install -g @mflrevan/ucp
```

The package automatically downloads the correct prebuilt binary for your platform (macOS, Linux, Windows).

> **pnpm users:** pnpm blocks postinstall scripts by default. After installing, run `pnpm approve-builds` to allow the binary download, or use `npm install -g @mflrevan/ucp` instead.

## Usage

```bash
cd /path/to/MyUnityProject
ucp connect         # verify connection to Unity bridge
ucp snapshot         # capture full scene hierarchy
ucp compile          # trigger recompilation
ucp play             # enter play mode
ucp write-file Assets/Scripts/MyScript.cs --content "..."
```

## Requirements

- Unity project with the UCP bridge package installed
- Supported platforms: macOS (x64, ARM64), Linux (x64), Windows (x64)

### Install the Bridge

```bash
ucp install
```

Or add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge"
  }
}
```

## Commands

| Command                                       | Description                       |
| --------------------------------------------- | --------------------------------- |
| `ucp connect`                                 | Verify connection to Unity bridge |
| `ucp doctor`                                  | Run health checks                 |
| `ucp compile`                                 | Trigger recompilation             |
| `ucp play` / `stop` / `pause`                 | Play mode control                 |
| `ucp scene list\|active\|load`                | Scene management                  |
| `ucp snapshot`                                | Capture scene hierarchy           |
| `ucp screenshot`                              | Capture screenshot                |
| `ucp logs`                                    | Stream console logs               |
| `ucp run-tests`                               | Run edit/play mode tests          |
| `ucp read-file` / `write-file` / `patch-file` | File operations                   |
| `ucp exec list\|run`                          | Run automation scripts            |
| `ucp vcs *`                                   | Version control (Plastic SCM)     |

All commands support `--json` for structured output.

## Alternative Install Methods

- **cargo:** `cargo install --git https://github.com/mflRevan/unity-control-protocol --path cli`
- **Binary:** [GitHub Releases](https://github.com/mflRevan/unity-control-protocol/releases)
- **Source:** `git clone` + `cargo build --release`

## Links

- [Full documentation](https://github.com/mflRevan/unity-control-protocol)
- [GitHub Releases](https://github.com/mflRevan/unity-control-protocol/releases)

## License

MIT
