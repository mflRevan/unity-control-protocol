---
name: unity-control-protocol-qa
description: >-
  Validate Unity Control Protocol releases against the bundled dev project,
  bridge smoke tests, and installer behavior. Use when the task is specifically
  about regression testing, release hardening, or verifying end-to-end UCP
  workflows before publishing.
compatibility: Requires the `ucp` CLI, the bundled `unity-project-dev/ucp-dev` project, and a locally available Unity Editor.
metadata:
  author: mflRevan
  version: '0.3.3'
---

# Unity Control Protocol QA

Use this companion skill when the goal is verification rather than ordinary editor automation.

## When to use this skill

- The user asks for release validation or regression testing
- You need to verify installer behavior against the bundled dev project
- You need to run bridge smoke tests or targeted Unity Test Runner cases
- You need to confirm docs, npm packaging, or website content after a release change

## Core workflow

```bash
./scripts/smoke-dev.ps1 -Project unity-project-dev/ucp-dev
ucp doctor --project unity-project-dev/ucp-dev
ucp editor status --project unity-project-dev/ucp-dev
ucp run-tests --project unity-project-dev/ucp-dev --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests"
```

## Release checks

```bash
cargo test --manifest-path cli/Cargo.toml
node scripts/sync-version.mjs --check
npm pack ./npm
```

## Focus areas for 0.3.1+

- Buffered log reads should respect `--count` without a hard 10-entry cap
- Regex log searches should filter before count truncation
- Object reference writes should fail loudly on invalid references
- Asset batch writes should update multiple serialized fields in one request
- `ucp install` should leave the project with automation-friendly PlayerSettings defaults
- Unity executable discovery should honor Unity Hub secondary install roots
- Negative-path controller errors should return protocol error codes without noisy internal-error logs
- The smoke script should cover start, connect, run-tests, and close against `unity-project-dev/ucp-dev`
