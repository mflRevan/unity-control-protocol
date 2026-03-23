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

# Filter using a Unity Test Runner test name or fully qualified name
ucp run-tests --filter "PlayerMovement"
ucp run-tests --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_TruncatesBulkResultsToTenEntries"

# JSON output for CI integration
ucp run-tests --json
```

| Flag                  | Description                                       |
| --------------------- | ------------------------------------------------- |
| `--mode <edit\|play>` | Test mode (default: edit)                         |
| `--filter <pattern>`  | Filter string passed through to Unity Test Runner |

`--filter` uses Unity Test Runner semantics rather than a UCP-defined regex engine. Prefer fully qualified test names when you need precise selection.

`ucp run-tests` now also blocks when the active scene has unsaved changes, so automated test runs do not fall through to Unity-owned save prompts during play-mode or recompilation-heavy test setup.

## CI/CD Integration

UCP's test runner is designed for CI pipelines. Use `--json` output and the exit code to determine pass/fail:

```bash
ucp run-tests --json > results.json
if [ $? -ne 0 ]; then
  echo "Tests failed!"
  exit 1
fi
```
