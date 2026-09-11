# Editor State & Modal Dialogs

Every command that talks to the bridge ends with one dim line describing the state it left the
editor in, and every `--json` envelope carries the same data under `editor`. The point is that an
agent is told, without asking, whether the console went red, whether Unity is in play mode, and
whether the scene is dirty, so it can decide to look closer instead of finding out three commands
later.

```text
[editor] edit mode · scene GetStarted_Scene (dirty) · console 0 errors, 8 warnings (+1 error from this command)
```

The line is built from a summary the bridge attaches to every response it produces on Unity's main
thread. It costs no extra request and no extra editor frame: the bridge reads a handful of O(1)
editor flags and the Console window's own badge counts while it already has the main thread.
`UCP_EDITOR_STATE=0` turns the line and the JSON field off.

## What it reports

Always:

| segment | meaning |
|---|---|
| `edit mode` / `play mode` / `play mode (paused)` | `EditorApplication.isPlaying` and `isPaused`; `entering play` / `exiting play` while a transition is pending |
| `scene <name>` | the active scene, with `(dirty)` and `(untitled)` when they apply, and `+N more dirty scenes` when other loaded scenes are dirty |
| `console N errors, M warnings` | the Console window's badge counts (errors include exceptions); `console clean` when both are zero |
| `(+N errors, +M warnings from this command)` | entries logged after this command's request was dispatched, so a mutation that logs an error is called out on the spot |

Only when true: `COMPILE ERRORS` (`EditorUtility.scriptCompilationFailed`), `compiling`,
`importing assets`, `building player`, `prefab stage <asset path>` (scene commands would target the
open prefab, not the scene), `recording` / `recording armed` (`ucp record`), and
`MODAL "<title>" [buttons]` when the CLI detected a dialog blocking the editor.

In JSON the same fields appear as an object:

```json
"editor": {
  "mode": "edit",
  "scene": { "name": "GetStarted_Scene", "dirty": true },
  "console": { "errors": 0, "warnings": 8, "newErrors": 1 },
  "compileErrors": true
}
```

`ucp editor state` is not a command; the summary rides on whatever you ran last. Use
`ucp logs status`, `ucp scene dirty-summary`, or `ucp compile` when you want the detail behind a
segment.

## Modal dialogs

Unity's modal dialogs (`Enter Safe Mode?`, `Packages with Errors`, save prompts, the API updater,
package restarts, anything a script raises with `EditorUtility.DisplayDialog`) block the editor's
main thread. The bridge socket keeps answering, but no command can run until a button is pressed,
and an unattended editor stays stuck.

The CLI now tells the two situations apart. The bridge's handshake reports how long ago the main
thread last ticked; when that exceeds 1.5 seconds the CLI enumerates the editor's dialog windows
before sending the request:

- A dialog the CLI recognises by title is answered according to `--dialog-policy` (default
  `auto`: decline Safe Mode with **Ignore**, close **Packages with Errors** with **Dismiss**,
  continue past the non-matching-editor and project-upgrade prompts).
- An unrecognised dialog is never answered automatically. The command fails at once, in about a
  tenth of a second rather than after the request timeout, naming the dialog and its buttons:

```text
[ERR] Unity is blocked by a modal dialog "Save Scene?" [Save | Don't Save | Cancel] and cannot run
      commands until it is closed. Answer it with `ucp editor dialog --answer "<button>"` (or in the editor).
[editor] MODAL "Save Scene?" [Save | Don't Save | Cancel]
```

- No dialog but a stalled main thread means a synchronous import, compile, or a native prompt the
  window enumeration cannot see; the CLI says so and waits as before.
- A dialog that opens after the check, while a request is already in flight, cannot be seen until
  that request times out; when it does, the CLI performs the same check, answers a recognised
  prompt, and names an unrecognised one in the error so the retry is deliberate.

Dialogs seen in practice, with the answer that keeps an unattended editor useful:

| dialog | buttons | automatic answer |
|---|---|---|
| `Enter Safe Mode?` (compile errors at open) | Enter Safe Mode / Ignore / Quit | Ignore (`auto`, `ignore`), Enter Safe Mode (`recover`, `safe-mode`) |
| `Packages with Errors` | Open Package Manager / Dismiss Forever / Dismiss | Dismiss |
| `Opening Project in Non-Matching Editor Installation` | Continue / Quit | Continue |
| `Project Upgrade Required` | Confirm / Cancel | Confirm |
| `Project Downgrade Required` | Continue / Quit | Continue |
| `Opening file failed` (asset database lost its `Library/` underneath the editor) | Try Again / Force Quit / Cancel | none; the editor is unrecoverable, `ucp editor dialog --answer "Force Quit"` and reopen |
| `Fatal Error!` (follows Force Quit and crashes) | Quit | none; press Quit, the process exits |
| `Script Updating Consent` (API updater) | Yes for these and later / No / Yes just these | none; pass `-accept-apiupdate` or answer it |
| `Input System native platform backend not enabled` | Enable & Restart / Don't Enable | none |
| a script's own `EditorUtility.DisplayDialog` | anything | none; fail fast and name it |

Unity's progress window (`Hold on...`, shown during imports, compiles, and play-mode entry) is not a dialog: it is never listed or answered, and its cancel-style button is never pressed.

Answer a dialog deliberately with `ucp editor dialog`:

```bash
ucp editor dialog                       # list open dialogs with their buttons
ucp editor dialog --answer "Don't Save" # press a button (exact, then substring, case-insensitive)
ucp editor dialog --json
```

Dialog detection presses buttons through the Win32 message loop and is Windows-only today; on
other platforms `ucp editor dialog` reports nothing and commands fall back to the request timeout.

### Avoiding dialogs in the first place

- Batch mode (`-batchmode`) suppresses most startup prompts; Safe Mode becomes an automatic quit
  unless `-ignoreCompilerErrors` is passed, and `-accept-apiupdate` pre-answers the API updater.
- Unity remembers "Don't ask again" answers under `EditorPrefs` keys prefixed `DialogOptOut.`,
  and `EditorPrefs["EnterSafeModeDialog"] = false` makes Unity enter Safe Mode silently.
- Bridge commands never trigger Unity's save prompts themselves: `ucp scene load`, `ucp play`, and
  `ucp editor close` go through the bridge's modal guard, which saves titled scenes or refuses
  with an explanation instead of letting Unity ask.
- Scripts run through `ucp exec` bypass that guard. Do not call `EditorSceneManager.SaveScene()`
  on an untitled scene or `EditorUtility.DisplayDialog` from an `IUCPScript`.
