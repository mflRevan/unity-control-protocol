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

There are two main distribution paths, depending on the tool you are using.

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

### 2. Claude Code marketplace install

Claude Code uses plugins rather than raw workspace skills as the primary marketplace abstraction. The repository marketplace ships **two** plugins, and you choose based on how you want skills to surface to the agent.

First, add the marketplace once:

```text
/plugin marketplace add mflRevan/unity-control-protocol
```

For local plugin testing of either plugin:

```bash
claude --plugin-dir .
```

#### Option A — the omni skill (`ucp`)

Install the single, broad Unity automation skill that covers every command surface in one place:

```text
/plugin install ucp@unity-control-protocol
```

That install exposes one skill:

- `/ucp:unity-control-protocol`

#### Option B — per-surface micro-skills (`ucp-surfaces`)

Install focused, surface-specific skills — one per `ucp` command group — instead of the single omni skill:

```text
/plugin install ucp-surfaces@unity-control-protocol
```

That install exposes fifteen focused skills, each invoked as `/ucp-surfaces:ucp-<surface>`:

- `/ucp-surfaces:ucp-objects` — `ucp object` (create incl. `--primitive`, components, properties, reparent, instantiate)
- `/ucp-surfaces:ucp-scene` — `ucp scene` + `ucp editor` lifecycle + `ucp play|stop|pause|compile|screenshot`
- `/ucp-surfaces:ucp-transform` — `ucp transform` (move/rotate/scale/look-at/get)
- `/ucp-surfaces:ucp-spatial` — `ucp spatial` (raycast/overlap/bounds/ground/nearest)
- `/ucp-surfaces:ucp-view` — `ucp view` (capture/isolate/orbit) + `ucp screenshot`
- `/ucp-surfaces:ucp-assets` — `ucp asset` + `ucp files` + `ucp shader errors`
- `/ucp-surfaces:ucp-materials` — `ucp material` (create/get/set properties, keywords, shader)
- `/ucp-surfaces:ucp-prefabs` — `ucp prefab` (status/apply/revert/unpack/create/overrides)
- `/ucp-surfaces:ucp-build` — `ucp build` (targets/scenes/defines/start)
- `/ucp-surfaces:ucp-packages` — `ucp packages` (search/add/remove/registries/`.unitypackage`)
- `/ucp-surfaces:ucp-settings` — `ucp settings` (player/quality/physics/lighting/tags-layers)
- `/ucp-surfaces:ucp-profiler` — `ucp profiler` + `ucp profile` + `ucp frame capture`
- `/ucp-surfaces:ucp-references` — `ucp references` (find/index/check/find-strings, read-only)
- `/ucp-surfaces:ucp-tests` — `ucp run-tests` + `ucp exec` + `ucp script doctor`
- `/ucp-surfaces:ucp-vcs` — `ucp vcs` (Plastic/Unity VCS fallback; prefer `cm`)

#### Which one should I pick?

- **Pick the omni skill (`ucp`)** for general use. One skill carries the full cross-surface workflow guidance, so a single activation covers multi-step tasks that span scenes, objects, assets, builds, and tests at once. This is the recommended default.
- **Pick the micro-skills (`ucp-surfaces`)** when you want tighter routing and a smaller per-skill context. Each micro-skill names its concrete subcommands in its description, so the agent loads only the surface relevant to the task (e.g. just `ucp-transform` for a positioning task) instead of the whole omni skill. This is useful when you want predictable, narrow activations or are composing UCP with many other skills.

The two plugins are independent — install either or both. The micro-skill descriptions explicitly defer to the omni skill for broad, multi-surface automation, so they coexist without fighting over routing.

## Primary skill preview

Below is the full content of the primary UCP Agent Skill. This is exactly what an AI agent sees when it activates the skill.
