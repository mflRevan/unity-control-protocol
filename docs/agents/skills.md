# Agent Skills

UCP ships with [Agent Skills](https://agentskills.io)-compatible skill files that allow AI coding agents to understand and use the full UCP toolset automatically.

## What are Agent Skills?

Agent Skills is a standardized format for packaging tool-specific instructions that AI agents can discover and load on demand. When an agent encounters a task involving Unity - such as modifying a scene, tweaking a material, or running a build - it activates the UCP skill and gains detailed knowledge of every available command, flag, and workflow.

## Where are Agent Skills used?

Agent Skills are supported by a growing number of AI coding tools:

- **Claude Code** - Install the dedicated UCP plugin from the repository marketplace, or test it locally with `claude --plugin-dir`
- **Cursor / Windsurf / Copilot** - Agents in VS Code-based editors can load skills from the workspace
- **Custom agent frameworks** - Any agent that follows the Agent Skills specification can consume `SKILL.md` files

## How to install

There are now three main distribution paths, depending on the tool you are using.

### 1. Manual workspace install

Use this when your agent tooling expects raw `skills/` folders in the workspace.

Copy the `skills/unity-control-protocol/` directory into your Unity project (or any workspace where you want agents to have UCP access):

```bash
# From the UCP repository
cp -r skills/unity-control-protocol/ /path/to/your-project/skills/

# Or download just the SKILL.md
curl -o skills/unity-control-protocol/SKILL.md \
  https://raw.githubusercontent.com/mflRevan/unity-control-protocol/main/skills/unity-control-protocol/SKILL.md
```

The agent will automatically discover and load the skill when it encounters Unity-related tasks.

### 2. ClawHub / OpenClaw discovery

The base UCP skill is also published through ClawHub as a marketplace-distributed `SKILL.md`.

- Discover it through ClawHub or OpenClaw skill search
- Install/update it through the ClawHub/OpenClaw skill workflow instead of manually copying files
- The ClawHub package is intentionally minimal and ships only the canonical base `SKILL.md`

This is the best fit when your runtime follows the OpenClaw / ClawHub skill model rather than Claude Code plugins.

### 3. Claude Code marketplace install

Claude Code uses plugins rather than raw workspace skills as the primary marketplace abstraction. UCP now ships the base Unity automation skill through the repository's canonical root plugin.

For local plugin testing:

```bash
claude --plugin-dir .
```

For marketplace-style install from GitHub:

```text
/plugin marketplace add mflRevan/unity-control-protocol
/plugin install ucp@unity-control-protocol
```

That default Claude install exposes:

- `/ucp:unity-control-protocol`

## Primary skill preview

Below is the full content of the primary UCP Agent Skill. This is exactly what an AI agent sees when it activates the skill.
