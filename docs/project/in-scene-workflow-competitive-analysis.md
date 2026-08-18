# In-Scene Workflow — Competitive Analysis & Hardening Roadmap

> **Superseded in part (2026-08-18).** Unity shipped its own terminal CLI and the
> `com.unity.pipeline` Editor-control package on 2026-07-20, which invalidates §2's conclusion that
> "Unity official is not our competitor on this axis" and §5's positioning takeaway. The §1 audit,
> §3 primitives, and the P1/P2 roadmap items remain accurate and partly unshipped. See
> [unity-cli-competitive-analysis.md](unity-cli-competitive-analysis.md) for the current position.

*Audit date: 2026-06-14. Scope: harden UCP's in-scene / GameObject / 3D-spatial / visual-iteration
surface for the next release, informed by a deep sweep of the three leading Unity-AI tools.*

UCP's thesis is **CLI-paradigm, not MCP**: a Rust `ucp` binary → JSON-RPC 2.0 over WebSocket → C#
Editor bridge, deterministic, headless/`-batchmode`-capable, offline, $0. This document keeps that
framing and only borrows *capabilities*, never the MCP transport.

---

## 1. Where UCP stands today (audit)

The bridge surface is **headless-edit-and-query oriented**, not **spatial-authoring oriented**. It is
broad and clean for CRUD + reflection property I/O, but thin everywhere "3D" actually lives.

**Have, and clean:**
- GameObject CRUD, reparent, instantiate (`HierarchyController`); component add/remove with
  AppDomain-wide type resolution.
- Reflection property I/O via `SerializedObject` (`PropertyController`: `object/get-fields`,
  `get-property`, `set-property`) — respects `[SerializeField]`/Inspector visibility, covers all
  scalar + Vector/Color/Quaternion/Bounds types, object-ref resolution by id/path/guid.
- Scene-graph dump (`SnapshotController.snapshot`, `scene/query`, `object/get-children`,
  `objects/transform`).
- Prefabs (status/apply/revert/unpack/create/overrides, all `InteractionMode.AutomatedAction`),
  materials/shaders, global render settings, play mode, **screenshots (game|scene → base64 PNG)**.
- **Per-op `Undo.*` registration on every mutation**, modal-safe save guard (`EditorModalGuard`),
  `SceneChangeTracker` dirty digest. This safety story is genuinely ahead of the OSS field.

**Gaps that matter for in-scene game-dev (ranked):**
1. **No spatial query API at all** — no raycast, bounds, overlap, nearest, frustum/visibility. An
   agent cannot answer any geometric question. *Single biggest gap.*
2. **Transforms are second-class** — no move/rotate/scale/look-at/translate; every nudge is a raw
   `m_LocalPosition` / quaternion `[x,y,z,w]` write through `set-property`. No align/snap/distribute,
   no drop-to-floor, no look-at.
3. **No batch / transactional edits** — N edits = N round-trips, each independently dirtying the
   scene; no grouped undo, no atomicity.
4. **No selection model** — can't get/set the Editor selection (only `scene/focus` touches it, as a
   side effect).
5. **No first-class camera control** — game cameras edited only as components; the SceneView camera
   can only be *framed on one object* (`scene/focus`), never freely posed (orbit/pan/zoom/set-pose).
6. **Screenshot is one-shot and uncomposed** — fixed to `Camera.main` / last SceneView; no
   per-camera selection, no isolated-object render, no multi-angle, no transparent bg.
7. **No primitive/builder helpers** — no "create Cube/Light/Camera"; must create + add-component +
   configure.
8. **Read granularity mismatch** — `snapshot` gives structure but no values; reading state needs
   per-object follow-ups. No bulk transform dump.
9. **Handle fragility** — `instanceId`-only addressing, invalidated by every domain reload; no stable
   path/GUID addressing, agents must constantly re-snapshot.
10. **No undo/redo RPC** — undo entries are registered but cannot be *invoked* remotely.

**Extensibility:** trivial. Controllers are `static class` + `Register(router)` in `BridgeServer.cs`;
adding a method is one `router.Register("foo/bar", Handle)` + a handler returning
`Dictionary<string,object>`. A new `SpatialController` / `TransformController` / `ViewController`
drops in without touching dispatch.

---

## 2. The competitive field

| | **IvanMurzak/Unity-MCP** | **CoplayDev/unity-mcp** | **Unity AI (official)** |
|---|---|---|---|
| Stars / license | ~3.2k, Apache-2.0 | ~10.6k, MIT | first-party, proprietary |
| Transport | MCP stdio/HTTP → **SignalR/WS** → Editor (+ **runtime** bridge) | MCP stdio/HTTP → framed-TCP/WS → Editor; Python server | MCP stdio → **relay sidecar** → named-pipe/socket → Editor |
| Tool model | reflection-registered `[AiTool]`, **ReflectorNet** | reflection `[McpForUnityTool]`, Python schema + C# handler (declared twice) | curated `[McpTool]` set via `McpToolRegistry` |
| Min Unity | pre-6.5 + 6000.5 (forked source) | **2021.3** | 6000.0.76f1 / 6.3+ |
| Headless / CI | no | no | **no** (Editor+cloud bound) |
| Offline | Editor-local | Editor-local (Python dep) | **no, cloud-mandatory, credit-metered** |
| Undo | **absent** (SetDirty only) | yes (`RegisterCreatedObjectUndo`/`RecordObject`) | checkpoint/rollback per action |

**Unity official is not our competitor on this axis.** It is a cloud-bound, credit-metered
($10/1,000 credits, exhaustible in a day, no rollover) *creative copilot* with strong generative
content (text/image→3D via Hunyuan 3D 3.0, textures, sprites) and an official MCP server. By its own
docs it **cannot run headless, cannot do CI, cannot see Game View in Play Mode, has no offline mode.**
That is precisely UCP's lane. We do **not** try to out-generate them; we own automation
infrastructure: deterministic, scriptable, headless, offline, $0, full-API breadth.

The two OSS tools are the real benchmark for *in-scene capability*, and both have converged on a clear
set of primitives worth copying.

---

## 3. What to copy — spatial/visual/agentic primitives (cross-tool consensus)

### A. Composed visual perception — *the highest-leverage borrow*
Both OSS tools invest heavily here; UCP's single uncomposed screenshot is the weakest part of our
visual loop.
- **IvanMurzak `screenshot-isolated`**: renders a *single GameObject* off-screen with a temp
  camera+RT, **auto-frames from renderer bounds** (`distance = radius·padding / sin(fov/2)`),
  **layer-31 isolation** (cull everything else), **6 ortho views (Front/Back/L/R/Top/Bottom) + a
  `Composite` 2×2 grid** in one image, configurable **transparent background** + JSON **light rig**.
  Purpose-built so an LLM can perceive 3D shape from one PNG.
- **CoplayDev `manage_camera`**: `screenshot` + `screenshot_multiview` with **`batch: 'surround'`
  (6 angles) / `'orbit'` (configurable grid)**, scene-view *or* game-view, **target framing**
  (name/path/id/`[x,y,z]`), positioned capture (`view_position`/`view_rotation`), supersampling,
  inline base64, **`max_resolution` longest-edge cap (default 640)** for token budget.

**→ UCP:** extend `ScreenshotController` into a real `ViewController`. Add `view/capture` (per-camera
or scene/game, target framing, max-edge cap), `view/isolate` (bounds-fit single-object render,
ortho set + composite grid, transparent bg), `view/orbit` (N-angle surround/orbit batch). This single
feature transforms the visual-iteration cycle.

### B. Spatial query controller — *leapfrog opportunity*
UCP has none; **IvanMurzak also has none** (no raycast, no scene-pick); only CoplayDev ships it
(`manage_physics`: `raycast`/`raycast_all`/`linecast`/`shapecast`/`overlap`). We can ship a *better*
one and pass both OSS tools.
**→ UCP `SpatialController`:** `physics/raycast` (origin/dir/distance/layerMask/triggerInteraction →
hit point/normal/instanceId/distance), `physics/overlap` (sphere|box|capsule), `object/bounds`
(world AABB + center + extents, the data `CalculateFocusBounds` already computes internally but never
returns), `spatial/nearest`, `spatial/ground` (drop-to-surface raycast). Critically, **return the hit
shape** — CoplayDev's docstring omits it; we document it.

### C. Polymorphic object locator — *fixes our fragile addressing*
Both OSS tools converged on a **union locator with priority fallback**, reused for scene objects
*and* assets:
- IvanMurzak `GameObjectRef`: `instanceID` (P1) → `path` `"character/hand/finger"` (P2) → `name` (P3)
  → inherits `assetPath`/`assetGuid`. `ComponentRef`: `instanceID` → `index` (`0`≈Transform) →
  `typeName`.
- CoplayDev: `target` = id|name|path with `search_method ∈ by_id|by_name|by_path|by_tag|by_layer|
  by_component`, auto-inferred.

**→ UCP:** introduce a shared `ObjectLocator` resolved in one place (extend
`UnityObjectCompat.ResolveByInstanceId`), accepting `{instanceId | path | name}` with documented
priority. Removes the constant re-snapshot tax and survives domain reloads via path. Document the
caveat both tools note: name/path are non-deterministic under duplicates → id stays canonical.

### D. Transform-first commands
CoplayDev ships `move_relative` (direction/distance/offset) and `look_at` (target + up) as
first-class; IvanMurzak sets transform inline at create to cut round-trips.
**→ UCP `TransformController`:** `object/move` (world|local, absolute|relative), `object/rotate`
(**euler-friendly**, world|local), `object/scale`, `object/look-at`, plus `object/align` /
`object/snap-to-grid` / `object/ground`. Euler in/out (convert to quaternion internally) — eliminates
the error-prone raw `[x,y,z,w]` writes.

### E. Batch execution + grouped undo
CoplayDev `batch_execute`: *"reduces latency and token costs 10–100×"*, `parallel` read-only
execution, `fail_fast`, default 25 / cap 100. IvanMurzak batches list-modify with parallel arrays.
**→ UCP:** `batch` RPC taking `[{method, params}]`, executed in one main-thread pump, wrapped in a
single `Undo.CollapseUndoOperations` group → **atomic, one-undo-step** multi-object authoring. This
also plays to our determinism story (replayable scripts).

### F. Editor-readiness gate (cheap, high-value robustness)
CoplayDev `mcpforunity://editor/state` exposes `ready_for_tools` + `blocking_reasons` (is_compiling,
domain-reload pending, play_mode) so agents don't act mid-recompile. UCP already has lifecycle waiting
in the CLI; **formalize it as an RPC** (`editor/ready` → `{ready, reasons[]}`) so any client can
pre-flight. Complements our existing modal-safe guard nicely.

### G. Selection model & reads-as-cheap-subcommands
- `selection/get` + `selection/set` (both OSS tools have it; UCP has neither).
- **Token-frugal reads**: both default `include*` flags off and offer **path-scoped partial reads**
  (`field/nested/[i]/[key]`). IvanMurzak's `Reflector.TryReadAt`/`View` and JSON Merge Patch
  (RFC 7396 + array/dict extensions) let an agent touch a deep field without serializing the whole
  object. **→ UCP:** add `object/transforms` (bulk transform dump for N ids in one call) and a
  path-scoped form of `get-fields`/`set-property` to cut round-trips on deep components.

### H. Undo/redo RPC — *a clean differentiator*
IvanMurzak has **no undo at all** (a real weakness). UCP already registers undo per-op but can't
invoke it. Expose `edit/undo` + `edit/redo`. Combined with E's grouped undo, this is a correctness
story neither OSS tool fully tells.

### I. Misc borrows worth a line
- **Hash-of-project-path → deterministic port** (IvanMurzak maps SHA256(path) into 20000–29999) — a
  cleaner multi-instance answer than a fixed port if we ever support concurrent projects.
- **Primitive builder** convenience (`gameobject-create` `primitiveType`): one-call Cube/Sphere/Plane/
  Light/Camera.
- **Dual-purpose tool attribute** (IvanMurzak emits both MCP schema *and* an on-disk `SKILL.md` from
  one annotation) — analogous idea: generate `ucp` help + the skill doc from one command-metadata
  source.

---

## 4. Prioritized roadmap for the next release

Each item is a new/extended **controller + CLI subcommand pair**, reusing `EditorModalGuard`,
`Undo.*`, `SceneChangeTracker`, and `InteractionMode.AutomatedAction`.

**P0 — the visual + spatial core (the reason this release exists):**
- **Composed view** (§A): `ViewController` → `view/capture`, `view/isolate`, `view/orbit`.
- **Spatial queries** (§B): `SpatialController` → `physics/raycast`, `physics/overlap`,
  `object/bounds` (surface the AABB we already compute), `spatial/ground`.
- **Transform-first** (§D): `TransformController` → `object/move|rotate|scale|look-at` (euler).

**P1 — agentic ergonomics & robustness:**
- **Batch + grouped undo** (§E): `batch` RPC, atomic one-undo-step.
- **Polymorphic locator** (§C): shared `{instanceId|path|name}` resolution.
- **Selection** (§G): `selection/get|set`.
- **Readiness gate** (§F): `editor/ready`.
- **Undo/redo** (§H): `edit/undo|redo`.

**P2 — depth & polish:**
- Bulk/partial reads (§G): `object/transforms`, path-scoped get/set + JSON Merge Patch (§J in
  research notes).
- Primitive builder convenience; first-class light/camera helpers.
- Multi-instance hash-port (§I) — only if concurrent-project support is on the table.

**Explicitly out of scope:** generative content (textures/3D/sprites). Unity owns it via cloud model
backends we won't replicate; competing there dilutes the automation-infrastructure position.

---

## 5. Positioning takeaway

The OSS tools tell us *what in-scene primitives an agent needs*; Unity official tells us *which lane
to avoid* (conversational/creative/cloud) and *which to own* (deterministic/headless/offline/$0/
full-API). The next release should make UCP the tool where an agent can **see** a scene
(composed/isolated/multi-angle capture), **reason** about it (raycast/bounds/overlap), and **author**
it (euler transforms, batched + atomic, undoable) — entirely offline, scriptable in CI, at zero
marginal cost. That combination exists nowhere else today: IvanMurzak lacks raycast *and* undo,
CoplayDev needs a Python runtime and an MCP client, and Unity official is cloud-locked and can't go
headless.
