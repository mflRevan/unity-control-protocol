---
name: ucp-runtime-debugging
description: >-
  Find out what the running Unity project is doing and why with `ucp logs`, `ucp run-tests`,
  `ucp exec`, `ucp profiler`, `ucp profile`, and `ucp frame capture`: read and follow console
  logs with filters, run edit-mode or play-mode tests by name pattern, execute registered editor
  scripts with parameters, profile frames and read hierarchies sorted by self time, and export
  structured captures. Use for playtesting loops, failure triage, test runs, and performance
  work. For compile errors and editor state use ucp-editor-lifecycle.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer.
metadata:
  author: mflRevan
  version: '0.6.3'
  homepage: https://unityctl.dev/skills/ucp-runtime-debugging
---

# Runtime debugging: logs, tests, scripts, profiler

## Logs

```bash
ucp logs status                                   # counts by level, top repeated messages, play-session window
ucp logs --count 20                               # newest buffered entries
ucp logs --level error --count 50
ucp logs --pattern 'NullReference|Exception' --count 100
ucp logs --filter 'level>=warning,channel=Shader'
ucp logs --id 1842                                # one entry with its full stack trace
ucp logs --after-id 1800 --level error            # only what happened since a cursor
ucp logs --follow --level error --count 5         # live; stops after 5 matches
ucp log tail --follow                             # `log` is an alias
```

- The bridge buffers console history since it loaded and seeds it from the Console window, so
  `logs` works for entries that predate the connection.
- `logs status` is the cheap first look: it collapses repeats into categories and reports the
  last play session's window separately.
- The `[editor]` line after every command already carries the console's error and warning
  counts and how many the command itself produced; use `logs` to read the messages.

## Tests

```bash
ucp run-tests                                     # edit-mode, whole project
ucp run-tests --mode play
ucp run-tests --filter ControllerSmokeTests       # regex over Namespace.Fixture.Test, substring fallback
ucp run-tests --filter 'Player.*Jump' --json
```

- Results include per-test status and duration; the exit code is non-zero on any failure.
- A console error or exception logged during the run fails an extra synthetic check, so a
  passing suite with a red console still fails. Lines a test declared as expected through
  `LogAssert.ignoreFailingMessages` are excluded.
- A filter that matches nothing is an error, not a pass.
- Play-mode tests enter play mode and reload the domain; expect the command to take longer and
  instance ids to change afterwards.

## Editor scripts (`IUCPScript`)

```bash
ucp exec list                                     # registered scripts in the project
ucp exec run setup-arena
ucp exec run spawn-wave --params '{"count":12,"radius":8}'
ucp exec run demo-autopilot --record run.mp4 --record-lead 0.5 --record-tail 1
```

A script is an editor class implementing `IUCPScript` (`Name`, `Description`,
`object Execute(string paramsJson)`); the return value comes back as JSON. Scripts run on the main
thread with full editor access, which makes them the tool for anything the command surface does
not cover. Two rules, because scripts bypass the bridge's guards: never call
`EditorUtility.DisplayDialog` or any native file panel, and never `SaveScene()` an untitled
scene; both block the editor on a prompt nobody can answer.

## Profiler

```bash
ucp profile --seconds 5 --mode play               # one-shot: start, wait, stop, summary
ucp profiler status                               # capabilities, frame buffer, current mode
ucp profiler config get
ucp profiler config set --deep-profile true --allocation-callstacks true
ucp profiler session start --mode play --clear-first
ucp play
ucp profiler frames list --limit 5                # take a fresh frame index from here
ucp profiler frames show --frame 1234 --include-threads
ucp profiler hierarchy --frame 1234 --thread 0 --sort self-time --limit 20 --fields name,selfMs,calls,gcMemory
ucp profiler timeline --frame 1234 --thread 0 --limit 100 --max-depth 3
ucp profiler callstacks --frame 1234 --kind hierarchy --item 42 --resolve-methods
ucp profiler summary --first-frame 1200 --last-frame 1300 --limit 10
ucp profiler capture save --output ProfilerCaptures/session.json
ucp profiler capture load --input ProfilerCaptures/session.raw
ucp profiler session stop
ucp stop
```

- Live frame indices churn while the editor runs; list frames immediately before `hierarchy`,
  `timeline`, or `callstacks`.
- `--fields` trims the payload to the columns you need; `--limit` and `--max-depth` bound it.
  Truncation is reported as "showing N of M", not silently.
- `capture save` writes a structured JSON snapshot for scripts and agents; `.raw`/`.data` files
  from the Profiler window can be loaded back with `capture load`.

## Frame export

```bash
ucp frame capture --out frame.json                # structured frame/profiler capture
ucp shader errors --errors-only                   # shader compile problems (see ucp-assets)
```

## Workflows

Autonomous playtest with triage:

```bash
ucp compile                                       # fail fast on CS#### errors
ucp logs status                                   # baseline counts
ucp play --log-file play.log
# drive the game: exec scripts, record, wait
ucp logs --level error --count 20
ucp stop
ucp logs status                                   # the lastPlayWindow block is this session
```

Find the hot path behind a spike:

```bash
ucp profiler session start --mode play --clear-first
ucp play
ucp profiler frames list --limit 3
ucp profiler summary --limit 10
ucp profiler hierarchy --frame <fresh> --sort self-time --limit 15 --fields name,selfMs,calls
ucp profiler callstacks --frame <fresh> --kind hierarchy --item <id> --resolve-methods
ucp profiler session stop && ucp stop
```

Run the tests that matter after a change:

```bash
ucp compile
ucp run-tests --filter 'Inventory' --json
ucp logs --level error --after-id <cursor from logs status>
```

## Pitfalls

- `run-tests --mode play` and `ucp play` both reload the domain; every instance id you hold is
  stale afterwards.
- Profiling in edit mode measures the editor; use `--mode play` for gameplay numbers.
- Deep profiling and allocation callstacks are expensive; turn them on for a targeted window,
  not a whole session.
