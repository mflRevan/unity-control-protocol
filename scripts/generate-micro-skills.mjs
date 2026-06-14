#!/usr/bin/env node
// Generates per-command-surface "micro-skills" for the `ucp-surfaces` plugin.
//
// Each surface below becomes plugins/ucp-surfaces/skills/<name>/SKILL.md with
// uniform frontmatter. The `description` deliberately names the concrete
// `ucp <surface>` subcommands so a routing model picks the focused micro-skill
// instead of the broad omni skill (skills/unity-control-protocol/SKILL.md).
//
// Run without args to (re)write all files. Run with --check to verify the
// on-disk files match what would be generated and exit nonzero on drift (CI).

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const skillsRoot = path.join(root, 'plugins', 'ucp-surfaces', 'skills');

// The version stamped into metadata.version. scripts/sync-version.mjs is the
// source of truth and rewrites this in place across all generated SKILL.md
// files; keep it in sync with version.json so a fresh generate + sync is a
// no-op.
const VERSION = '0.5.2';

const HOMEPAGE = 'https://github.com/mflRevan/unity-control-protocol';
const DOCS = 'https://unityctl.dev';
const COMPAT =
  'Requires the `ucp` CLI and the UCP Bridge package in the target Unity project. Unity 2021.3+.';

/**
 * @typedef {Object} Surface
 * @property {string} name        Folder name AND frontmatter `name`.
 * @property {string} title       Human heading.
 * @property {string} capability  Capability sentence naming exact subcommands.
 * @property {string} trigger     Short "Use when <trigger>" phrase.
 * @property {string[]} examples   Realistic example command lines.
 * @property {string} whenToUse   One-line "When to use".
 * @property {string} whenNot      One-line "When NOT to use".
 */

/** @type {Surface[]} */
const surfaces = [
  {
    name: 'ucp-objects',
    title: 'UCP Objects',
    capability:
      'Create and edit GameObjects and components with `ucp object` ' +
      '(create incl. `--primitive`, get/set-property, get-fields, ' +
      'add/remove-component, reparent, instantiate, delete, set-active, set-name)',
    trigger: 'the user wants to spawn, inspect, or modify GameObjects and their components',
    examples: [
      '# A plain `object create` makes an EMPTY object (no mesh, will not render).',
      '# `--primitive` is THE way to make a visible built-in mesh from the CLI.',
      'ucp object create Crate --primitive Cube',
      'ucp object create EnemyRoot                 # empty container, no renderer',
      'ucp object add-component --id -15774 --component Rigidbody',
      'ucp object get-fields --id 46894 --component Transform',
      'ucp object set-property --id 46894 --component BoxCollider --property m_IsTrigger --value true',
      'ucp object reparent --id -15775 --parent -15774',
    ],
    whenToUse:
      'Use when creating primitives/empties, attaching or removing components, reading/writing serialized fields, reparenting, instantiating prefab assets, or toggling active/name.',
    whenNot:
      'Use `object instantiate` only for prefab assets under `Assets/`, not built-in primitives. For moving/rotating/scaling, prefer the `ucp-transform` skill.',
  },
  {
    name: 'ucp-scene',
    title: 'UCP Scene & Editor Lifecycle',
    capability:
      'Manage scenes and the editor lifecycle with `ucp scene` ' +
      '(list/load/active/save/focus/snapshot/query), `ucp editor`, and ' +
      '`ucp play|stop|pause|compile|screenshot`',
    trigger:
      'the user wants to open/save/snapshot scenes, enter or exit play mode, recompile, or screenshot the editor',
    examples: [
      'ucp scene snapshot --filter "Player"        # live instance IDs for object work',
      'ucp scene save                              # save active scene before loading another',
      'ucp scene load Assets/Scenes/Level1.unity',
      'ucp scene load Assets/Scenes/Lighting.unity --additive',
      'ucp scene focus --id 46894 --axis 1 0 0',
      'ucp play && ucp pause && ucp stop',
      'ucp compile',
    ],
    whenToUse:
      'Use to list/load/save scenes, snapshot the hierarchy for fresh instance IDs, frame the scene view, drive play/stop/pause, force a recompile, or grab an editor screenshot.',
    whenNot:
      'Treat instance IDs from `scene snapshot` as short-lived; refresh after compile, reloads, or scene loads. For object edits use `ucp-objects`; for render captures use `ucp-view`.',
  },
  {
    name: 'ucp-transform',
    title: 'UCP Transform',
    capability:
      'Author object transforms with `ucp transform` ' +
      '(move/rotate/scale/look-at/get) using Euler degrees, world|local space, and `--relative` offsets',
    trigger: 'the user wants to position, orient, or scale objects in the scene',
    examples: [
      'ucp transform move --id 1234 --to 3 0 0',
      'ucp transform move --id 1234 --to 0 1 0 --relative --space local',
      'ucp transform rotate --id 1234 --euler 0 45 0       # Euler X Y Z degrees',
      'ucp transform scale --id 1234 --uniform 2',
      'ucp transform look-at --id 1234 --target 0 0 0      # or --target-id <id>',
      'ucp transform get --id 1234 --space world',
    ],
    whenToUse:
      'Use to move/rotate/scale by absolute value or `--relative` offset, aim an object with look-at, or read a transform in world or local space.',
    whenNot:
      'Do not write transform values through raw serialized properties. For surface placement (ground/raycast) use `ucp-spatial`; for hierarchy reparenting use `ucp-objects`.',
  },
  {
    name: 'ucp-spatial',
    title: 'UCP Spatial Queries',
    capability:
      'Reason about scene geometry with `ucp spatial` ' +
      '(raycast/overlap/bounds/ground/nearest)',
    trigger:
      'the user wants to place objects on surfaces, cast rays, or query bounds and nearby objects',
    examples: [
      'ucp spatial ground --id 1234                        # drop onto the surface below',
      'ucp spatial raycast --origin 0 10 0 --direction 0 -1 0',
      'ucp spatial overlap --center 0 0 0 --radius 5',
      'ucp spatial bounds --id 1234                        # world AABB center/size/min/max',
      'ucp spatial nearest --point 0 0 0 --max 5',
    ],
    whenToUse:
      'Use to snap objects onto the surface beneath them, cast rays for hit tests, query an object world AABB, find overlapping colliders, or list nearest objects to a point.',
    whenNot:
      'Spatial queries report geometry and place via `ground`; to set an exact position/rotation/scale use `ucp-transform`.',
  },
  {
    name: 'ucp-view',
    title: 'UCP View & Capture',
    capability:
      'Render objects and the scene for vision models with `ucp view` ' +
      '(capture/isolate/orbit) and `ucp screenshot`',
    trigger:
      'the user wants a rendered image of an object or the scene to inspect 3D shape or state',
    examples: [
      'ucp view capture --target-id 1234 --max-edge 768 --output framed.png',
      'ucp view isolate --id 1234 --output hero.png        # Front/Right/Back/Top grid',
      'ucp view orbit --id 1234 --count 6 --output orbit.png',
      'ucp screenshot --view scene --output before.png',
      'ucp screenshot --view game --output game.png',
    ],
    whenToUse:
      'Use to frame a single object (`capture`), composite an orthographic grid (`isolate`), spin an object for a turntable (`orbit`), or grab a scene/game screenshot.',
    whenNot:
      'For aligning the scene view before a screenshot use `ucp scene focus` (the `ucp-scene` skill); for transforms use `ucp-transform`.',
  },
  {
    name: 'ucp-assets',
    title: 'UCP Assets & Files',
    capability:
      'Manage project assets with `ucp asset` ' +
      '(search/move/bulk-move/import-settings/reimport/info/inspect), bridge-mediated file I/O via ' +
      '`ucp files` (read/write/patch), and shader diagnostics via `ucp shader errors`',
    trigger:
      'the user wants to find/move assets, edit importer settings, reimport, or read/write project files',
    examples: [
      'ucp asset search -t Material --max 10',
      "ucp asset search -n '^SCN_[0-9]+$' --regex",
      'ucp asset move "Assets/Legacy/Enemy.prefab" "Assets/Characters/Enemy.prefab"',
      'ucp asset import-settings write "Assets/Textures/HUD.png" --field m_IsReadable --value true',
      'ucp asset reimport "Assets/Generated" --recursive',
      'ucp files write Assets/Scripts/EnemyAI.cs --content "..."   # auto-reimports',
      'ucp shader errors "Assets/Shaders/Water.shader"',
    ],
    whenToUse:
      'Use to search assets, do Unity-aware moves/bulk-moves that preserve `.meta`/GUIDs, edit importer settings instead of raw `.meta`, reimport, or do sandboxed file read/write/patch.',
    whenNot:
      'Prefer direct workspace edits + `ucp compile` when you have filesystem access; use `ucp files` as a fallback. For material property edits use `ucp-materials`; for cross-project reference lookups use `ucp-references`.',
  },
  {
    name: 'ucp-materials',
    title: 'UCP Materials',
    capability:
      'Create and edit materials with `ucp material` ' +
      '(create/get-properties/get-property/set-property/keywords/set-keyword/set-shader)',
    trigger: 'the user wants to create a material or read/write its properties, keywords, or shader',
    examples: [
      'ucp material create "Assets/Materials/Agent.mat" --shader "Standard"',
      'ucp material get-properties --path "Assets/Materials/Agent.mat"',
      'ucp material set-property --path "Assets/Materials/Agent.mat" --property _Metallic --value "0.5"',
      'ucp material set-property --path "Assets/Materials/Agent.mat" --property _Color --value "1 0 0 1"',
      'ucp material set-keyword --path "Assets/Materials/Agent.mat" --keyword _EMISSION --enabled true',
      'ucp material set-shader --path "Assets/Materials/Agent.mat" --shader "Universal Render Pipeline/Lit"',
    ],
    whenToUse:
      'Use to create a `.mat`, enumerate or read a single property, set scalar/color/vector properties, toggle shader keywords, or swap the shader.',
    whenNot:
      'For moving/renaming `.mat` files use `ucp asset move` (`ucp-assets`); to find every object using a material use `ucp references find` (`ucp-references`).',
  },
  {
    name: 'ucp-prefabs',
    title: 'UCP Prefabs',
    capability:
      'Work with prefabs via `ucp prefab` ' +
      '(status/apply/revert/unpack/create/overrides)',
    trigger:
      'the user wants to create prefabs, apply or revert overrides, unpack, or inspect prefab status',
    examples: [
      'ucp prefab create --id -15774 --path "Assets/Prefabs/EnemyRoot.prefab"',
      'ucp prefab status --id -136722',
      'ucp prefab overrides --id -136722',
      'ucp prefab apply --id -136722',
      'ucp prefab revert --id -136722',
      'ucp prefab unpack --id -136722',
    ],
    whenToUse:
      'Use to persist a scene hierarchy as a prefab (`create`), inspect/apply/revert instance overrides, or unpack a prefab instance.',
    whenNot:
      'To spawn a prefab asset into the scene use `ucp object instantiate` (`ucp-objects`). To assemble the hierarchy first, use `ucp-objects` + `ucp-transform`.',
  },
  {
    name: 'ucp-build',
    title: 'UCP Build Pipeline',
    capability:
      'Drive the build pipeline with `ucp build` ' +
      '(targets/active-target/set-target/scenes/set-scenes/start/defines/set-defines)',
    trigger:
      'the user wants to inspect or change the build target, scene list, scripting defines, or run a build',
    examples: [
      'ucp build targets',
      'ucp build active-target',
      'ucp build set-target StandaloneWindows64',
      'ucp build set-scenes "Assets/Scenes/Menu.unity;Assets/Scenes/Level1.unity"',
      'ucp build set-defines "CI;RELEASE"',
      'ucp build start --output "Builds/Game.exe"',
    ],
    whenToUse:
      'Use to list/switch build targets, read or set the scenes-in-build list, manage scripting define symbols, or kick off a player build.',
    whenNot:
      'For player/quality/physics project settings use `ucp-settings`. For package/manifest changes that affect a build use `ucp-packages`.',
  },
  {
    name: 'ucp-packages',
    title: 'UCP Packages',
    capability:
      'Manage Unity packages with `ucp packages` ' +
      '(search/add/remove/info/dependency, scoped registries, and selective `.unitypackage` import)',
    trigger:
      'the user wants to install/remove UPM packages, manage scoped registries, or selectively import a `.unitypackage`',
    examples: [
      'ucp packages search com.unity.cinemachine',
      'ucp packages add com.unity.cinemachine',
      'ucp packages info com.unity.cinemachine',
      'ucp packages dependency set com.company.tooling file:../tooling-package',
      'ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company',
      'ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage',
      'ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees',
    ],
    whenToUse:
      'Use for normal UPM add/remove, package info, manifest `file:` dependencies, scoped registry setup, and machine-friendly selective import of `.unitypackage` archives.',
    whenNot:
      'A new scoped registry may trigger Unity\'s own security popup the first time. For scripting defines or build scenes use `ucp-build`; for project settings use `ucp-settings`.',
  },
  {
    name: 'ucp-settings',
    title: 'UCP Project Settings',
    capability:
      'Read and write project settings with `ucp settings` ' +
      '(player/quality/physics/lighting/tags-layers and their set-* plus add-tag/add-layer)',
    trigger:
      'the user wants to inspect or change player, quality, physics, lighting, or tags/layers settings',
    examples: [
      'ucp settings player',
      'ucp settings set-player --key runInBackground --value true',
      'ucp settings set-quality --key shadowDistance --value 150',
      'ucp settings set-physics --key gravity --value "0 -9.81 0"',
      'ucp settings set-lighting --key fog --value true',
      'ucp settings add-tag --name Interactable',
      'ucp settings add-layer --name Water --index 8',
    ],
    whenToUse:
      'Use to read settings groups, set individual player/quality/physics/lighting keys, or add tags and layers.',
    whenNot:
      'For build target/scenes/defines use `ucp-build`. For package registries use `ucp-packages`.',
  },
  {
    name: 'ucp-profiler',
    title: 'UCP Profiler',
    capability:
      'Profile play mode with `ucp profiler` ' +
      '(status/config/session/capture/frames/hierarchy/timeline/callstacks/summary), plus `ucp profile` and `ucp frame capture`',
    trigger: 'the user wants to capture profiler frames and analyze performance, spikes, or hot paths',
    examples: [
      'ucp profiler status',
      'ucp profiler session start --mode play',
      'ucp profiler frames list --limit 1 --json     # grab a FRESH frame id',
      'ucp profiler timeline --frame <fresh-frame> --thread 0 --limit 20',
      'ucp profiler hierarchy --frame <fresh-frame> --thread 0 --limit 20',
      'ucp profiler summary --limit 10',
      'ucp profiler capture save --output ProfilerCaptures/session.json',
    ],
    whenToUse:
      'Use to start/stop a profiler session, list captured frames, drill into timeline/hierarchy/callstacks for a specific frame, get a bounded summary, or export a capture.',
    whenNot:
      'Live frame ids churn fast: pull a fresh id from `frames list` immediately before `timeline`/`hierarchy`/`callstacks`. For play/stop control use `ucp-scene`.',
  },
  {
    name: 'ucp-references',
    title: 'UCP Reference Search',
    capability:
      'Find references across the project with `ucp references` ' +
      '(find/index/check/find-strings) using native Rust indexing — read-only',
    trigger:
      'the user wants to find every place an asset, script, material, or prefab is referenced',
    examples: [
      'ucp references check                        # native indexing compatibility',
      'ucp references find --asset "Assets/Materials/Agent.mat" --detail summary',
      'ucp references find --asset 933532a4fcc9baf4fa0491de14d08ed7',
      'ucp references check Assets/Prefabs          # missing-target verification',
      'ucp references find-strings --pattern "SCN_Menu"',
      'ucp references find --asset "Assets/Prefabs/Enemy.prefab" --json --detail normal',
    ],
    whenToUse:
      'Use to locate all references to an asset by path or GUID, verify references after a move/refactor, or find string-based references Unity will not migrate automatically.',
    whenNot:
      'Native indexing needs Force Text serialization + Visible Meta Files and does not require a running editor; pass `--approach bridge` only as a fallback. Use `--detail summary` to avoid context bloat.',
  },
  {
    name: 'ucp-tests',
    title: 'UCP Tests & Editor Scripts',
    capability:
      'Run tests and named editor scripts with `ucp run-tests`, `ucp exec` (list/run), and `ucp script doctor`',
    trigger:
      'the user wants to run edit/play-mode tests, execute a named editor script, or diagnose a script',
    examples: [
      'ucp run-tests --mode edit',
      'ucp run-tests --mode edit --filter "UCP.Bridge.Tests.ControllerSmokeTests.LogsTail_ReturnsRequestedBufferedCount"',
      'ucp exec list',
      'ucp exec run SetupScene',
      'ucp script doctor',
    ],
    whenToUse:
      'Use to run the Unity Test Runner (edit/play mode, optionally filtered by fully qualified name), list/run registered editor scripts, or run the script doctor.',
    whenNot:
      'Prefer fully qualified test names when filtering and `--json` for structured consumption. For raw play/stop and compile use `ucp-scene`.',
  },
  {
    name: 'ucp-vcs',
    title: 'UCP Version Control',
    capability:
      'Access Unity Version Control (Plastic SCM) as a lightweight fallback with `ucp vcs`',
    trigger:
      'the user wants bridge-backed version-control actions and the native `cm` CLI is unavailable',
    examples: [
      'ucp vcs                                      # list available bridge VCS commands',
      'cm status                                    # prefer the native Plastic/Unity VCS CLI',
      'cm checkin -c "message"                      # prefer cm when available',
    ],
    whenToUse:
      'Use `ucp vcs` only as a lightweight fallback to discover and run bridge-backed Unity VCS commands when the native `cm` CLI is not available.',
    whenNot:
      'Prefer the native `cm` CLI for normal Unity Version Control work; reach for `ucp vcs` only when `cm` is missing.',
  },
];

function buildSkill(surface) {
  // The leading `ucp <surface>` token (e.g. `ucp spatial`) from the first
  // example command, used in the body's opening sentence.
  const firstCommand = surface.examples
    .filter((line) => line.trim().startsWith('ucp '))
    .map((line) => line.split('#')[0].trim().split(/\s+/).slice(0, 2).join(' '))[0];
  const surfaceCommand = '`' + firstCommand + '`';

  const description =
    `${surface.capability}. Use when ${surface.trigger}. ` +
    'For broad multi-surface Unity automation, use the unity-control-protocol skill instead.';

  const frontmatter = [
    '---',
    `name: ${surface.name}`,
    'description: >-',
    ...wrap(description, 2),
    `homepage: ${HOMEPAGE}`,
    `compatibility: ${COMPAT}`,
    'metadata:',
    '  author: mflRevan',
    `  version: '${VERSION}'`,
    '---',
  ].join('\n');

  const body = [
    '',
    `# ${surface.title}`,
    '',
    `Focused micro-skill for the ${surfaceCommand} command surface of the`,
    `Unity Control Protocol (\`ucp\`) CLI. Always confirm the live surface with`,
    '`ucp <cmd> --help` and see the docs at ' + DOCS + '.',
    '',
    '## Examples',
    '',
    '```bash',
    ...surface.examples,
    '```',
    '',
    '## When to use',
    '',
    surface.whenToUse,
    '',
    '## When NOT to use (use the omni skill instead)',
    '',
    surface.whenNot,
    '',
    'For broad, multi-surface Unity automation that spans several of these',
    'command groups at once, use the `unity-control-protocol` omni skill instead',
    'of this focused micro-skill.',
    '',
  ].join('\n');

  return `${frontmatter}\n${body}`;
}

// Wrap a long description onto continuation lines under a YAML `>-` block.
// Each line is indented by `indent` spaces. Keeps words intact and aims for
// ~76-char lines so the frontmatter stays readable and diff-stable.
function wrap(text, indent) {
  const pad = ' '.repeat(indent);
  const words = text.split(/\s+/);
  const lines = [];
  let current = '';
  for (const word of words) {
    const candidate = current ? `${current} ${word}` : word;
    if (candidate.length + indent > 76 && current) {
      lines.push(pad + current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current) lines.push(pad + current);
  return lines;
}

function targetPath(surface) {
  return path.join(skillsRoot, surface.name, 'SKILL.md');
}

function main() {
  const isCheck = process.argv.slice(2).includes('--check');
  const drift = [];

  for (const surface of surfaces) {
    const filePath = targetPath(surface);
    const expected = buildSkill(surface);

    if (isCheck) {
      if (!fs.existsSync(filePath)) {
        drift.push(`${surface.name} (missing)`);
        continue;
      }
      const actual = fs.readFileSync(filePath, 'utf8');
      if (normalize(actual) !== normalize(expected)) {
        drift.push(surface.name);
      }
    } else {
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      const existing = fs.existsSync(filePath) ? fs.readFileSync(filePath, 'utf8') : null;
      const next = withExistingLineEndings(expected, existing ?? expected);
      if (next !== existing) {
        fs.writeFileSync(filePath, next);
      }
    }
  }

  if (isCheck) {
    if (drift.length > 0) {
      console.error('Micro-skill files are out of sync (run scripts/generate-micro-skills.mjs):');
      for (const name of drift) console.error(`- ${name}`);
      process.exit(1);
    }
    console.log(`Micro-skills in sync: ${surfaces.length} skills under plugins/ucp-surfaces/skills.`);
  } else {
    console.log(`Generated ${surfaces.length} micro-skills under plugins/ucp-surfaces/skills.`);
  }
}

function normalize(content) {
  return content.replace(/\r\n/g, '\n');
}

function withExistingLineEndings(next, reference) {
  const newline = reference.includes('\r\n') ? '\r\n' : '\n';
  return normalize(next).replace(/\n/g, newline);
}

main();
