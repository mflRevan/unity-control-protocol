---
name: ucp-project-config
description: >-
  Configure a Unity project from the terminal with `ucp packages`, `ucp settings`, and
  `ucp build`: search, add, and remove UPM packages, manage scoped registries and manifest
  dependencies, inspect and selectively import .unitypackage archives, read and set player,
  quality, physics, and lighting settings, tags and layers, and drive the build pipeline
  (targets, scenes, scripting defines, builds). Use for project setup, dependency work, and
  release configuration. For scene content use ucp-scene-authoring; for assets use ucp-assets.
compatibility: Requires the `ucp` CLI (npm `@mflrevan/ucp`) and the UCP bridge package in the target Unity project. Unity 2021.3 or newer.
metadata:
  author: mflRevan
  version: '0.6.4'
  homepage: https://unityctl.dev/skills/ucp-project-config
---

# Project configuration: packages, settings, builds

## Packages

```bash
ucp packages list                                       # installed, direct dependencies
ucp packages list --all --offline                       # include indirect, cached data only
ucp packages search cinemachine --max 10
ucp packages info com.unity.cinemachine
ucp packages add com.unity.cinemachine                  # waits for resolve and bridge reload
ucp packages add com.unity.inputsystem@1.19.0 com.unity.textmeshpro
ucp packages add https://github.com/org/pkg.git?path=/Packages/com.org.pkg#v1.2.0
ucp packages remove com.unity.timeline
ucp packages dependencies                               # manifest.json as it is
ucp packages dependency set com.company.tooling file:../tooling-package
ucp packages dependency remove com.company.tooling
ucp packages registries list
ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company --scope com.partner
ucp packages registries remove --name github
```

- `add`/`remove` go through the Package Manager and wait for resolution and the domain reload
  that follows; `--no-wait` returns after the request is accepted. Multiple packages resolve one
  after another because the Package Manager serializes operations.
- `dependency set` edits the manifest directly, which is the right tool for `file:` references
  and pinned git URLs; `add` is the right tool for registry packages.
- Adding a new scoped registry can raise Unity's registry-trust prompt once; the CLI answers
  recognised prompts per `--dialog-policy` and names unknown ones.
- A package that fails to compile puts a green console into the red and, on load, shows
  "Packages with Errors"; the `[editor]` line and `ucp compile` tell you which.

## `.unitypackage` archives

```bash
ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage      # asset tree, sizes, guids
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --dry-run
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees --select Assets/Environment/Materials
ucp packages unitypackage import Downloads/Pack.unitypackage --unselect Assets/Demo --no-reimport
```

Selective import extracts only the chosen paths with their `.meta` files, so GUIDs match what
other assets in the archive expect.

## Settings

```bash
ucp settings player                                     # values + the keys set-player accepts
ucp settings set-player --key productName --value "My Game"
ucp settings set-player --key runInBackground --value true
ucp settings quality && ucp settings set-quality --key vSyncCount --value 0
ucp settings physics && ucp settings set-physics --key gravity --value [0,-9.81,0]
ucp settings lighting && ucp settings set-lighting --key fog --value true --save
ucp settings set-lighting --key ambientMode --value "Flat"
ucp settings tags-layers
ucp settings add-tag Enemy
ucp settings add-layer Interactable --index 10
```

Each `settings <group>` call lists the exact keys its `set-<group>` accepts; values are JSON.
Lighting settings live in the scene, hence `--save`.

## Build

```bash
ucp build targets                                       # installed targets
ucp build active-target
ucp build set-target StandaloneWindows64               # switches; expect a reimport and reload
ucp build scenes
ucp build set-scenes "Assets/Scenes/Boot.unity,Assets/Scenes/Level1.unity"
ucp build defines
ucp build set-defines "CI;RELEASE"
ucp build start --output Builds/win/Game.exe
ucp build start --output Builds/Android/Game.apk --development
```

`build start` blocks the editor and the command for as long as the build takes; the CLI waits
indefinitely for it rather than applying `--timeout`. Read the `[editor]` line and `ucp logs
--level error` afterwards.

## Workflows

Set up a fresh project for automation:

```bash
ucp install && ucp open
ucp settings set-player --key runInBackground --value true
ucp packages add com.unity.inputsystem com.unity.cinemachine
ucp compile
```

CI validation pass:

```bash
ucp connect --json || exit 1
ucp run-tests --mode edit --json
ucp build set-defines "CI;RELEASE"
ucp build set-scenes "Assets/Scenes/Boot.unity,Assets/Scenes/Level1.unity"
ucp build start --output Builds/Game.exe --json
```

Bring in part of an asset-store pack:

```bash
ucp packages unitypackage inspect Downloads/Pack.unitypackage --json
ucp packages unitypackage import Downloads/Pack.unitypackage --select Assets/Pack/Prefabs --dry-run
ucp packages unitypackage import Downloads/Pack.unitypackage --select Assets/Pack/Prefabs
ucp references check Assets/Pack
```

## Pitfalls

- Package operations and target switches reload the domain: instance ids die, recordings end,
  and the next command waits for the bridge to return.
- Removing a package other packages depend on fails at resolve time; the error names the
  dependent.
- `set-defines` replaces the whole list; read `defines` first and pass the full set.
