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

## Key workflow notes

- `ucp profiler summary` defaults to the most recent 120 buffered frames so it stays practical in live editor sessions. Pass `--first-frame` and `--last-frame` when you need an explicit range.
- New sessions automatically clear stale buffered frames when previous captures are still loaded, and the bridge clamps profiler buffer memory to safer live-editor budgets. Heavier modes such as allocation callstacks use a tighter cap.
- In the Unity Editor, `Profiler.enableBinaryLog` stays disabled at runtime. `ucp profiler capture save --output <file>.json` exports a structured snapshot instead; use the Profiler window for manual raw/data export if you need Unity's native file formats.
- Frame ids can churn quickly in a live buffer. For `timeline`, `hierarchy`, `callstacks`, and narrow `summary` queries, prefer grabbing a fresh frame id from `ucp profiler frames list` or `ucp profiler frames show` immediately before the follow-up command.
- Callstacks may legitimately come back empty for samples that do not carry stack data. Enabling allocation callstacks increases overhead and is most useful when you are specifically hunting allocations.

## Example edit-mode workflow

```bash
ucp profiler session clear
ucp profiler session start --mode edit --allocation-callstacks true --clear-first
ucp scene snapshot --depth 1
ucp profiler frames list --limit 5
ucp profiler timeline --frame 61792 --thread 0 --limit 10
ucp profiler hierarchy --frame 61792 --thread 0 --limit 10
ucp profiler summary --limit 5
ucp profiler capture save --output ProfilerCaptures/edit-loop.json
ucp profiler session stop
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
