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

Use the logs command in three modes:

- live follow mode for incoming logs
- buffered history mode for tail/search/get operations against logs captured since the bridge started
- curated status mode for a quick summary of buffered log health and recent play-session activity

`ucp log tail` is an alias-friendly form for agents that expect a singular log command; it accepts the same tail/follow flags as `ucp logs`.

```bash
# Summarize the current buffered log state
ucp logs status

# Stream all new logs
ucp logs --follow

# Stream only new errors
ucp logs --follow --level error

# Stream warnings/errors whose message or stack mentions Shader
ucp log tail --follow --filter level>=warning --filter channel=Shader

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

# Capture all play-mode logs to a file until play mode exits
ucp play --log-file Logs/play-session.log
ucp stop
```

| Flag                          | Description                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------------ |
| `--follow`                    | Follow live incoming logs instead of querying buffered history                             |
| `--level <info\|warn\|error>` | Filter by log severity threshold                                                           |
| `--channel <text>`            | Filter by coarse channel/category text in the message or stack trace                       |
| `--filter <expr>`             | Convenience filter expression such as `level>=warning`, `channel=Shader`, or `text=depth`  |
| `--count <n>`                 | History window size for tail/search, or number of live logs before stopping in follow mode |
| `--pattern <regex>`           | Regex search against buffered message and stack trace text                                 |
| `--id <logId>`                | Read a single buffered log entry in full                                                   |
| `--before-id <logId>`         | Restrict buffered reads to ids lower than this value                                       |
| `--after-id <logId>`          | Restrict buffered reads to ids higher than this value                                      |

Bulk history reads are intentionally capped to `10` returned entries even if more logs match. Use the returned ids with `ucp logs --id <logId>` or narrow the search space further.

`ucp logs status` reports total buffered entries, per-level counts, collapsed category counts, the buffered-history window, and play-session timing/log counts when applicable.

The same curated summary is also appended automatically by blocking lifecycle commands that wait for Unity to settle after reimport, compilation, or domain reload work.

`ucp play --log-file <path>` writes a plain-text play-session log from Unity's `Application.logMessageReceived` stream. Relative paths are resolved from the Unity project root, and capture stops automatically when Unity exits play mode.
