# Screenshots & Logs

Capture visual output and inspect Unity console logs.

## Commands

### `ucp screenshot`

Capture a screenshot of the game or scene view.

For in-scene greybox work, the recommended loop is:

```bash
ucp scene focus --id <instanceId> --axis 1 0 0
ucp screenshot --view scene -o scene-iteration.png
ucp object set-property --id <instanceId> --component Transform --property m_LocalPosition --value "[x,y,z]"
ucp scene focus --id <instanceId> --axis 0 0 -1
ucp screenshot --view scene -o scene-iteration-2.png
```

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

Use the logs command in two modes:

- live follow mode for incoming logs
- buffered history mode for tail/search/get operations against logs captured since the bridge started

```bash
# Stream all new logs
ucp logs --follow

# Stream only new errors
ucp logs --follow --level error

# Read the latest buffered logs
ucp logs --count 10

# Regex search across buffered logs
ucp logs --pattern "NullReference|Exception" --count 100

# Narrow a search window using ids
ucp logs --pattern "failed" --before-id 200 --after-id 100

# Inspect one buffered log entry in full
ucp logs --id 42

# JSON output
ucp logs --pattern "warning|error" --json
```

| Flag                          | Description                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------------ |
| `--follow`                    | Follow live incoming logs instead of querying buffered history                             |
| `--level <info\|warn\|error>` | Filter by log severity threshold                                                           |
| `--count <n>`                 | History window size for tail/search, or number of live logs before stopping in follow mode |
| `--pattern <regex>`           | Regex search against buffered message and stack trace text                                 |
| `--id <logId>`                | Read a single buffered log entry in full                                                   |
| `--before-id <logId>`         | Restrict buffered reads to ids lower than this value                                       |
| `--after-id <logId>`          | Restrict buffered reads to ids higher than this value                                      |

Bulk history reads are intentionally capped to `10` returned entries even if more logs match. Use the returned ids with `ucp logs --id <logId>` or narrow the search space further.
