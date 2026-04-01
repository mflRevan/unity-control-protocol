# PROJECT.md - Unity Control Protocol Architecture And Principles

This document captures the current architectural shape of the repository and the general engineering principles that should guide future development.

It is intentionally not a low-level rulebook. The goal is to preserve clarity, scalability, and maintainability as the CLI and Unity bridge continue to grow.

It is grounded in the current implementation in `cli/` and `unity-package/com.ucp.bridge/`.

## Scope

UCP is built from four main layers:

- a Rust CLI in `cli/`
- a Unity Editor bridge package in `unity-package/com.ucp.bridge/`
- an npm distribution wrapper in `npm/`
- documentation and website content in `docs/`, `website/`, and `skills/`

The core product is the CLI plus the Unity bridge. The other layers exist to package, distribute, and explain that system.

## Current Architecture

At a high level, the system works like this:

1. The CLI parses user intent and global runtime options.
2. Command modules resolve project context and bridge readiness.
3. The Rust client communicates with the bridge over localhost WebSocket using JSON-RPC.
4. The Unity bridge routes RPC methods into editor-side controllers.
5. Controllers translate protocol requests into Unity Editor operations and structured responses.
6. The CLI renders the result either for humans or for machine consumption.

That flow should remain easy to follow. Future work should preserve a clear separation between:

- command orchestration
- transport and lifecycle management
- protocol definition
- Unity-side execution
- presentation and documentation

## Unity Interaction Lifecycle Policy

Unity automation is only polished when a command leaves the editor genuinely ready for the next step. In practice, many Unity APIs return before the editor has finished import, metadata generation, serialization, compilation, or domain reload work.

UCP therefore treats Unity-facing commands as lifecycle categories, not just RPC calls:

- **Read-only inspection**: queries such as status, reads, search, snapshots, and logs. These do not wait after the call.
- **Editor-settle mutations**: scene/object/material/prefab/settings/file/asset operations that can trigger asset refresh, metadata writes, serialization, or other editor background work. These must wait for the editor to settle before reporting success.
- **Restart-then-settle mutations**: compile-heavy or package-heavy operations that can restart the bridge or trigger a domain reload, such as package changes, explicit recompilation, scripting-define changes, and build-target switches. These must survive bridge restart and then wait for editor settle.
- **Custom confirmation flows**: commands whose completion is defined by a domain-specific signal rather than generic editor settle, such as play-mode entry confirmation, test-run notifications, or build completion reports.

This policy is a core architectural rule, not a UX nicety. A mutating command should not claim success while Unity still has deferred catch-up work that will only surface later when the editor regains focus.

The same policy now also distinguishes between scene-editing mutations and scene-disruptive mutations:

- **Scene-editing mutations**: object, prefab, and scene-lighting operations can intentionally leave the active scene dirty. These surfaces should expose an explicit `--save` option rather than silently persisting scene changes.
- **Scene-disruptive mutations**: commands that can close the editor, switch scenes, enter play mode, trigger recompilation/domain reloads, or kick off package/build-target/define refresh flows must preflight the active scene first. If the active scene is dirty, they should fail with a concise scene-change summary instead of letting Unity show its native save dialog.

That separation is a core operability contract for autonomous Unity work: edits may stay unsaved by default, but disruptive transitions must never surprise the user with an unmanaged modal.

## Repository Shape

The current repository has a clear top-level split:

- `cli/` contains the Rust command-line product.
- `unity-package/com.ucp.bridge/` contains the Unity bridge package.
- `npm/` packages the released CLI and bridge payload for JavaScript users.
- `docs/`, `website/`, and `skills/` describe and expose the system externally.
- `scripts/` and `version.json` support release and metadata consistency.

This separation is useful and should be preserved. Code should generally evolve deeper within the right layer rather than by adding new cross-cutting entrypoints or parallel structures.

## Source Of Truth

Some parts of the repository are foundational and should stay aligned:

- `version.json` is the source of truth for release and protocol metadata.
- `scripts/sync-version.mjs` exists to propagate that metadata consistently.
- `scripts/validate-release.ps1` is the shared validation entrypoint for local release preflight and GitHub Actions gating.
- `scripts/unity-version-matrix.ps1` is the canonical Unity compatibility harness for Unity 6 slot coverage (`6000.0` through `6000.4`) with explicit fallback and skip reporting.
- `skills/unity-control-protocol/` is the canonical agent skill source used by docs and the Claude Code plugin wrapper.
- `.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json` define the Claude Code marketplace-facing wrapper for the base skill.
- the CLI and bridge must remain aligned on protocol version and compatibility expectations.
- repo-level docs should reflect the implemented system, not an aspirational redesign.

## Engineering Principles

### 1. Preserve strong boundaries

The project scales best when each layer has a clear responsibility.

- The CLI should remain the user-facing orchestration layer.
- Transport and lifecycle behavior should stay centralized rather than being reimplemented in individual commands.
- The Unity package should remain the editor-execution layer.
- Docs and packaging should describe and distribute the product, not reshape its runtime behavior.

Good changes strengthen those boundaries. Fragile changes usually blur them.

### 2. Prefer coherent extension over structural expansion

As the command surface grows, maintainability depends more on coherence than on novelty.

- Prefer extending an existing domain before creating a new top-level abstraction.
- Prefer evolving the current command and controller families before introducing parallel patterns.
- Prefer local improvements in the correct layer over broad rewrites that spread responsibility.

New concepts should only be introduced when the current structure is no longer carrying its weight.

### 3. Keep data flow explicit

This codebase works best when runtime behavior is easy to trace.

- Inputs should be validated near the boundary where they enter the system.
- Context should be passed clearly, not hidden behind ambient state.
- Protocol shapes should be deliberate, stable, and understandable.
- Error paths should remain meaningful enough to debug real automation failures quickly.

This is especially important here because UCP spans a native CLI, a network transport, and Unity Editor internals.

### 4. Optimize for operational clarity

UCP is not just a library; it is a workflow tool used by humans, scripts, and agents.

That means maintainability includes:

- predictable lifecycle behavior
- understandable output
- consistent protocol behavior
- clear recovery when Unity, the bridge, or a project is in a bad state

Features that make the system more powerful but harder to operate should be treated carefully.

For Unity-facing mutations, operational clarity specifically means:

- the command chooses the correct lifecycle category up front
- post-action waits stay centralized instead of being reimplemented ad hoc
- the CLI reports success only after the category's readiness guarantee is satisfied
- future commands extend the same lifecycle framework instead of inventing local exceptions

### 5. Favor stable internal conventions over cleverness

The codebase already benefits from recurring patterns in both the Rust CLI and the Unity bridge. Future work should continue that direction.

In practice this means:

- readable module ownership
- unsurprising naming
- straightforward request and response shaping
- localized helpers instead of hidden magic
- explicit tradeoffs when something more complex is truly necessary

The standard to aim for is not minimal code. It is code that remains easy to extend correctly months later.

### 6. Respect the strengths of the tech stack

Each part of the stack suggests a natural style.

- In Rust, lean toward explicit types, clear ownership, and focused modules.
- In the CLI layer, keep user intent, lifecycle handling, transport, and presentation conceptually separate.
- In Unity Editor code, respect editor semantics, serialization pathways, and assembly boundaries.
- In bridge code, prefer simple and robust protocol behavior over overly abstract frameworks.

The goal is not purity for its own sake. The goal is to use each layer in a way that remains maintainable under growth.

### 7. Treat documentation as part of the architecture

In this repository, documentation is not just explanatory text. It is part of the maintenance surface.

- `README.md` explains the product and workflow.
- `PROJECT.md` explains architectural direction.
- `CONTRIBUTING.md` explains contribution expectations.
- docs and skills explain the public surface.

When the system changes materially, these documents should change with it so future work continues from reality rather than stale assumptions.

## Technology-Oriented Guidance

### Rust CLI

The Rust CLI should continue to emphasize:

- small command-oriented modules
- centralized lifecycle and bridge readiness handling
- mutating commands that leave the Unity editor settled before they report success
- a shared lifecycle-policy layer that classifies Unity mutations into read-only, settle, restart-then-settle, or custom-confirmation flows
- explicit protocol interactions
- predictable user and JSON output
- errors that are useful in automation contexts

The CLI is the orchestration layer. It should remain easy to reason about even as the command surface expands.

### Unity Bridge

The Unity package should continue to emphasize:

- a clear editor-only execution boundary
- domain-oriented controller organization
- simple RPC routing
- explicit request parsing and response shaping
- compatibility with normal Unity editor behavior

The bridge should stay pragmatic and reliable. It should not become harder to evolve than the editor workflows it is meant to automate.

### Packaging And Metadata

Release metadata, package metadata, and protocol metadata should continue to move through a small number of known sources rather than through ad hoc edits across the repo.

Release hardening now also depends on a shared validation path:

- pull requests and `main` pushes run `.github/workflows/validate.yml`
- tag releases run the same preflight before binary packaging and npm publish
- Unity compatibility testing should prefer one canonical dev project source plus disposable per-run copies, rather than maintaining long-lived per-version project clones

Claude Code plugin metadata should also stay version-aligned with the same source of truth so marketplace installs track the same release identity as the CLI, bridge, npm package, docs, and agent skill.

This matters because UCP ships through multiple channels and the cost of drift is high.

## What To Preserve As The Codebase Grows

As the repository evolves, maintainability depends on preserving a few core qualities:

- a visible architecture with low surprise
- narrow responsibilities per layer
- extension by domain rather than by duplication
- protocol and lifecycle behavior that remain coherent
- documentation that stays synchronized with implementation

If a change adds capability but weakens those qualities, it should be reconsidered or reframed.

## Practical Standard For Future Work

Future work should generally make the repository:

- easier to navigate
- easier to extend in the correct place
- easier to debug in real Unity projects
- easier to review without hidden assumptions

That is the standard this project should optimize for.
