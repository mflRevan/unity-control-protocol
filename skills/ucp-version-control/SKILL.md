---
name: ucp-version-control
description: >-
  Lightweight Unity Version Control (Plastic SCM) operations through the editor with `ucp vcs`:
  provider status, pending changes, checkout, revert, commit, diff, incoming, update, lock,
  unlock, history, and conflict resolution. Use when a project is under Unity VCS and the native
  `cm` CLI is unavailable or you want the editor's own view of pending changes. Prefer `cm`
  (or git) for everything else.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`), the UCP bridge package, and a Unity project connected to Unity Version Control. `branches` and `history` need the `cm` CLI. Unity 2021.3 or newer.
metadata:
  author: mflRevan
  version: '0.6.3'
  homepage: https://unityctl.dev/skills/ucp-version-control
---

# Version control fallback

`ucp vcs` mirrors the editor's Version Control window for projects on Unity VCS / Plastic SCM. It
is a fallback: the native `cm` CLI is faster, complete, and scriptable, and git projects should
use git directly. Reach for `ucp vcs` when `cm` is not installed or when you want the state the
editor itself sees (checked-out files, editor-side pending changes).

```bash
ucp vcs info                                      # provider, workspace, connection state
ucp vcs status                                    # all pending changes
ucp vcs status --path Assets/Scenes               # scoped
ucp vcs diff                                      # change summary
ucp vcs diff Assets/Scenes/Level1.unity           # per-file status
ucp vcs checkout Assets/Scenes/Level1.unity Assets/Prefabs/Enemy.prefab
ucp vcs checkout --all                            # every modified/added asset
ucp vcs revert Assets/Scenes/Level1.unity
ucp vcs revert --all --keep-local                 # undo checkouts, keep edits on disk
ucp vcs commit -m "Rearrange level 1 props" Assets/Scenes/Level1.unity
ucp vcs commit -m "Checkpoint"                    # all pending
ucp vcs incoming
ucp vcs update                                    # get latest and apply
ucp vcs lock Assets/Scenes/Level1.unity
ucp vcs unlock Assets/Scenes/Level1.unity
ucp vcs history --limit 20                        # needs cm
ucp vcs branches                                  # needs cm
ucp vcs resolve Assets/Scenes/Level1.unity --method theirs   # merge (default) | mine | theirs
```

## Working rules

- Check out before editing binary or scene assets on a locked-file workflow; `ucp files write`
  and `asset` commands do not check out for you.
- `commit` without paths commits everything pending. Review with `status` and `diff` first.
- `update` can change files under a scene you have open; save or close it first, and expect
  `importing assets` on the `[editor]` line afterwards.
- On a git project every command reports the provider as unavailable; nothing is attempted.
