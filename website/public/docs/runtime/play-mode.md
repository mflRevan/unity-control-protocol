# Play Mode & Compilation

Control Unity's play mode state from the command line.

## Commands

### `ucp play`

Enter play mode.

If Unity refuses to enter play mode because there are still breaking script errors, `ucp play` now returns a failure instead of reporting a false success. Use the existing log commands if you need the full console details.

If Unity is already in play mode, `ucp play` now fails clearly and points you to `ucp stop` instead of looking like a no-op toggle.

`ucp play` also refuses to proceed when the active scene has unsaved changes. Save explicitly with `ucp scene save`, or use `--save` on the scene-editing command that produced the change.

For unattended editor startup flows, pair lifecycle commands with `--dialog-policy` when Unity may raise recovery or Safe Mode prompts. A blocked startup dialog can leave the editor process alive without a live bridge until the prompt is resolved.

```bash
ucp play
```

### `ucp stop`

Exit play mode and return to edit mode.

```bash
ucp stop
```

On success, `ucp stop` also appends the same curated summary returned by `ucp logs status` so agents can immediately see warning/error counts from the just-finished play session without fetching the full log stream first.

### `ucp pause`

Toggle pause state during play mode.

```bash
ucp pause
```

### `ucp compile`

Trigger script recompilation. By default, blocks until compilation finishes and then reports the
result: per-assembly compiler errors and warnings are surfaced, and the command **exits non-zero
when compilation fails** instead of always reporting success. In `--json` mode the breakdown is
under `data.diagnostics` (with `errorCount`/`warningCount`). This needs a bridge that supports
`compile/diagnostics`; older bridges fall back to a plain completion report.

```bash
# Wait for compilation; non-zero exit + error list if a build breaks
ucp compile

# Fire and forget (skips error reporting)
ucp compile --no-wait
```

| Flag        | Description                                                          |
| ----------- | ------------------------------------------------------------------- |
| `--no-wait` | Return immediately without waiting for compilation (no diagnostics) |

Like `ucp play`, `ucp compile` now blocks on unsaved active-scene changes before triggering the reload path.

## Editing during play mode

`ucp object …` edits still apply while the editor is in Play Mode, but they affect only the running
instance — Unity discards runtime changes when you exit play, and it refuses to save scenes during
play. Rather than failing the `--save` opaquely, these commands detect Play Mode, skip the doomed
save, and return a clear warning (in human output and as `playMode: true` / `warning` in `--json`)
that the change will not persist. Stop play with `ucp stop` first if you need an edit to stick.

## Example Workflow

```bash
# Edit scripts directly in the project, compile, then test
ucp compile
ucp play
ucp screenshot -o test.png
ucp stop
```
