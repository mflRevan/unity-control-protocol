# Play Mode

Control Unity's play mode state from the command line.

## Commands

### `ucp play`

Enter play mode.

If Unity refuses to enter play mode because there are still breaking script errors, `ucp play` now returns a failure instead of reporting a false success. Use the existing log commands if you need the full console details.

```bash
ucp play
```

### `ucp stop`

Exit play mode and return to edit mode.

```bash
ucp stop
```

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

## Example Workflow

```bash
# Edit scripts directly in the project, compile, then test
ucp compile
ucp play
ucp screenshot -o test.png
ucp stop
```
