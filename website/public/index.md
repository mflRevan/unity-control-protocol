# Unity Control Protocol

A Rust CLI and an in-editor bridge that expose the Unity Editor as commands: scenes, objects, assets, materials, prefabs, UI Toolkit, play mode, tests, profiler, builds, screenshots and video. Built for humans and AI agents.

```bash
npm install -g @mflrevan/ucp
cd path/to/UnityProject
ucp install      # adds the com.ucp.bridge package to the project
ucp open         # launches the editor and waits for the bridge
ucp scene list
```

## For agents

- https://unityctl.dev/llms.txt: index of every documentation page and skill
- https://unityctl.dev/llms-full.txt: the entire documentation in one file
- https://unityctl.dev/docs/<page>.md: raw Markdown of any documentation page
- https://unityctl.dev/skills/index.md and https://unityctl.dev/skills/index.json: the skill catalog
- https://unityctl.dev/skills/<name>.md: any skill, ready to save as SKILL.md

## Documentation

- [Introduction](https://unityctl.dev/docs/index.md)
- [Installation](https://unityctl.dev/docs/installation.md)
- [Quick start](https://unityctl.dev/docs/quickstart.md)
- [CLI overview](https://unityctl.dev/docs/overview.md)
- [Project setup and bridge](https://unityctl.dev/docs/overview/project-setup.md)
- [Editor lifecycle](https://unityctl.dev/docs/overview/editor-lifecycle.md)
- [Scenes](https://unityctl.dev/docs/authoring/scenes.md)
- [Objects and components](https://unityctl.dev/docs/authoring/objects.md)
- [Prefabs](https://unityctl.dev/docs/authoring/prefabs.md)
- [Assets](https://unityctl.dev/docs/authoring/assets.md)
- [UI Toolkit](https://unityctl.dev/docs/authoring/ui-toolkit.md)
- [Materials](https://unityctl.dev/docs/authoring/materials.md)
- [Reference search](https://unityctl.dev/docs/authoring/references.md)
- [Files](https://unityctl.dev/docs/authoring/files.md)
- [Scripting](https://unityctl.dev/docs/authoring/scripting.md)
- [Play mode and compilation](https://unityctl.dev/docs/runtime/play-mode.md)
- [Screenshots, recordings and logs](https://unityctl.dev/docs/runtime/logs-and-media.md)
- [Editor state and dialogs](https://unityctl.dev/docs/runtime/editor-state.md)
- [Testing](https://unityctl.dev/docs/runtime/testing.md)
- [Profiler](https://unityctl.dev/docs/runtime/profiler.md)
- [Packages](https://unityctl.dev/docs/project/packages.md)
- [Settings](https://unityctl.dev/docs/project/settings.md)
- [Build pipeline](https://unityctl.dev/docs/project/build.md)
- [Version control](https://unityctl.dev/docs/project/version-control.md)
- [Skills](https://unityctl.dev/docs/agents/skills.md)

## Links

- Repository: https://github.com/mflRevan/unity-control-protocol
- npm: https://www.npmjs.com/package/@mflrevan/ucp
- Discord: https://discord.gg/F4RjhdVTbz
