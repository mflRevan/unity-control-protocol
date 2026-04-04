import introMd from '@docs/getting-started/introduction.md?raw';
import installationMd from '@docs/getting-started/installation.md?raw';
import quickstartMd from '@docs/getting-started/quickstart.md?raw';
import overviewMd from '@docs/overview/overview.md?raw';
import projectSetupMd from '@docs/overview/project-setup.md?raw';
import editorLifecycleMd from '@docs/overview/editor-lifecycle.md?raw';
import scenesMd from '@docs/authoring/scenes.md?raw';
import objectsMd from '@docs/authoring/objects.md?raw';
import prefabsMd from '@docs/authoring/prefabs.md?raw';
import assetsMd from '@docs/authoring/assets.md?raw';
import materialsMd from '@docs/authoring/materials.md?raw';
import referencesMd from '@docs/authoring/references.md?raw';
import filesMd from '@docs/authoring/files.md?raw';
import scriptingMd from '@docs/authoring/scripting.md?raw';
import playModeMd from '@docs/runtime/play-mode.md?raw';
import logsAndMediaMd from '@docs/runtime/logs-and-media.md?raw';
import testingMd from '@docs/runtime/testing.md?raw';
import profilerMd from '@docs/runtime/profiler.md?raw';
import packagesMd from '@docs/project/packages.md?raw';
import settingsMd from '@docs/project/settings.md?raw';
import buildMd from '@docs/project/build.md?raw';
import versionControlMd from '@docs/project/version-control.md?raw';
import skillsMd from '@docs/agents/skills.md?raw';

// Raw SKILL.md for preview/download on skills page
export { default as skillFileRaw } from '@skills/unity-control-protocol/SKILL.md?raw';

export interface DocsNavItem {
  title: string;
  href: string;
}

export interface DocsNavGroup {
  title: string;
  items: DocsNavItem[];
}

export const docsNavigation: DocsNavGroup[] = [
  {
    title: 'Getting Started',
    items: [
      { title: 'Introduction', href: '/docs' },
      { title: 'Installation', href: '/docs/installation' },
      { title: 'Quick Start', href: '/docs/quickstart' },
    ],
  },
  {
    title: 'Overview',
    items: [
      { title: 'CLI Overview', href: '/docs/overview' },
      { title: 'Project Setup & Bridge', href: '/docs/overview/project-setup' },
      { title: 'Editor Lifecycle', href: '/docs/overview/editor-lifecycle' },
    ],
  },
  {
    title: 'Authoring',
    items: [
      { title: 'Scenes', href: '/docs/authoring/scenes' },
      { title: 'Objects & Components', href: '/docs/authoring/objects' },
      { title: 'Prefabs', href: '/docs/authoring/prefabs' },
      { title: 'Assets', href: '/docs/authoring/assets' },
      { title: 'Materials', href: '/docs/authoring/materials' },
      { title: 'Reference Search', href: '/docs/authoring/references' },
      { title: 'Files', href: '/docs/authoring/files' },
      { title: 'Scripting', href: '/docs/authoring/scripting' },
    ],
  },
  {
    title: 'Runtime & Diagnostics',
    items: [
      { title: 'Play Mode & Compilation', href: '/docs/runtime/play-mode' },
      { title: 'Screenshots & Logs', href: '/docs/runtime/logs-and-media' },
      { title: 'Testing', href: '/docs/runtime/testing' },
      { title: 'Profiler', href: '/docs/runtime/profiler' },
    ],
  },
  {
    title: 'Project Operations',
    items: [
      { title: 'Packages', href: '/docs/project/packages' },
      { title: 'Settings', href: '/docs/project/settings' },
      { title: 'Build Pipeline', href: '/docs/project/build' },
      { title: 'Version Control', href: '/docs/project/version-control' },
    ],
  },
  {
    title: 'Agents',
    items: [{ title: 'Skills', href: '/docs/agents/skills' }],
  },
];

const canonicalDocsContent: Record<string, string> = {
  '': introMd,
  installation: installationMd,
  quickstart: quickstartMd,
  overview: overviewMd,
  'overview/project-setup': projectSetupMd,
  'overview/editor-lifecycle': editorLifecycleMd,
  'authoring/scenes': scenesMd,
  'authoring/objects': objectsMd,
  'authoring/prefabs': prefabsMd,
  'authoring/assets': assetsMd,
  'authoring/materials': materialsMd,
  'authoring/references': referencesMd,
  'authoring/files': filesMd,
  'authoring/scripting': scriptingMd,
  'runtime/play-mode': playModeMd,
  'runtime/logs-and-media': logsAndMediaMd,
  'runtime/testing': testingMd,
  'runtime/profiler': profilerMd,
  'project/packages': packagesMd,
  'project/settings': settingsMd,
  'project/build': buildMd,
  'project/version-control': versionControlMd,
  'agents/skills': skillsMd,
};

const legacyAliases: Record<string, string> = {
  commands: overviewMd,
  'commands/overview': overviewMd,
  'commands/connection': projectSetupMd,
  'commands/editor': editorLifecycleMd,
  'commands/scenes': scenesMd,
  'commands/objects': objectsMd,
  'commands/prefabs': prefabsMd,
  'commands/assets': assetsMd,
  'commands/materials': materialsMd,
  'commands/references': referencesMd,
  'commands/files': filesMd,
  'commands/scripting': scriptingMd,
  'commands/playmode': playModeMd,
  'commands/media': logsAndMediaMd,
  'commands/testing': testingMd,
  'commands/profiler': profilerMd,
  'commands/packages': packagesMd,
  'commands/settings': settingsMd,
  'commands/build': buildMd,
  'commands/vcs': versionControlMd,
};

/**
 * Maps route paths (relative to /docs/) to raw markdown content.
 * Key '' maps to the docs index (/docs).
 */
export const docsContent: Record<string, string> = {
  ...canonicalDocsContent,
  ...legacyAliases,
};
