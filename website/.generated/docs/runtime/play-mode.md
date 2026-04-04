# Play Mode & Compilation

Control Unity's play mode state from the command line.

## Commands

### `ucp play`

Enter play mode.

If Unity refuses to enter play mode because there are still breaking script errors, `ucp play` now returns a failure instead of reporting a false success. Use the existing log commands if you need the full console details.

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

Trigger script recompilation. By default, blocks until compilation finishes.

```bash
# Wait for compilation
ucp compile

# Fire and forget
ucp compile --no-wait
```

| Flag        | Description                                        |
| ----------- | -------------------------------------------------- |
| `--no-wait` | Return immediately without waiting for compilation |

Like `ucp play`, `ucp compile` now blocks on unsaved active-scene changes before triggering the reload path.

## Example Workflow

```bash
# Edit scripts directly in the project, compile, then test
ucp compile
ucp play
ucp screenshot -o test.png
ucp stop
```
