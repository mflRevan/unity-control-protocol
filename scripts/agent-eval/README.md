# Agent-in-the-loop CLI evaluation

UCP's primary users are AI agents, not the humans behind them. So interactive/CLI features are
validated the way an agent will actually consume them: a **weak, cheap model** drives the new surface
from nothing but the CLI's own `--help`, given a **deliberately terse, outcome-only task**. Wherever
it stumbles — repeated `--help` probing, guessed/misused flags, wrong addressing, dead-ends, giving up
— that is a documentation or ergonomics defect, surfaced cheaply before it reaches a frontier agent.

This is the standard validation step for any new/changed `ucp` command, flag, output shape, or
skill/docstring.

## Run it

```pwsh
./scripts/agent-eval/run-eval.ps1 -TaskFile scripts/agent-eval/tasks/arrange-and-inspect.md -Name arrange
```

The runner: ensures the live editor + **local** bridge are up, hands the task to the model
(`opencode run -m opencode/deepseek-v4-flash-free --dir sandbox`), and captures the transcript, the
`ucp` command sequence, and before/after scene snapshots under `runs/<name>-<stamp>/` (gitignored).

- **Sandbox / model constraints:** `sandbox/AGENTS.md` (intentionally minimal — tests whether
  `--help` alone is self-sufficient).
- **Tasks:** `tasks/*.md`.
- **Default model:** a free weak one on purpose (`deepseek-v4-flash-free`, `mimo-v2.5-free`). Strong
  models paper over doc gaps; weak ones expose them.

## Procedure

1. Build the surface with honest, non-exhaustive docstrings (don't over-polish docs up front).
2. Stand up a live editor with the local bridge embedded.
3. Hand a terse, outcome-only task to a weak model; sandbox it to `ucp` only.
4. Read the **trace**, not just the outcome.
5. Fix docstrings/ergonomics/missing-commands, then **re-run the same task** to confirm.
6. Record durable lessons below.

## Recorded pitfalls (from real runs)

First in-scene-authoring eval (`deepseek-v4-flash-free`, "arrange three pillars") — each became a fix:

1. **Every "create a thing in the scene" workflow needs a one-call primitive/builder.** The model
   could not make a cube: `object create` only made empty GameObjects, so it hand-added
   MeshFilter+MeshRenderer+Collider and could not reference the built-in mesh over the CLI. Fix:
   `ucp object create --primitive`. Expose the intent ("a cube"), not just the parts.
2. **Never terminate the editor with an OS window-close (WM_CLOSE).** On a dirty scene it raises
   Unity's native save-on-quit dialog, which hangs the main thread and the bridge. Use the in-editor
   `editor/quit` (`EditorApplication.Exit(0)`, prompt-free); else force-kill. Recovery must never risk
   another modal.
3. **`ucp exec` / user IUCPScript code bypasses `EditorModalGuard`.** A script that calls
   `SaveScene()` on an untitled scene (or any DisplayDialog/file panel) hangs the bridge. `--timeout`
   protects the CLI, not the editor. Exec scripts must be modal-safe; force-close is the recovery.
4. **`opencode run` with stdout redirected only flushes summary lines.** The real command trace is in
   `~/.local/share/opencode/log/opencode.log` (or the sqlite db), filtered to the run window — or use
   a logging `ucp` shim first on PATH.
5. **Do not mutate the bridge package while a live eval runs against it.** It is mounted by symlink;
   editing a controller — or `git commit` rewriting line endings via autocrlf — triggers a Unity
   domain reload mid-run, making the bridge unresponsive and corrupting the eval. Freeze it.
6. **A new flag/command is not enough — it must be discoverable from `--help` alone.** Adding
   `object create --primitive` did not, by itself, change the weak model's behavior: it read the
   help, created empty objects out of habit, then hunted (`grep CreatePrimitive`, `instantiate
   "PrimitiveType.Cube"`) and fell back to an exec script. Weak agents follow habit and the first
   line they read. Mitigations applied: put the right path in the command's one-line summary, add a
   redirecting error on the predictable wrong path (`instantiate "PrimitiveType.X"` → "use
   `--primitive`"), and show it in the skill examples.
7. **Calibrate the probe model — and confirm it actually does agentic tool-use.** Choosing the model
   is as important as the task. Of the free models tried: `deepseek-v4-flash-free` *engages* (runs
   commands) and is excellent at surfacing **missing-capability** gaps (it found the modal hang and
   the primitive gap) — but it is too habit-bound to follow even explicit docs, so it cannot confirm
   a **documentation/ergonomics** fix (it ignored prominent `--primitive` help and a redirecting
   error across two runs). `mimo-v2.5-free` and `north-mini-code-free` barely engaged (~10 log lines,
   zero `ucp` calls — they don't reliably tool-call through opencode). Lesson: use the weak *engaging*
   model to find capability gaps; use a *capable* model to confirm that a doc/ergonomics fix actually
   changes behavior. A model that ignores instructions, or won't tool-call, gives no signal about doc
   quality. Note on this opencode account: the capable models (e.g. `qwen3.6-plus`) require a payment
   method — without one, `opencode run` exits 0 after bootstrap with no model turn (a silent no-op
   that looks like "the agent did nothing"). Probe a model with a trivial prompt first to confirm it
   actually responds before trusting an empty eval result.
