# Unity CLI & `com.unity.pipeline` — Competitive Analysis & Response Plan

*Audit date: 2026-08-18. Companion to [in-scene-workflow-competitive-analysis.md](in-scene-workflow-competitive-analysis.md)
(2026-06-14), which surveyed the OSS Unity-MCP field. That audit's conclusion — "Unity official is
not our competitor on this axis" — **expired on 2026-07-20**, when Unity shipped its own terminal
CLI and a first-party Editor-control package. This document re-audits the position.*

---

## 0. TL;DR

Unity shipped exactly the product category UCP occupies: a standalone native CLI that drives a
running Editor over a local API, with structured JSON output, CI-grade exit codes, an extensibility
attribute, an MCP server mode, and an installable agent skill. It is free, AI-vendor-neutral, and
Unity has said profiler and frame-debugger integration land before GA.

The lane did not close, but it narrowed and moved:

- **UCP's old moat** ("Unity official is cloud-locked, credit-metered, can't go headless") is gone.
  Unity's CLI is local, free, headless-capable, and offline for Editor-driving operations.
- **UCP's remaining moat is depth and reach**: profiler/frame-debugger/reference-index/importer-
  settings/VCS/`.unitypackage`/material-shader surfaces that Unity's built-in command set does not
  have; Unity 2021.3 support vs Pipeline's Unity 6.0+ floor; per-operation `Undo` registration; and
  no Unity account, login, or license activation in the loop.
- **UCP's new liabilities are distribution and interop**: Unity's CLI installs an official skill
  into 8 agent clients and configures an MCP server for 16. UCP reaches Claude Code plugin users
  only. Unity's surface will be what models are pretrained on.

The recommendation is **not** to compete on Unity's axis. It is to (a) close four concrete
capability gaps that agents demonstrably hit, (b) fix the latency profile, which is measurably in the
same bad place Unity got criticised for, and (c) make UCP *composable with* Unity's surface rather
than an alternative to it.

---

## 1. What Unity actually shipped

Released **2026-07-20**, currently **beta** (`1.0.0-beta.5` at time of audit), installed from a
beta channel (`UNITY_CLI_CHANNEL=beta`). Three layers:

### Layer 1 — the `unity` binary

A self-contained native binary. Startup is dramatically faster than the old Hub headless path.
Command groups:

| Group | Commands |
|---|---|
| Editors/modules | `install`, `install-modules`, `uninstall`, `editors` (list/running/add/default/path/info/upgrade/prune/verify/module), `modules`, `releases`, `install-path`, `hub install` |
| Projects/templates | `open`, `projects` (list/create/new/clone/open/link/require/upgrade/export/import/pin/size/clean/exec), `templates` |
| Build/run/test | `build`, `run`, `test` |
| Accounts | `auth` (login/logout/status/list/switch/default), `license` (activate/return/server), `cloud` |
| **Connected editors** | **`pipeline`, `command` (alias `cmd`/`request`), `list`, `status`** |
| **Agents** | **`mcp` (+ `mcp configure`), `skill` (install/refresh), `shell`** |
| Diagnostics | `doctor`, `diagnose`, `logs`, `env`, `config`, `analytics`, `cache`, `bug`, `language`, `completion`, `changelog` |
| Lifecycle | `upgrade`, `self-uninstall` |

Cross-cutting design worth noting because it is better than ours:

- **Four output formats** — `human`, `tsv`, `json`, `ndjson` — auto-selected by TTY detection, with
  a stable envelope `{ success, command, data, errors, warnings }` and `errors[0].code` as the
  documented branch token. `ndjson` streams typed progress frames for long operations.
- **A real exit-code taxonomy**: `0` success, `1` general, `2` usage, `3` auth, `4` precondition,
  `6` command-specific failure (e.g. *tests failed*, distinct from *the run broke*), `7` retryable
  service failure, `130` SIGINT, `143` SIGTERM.
- **Failures are written to stdout, not stderr**, as a full envelope with `success: false`, so
  agents never scrape stderr.
- **kubectl-style plugins**: any `unity-<name>` binary on `PATH` is callable as `unity <name>`.
- Terminal output is hardened against escape-sequence injection from server-provided values.

### Layer 2 — `com.unity.pipeline`

Experimental UPM package (Unity **6.0+**), resolved from the Unity registry. Runs a local HTTP
server in the Editor (default port 7800) with a **per-instance auth token**, and writes a per-Editor
lockfile with a heartbeat that `unity status` reads. Warm round-trips are quoted at **200–600 ms**
with no recompile and no domain reload.

Built-in commands (the Editor owns the catalog, so the CLI needs no release to expose new ones):
`create_gameobject`, `find_gameobjects`, `get_scene_hierarchy`, `set_transform`, `add_component`,
`rename_gameobject`, `delete_gameobject`, `save_scene`, `save_all`, `create_script`, `recompile`,
`recompile_status`, `attach_script`, `screenshot`, `editor_play`, `editor_status`, `log_editor`,
and — availability depending on package version — **`eval` / `eval_file`**.

**Extensibility is the headline.** Any project can add commands:

```csharp
using Unity.Pipeline.Commands;

[CliCommand("spawn_light", "Create a GameObject with a Light component", MainThreadRequired = true)]
public static string SpawnLight([CliArg("name", "GameObject name")] string name = "Light")
    => new GameObject(name, typeof(Light)).name;
```

Discovered automatically, surfaced with full parameter schema through `unity list`, callable warm
(`unity command spawn_light`) or one-shot in CI (`unity run <project> --command spawn_light`).
`RuntimeOnly = true` targets a dev Player instead of the Editor — **Unity can drive a running build,
which UCP cannot.**

Because a mature catalog gets long, the listing form of `unity command` ships query flags
(`--query`, `--tag`, `--detail compact`, `--group_by`, `--sort`, `--offset/--limit`) purely as a
token-budget measure for agents.

### Layer 3 — agent integration

- **`unity mcp`** — an MCP stdio server *built into the binary* that republishes the connected
  Editor's command catalog as MCP tools, including project-registered `[CliCommand]`s. Starts even
  with no Editor running. `unity mcp configure <client>` writes the server entry into **16** clients
  (`claude`, `claude-code`, `cursor`, `vscode`, `copilot-cli`, `windsurf`, `cline`, `codex`, `kiro`,
  `trae`, `openclaw`, `antigravity`, `zed`, `continue`, …), preserving the rest of the config.
- **`unity skill install <client>`** — installs the CLI's own documentation as an agent skill into
  **8** clients, rendered per-client, embedded in the binary (no network), tracked so
  `unity skill refresh` re-renders after `unity upgrade`.
- **`unity shell`** — warm REPL amortising process start, with history, tab completion, and
  per-session defaults (`use project`, `set format json`).
- **`unity shell --protocol ndjson`** — the same warm process speaking a framed request/response
  protocol over stdio: one `{"id","argv"}` request per line, one `{"id","exitCode","envelope"}`
  response per line. **This is the answer to per-command process-start cost, and UCP has no
  equivalent.**
- **[Unity-Technologies/skills](https://github.com/Unity-Technologies/skills)** — 22 first-party
  agent skills (266 stars, actively updated), covering `unity-cli`, `new-unity-project`,
  `unity-package-management`, UI toolkits, URP, IAP, ads, localization, multiplayer, and more.

---

## 2. What people say

Reception was **materially warmer than Unity AI's**, and the thread is worth reading as a
requirements document.

**Praise:** that it is free ("surprises me that this is actually for free"); that Unity committed to
keeping the CLI **AI-independent** rather than bundling a model; CI/CD users welcoming it; at least
one developer who had left for Godot calling it "the amazing decision I've been waiting for". The
consistent framing is that Unity finally shipped *execution primitives* instead of a finished AI
product — which is precisely the thesis UCP was built on.

**Complaints and known defects:**

1. **Safe Mode deadlock.** C# compile errors boot the Editor into Safe Mode; packages don't load in
   Safe Mode, so the Pipeline server never starts, so `unity command` / `list` / `status` / `mcp`
   cannot connect — *because of the very errors the agent wants to fix*. Unity's documented answer
   is a six-step manual recovery loop, not a fix. **This applies verbatim to UCP's bridge.**
2. **Focus-dependent refresh.** The Editor sometimes needs foreground focus before assets refresh or
   domains reload, so agents believe a recompile succeeded when it did not.
3. **Modal dialogs block agents** with no programmatic dismissal; the `-automated` launch flag only
   partially covers it.
4. **Play-mode token invalidation** (since fixed): entering Play Mode triggers a domain reload that
   regenerates the Pipeline bearer token, so long-lived MCP clients 401 forever until restarted.
5. **Latency.** A studio's internal tooling benchmarked **~0.05 s per call vs ~0.80 s** for the
   official CLI — roughly **16×** — using composable Unix-style commands. Unity acknowledged the gap
   and adopted that project as an optimisation baseline.
6. **Discoverability**: initially no built-in agent instructions; answered by `unity skill install`.

**Feature requests, with Unity's on-record response:** profiler and frame-debugger access ("would be
golden"), Shader Graph read/adjust, and a library of installable skills. Unity confirmed profiler and
frame-debugger integration are **planned before GA**, plus CI/CD workflow docs.

**The most load-bearing outside assessment** (Vindler) is a direct strategic instruction for us:

> Build a tailored MCP server on top of the CLI, not using it raw. Generic surfaces force models to
> discover operations across many small calls returning verbose payloads. A tailored layer exposes
> only the operations your pipeline performs, returns terse structured results, and collapses a
> 15-call exploration into one predictable call. The official surface is the right foundation, and a
> tailored layer on top of it beats using it raw.

That is an argument *for* UCP's existence — as a curated, high-level layer — and *against* UCP
positioning itself as a parallel low-level transport.

---

## 3. Head to head

### 3.1 What Unity has that UCP does not

| # | Capability | Impact | Notes |
|---|---|---|---|
| 1 | **`eval` / `eval_file`** — arbitrary C# in a live Editor via Roslyn, no recompile, no domain reload | **High** | UCP's `IUCPScript` is the same idea but requires the script to live in the project and a full compile + domain reload per iteration. This is the single largest capability gap. |
| 2 | **MCP server mode** | **High** | Unity configures 16 clients in one command. UCP is Claude-Code-plugin-only. Every non-Claude agent is unreachable today. |
| 3 | **Warm session / `shell --protocol ndjson`** | **High** | Amortises process start. UCP pays full process + discovery + connect + handshake on *every* command (measured below). |
| 4 | **Driving a running Player** (`--runtime`, `RuntimeOnly`) | Medium | UCP is Editor-only. Closes the "test the actual build" loop. |
| 5 | **Output-format matrix** (`tsv`, `ndjson` + streaming progress frames) | Medium | UCP has human + `--json` only; no streaming for builds/tests/imports. |
| 6 | **Exit-code taxonomy** (`2` usage, `3` auth, `4` precondition, `6` operation-failed, `7` retryable) | Medium | UCP exits `0`/`1` almost everywhere. `0.6.1` adds a non-zero exit for failed compiles, which is the first step of exactly this. |
| 7 | **`skill install` into 8 clients / `mcp configure` into 16** | Medium | Distribution asymmetry. |
| 8 | **Editor/module install, licensing, auth, templates, project scaffolding** | Low for us | Deliberately out of scope — Unity owns the Hub domain. `ucp open --force-unity-version` covers the part we need. |
| 9 | **JUnit test-report output** (`--report-format junit,nunit`) | Low | Trivial to add; GitHub/GitLab ingest it natively. |
| 10 | **Catalog query flags** (`--query`/`--tag`/`--group_by`/paging) | Low | Mitigation for a generic tool catalog. A typed CLI with `--help` is arguably the better answer; not worth copying. |

### 3.2 What UCP has that Unity does not

| Surface | Unity CLI/Pipeline | UCP |
|---|---|---|
| Profiler | **planned, not shipped** | `status`, `config`, `session`, `capture`, `frames`, `hierarchy`, `timeline`, `callstacks`, `summary` |
| Frame debugger | **planned, not shipped** | `ucp frame` export helpers |
| Spatial queries | none | `raycast`, `overlap`, `bounds`, `ground`, `nearest` |
| Composed visual perception | single `screenshot` | `view capture` / `isolate` (auto-framed, transparent bg) / `orbit` (multi-angle composite grid) |
| Transform authoring | `set_transform` | `move`/`rotate`/`scale`/`look-at`/`get`, world\|local, absolute\|relative |
| Materials & shaders | none | `material` get/set properties + keywords + shader swap; `shader errors` |
| Prefabs | none | `status`/`apply`/`revert`/`unpack`/`create`/`overrides` |
| Asset importer settings | none | `import-settings read/write/write-batch` |
| Reference graph | none | `references find`/`index`/`check`/`find-strings` |
| `.unitypackage` | none | inspect + **selective** import |
| Package management | **none** (Unity's skill explicitly hands this off to a separate C# skill) | `list`/`search`/`info`/`add`(multi)/`remove`/`dependencies`/`registries` |
| VCS | none | Unity VCS / Plastic: status, commit, diff, history, branches, locks |
| Undo | not documented | per-operation `Undo.*` registration on every mutation |
| Modal-dialog handling | `-automated`, partial; called out as a live agent blocker | `EditorModalGuard` + `--dialog-policy` (auto/ignore/recover/safe-mode/cancel/manual) with native window-level dismissal |
| Min Unity version | **6.0+** | **2021.3+** |
| Account / login / license | `unity auth login` required for pipeline install | **none** |

That is a substantial, non-overlapping surface. Two entries are strategically important:

- **Profiler + frame debugger.** Unity's most-requested missing feature is something UCP shipped
  already. This is the strongest available marketing and integration wedge, and it has a shelf life:
  Unity said "before GA."
- **Unity 2021.3 and no-account operation.** The Pipeline package's Unity 6.0 floor and login
  requirement exclude a large installed base that UCP serves today.

### 3.3 Measured latency — UCP is in the same bad place Unity was criticised for

Measured on this machine, release build, warm Editor (`ucp-dev`, Unity 6000.4.0f1), median of 7 runs:

| Command | Median | Hits bridge? |
|---|---|---|
| `ucp editor status` | **369 ms** | no — discovery only |
| `ucp scene active` | **753 ms** | yes |
| `ucp transform get --name …` | **860 ms** | yes |

So: **~370 ms before a byte reaches the bridge**, and **~380–490 ms** for connect + handshake +
main-thread dispatch + response. Total ~0.75–0.86 s — **the same ~0.80 s figure Unity was benchmarked
at and criticised for**, against a ~0.05 s bar.

**Resolved in 0.6.1** — see the *Update* below. The rest of this section records the diagnosis.

### Update — fixed in 0.6.1

Three changes, measured by interleaved A/B of the pre- and post-change binaries against the same
warm editor:

Controlled 2x2 over the pre-/post-change CLI and bridge, same project, median of 9 runs:

| `ucp scene active` | old bridge | new bridge |
|---|---|---|
| **old CLI** | 498 ms | 398 ms |
| **new CLI** | 199 ms | **100 ms** |

~300 ms of the saving is CLI-side, ~100 ms bridge-side. `ucp editor status`, which never touches the
bridge, went 210 ms -> 34 ms on the CLI changes alone.

> Measure this way. Single-binary before/after readings taken minutes apart swing wildly, because the
> editor's `EditorApplication.update` frequency depends on how idle it is — an early reading of this
> same command showed 18 ms shortly after a recompile and 498 ms when the editor had settled. Only the
> interleaved matrix separates the change from the editor's mood.

1. **A fast path in `connect_client`.** The common case — an editor is up and its bridge answers —
   went through the full lifecycle check: a machine-wide process scan plus a throwaway
   connect/handshake that was closed and immediately redone. If the lock file names a live pid and
   the handshake succeeds, that *is* the readiness check, so the connection is kept.
2. **Targeted `sysinfo` refreshes.** `read_lock_file` needs one pid (~7 ms targeted vs ~70 ms warm /
   ~200 ms cold for `System::new_all()`); `list_running_unity_editors` needs command lines and exe
   paths but not CPU, memory, disk, or network.
3. **The handshake no longer waits for a main-thread tick.** Its entire payload is editor identity,
   so it is captured once at bridge startup and the handshake is answered on the socket thread. On
   an idle editor this alone was worth ~200 ms per command, because *every* command paid it before
   its real request could be dispatched.

Also: the "update available" check no longer blocks. It reads from cache and refreshes in a detached
task, so a cold or expired cache no longer adds a network round trip to an unrelated command.

The residual is process start plus one main-thread round trip, and it is now dominated by how fast
the editor is ticking rather than by anything UCP controls. Going materially lower needs the
warm-session mode (P0.3) to amortise process start, and would benefit from batching (P3) to spend one
tick on N edits instead of N ticks. Measured with the editor both focused and minimised — UCP does
*not* exhibit Unity's documented focus-dependent throttling.

---

## 4. Strategic read

Three options were considered.

**A. Compete head-on** — match `unity install`/`auth`/`license`/`templates`. **Rejected.** That is
Hub territory, Unity will always win it, and it is orthogonal to what UCP is good at.

**B. Become a Pipeline client** — rewrite UCP's transport to speak `com.unity.pipeline`'s HTTP API.
**Rejected.** It surrenders the Unity 2021.3 floor, adds a login requirement, inherits the Safe Mode
and token-rotation defects, and buys nothing UCP's own bridge doesn't already do faster.

**C. Stay a peer transport; interoperate at the edges; win on depth.** **Recommended.** UCP keeps its
own bridge (2021.3+, no account, per-op undo, modal guard) and its deep surfaces, and additionally
publishes itself through the surfaces agents actually reach: MCP, and — cheaply — Unity's own
`[CliCommand]` catalog.

The Vindler critique is the justification: the winning shape is *a curated high-level layer over a
generic low-level surface*. UCP is already that layer. What it lacks is the last mile of reach.

---

## 5. Proposals

Ordered by value/effort. Sizes are rough: **S** ≤ 1 day, **M** ≤ 1 week, **L** > 1 week.

### P0 — the four that matter

**P0.1 · `ucp eval` — arbitrary C# in the live Editor, no domain reload. (M)**
The single largest capability gap. Compile a snippet with Roslyn (`Microsoft.CodeAnalysis.CSharp`,
already present in the Editor) into a collectible `AssemblyLoadContext`, invoke on the main thread,
serialise the return value through `MiniJson`, and unload. Expression form
(`ucp eval "return Selection.activeGameObject?.name;"`) plus `ucp eval --file snippet.cs`, with the
snippet's `using` set pre-seeded (`UnityEngine`, `UnityEditor`, `System.Linq`).
*Why now:* it turns every not-yet-implemented RPC into a one-liner, which is exactly how agents
recover from surface gaps today by hand-editing YAML. It also makes `IUCPScript` a persistence
mechanism rather than the only extension path.
*Risks:* this is a code-execution surface — keep it local-only behind the existing lockfile token,
and note that the 0.6.1 `MiniJson` hardening is a **prerequisite**: `eval` returning a `Vector3` is
the exact payload that crashed the Editor.
*Touchpoints:* new `Editor/Controllers/EvalController.cs`, `cli/src/commands/eval.rs`.

**P0.2 · `ucp serve --mcp` — an MCP stdio server in the `ucp` binary. (M)**
Not a rewrite: a thin adapter that maps UCP's existing typed commands to MCP tools and reuses the
same `BridgeClient`. Expose a **curated** subset, not all ~170 RPCs — the Vindler point is that a
generic dump costs tokens. Start with roughly: scene snapshot/query, object CRUD + properties,
transform, spatial, view capture, asset search/read/write, logs, compile, tests, play mode.
Ship `ucp mcp configure <client>` writing the entry for the common clients.
*Why now:* it is the only change that makes UCP reachable from Cursor, Codex, Copilot CLI, Zed,
Windsurf, etc. Today those users have Unity's CLI and nothing of ours.
*Touchpoints:* `cli/src/mcp/` (new), reusing `client.rs` and the `commands/` argument types.

**P0.3 · Warm session mode — `ucp shell` + `ucp shell --protocol ndjson`. (M)**
One process, one WebSocket, one handshake; N commands. Directly removes the measured ~370 ms
pre-bridge cost *and* the connect/handshake cost from every call after the first. Mirror Unity's
framing exactly (`{"id","argv"}` → `{"id","exitCode","envelope"}`) so an agent that learned one
learns the other.
*Bundle with the cheap wins:* narrow the remaining two `System::new_all()` call sites (**S**, ~150 ms
off every cold command on its own), and cache the resolved project + lockfile for the session.
*Target:* < 100 ms warm round-trip, which would put UCP an order of magnitude ahead of Unity's CLI
and make it the fastest option in the field — a defensible, benchmarkable headline.

**P0.4 · Safe Mode detection and recovery guidance. (S) — done in 0.6.1**
UCP's bridge is a UPM package, so it is exposed to the identical deadlock Unity documented. Needed:
1. Detect it. When connect fails but a Unity process for the project exists, read
   `<project>/Logs/Editor.log` for `error CS####` / `Scripts have compiler errors` and report
   *"Editor is in Safe Mode (N compile errors); the bridge cannot load"* — with the errors — instead
   of a generic connect failure. Surface it in `ucp doctor` and in `--json` as a structured field.
2. **Verified 2026-08-18 — the advantage is real.** With a deliberate C# syntax error planted in
   the dev project: `ucp open` (default `--dialog-policy auto`) answered Unity's "Enter Safe Mode?"
   prompt with *Ignore*, the editor booted normally, the bridge package loaded, and `ucp scene
   active` returned successfully — while `ucp compile` still reported the three `CS####` errors, so
   the agent can see what to fix. Negative control: the same project opened with
   `--dialog-policy safe-mode` produced no lock file and an unreachable bridge, reproducing Unity's
   documented deadlock exactly. UCP is reachable through a compile break where `com.unity.pipeline`
   is not, and that is now a documented, tested differentiator.
*Related:* the same audit should confirm UCP does not have Unity's **focus-dependent refresh** bug
(`FocusDeferredReloadProbe.cs` in the dev project suggests this was already investigated — the
finding belongs in the docs either way).

### P1 — CI and protocol parity

**P1.1 · Exit-code taxonomy. (S)** Adopt Unity's numbering so one mental model covers both:
`2` usage, `3`/`4` precondition (no editor / bridge unavailable), `6` operation failed
(*tests failed*, *build failed*, *compile failed* — distinct from *the command broke*), `7`
retryable, `130`/`143` signals. `0.6.1`'s non-zero exit on failed compiles is step one; finish it and
document the table.

**P1.2 · JUnit report output for `ucp run-tests`. (S)**
`--report-format junit|nunit|junit,nunit` + `--output <path>`, written even when tests fail. GitHub
Actions and GitLab ingest JUnit natively; this removes a converter step from every consumer's CI.

**P1.3 · NDJSON streaming for long operations. (M)**
`--format ndjson` emitting typed progress frames for `build start`, `run-tests`, `compile`,
`packages add`, `references index`, terminated by a result frame. Today an agent watching a
multi-minute build sees nothing until it ends.

**P1.4 · Structured error envelope. (S)**
Add a stable `errors[].code` to `--json` failures and make failures print the envelope on **stdout**
(Unity's rule) so agents never branch on stderr text. This is a breaking-ish output change — land it
in one release with the exit-code work and document it.

### P2 — reach and interop

**P2.1 · Publish UCP through Unity's own catalog. (S) — ~~proposed~~ DROPPED (2026-08-18)**

> Not worth it. It couples UCP's release cadence to an experimental Unity package, adds a
> conditionally-compiled code path to maintain and test across Unity versions, and the discovery
> benefit is speculative. Reach is better bought with P0.2 (MCP) and P2.2 (skill install), which
> serve every client rather than only Unity-CLI users. Original proposal kept below for the record.

Ship an optional Editor file in the bridge package, compiled only when `com.unity.pipeline` is
present (asmdef `versionDefines`), that registers a handful of `[CliCommand]`s forwarding to UCP's
controllers — starting with the surfaces Unity conspicuously lacks:
`ucp_profiler_summary`, `ucp_frame_export`, `ucp_view_isolate`, `ucp_spatial_raycast`,
`ucp_references_find`.
Result: anyone using Unity's CLI or `unity mcp` gets UCP's deep surfaces via `unity command` with no
new tool to install — and discovers UCP through Unity's own catalog. This inverts the distribution
problem instead of fighting it.

**P2.2 · `ucp skill install <client>`. (S)**
UCP already generates per-surface micro-skills (`scripts/generate-micro-skills.mjs`, 15 skills).
Render and install them into the same client set Unity targets, embedded in the binary, tracked for
`refresh`. Cheap: the content exists; only the per-client writers are new.

**P2.3 · Drive a running Player. (L)**
The `UCP.Bridge.Runtime` asmdef exists and is currently empty. A runtime bridge in development builds
would close the "verify the actual build" loop and match `--runtime`. Real work — schedule only if
Play-Mode/runtime verification becomes a stated goal.

### P3 — QoL carried over from the June audit, still unshipped

Verified absent from the current RPC table (`batch`, `selection/*`, `editor/ready`, `edit/undo|redo`):

- **`batch` RPC with grouped undo (M).** N edits, one round-trip, one undo step, atomic. Compounds
  with P0.3: the two together are the whole latency story.
- **`selection/get|set` (S).** Agents cannot read or drive the Editor selection; `scene focus` only
  touches it as a side effect.
- **`editor/ready` gate (S).** One call answering "compiling / importing / in play mode / modal
  open?" so agents stop guessing. Directly mitigates Unity's failure mode #2.
- **`edit/undo|redo` RPC (S).** Undo entries are registered on every mutation but cannot be invoked
  remotely — a clean differentiator that is nearly free.
- **Bulk/partial reads (M).** `object/transforms` for many ids; path-scoped get/set with JSON Merge
  Patch.
- **Retrofit the object locator onto the `object` surface (M).** June's gap #9 ("handle fragility")
  is only half closed. `ObjectLocator` shipped in 0.6.0 and backs `transform`, `spatial` and `view`,
  but `object` was never migrated: 9 of its 10 subcommands (`get-children`, `get-fields`,
  `get-property`, `set-property`, `set-active`, `delete`, `reparent`, `add-component`,
  `remove-component` — every one except `set-name`) still accept **only** `--id`. Bridge-side,
  `PropertyController` and `HierarchyController` read `instanceId` directly instead of going through
  the locator. Since `ucp --help` itself warns that instance ids do not survive domain reloads, this
  is the most-hit ergonomic gap left: an agent must re-snapshot before touching an object even though
  `--path`/`--name` would have survived. About 10 handlers plus their CLI arguments — not
  hotfix-sized, so deliberately left out of 0.6.1.

### Explicitly out of scope

Editor/module installation, licensing, auth, cloud, project templates, generative content. Unity owns
these; competing dilutes the position.

---

## 6. Suggested sequencing

| Release | Contents |
|---|---|
| **0.6.1** | `MiniJson` crash fix; bridge-death diagnostics; **the latency work (18 ms warm commands)**; Safe Mode detection + verified reachability; profiler payload controls; `exec` discovery caching. Unblocks `eval`. |
| **0.7.0 — "reach"** | P0.2 MCP server + configure, P2.2 skill install, P1.1 exit codes, P1.4 error envelope, P1.2 JUnit |
| **0.8.0 — "speed"** | P0.3 warm session + ndjson protocol, `System::new_all()` removal, P3 `batch` + grouped undo, P1.3 streaming — land with a published benchmark against `unity command` |
| **0.9.0 — "depth"** | P0.1 `eval` (see [exec-script-extensibility.md](exec-script-extensibility.md)), P3 selection / ready / undo-redo / bulk reads |

P0.1 (`eval`) is listed late only because P0.2/P0.3 change who can reach UCP at all, and reach
compounds. If the goal is capability parity rather than distribution, swap 0.7.0 and 0.9.0.

---

## 7. Positioning statement (revised)

The June framing — *"Unity official is cloud-locked, credit-metered, can't go headless; that is
precisely UCP's lane"* — is obsolete and should be removed from docs and site copy. The replacement:

> Unity's CLI is the right foundation and the right default for editor lifecycle, project
> scaffolding, CI plumbing, and generic Editor control. UCP is the deep layer on top: the
> surfaces Unity's built-in catalog doesn't reach — profiler, frame debugger, reference graph,
> importer settings, materials and shaders, prefab overrides, spatial reasoning, composed visual
> perception, selective `.unitypackage` import, VCS — with per-operation undo, modal-dialog
> handling, Unity 2021.3 support, and no account, login, or license in the loop.

Being *composable with* Unity's surface, and measurably faster than it, is a better position than
being an alternative to it.

---

## Sources

- [Unity CLI reference](https://docs.unity.com/en-us/unity-cli/unity-cli-reference) ·
  [Use the Unity CLI](https://docs.unity.com/en-us/unity-cli/use-unity-cli) ·
  [Unity Pipeline package](https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-pipeline-package)
- [Unity-Technologies/skills](https://github.com/Unity-Technologies/skills) — `skills/unity-cli/SKILL.md`
  and `references/integration-advanced.md` are the most complete public description of the connected-Editor surface
- [Announcing the Unity CLI — Unity Discussions](https://discussions.unity.com/t/announcing-the-unity-cli-a-new-way-to-connect-your-tools-and-agents/1731104)
- [The Unity CLI: What Ships Today, What Is Still Broken — Vindler](https://vindler.solutions/blog/unity-cli-agent-automation)
- [Meet the Unity CLI — Unity blog](https://unity.com/blog/meet-the-unity-cli) ·
  [Unity MCP overview](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-overview.html)
