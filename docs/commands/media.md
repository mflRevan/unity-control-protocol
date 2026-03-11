# Screenshots & Logs

Capture visual output and stream console logs from Unity.

## Commands

### `ucp screenshot`

Capture a screenshot of the game or scene view.

```bash
# Save to file
ucp screenshot -o capture.png

# Scene view instead of game view
ucp screenshot --view scene -o scene.png

# Custom resolution
ucp screenshot --width 3840 --height 2160 -o hires.png

# Base64 to stdout (for piping)
ucp screenshot
```

| Flag                   | Description                      |
| ---------------------- | -------------------------------- |
| `--view <game\|scene>` | View to capture (default: game)  |
| `--width <px>`         | Width in pixels (default: 1920)  |
| `--height <px>`        | Height in pixels (default: 1080) |
| `-o, --output <path>`  | Output file path                 |

### `ucp logs`

Stream Unity console logs in real time, or fetch recent logs.

```bash
# Stream all logs
ucp logs

# Filter by level
ucp logs --level error

# Get last 20 logs and exit
ucp logs --count 20

# JSON output
ucp logs --level warn --json
```

| Flag                          | Description               |
| ----------------------------- | ------------------------- |
| `--level <info\|warn\|error>` | Filter by log level       |
| `--count <n>`                 | Get last N logs then exit |
