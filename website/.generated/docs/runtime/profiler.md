# Profiler

Capture, inspect, and summarize Unity Profiler data through the bridge.

## Commands

| Command | Description |
| ------- | ----------- |
| `ucp profiler status` | Show profiler capabilities, current config, session state, and buffered frame range |
| `ucp profiler config get` | Read the current profiler configuration |
| `ucp profiler config set` | Update mode, deep profile, allocation callstacks, categories, and buffer settings |
| `ucp profiler session start` | Start a profiling session in edit or play mode |
| `ucp profiler session stop` | Stop the active profiling session |
| `ucp profiler session clear` | Clear buffered profiler frames |
| `ucp profiler capture save` | Save the current capture as a structured JSON snapshot or copy an existing raw/data capture |
| `ucp profiler capture load` | Load an existing `.raw` or `.data` capture into the Profiler |
| `ucp profiler frames list` | List buffered frames with CPU, FPS, thread count, and GC allocation summaries |
| `ucp profiler frames show` | Inspect one frame in more detail, optionally with thread enumeration |
| `ucp profiler timeline` | Read ordered timeline samples for a frame/thread |
| `ucp profiler hierarchy` | Read hierarchy items for a frame/thread |
| `ucp profiler callstacks` | Resolve raw-sample or hierarchy-item callstacks when Unity exposes them |
| `ucp profiler summary` | Aggregate bounded profiler stats and top markers |
| `ucp profile --seconds <n>` | Start a short profiler session, wait, stop, and print a compact frame-time summary |
| `ucp frame capture --out <file>.json` | Export the current profiler/frame buffer as structured JSON for frame-debugging workflows |

## Key workflow notes

- `ucp profiler summary` defaults to the most recent 120 buffered frames so it stays practical in live editor sessions. Pass `--first-frame` and `--last-frame` when you need an explicit range. The span is hard-capped at 600 frames: aggregation walks every raw frame view on the editor's main thread, so a wider request would stall the editor. When the cap applies, the response says so in `warnings` and keeps the most recent frames.
- **Keep responses small.** `timeline` and `hierarchy` are the two surfaces that can flood an agent's context:
  - `--fields name,selfMs` returns only the columns you name. Hierarchy rows carry `item, name, path, depth, totalMs, selfMs, calls, gcMemory, childCount`; timeline samples carry `sample, name, category, startMs, durationMs, depth, childCount, metadataCount`. On a 20-row hierarchy, `--fields name,selfMs` cuts the JSON payload by about 70%.
  - Both report `totalCount` next to `count`, so `truncated: true` is quantified — you can tell "50 of 52" from "50 of 50,000" and decide whether to look further. Human output prints it as `Showing 50 of 4,312 rows`.
  - Reach for `--sort self-time --limit 20 --fields name,selfMs` to find hot paths, and `--max-depth` to keep the tree shallow, rather than raising `--limit` and reading everything.
- New sessions automatically clear stale buffered frames when previous captures are still loaded, and the bridge clamps profiler buffer memory to safer live-editor budgets. Heavier modes such as allocation callstacks use a tighter cap.
- In the Unity Editor, `Profiler.enableBinaryLog` stays disabled at runtime. `ucp profiler capture save --output <file>.json` exports a structured snapshot instead; use the Profiler window for manual raw/data export if you need Unity's native file formats.
- Frame ids can churn quickly in a live buffer. For `timeline`, `hierarchy`, `callstacks`, and narrow `summary` queries, prefer grabbing a fresh frame id from `ucp profiler frames list` or `ucp profiler frames show` immediately before the follow-up command.
- Callstacks may legitimately come back empty for samples that do not carry stack data. Enabling allocation callstacks increases overhead and is most useful when you are specifically hunting allocations.
- `ucp profile --seconds N` is the quick "did this optimization help?" path. It clears stale frames, profiles for the requested window, stops, and reports average CPU/GPU/FPS plus top markers from the buffered frames.
- `ucp frame capture --out frame.json` writes the same structured capture payload used by `ucp profiler capture save`, giving agents a durable frame dump without opening the Profiler window. Unity does not expose every Frame Debugger event through public APIs, so the export focuses on profiler frame/timeline/hierarchy data and reports warnings when frame data is unavailable.

## Example edit-mode workflow

```bash
ucp profiler session clear
ucp profiler session start --mode edit --allocation-callstacks true --clear-first
ucp scene snapshot --depth 1
ucp profiler frames list --limit 5
ucp profiler timeline --frame 61792 --thread 0 --limit 10
ucp profiler hierarchy --frame 61792 --thread 0 --limit 10
ucp profiler hierarchy --sort self-time --limit 20 --fields name,selfMs   # hot paths, minimal payload
ucp profiler summary --limit 5
ucp profiler capture save --output ProfilerCaptures/edit-loop.json
ucp profiler session stop

# One-shot profile summary
ucp profile --seconds 5 --mode edit

# Structured frame dump
ucp frame capture --out ProfilerCaptures/frame.json
```

## Example play-mode workflow

```bash
ucp profiler session start --mode play --deep-profile false --clear-first
ucp play
ucp profiler frames list --limit 10
ucp profiler summary --limit 10
ucp stop
ucp profiler session stop
```

## JSON-first usage

All profiler commands support `--json`.

```bash
ucp profiler status --json
ucp profiler frames list --limit 3 --json
ucp profiler summary --limit 5 --json
ucp profiler capture save --output ProfilerCaptures/agent-snapshot.json --json
```
