<p align="center">
  <img src="assets/branding/ucp-icon.svg" alt="UCP logo" width="80" />
</p>

<h1 align="center">Unity Control Protocol</h1>

<p align="center">
  <strong>The Unity Editor as a command line.</strong><br>
  Scenes, objects, assets, materials, prefabs, UI Toolkit, play mode, tests, profiler, builds, screenshots and video, from a terminal or an AI agent.
</p>

<p align="center">
  <a href="https://www.npmjs.com/package/@mflrevan/ucp"><img src="https://img.shields.io/npm/v/@mflrevan/ucp?style=flat&color=7c3aed&label=npm" alt="npm version" /></a>&nbsp;
  <a href="https://github.com/mflRevan/unity-control-protocol/releases"><img src="https://img.shields.io/github/v/release/mflRevan/unity-control-protocol?style=flat&color=7c3aed&label=release" alt="GitHub release" /></a>&nbsp;
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-7c3aed?style=flat" alt="MIT license" /></a>&nbsp;
  <a href="https://discord.gg/F4RjhdVTbz"><img src="https://img.shields.io/badge/discord-join-5865F2?style=flat&logo=discord&logoColor=white" alt="Discord" /></a>
</p>

<p align="center">
  <a href="https://unityctl.dev/docs">Docs</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://unityctl.dev/skills">Agent skills</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://github.com/mflRevan/unity-control-protocol/releases">Releases</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://discord.gg/F4RjhdVTbz">Discord</a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://unityctl.dev/llms.txt">llms.txt</a>
</p>

<br>

```console
$ ucp object create Crate --primitive Cube
[OK] Created 'Crate' (id: -5576)
[editor] edit mode · scene GetStarted_Scene (dirty) · console clean

$ ucp spatial ground --name Crate && ucp view isolate --name Crate --views front,right -o crate.png
[OK] Saved crate.png (1024x512)
[editor] edit mode · scene GetStarted_Scene (dirty) · console clean

$ ucp play && ucp record capture --duration 5 --slowdown 4 -o run.mp4
[OK] Entered play mode
[OK] Recording saved: run.mp4
[editor] play mode · scene GetStarted_Scene · console clean
```

`ucp` is a Rust CLI that talks to a small bridge package inside the Unity Editor over a
localhost WebSocket. Every editor operation becomes a command with `--json` output, an exit
code, and a one-line report of the editor's state. It runs entirely on your machine: no cloud
service, no Unity account, no license activation. It attaches to the editor you have open or
launches one.

## Contents

- [Why](#why)
- [Sixty seconds to a working setup](#sixty-seconds-to-a-working-setup)
- [What it can do](#what-it-can-do)
- [Built for agents](#built-for-agents)
- [Workflows](#workflows)
- [How it works](#how-it-works)
- [Compatibility](#compatibility)
- [Contributing, community, license](#contributing-community-license)

## Why

The Unity Editor is a GUI. Everything that matters in a project, the scene graph, serialized
properties, importer settings, play mode, the console, the profiler, lives behind windows and
mouse clicks. Scripts, CI, and AI agents cannot click, so they end up guessing from YAML on disk
or driving the editor through brittle automation.

UCP gives that surface a stable command line. Unity's own CLI (July 2026) covers editor
installs, licensing, project scaffolding, CI plumbing, and a generic command catalog; UCP is the
layer inside the editor that it does not reach, and the two compose.

| | Unity CLI + Pipeline | UCP |
| --- | --- | --- |
| Scene hierarchy, objects, components, serialized properties | catalog | yes, with Undo |
| Transforms, spatial queries (raycast, bounds, ground) | - | yes |
| Composed renders a vision model can read (isolate, orbit) | screenshot | yes |
| Video capture, event-triggered, model-paced (`--slowdown`) | - | yes |
| UI Toolkit lint / inspect / populate / screenshot | - | yes (Unity 6) |
| Profiler sessions, frames, hierarchy, callstacks | planned | yes |
| Reference graph across the project (native, no editor needed) | - | yes |
| Asset moves that keep GUIDs, importer settings, materials, prefabs | - | yes |
| Packages, scoped registries, selective `.unitypackage` import | - | yes |
| Editor state on every command, modal dialog detection and answering | - | yes |
| Works while the project has compile errors | no (Safe Mode) | yes |
| Minimum Unity version | 6.0 | 2021.3 |
| Requires a Unity account | yes | no |

Full comparison: [docs/project/unity-cli-competitive-analysis.md](docs/project/unity-cli-competitive-analysis.md).

## Sixty seconds to a working setup

```bash
npm install -g @mflrevan/ucp        # or: pnpm add -g @mflrevan/ucp && pnpm approve-builds

cd /path/to/YourUnityProject
ucp install                          # adds the bridge package to Packages/manifest.json
ucp open                             # launches Unity (or adopts the open editor) and waits for the bridge
ucp doctor                           # CLI, bridge, Unity resolution, serialization settings
```

Then anything:

```bash
ucp scene snapshot                   # hierarchy with live instance ids
ucp scene query 'component=Camera' --fields instanceId,name
ucp object set-property --id 46894 --component Rigidbody --property m_Mass --value 2.5 --save
ucp compile                          # exits non-zero with the CS#### errors
ucp run-tests --filter Inventory --json
ucp screenshot -o game.png
```

Other install paths (cargo, prebuilt binaries): [docs/getting-started/installation.md](docs/getting-started/installation.md).

## What it can do

| surface | commands | for example |
| --- | --- | --- |
| **Editor lifecycle** | `install` `open` `connect` `doctor` `editor` `bridge` `compile` `play` `stop` `pause` | `ucp editor dialog --answer Ignore` |
| **Scene authoring** | `scene` `object` `transform` `spatial` `prefab` | `ucp object create Crate --primitive Cube` |
| **Assets** | `asset` `files` `material` `references` `shader` `script` | `ucp asset bulk-move --moves '{...}' --dry-run` |
| **UI Toolkit** | `ui list` `lint` `inspect` `screenshot` `check` | `ucp ui check Inventory.ucp-ui.json --all-states` |
| **Visual feedback** | `screenshot` `view capture` `isolate` `orbit` `record` | `ucp view isolate --name Crate -o grid.png` |
| **Runtime debugging** | `logs` `run-tests` `exec` `profiler` `profile` `frame` | `ucp profiler hierarchy --sort self-time --limit 20` |
| **Project config** | `packages` `settings` `build` | `ucp packages unitypackage import Pack.unitypackage --select Assets/Trees` |
| **Version control** | `vcs` | `ucp vcs status` |

Every command has `--help` with real argument hints, and every surface has a docs page at
[unityctl.dev/docs](https://unityctl.dev/docs).

<p align="center">
  <img src="assets/readme/capabilities.png" alt="UCP capabilities across Setup, Author, Runtime, and Ship phases" width="820" />
</p>

## Built for agents

An agent driving a GUI application blind makes the same three mistakes: it does not notice the
console went red, it does not notice the editor is in play mode or stuck on a dialog, and it keeps
using object ids that a recompile invalidated. UCP addresses each at the protocol level.

- **State on every command.** Each command that reaches the editor ends with one line, and `--json`
  envelopes carry it as an `editor` object: mode, active scene and dirtiness, console error and
  warning counts, what this command logged, and flags such as `COMPILE ERRORS`, `compiling`,
  `prefab stage`, or `MODAL "Save Scene?" [Save | Don't Save | Cancel]`.
- **Modal dialogs are handled, not hung on.** Recognised Unity prompts (Safe Mode, package errors,
  version mismatch) are answered by policy; anything else fails in a tenth of a second with the
  dialog's buttons and `ucp editor dialog --answer "<button>"` to press one.
- **Vision-ready capture.** `view isolate` renders an object from four sides in one image;
  `record capture --slowdown 6` stretches a clip so a video model that samples one frame per second
  actually sees the motion.
- **Structured everything.** `--json` on every command, exit codes that mean something, payloads
  bounded with `--limit`, `--fields`, `--detail summary`, and truncation reported rather than hidden.

### Skills

The skills are the manual an agent loads when a task touches Unity. They follow the
[Agent Skills](https://agentskills.io) format, live in [`skills/`](skills/) as hand-written ground
truth, and are published for every harness:

```text
# Claude Code
/plugin marketplace add mflRevan/unity-control-protocol
/plugin install ucp@unity-control-protocol            # one omni skill, the default
/plugin install ucp-surfaces@unity-control-protocol   # eight focused skills, one per surface

# Codex, Cursor, Copilot, Gemini CLI, opencode, Amp (anything reading .agents/skills)
npx skills add mflRevan/unity-control-protocol
gh skill install mflRevan/unity-control-protocol unity-control-protocol

# Manual: every skill is served raw
curl -fsSL https://unityctl.dev/skills/unity-control-protocol.md -o .agents/skills/unity-control-protocol/SKILL.md
```

| skill | scope |
| --- | --- |
| `unity-control-protocol` | the whole surface; recommended default |
| `ucp-editor-lifecycle` | install, open, state line, compile, play, dialogs |
| `ucp-scene-authoring` | hierarchy, primitives, properties, transforms, spatial, prefabs |
| `ucp-assets` | assets, files, materials, references, importer settings |
| `ucp-ui-toolkit` | UXML/USS lint, inspect, populate, screenshot, check |
| `ucp-visual-feedback` | screenshots, composed views, recordings |
| `ucp-runtime-debugging` | logs, tests, exec scripts, profiler |
| `ucp-project-config` | packages, settings, builds |
| `ucp-version-control` | Unity VCS fallback |

For agents that read documentation directly: [`llms.txt`](https://unityctl.dev/llms.txt),
[`llms-full.txt`](https://unityctl.dev/llms-full.txt), and every docs page as Markdown by
appending `.md`. Details in [docs/agents/skills.md](docs/agents/skills.md).

## Workflows

**Playtest and triage, unattended.**

```bash
ucp compile
ucp play --log-file play.log
ucp record arm --on 'log:Boss spawned' --duration 5 -o boss.mp4 --wait-timeout 300
# drive the game through exec scripts or wait
ucp logs --level error --count 20
ucp stop && ucp logs status
```

**Arrange a set piece and check it by eye.**

```bash
ucp object create Floor --primitive Plane && ucp transform scale --name Floor --uniform 4
ucp object create Crate --primitive Cube && ucp transform move --name Crate --to 2 5 0
ucp spatial ground --name Crate
ucp view isolate --name Crate --views front,right -o crate.png
ucp scene save
```

**Refactor a folder without breaking a single reference.**

```bash
ucp references find --asset Assets/Legacy/Enemy.prefab --detail summary
ucp asset bulk-move --moves '{"Assets/Legacy":"Assets/Characters/Legacy"}' --dry-run
ucp asset bulk-move --moves '{"Assets/Legacy":"Assets/Characters/Legacy"}'
ucp references check Assets/Characters
```

**UI Toolkit, edit to verified.**

```bash
ucp ui lint Assets/UI/Inventory.ucp-ui.json --fail-on-warnings
ucp ui inspect Assets/UI/Inventory.ucp-ui.json --state populated --query '#cards' --json
ucp ui check Assets/UI/Inventory.ucp-ui.json --all-states --out-dir artifacts/ui --force
```

**Find the hot path.**

```bash
ucp profiler session start --mode play --clear-first && ucp play
ucp profiler frames list --limit 3
ucp profiler hierarchy --frame <fresh> --sort self-time --limit 15 --fields name,selfMs,calls
ucp profiler session stop && ucp stop
```

**CI gate.**

```bash
ucp connect --json || exit 1
ucp run-tests --mode edit --json
ucp build set-defines "CI;RELEASE" && ucp build start --output Builds/Game.exe --json
```

## How it works

<p align="center">
  <img src="assets/readme/architecture.png" alt="UCP architecture: AI Agent → ucp CLI → Unity Editor" width="820" />
</p>

- **CLI** (`cli/`, Rust): argument parsing, project and editor discovery, launching or adopting
  Unity, dialog detection, output shaping. One static binary per platform, published to npm as
  `@mflrevan/ucp`.
- **Bridge** (`unity-package/com.ucp.bridge`, C#): an editor package that starts a WebSocket
  server on `127.0.0.1`, dispatches JSON-RPC 2.0 requests on the main thread, registers Undo for
  mutations, and attaches the editor-state summary to every response. It restarts itself across
  domain reloads; the CLI waits for it.
- **Native paths**: reference search parses Unity's YAML from disk in parallel without an editor.

Per-session tokens in a lock file gate the socket; nothing listens outside localhost.

## Compatibility

| | |
| --- | --- |
| Unity | 2021.3 LTS and newer. Release validation runs the full command surface on Unity 6000.0 through 6000.5. UI Toolkit commands need Unity 6. |
| CLI | Windows x64, macOS x64 and Apple Silicon, Linux x64 |
| Node | 16+ for the npm install only |

## Contributing, community, license

- [CONTRIBUTING.md](CONTRIBUTING.md): development setup, test matrix, release flow.
- [Discord](https://discord.gg/F4RjhdVTbz) for questions; [issues](https://github.com/mflRevan/unity-control-protocol/issues) for bugs.
- [MIT](LICENSE.md).

```text
cli/                              Rust CLI (the ucp binary)
unity-package/com.ucp.bridge/     Unity Editor bridge package
skills/                           Agent skills (ground truth) + generated catalog
plugins/ucp-surfaces/             Claude Code plugin mirror of the surface skills
docs/                             Documentation source (Markdown)
website/                          unityctl.dev, generated from docs/ and skills/
npm/                              npm distribution wrapper
scripts/                          Build, validation, and release tooling
```
