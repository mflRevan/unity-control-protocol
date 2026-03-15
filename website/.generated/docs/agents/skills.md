# Agent Skills

UCP ships with [Agent Skills](https://agentskills.io)-compatible skill files that allow AI coding agents to understand and use the full UCP toolset automatically.

## What are Agent Skills?

Agent Skills is a standardized format for packaging tool-specific instructions that AI agents can discover and load on demand. When an agent encounters a task involving Unity - such as modifying a scene, tweaking a material, or running a build - it activates the UCP skill and gains detailed knowledge of every available command, flag, and workflow.

## Where are Agent Skills used?

Agent Skills are supported by a growing number of AI coding tools:

- **Claude Code** - Place the skill directory in your project and Claude will discover it automatically
- **Cursor / Windsurf / Copilot** - Agents in VS Code-based editors can load skills from the workspace
- **Custom agent frameworks** - Any agent that follows the Agent Skills specification can consume `SKILL.md` files

## How to install

Copy the `skills/unity-control-protocol/` directory into your Unity project (or any workspace where you want agents to have UCP access):

```bash
# From the UCP repository
cp -r skills/unity-control-protocol/ /path/to/your-project/skills/

# Or download just the SKILL.md
curl -o skills/unity-control-protocol/SKILL.md \
  https://raw.githubusercontent.com/mflRevan/unity-control-protocol/main/skills/unity-control-protocol/SKILL.md
```

The agent will automatically discover and load the skill when it encounters Unity-related tasks.

If you also want a release-validation workflow for the bundled dev project and bridge smoke suite, copy `skills/unity-control-protocol-qa/` as well.

## Primary skill preview

Below is the full content of the primary UCP Agent Skill. This is exactly what an AI agent sees when it activates the skill.
