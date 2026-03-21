# Version Control

Bridge-backed fallback commands for Unity Version Control (Plastic SCM / UVCS).

When the native `cm` CLI is available, prefer `cm` for normal Unity Version Control work. It exposes a much richer command surface for branch management, shelvesets, merge workflows, workspace operations, and other full-source-control tasks.

Use `ucp vcs` when you specifically want a lightweight fallback through the Unity bridge, or when an agent needs a small editor-adjacent VCS action without leaving the UCP workflow.

## Commands

```bash
ucp vcs
```

Running `ucp vcs` prints the currently available fallback subcommands and flags.

Typical fallback usage includes lightweight status, checkout, revert, commit, diff, history, lock, unlock, update, and conflict-resolution actions. For richer Unity Version Control workflows, use `cm`.

## Requirements

Version control commands require Unity Version Control (Plastic SCM) to be configured in your project.

`ucp vcs` is not intended to replace the native `cm` CLI. Treat it like `ucp files ...`: useful as a fallback inside bridge-driven automation, but not the preferred surface when you already have direct workspace access to the real tool.
