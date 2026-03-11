# Version Control

Full integration with Unity Version Control (Plastic SCM). Manage your workspace directly from the command line.

## Commands

### `ucp vcs status`

Show working copy status — pending changes, added, deleted, and modified files.

```bash
ucp vcs status
```

### `ucp vcs commit`

Commit pending changes with a message.

```bash
ucp vcs commit -m "Add player controller"
```

### `ucp vcs checkout`

Revert files to their last committed state.

```bash
ucp vcs checkout Assets/Scripts/Player.cs
```

### `ucp vcs diff`

Show differences for pending changes.

```bash
ucp vcs diff
```

### `ucp vcs history`

View changeset history.

```bash
# Recent history
ucp vcs history

# Limit results
ucp vcs history --count 10
```

### `ucp vcs lock`

Lock a file to prevent others from editing it.

```bash
ucp vcs lock Assets/Scenes/MainScene.unity
```

### `ucp vcs unlock`

Release a file lock.

```bash
ucp vcs unlock Assets/Scenes/MainScene.unity
```

### Branch Operations

```bash
# List branches
ucp vcs branch list

# Create a branch
ucp vcs branch create feature/new-ui

# Switch branches
ucp vcs branch switch feature/new-ui
```

## Requirements

Version control commands require Unity Version Control (Plastic SCM) to be configured in your project. These commands interact with the Plastic SCM CLI through Unity's VCS integration.
