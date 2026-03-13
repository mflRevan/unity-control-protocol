# PROJECT.md — Unity Control Protocol

This file is the repo-level operational reference for structure, workflows, validation, and release constraints.

## Product summary

UCP exposes the Unity Editor as a local automation target through:

- a Rust CLI in `cli/`
- a Unity Editor bridge package in `unity-package/com.ucp.bridge/`
- an npm wrapper in `npm/`
- a docs site in `website/` sourced from `docs/`, deployed on Vercel

The CLI talks to the bridge over localhost WebSocket using JSON-RPC 2.0.

Current release target: `0.3.0`
Current protocol version: `0.3.0`
Canonical metadata source: `version.json`

## Repository layout

```text
unity-control-protocol/
├── cli/                              Rust CLI crate
│   ├── Cargo.toml
│   └── src/
│       ├── main.rs                   CLI entrypoint
│       ├── bridge_lifecycle.rs       bridge wait/reconnect orchestration
│       ├── client.rs                 WebSocket client and RPC transport
│       ├── config.rs                 protocol constants and lock-file types
│       ├── discovery.rs              project discovery and Unity process detection
│       ├── error.rs                  shared CLI error handling
│       ├── output.rs                 human and JSON output helpers
│       ├── protocol.rs               JSON-RPC message types
│       └── commands/                 command implementations
├── unity-package/
│   └── com.ucp.bridge/
│       ├── package.json
│       ├── Editor/
│       │   ├── Bridge/               server lifecycle and registration
│       │   ├── Controllers/          domain controllers for RPC methods
│       │   ├── Protocol/             JSON-RPC plumbing and router
│       │   └── Scripts/              `IUCPScript` support
│       ├── Runtime/
│       └── Tests/Editor/             package EditMode tests
├── npm/                              npm distribution wrapper
├── website/                          Vite/React docs site
├── docs/                             markdown docs rendered by the site
├── skills/                           canonical agent skill content
├── scripts/
│   ├── sync-version.mjs              sync version-bearing metadata from `version.json`
│   └── smoke-dev.ps1                 local dev bridge injection + smoke validation
├── README.md
├── AGENTS.md
├── PROJECT.md
└── version.json
```

## Runtime model

### Transport

- The bridge listens only on localhost.
- The bridge writes `.ucp/bridge.lock` into the Unity project root.
- The CLI discovers the active bridge from that lock file, validates the PID, and performs a handshake.

### Security model

- Localhost-only listener.
- Per-session token carried in the project lock file.
- File operations remain scoped to the target Unity project.

## Command surface

### Core lifecycle

- `doctor`
- `connect`
- `install`
- `uninstall`
- `play`
- `stop`
- `pause`
- `compile`

### Read and inspect

- `scene list|active|load`
- `snapshot`
- `logs`
- `screenshot`
- `run-tests`
- `exec list|run`
- `read-file`
- `object ...`
- `asset ...`
- `material ...`
- `prefab ...`
- `settings ...`
- `build ...`
- `vcs ...`

## Release and packaging contracts

### Metadata

- `version.json` is the source of truth for release version and protocol version.
- `scripts/sync-version.mjs <version>` propagates metadata to Cargo, npm, Unity package metadata, docs, and other version-bearing files.
- `.github/workflows/release.yml` runs `node scripts/sync-version.mjs --check <tag-version>` before publishing.

### Bridge/package alignment

- `ucp install` is manifest-first by default and writes a tracked git dependency pinned to the CLI version.
- Published npm packages bundle `bridge/com.ucp.bridge` alongside the CLI wrapper package.
- GitHub releases publish bundled archives containing the CLI binary plus `bridge/com.ucp.bridge`.
- Local embedded bridge mounts are explicit via `ucp install --dev`, `ucp install --embedded`, or `ucp install --bridge-path`.
- The default bridge dependency is:
  `https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v<cli-version>`
- The npm wrapper downloads release binaries from the matching GitHub release tag.

## Local development workflow

### Local bridge injection

Use the repo-local package while developing bridge changes:

```powershell
cargo run --manifest-path cli/Cargo.toml -- --project <UnityProject> install --dev
```

Behavior:

- `ucp install` writes the tracked git dependency to `Packages/manifest.json` by default.
- Default install does not inject a local `file:` dependency.
- `install --dev` mounts `unity-package/com.ucp.bridge/` into the target Unity project as `Packages/com.ucp.bridge`.
- This keeps `Packages/manifest.json` unchanged, so the dev bridge stays local to that workspace instead of becoming a tracked project dependency.
- When the target project is a git repo, the installer adds `Packages/com.ucp.bridge/` and `.ucp/` to `.git/info/exclude` instead of editing tracked ignore files.
- On Windows, the CLI actively brings the Unity editor forward. It first tries the native window APIs and then falls back to `WScript.Shell.AppActivate`, which proved necessary in live testing.
- If the same local embedded mount is already present, rerunning `install --dev` reuses it and still performs the bridge wait/reconnect flow.
- If the bridge is already running, install/update also requests `refresh-assets` through the existing bridge before waiting.
- Install flows are non-interactive by default (no terminal confirmation prompt). Use `ucp install --confirm` only when a human approval gate is desired.

### Local bridge source overrides

Advanced options:

- `ucp install --embedded` to force a local embedded install even if a tracked dependency path is available
- `ucp install --manifest` to force tracked manifest mode explicitly (same behavior as default)
- `ucp install --bridge-path <path>` to point at another local Unity package directory
- `ucp install --bridge-ref <manifest-ref>` to inject an explicit dependency reference
- `ucp install --confirm` to require interactive confirmation before install actions
- `ucp install --no-wait` to skip the wait/reconnect step

Tracked manifest dependency for Unity to pull directly from GitHub remains supported and explicit:

```json
{
  "dependencies": {
    "com.ucp.bridge": "https://github.com/mflRevan/unity-control-protocol.git?path=unity-package/com.ucp.bridge#v0.2.3"
  }
}
```

## Validation workflow

### Minimum pre-release validation

1. `cargo test --manifest-path cli/Cargo.toml`
2. `cargo check --manifest-path cli/Cargo.toml`
3. `npm run build` inside `website/`
4. Run the local-dev smoke suite against a real Unity project
5. Verify versions, changelog, and release notes align with `version.json`

### Live smoke suite

Preferred wrapper:

```powershell
./scripts/smoke-dev.ps1 -Project <UnityProject>
```

Current smoke set:

- `install --dev`
- `doctor`
- `connect`
- `snapshot --json`
- `scene active`
- `asset search -t Material --max 5 --json`
- `build active-target`
- `settings player`

Optional focused reads:

- `asset read <path>`
- `material get-properties --path <material>`
- `object get-fields --id <id> --component <type>`

### Extensive QA report (2026-03-13)

Playground target: `unity-project-dev/ucp-dev`

- Full command-palette harness (`scripts/qa-playground.ps1`) achieved green status at `50/50` passing with process exit code `0`.
- Embedded EditMode verification in the same run: `run-tests --mode edit` passed `11/11`.
- Harness reliability fixes landed for bridge reconnect windows around `play/pause/stop`, screenshot assertions, prefab unpack flags, and cleanup idempotency.
- Unity-side guard added so edit-mode test execution is deferred until Play Mode exits, preventing Test Runner invalid-operation failures.

### Unattended workflow manual (no-click automation)

Use this sequence for full-cycle agent runs without manual dialogs:

1. `ucp install --dev --no-wait`
2. `ucp connect`
3. Perform object/asset/material/settings/build operations
4. `ucp compile --no-wait`
5. `ucp play` / `ucp pause` / `ucp stop`
6. `ucp run-tests --mode edit`

Dialog-avoidance defaults now active:

- `ucp install` skips interactive confirmation unless `--confirm` is provided.
- `ucp scene load` auto-saves dirty named scenes and discards dirty untitled scenes unless overridden.
- `ucp play` applies the same dirty-scene handling before entering play mode.

Safety override switches for both `scene load` and `play`:

- `--no-save` to disable auto-save behavior
- `--keep-untitled` to prevent auto-discard of untitled dirty scenes

### Persistent tests

- Rust unit tests cover discovery parsing and install/dev reference generation.
- Unity package EditMode tests live in `unity-package/com.ucp.bridge/Tests/Editor/` and currently cover:
  - lean snapshot defaults and schema shape
  - asset search behavior for real subasset type matches

## Output guardrails

These are intentional to keep agent and terminal usage from collapsing into oversized payloads:

- `snapshot` defaults to depth `0` and returns lean object metadata only.
- `object get-fields` human-mode output is capped and instructs callers to narrow with `--property` or switch to `--json`.
- `asset read` human-mode output is capped and instructs callers to narrow with `--field`.
- `material get-properties` human-mode output is capped and directs callers to `get-property` or `--json` for deep inspection.
- `prefab overrides` human-mode output caps property modifications and component deltas.
- `settings player|quality|physics|lighting` summarize long arrays, objects, and strings in human mode.
- `logs tail/search` bulk results are capped at `10` summaries and require `logs --id <id>` or a narrower search space for deep inspection.

JSON output remains intentionally full-fidelity. Use it for targeted machine reads, not broad exploratory dumps.

## Known QA findings

- The live HijraVR smoke pass confirmed the new lean snapshot payload and local dev bridge injection flow.
- Unity on Windows did not reliably react to plain foreground-window APIs alone; `AppActivate` was required as a fallback.
- `run-tests --filter` semantics are still governed by Unity Test Runner behavior and should be validated against host-project expectations before relying on it as a strict selector.
- Some host projects still report container-level names like the project name in Unity test results, so current live test output should be treated as pass/fail confirmation rather than authoritative leaf-test naming.
- Raw standalone binaries without the bundled archive layout still do not carry the bridge payload; use the bundled release archives or npm package when local-first install behavior matters.
- Fresh snapshot-derived instance ids were valid in HijraVR; the earlier failure was caused by stale ids after scene/reload churn, and the CLI's field output now reads the correct `name` key from the bridge response.
- Treat instance ids as session-scoped editor handles. Re-run `snapshot` after compilation, package reloads, scene loads, or tests before using object-level commands.

## Documentation sync rules

If command behavior changes, update all relevant surfaces:

- `README.md`
- `PROJECT.md`
- docs in `docs/`
- website pages that mirror those docs
- skill content in `skills/unity-control-protocol/`
- changelog entries when behavior is user-visible
