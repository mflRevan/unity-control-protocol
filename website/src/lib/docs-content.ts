import introMd from '@docs/getting-started/introduction.md?raw';
import installationMd from '@docs/getting-started/installation.md?raw';
import quickstartMd from '@docs/getting-started/quickstart.md?raw';
import overviewMd from '@docs/commands/overview.md?raw';
import connectionMd from '@docs/commands/connection.md?raw';
import playmodeMd from '@docs/commands/playmode.md?raw';
import scenesMd from '@docs/commands/scenes.md?raw';
import filesMd from '@docs/commands/files.md?raw';
import mediaMd from '@docs/commands/media.md?raw';
import testingMd from '@docs/commands/testing.md?raw';
import scriptingMd from '@docs/commands/scripting.md?raw';
import vcsMd from '@docs/commands/vcs.md?raw';
import objectsMd from '@docs/commands/objects.md?raw';
import assetsMd from '@docs/commands/assets.md?raw';
import materialsMd from '@docs/commands/materials.md?raw';
import prefabsMd from '@docs/commands/prefabs.md?raw';
import settingsMd from '@docs/commands/settings.md?raw';
import buildMd from '@docs/commands/build.md?raw';
import skillsMd from '@docs/agents/skills.md?raw';

// Raw SKILL.md for preview/download on skills page
export { default as skillFileRaw } from '@skills/unity-control-protocol/SKILL.md?raw';

/**
 * Maps route paths (relative to /docs/) to raw markdown content.
 * Key '' maps to the docs index (/docs).
 */
export const docsContent: Record<string, string> = {
  '': introMd,
  installation: installationMd,
  quickstart: quickstartMd,
  commands: overviewMd,
  'commands/connection': connectionMd,
  'commands/playmode': playmodeMd,
  'commands/scenes': scenesMd,
  'commands/files': filesMd,
  'commands/media': mediaMd,
  'commands/testing': testingMd,
  'commands/scripting': scriptingMd,
  'commands/vcs': vcsMd,
  'commands/objects': objectsMd,
  'commands/assets': assetsMd,
  'commands/materials': materialsMd,
  'commands/prefabs': prefabsMd,
  'commands/settings': settingsMd,
  'commands/build': buildMd,
  'agents/skills': skillsMd,
};
