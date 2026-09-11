# Screenshots, Recordings & Logs

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

### `ucp view`

Composed renders for eyes that are not yours. `ucp screenshot` shows the Game view as the player
sees it; `ucp view` places a temporary camera so a vision model gets exactly the framing it needs.

```bash
# Frame the main camera, or a chosen camera, without touching the scene
ucp view capture -o frame.png
ucp view capture --target-name Windmill -o windmill-in-context.png

# One object alone, auto-framed from its bounds; one file per requested angle
ucp view isolate --name Windmill --views front,right,top --max-edge 900 -o windmill.png

# A ring of angles around an object as one composite grid
ucp view orbit --name Windmill -o windmill-orbit.png
```

| Option                  | Description                                                     |
| ----------------------- | --------------------------------------------------------------- |
| `--id / --path / --name`| Target object (isolate, orbit)                                  |
| `--views <list>`        | Angles for `isolate`: `front,back,left,right,top,bottom`         |
| `--max-edge <px>`       | Longest edge of each render (default 512)                        |
| `--background <color>`  | Background color for isolated renders (Built-in and URP)         |
| `-o, --output <path>`   | Output file; `isolate` writes `<name>-<view>.png` per angle      |

The isolated object is rendered with the scene's lighting. Under HDRP the sky stays behind the
object and `--background` has no effect: HDRP ignores the camera clear color, and clearing to a
solid color would throw off its automatic exposure. Under Built-in and URP the background is the
requested color, or transparent when the color has zero alpha.

### `ucp record`

Record a lightweight, silent Game or Scene view video without adding objects or scripts to the
scene. The defaults are tuned for agent vision: 960px on the longest edge, source aspect ratio
preserved, 15fps, 2Mbps, and H.264/MP4 on Windows and macOS or VP8/WebM on Linux.

```bash
# One bounded clip; waits for the finalized file
ucp record capture --duration 5 --view game -o playtest.mp4

# Surround any CLI/scripted sequence
ucp record start --view scene -o sequence.mp4
ucp transform move --name Player --to 0 1 4
ucp scene focus --name Player
ucp record stop

# Record an IUCPScript call with lead/tail context
ucp exec run SetupScene --record setup.mp4 --record-view scene

# Arm an event-triggered five-second clip
ucp record arm --on play-enter --duration 5 -o play.mp4
ucp record arm --on 'log:Boss spawned' --duration 5 -o boss.mp4
ucp record arm --on signal:impact --duration 5 -o impact.mp4
ucp record signal impact
```

`record capture` is the simplest choice for agents because it does not return until the atomic
`.partial` file has been finalized. `start` is detached and has a 60-second safety limit by default;
use `--max-duration 0` only when the caller guarantees `record stop`. `arm` also defaults to a
60-second trigger wait; use `--wait-timeout 0` to wait indefinitely. `status` reports the active,
armed, completed, or failed state plus path, dimensions, frames, dropped frames, duration, codec,
and file size. Use `ucp record <command> --help` for resolution, FPS, bitrate, format, overwrite,
and trigger options.

An active encoder is finalized before an assembly/domain reload because Unity cannot preserve its
native encoder across that boundary. Use `arm --on play-enter` or `arm --on play-exit` when the
event itself causes a reload; use detached `start`/`stop` for sequences that stay in the same domain.

Both dimensions may be supplied for a fixed canvas; UCP letterboxes as needed instead of stretching
the source. Supplying one dimension derives the other from the live view. Relative output paths are
resolved from the Unity project root, and extensionless paths receive the selected container suffix.

#### Recording for a model to watch: `--slowdown`

Video-understanding models do not watch a file, they sample it, typically at about one frame per
second regardless of the file's own frame rate. A six-second clip therefore reaches the model as
roughly six frames, and whatever happens between those samples is invisible: foot sliding, a camera
settling, a one-frame animation pop, a physics jitter. Raising `--fps` does not help, because the
sampler ignores it.

`--slowdown <factor>` raises effective temporal resolution instead. Frames are still captured at
`--fps` in real time; only the container's declared playback rate is divided by the factor, so the
same frames are spaced further apart. Nothing is re-encoded and no frames are interpolated, so the
model sees exactly what was rendered.

```bash
# One second of gameplay becomes six seconds of file: ~6 samples per gameplay second, not ~1
ucp record capture --duration 3 --fps 30 --slowdown 6 -o analysis.mp4
```

Use it whenever an agent has to judge motion -- contact, timing, smoothness, settling. Leave it at
the default of `1` for clips a human will watch, which are wrong at any other value. `record status`
reports the applied `slowdown` and the resulting `playbackFps`.

#### Which camera gets recorded

`--view game` records `Camera.main`, the camera tagged `MainCamera` -- not the Game view's composited
output. A project that renders through more than one enabled camera records only the tagged one, and
raising another camera's depth does not change the selection even though that camera visibly wins in
the Game view.

`--view scene` records the Scene view camera, which is independent of gameplay. That is the supported
way to hold a fixed vantage point while the game camera keeps following the player -- useful for
before/after comparisons, where a following camera hides exactly the difference being measured.

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
