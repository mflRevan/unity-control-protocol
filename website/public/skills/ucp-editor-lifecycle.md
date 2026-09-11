---
name: ucp-editor-lifecycle
description: >-
  Bring a Unity project under control with the `ucp` CLI: install the bridge, open or adopt the
  editor, read the `[editor]` state line every command prints, recompile, enter and leave play
  mode, and recover from modal dialogs. Use when a task starts (is Unity running? is the console
  red? is the scene dirty?), when a command reports COMPILE ERRORS or a MODAL, or when the editor
  must be opened, restarted, or closed. For scene content, assets, UI, capture, debugging, or
  project configuration, use the matching ucp-* skill or the unity-control-protocol omni skill.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer.
metadata:
  author: mflRevan
  version: '0.6.4'
  homepage: https://unityctl.dev/skills/ucp-editor-lifecycle
---

# Editor lifecycle, state, and recovery

`ucp` talks to a bridge inside the Unity Editor over a local WebSocket. Commands run on Unity's
main thread, so anything that blocks it (a modal dialog, a synchronous import, a compile) blocks
every command. This skill is about knowing which state the editor is in and steering it, so the
other surfaces have a working editor to talk to.

## Ground rules

- Pin the project once with `UCP_PROJECT=/path/to/project` (or `--project`); every command
  auto-detects the project from the working directory otherwise.
- Every command that reaches the bridge ends with a dim line. Read it before the next command:

  ```text
  [editor] edit mode · scene SampleScene (dirty) · console 2 errors, 1 warning (+1 error from this command)
  ```

  Segments that change what you do next: `COMPILE ERRORS` (fix scripts, then `ucp compile`),
  `compiling` / `importing assets` (wait, retry), `prefab stage <path>` (scene commands target the
  open prefab, not the scene), `MODAL "<title>"` (answer it, see below), `play mode` (you are in
  play mode; edits are not saved). In `--json` the same data is the `editor` object.
- Instance ids from `ucp scene snapshot` are short-lived. They change after recompiles, domain
  reloads, scene loads, package changes, and test runs. Re-snapshot before reusing one, or address
  objects by `--path "Root/Child"` where a command supports it.
- `--json` on any command gives a `{ "success": ..., "data": ..., "editor": ... }` envelope and a
  non-zero exit on failure. Prefer it when you parse output.

## Install and connect

```bash
ucp doctor                 # CLI, bridge package, Unity resolution, serialization settings
ucp install                # add the bridge to Packages/manifest.json, pinned to this CLI version
ucp install --dev          # repo checkout only: mount the local bridge for bridge development
ucp connect                # handshake; prints Unity version, protocol, and main-thread readiness
ucp bridge status          # installed bridge source and whether it matches the CLI
ucp bridge update          # move the manifest reference to this CLI's bridge version
```

`ucp connect` reports `Main thread: responsive` or `not serving yet (first import or compile in
progress)`. A socket answering is not a usable editor; wait for the responsive line after a fresh
open.

## Open, adopt, close

```bash
ucp open                   # launch the resolved Unity for the project, or adopt a running one, wait for the bridge
ucp editor status          # pid, executable, project version, requested version, session
ucp editor ps              # every Unity process ucp can see (import workers are filtered out)
ucp editor restart         # in-editor quit, then relaunch; waits for the old process to exit
ucp editor close           # in-editor quit; --force kills the process if the quit does not return
ucp editor logs --lines 200
```

- Pick the editor version with `--unity <path/to/Unity.exe>` or `--force-unity-version 6000.4.0f1`
  when the project's `ProjectVersion.txt` is not what you want.
- The first open after a Library wipe imports for minutes. `ucp open` waits until the main thread
  actually serves requests; with `--timeout 0` it waits indefinitely.
- Never close Unity through the OS window. On a dirty scene that raises Unity's native save
  prompt with nobody to answer it. `ucp editor close` uses the in-editor quit, which is prompt
  free, and `--force` is the recovery path.

## Compile, play, stop, pause

```bash
ucp compile                # recompile and wait; prints per-assembly CS#### errors, exits non-zero on failure
ucp compile --no-wait      # kick off compilation and return (a later command waits for the reload itself)
ucp play                   # saves dirty titled scenes first; refuses on a dirty untitled scene
ucp play --log-file play.log
ucp pause                  # toggles
ucp stop
```

- Entering play mode reloads the domain. `ucp play` confirms the transition and reports Unity's
  refusal reason when scripts do not compile. Do not retry blindly; read `ucp compile`.
- A dirty untitled scene blocks `play`, `scene load`, and `editor close` on purpose (Unity would
  otherwise ask where to save). Save it under a path with `ucp scene save` after giving it one, or
  discard with `--keep-untitled`/`--no-save` variants where offered, or start from a titled scene.
- Edits made in play mode are lost on `stop`, exactly as in the editor.

## Modal dialogs

The bridge handshake reports how long ago the main thread last ticked. When it is stale, the CLI
looks for a dialog before sending anything:

- Known Unity prompts are answered per `--dialog-policy` (default `auto`): Safe Mode is declined
  with Ignore, "Packages with Errors" is dismissed, editor-version and project-upgrade prompts
  are continued. Use `--dialog-policy manual` to answer nothing automatically.
- Anything else fails at once with the dialog's title and buttons instead of a 30 s timeout:

  ```text
  [ERR] Unity is blocked by a modal dialog "Save Scene?" [Save | Don't Save | Cancel] ...
  ```

  Answer it deliberately:

  ```bash
  ucp editor dialog                         # list open dialogs and their buttons
  ucp editor dialog --answer "Don't Save"   # exact label first, then substring, case-insensitive
  ```

- A request that times out while a dialog opened mid-flight is diagnosed the same way when it
  returns. Dialog detection is Windows-only today; elsewhere the request timeout applies.
- Scripts run through `ucp exec` bypass the bridge's save guard. Never call
  `EditorUtility.DisplayDialog` or `SaveScene()` on an untitled scene from an `IUCPScript`.

## Diagnose a stuck editor

```bash
ucp connect --timeout 5    # main thread responsive? compiling?
ucp editor dialog          # anything modal?
ucp logs status            # console counts and the most repeated messages
ucp editor logs --lines 100
ucp editor close --force && ucp open
```

If `ucp open` reports the editor is running without a bridge and the project has compile errors,
the editor is in Safe Mode: fix the reported `CS####` errors, then `ucp editor restart`.

## Global flags worth knowing

`--project`, `--unity`, `--force-unity-version`, `--json`, `--timeout <s>` (0 waits forever; UI
render commands default to 310 s, everything else to 30 s), `--dialog-policy
auto|manual|ignore|recover|safe-mode|cancel`, `--bridge-update-policy`. `UCP_EDITOR_STATE=0`
silences the `[editor]` line.
