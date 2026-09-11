# Agent Skills

UCP is built for agents. The command surface is the API, and the skills are the manual an agent
loads when a task touches Unity. They follow the [Agent Skills specification](https://agentskills.io/specification):
a directory named after the skill with a `SKILL.md` whose frontmatter carries `name`,
`description`, `compatibility`, and `metadata`, and whose body is the instructions. Any harness
that implements the spec (Claude Code, Codex, Cursor, Copilot, Gemini CLI, opencode, Amp, and
others) can consume them unchanged.

The skills are hand-written ground truth under `skills/` in the repository. Everything else, the
Claude Code plugins, the pages on this site, and the raw Markdown endpoints, is generated from
those files, so what an agent reads is exactly what is maintained.

## The skills

| skill | covers | raw Markdown |
|---|---|---|
| `unity-control-protocol` | the whole surface in one skill; the recommended default | [unity-control-protocol.md](https://unityctl.dev/skills/unity-control-protocol.md) |
| `ucp-editor-lifecycle` | install, open, adopt, close, `[editor]` state line, compile, play/stop/pause, modal dialogs | [ucp-editor-lifecycle.md](https://unityctl.dev/skills/ucp-editor-lifecycle.md) |
| `ucp-scene-authoring` | `scene`, `object`, `transform`, `spatial`, `prefab`: hierarchy, primitives, properties, placement | [ucp-scene-authoring.md](https://unityctl.dev/skills/ucp-scene-authoring.md) |
| `ucp-assets` | `asset`, `files`, `material`, `references`, `shader`, `script`: assets on disk without breaking GUIDs | [ucp-assets.md](https://unityctl.dev/skills/ucp-assets.md) |
| `ucp-ui-toolkit` | `ui`: lint, inspect, populate, screenshot, and check UXML/USS (Unity 6+) | [ucp-ui-toolkit.md](https://unityctl.dev/skills/ucp-ui-toolkit.md) |
| `ucp-visual-feedback` | `screenshot`, `view`, `record`: seeing the scene, composed renders, clips with `--slowdown` | [ucp-visual-feedback.md](https://unityctl.dev/skills/ucp-visual-feedback.md) |
| `ucp-runtime-debugging` | `logs`, `run-tests`, `exec`, `profiler`, `profile`, `frame`: playtest loops and triage | [ucp-runtime-debugging.md](https://unityctl.dev/skills/ucp-runtime-debugging.md) |
| `ucp-project-config` | `packages`, `settings`, `build`: dependencies, project settings, builds | [ucp-project-config.md](https://unityctl.dev/skills/ucp-project-config.md) |
| `ucp-version-control` | `vcs`: Unity VCS / Plastic fallback when `cm` is unavailable | [ucp-version-control.md](https://unityctl.dev/skills/ucp-version-control.md) |

Which to install: the omni skill for general use, since one activation carries the cross-surface
workflows a real task needs. The surface skills when you want narrow, predictable activations
or compose UCP with many other skills; each names its commands in its description so the agent
loads only what the task needs, and each defers to the omni skill for anything broader. Both sets
coexist without fighting over routing.

A machine-readable catalog lives at [skills/index.json](https://unityctl.dev/skills/index.json)
(name, description, compatibility, commands, raw URL, version).

## Install

### Claude Code

```text
/plugin marketplace add mflRevan/unity-control-protocol
/plugin install ucp@unity-control-protocol            # omni skill
/plugin install ucp-surfaces@unity-control-protocol   # the eight surface skills
```

The skills surface as `/ucp:unity-control-protocol` and `/ucp-surfaces:ucp-<surface>`. To try a
checkout without installing: `claude --plugin-dir /path/to/unity-control-protocol`. Third-party
marketplaces do not auto-update by default; enable it in `/plugin` or run `/plugin update`.

### Any harness that reads `.agents/skills` (Codex, Cursor, Copilot, Gemini CLI, opencode, Amp)

```bash
npx skills add mflRevan/unity-control-protocol                  # pick skills interactively
npx skills add mflRevan/unity-control-protocol --skill unity-control-protocol -y
npx skills update                                                # re-resolve against the repo
```

[skills.sh](https://www.skills.sh) discovers the `skills/` directory and the marketplace manifest,
installs into the right directory for each agent (`.agents/skills`, `.claude/skills`,
`.cursor/skills`, ...), and records what it installed in `skills-lock.json`.

GitHub CLI (2.90+) works the same way:

```bash
gh skill install mflRevan/unity-control-protocol unity-control-protocol --agent codex --scope user
gh skill update
```

### Manual

Every skill is served raw, frontmatter included, so a plain download is a valid install:

```bash
mkdir -p .agents/skills/unity-control-protocol
curl -fsSL https://unityctl.dev/skills/unity-control-protocol.md -o .agents/skills/unity-control-protocol/SKILL.md
```

`.agents/skills/` is the project-level path every spec-compliant harness reads. Claude Code reads
`.claude/skills/` instead; user-level installs go under `~/.agents/skills/` or `~/.claude/skills/`.

## Versioning

Each skill's `metadata.version` matches the CLI release it documents, and the release flow stamps
it. Skills describe the command surface of that release; an older CLI may lack a flag a newer
skill mentions. `ucp --version` and `ucp <command> --help` are always authoritative, and every
skill says so.

## For agents reading this site directly

- `https://unityctl.dev/llms.txt` lists every page and skill with a one-line summary.
- `https://unityctl.dev/llms-full.txt` is the entire documentation and every skill in one file.
- `https://unityctl.dev/docs/<page>.md` and `https://unityctl.dev/skills/<skill>.md` are the raw
  Markdown sources of the human pages; append `.md` to any docs URL.
