# Testing

Run Unity Test Framework tests from the command line.

## Commands

### `ucp run-tests`

Execute tests in edit mode or play mode.

```bash
# Run all edit mode tests
ucp run-tests

# Run in play mode
ucp run-tests --mode play

# Filter by test name
ucp run-tests --filter "PlayerMovement"

# JSON output for CI integration
ucp run-tests --json
```

| Flag                  | Description                  |
| --------------------- | ---------------------------- |
| `--mode <edit\|play>` | Test mode (default: edit)    |
| `--filter <pattern>`  | Filter tests by name pattern |

## CI/CD Integration

UCP's test runner is designed for CI pipelines. Use `--json` output and the exit code to determine pass/fail:

```bash
ucp run-tests --json > results.json
if [ $? -ne 0 ]; then
  echo "Tests failed!"
  exit 1
fi
```
