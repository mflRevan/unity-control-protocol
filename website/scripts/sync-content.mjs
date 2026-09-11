// Builds every artifact the website serves from the ground truth in the repository:
//
//   <repo>/docs/**/*.md            human documentation
//   <repo>/skills/*/SKILL.md       agent skills (Agent Skills spec)
//   <repo>/skills/index.json       catalog written by scripts/sync-skills.mjs
//   <repo>/version.json            release version
//
// Outputs:
//
//   website/.generated/content.json   navigation + page metadata the SPA imports (small)
//   website/public/docs/<route>.md    raw page Markdown, fetched by the SPA and by agents
//   website/public/skills/<name>.md   raw SKILL.md, frontmatter included (a valid install)
//   website/public/skills/index.json  the catalog
//   website/public/skills/index.md    the catalog as Markdown
//   website/public/index.md           the site front page as Markdown
//   website/public/llms.txt           llmstxt.org index of every page and skill
//   website/public/llms-full.txt      every page and skill in one file
//   website/public/sitemap.xml, robots.txt
//
// The navigation table below is the single source of truth for which docs exist and where
// they live; the SPA, the mirrors, llms.txt, and the sitemap all derive from it.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import GithubSlugger from 'github-slugger';

const websiteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(websiteRoot, '..');
const generatedRoot = path.join(websiteRoot, '.generated');
const publicRoot = path.join(websiteRoot, 'public');
const docsRoot = path.join(repoRoot, 'docs');
const skillsRoot = path.join(repoRoot, 'skills');

const SITE = 'https://unityctl.dev';
const REPO = 'https://github.com/mflRevan/unity-control-protocol';
const { version } = JSON.parse(fs.readFileSync(path.join(repoRoot, 'version.json'), 'utf8'));

// ------------------------------------------------------------------ navigation

const docsNavigation = [
  {
    title: 'Getting started',
    items: [
      { title: 'Introduction', route: '', source: 'getting-started/introduction.md' },
      { title: 'Installation', route: 'installation', source: 'getting-started/installation.md' },
      { title: 'Quick start', route: 'quickstart', source: 'getting-started/quickstart.md' },
    ],
  },
  {
    title: 'Overview',
    items: [
      { title: 'CLI overview', route: 'overview', source: 'overview/overview.md' },
      { title: 'Project setup and bridge', route: 'overview/project-setup', source: 'overview/project-setup.md' },
      { title: 'Editor lifecycle', route: 'overview/editor-lifecycle', source: 'overview/editor-lifecycle.md' },
    ],
  },
  {
    title: 'Authoring',
    items: [
      { title: 'Scenes', route: 'authoring/scenes', source: 'authoring/scenes.md' },
      { title: 'Objects and components', route: 'authoring/objects', source: 'authoring/objects.md' },
      { title: 'Prefabs', route: 'authoring/prefabs', source: 'authoring/prefabs.md' },
      { title: 'Assets', route: 'authoring/assets', source: 'authoring/assets.md' },
      { title: 'UI Toolkit', route: 'authoring/ui-toolkit', source: 'authoring/ui-toolkit.md' },
      { title: 'Materials', route: 'authoring/materials', source: 'authoring/materials.md' },
      { title: 'Reference search', route: 'authoring/references', source: 'authoring/references.md' },
      { title: 'Files', route: 'authoring/files', source: 'authoring/files.md' },
      { title: 'Scripting', route: 'authoring/scripting', source: 'authoring/scripting.md' },
    ],
  },
  {
    title: 'Runtime and diagnostics',
    items: [
      { title: 'Play mode and compilation', route: 'runtime/play-mode', source: 'runtime/play-mode.md' },
      { title: 'Screenshots, recordings and logs', route: 'runtime/logs-and-media', source: 'runtime/logs-and-media.md' },
      { title: 'Editor state and dialogs', route: 'runtime/editor-state', source: 'runtime/editor-state.md' },
      { title: 'Testing', route: 'runtime/testing', source: 'runtime/testing.md' },
      { title: 'Profiler', route: 'runtime/profiler', source: 'runtime/profiler.md' },
    ],
  },
  {
    title: 'Project operations',
    items: [
      { title: 'Packages', route: 'project/packages', source: 'project/packages.md' },
      { title: 'Settings', route: 'project/settings', source: 'project/settings.md' },
      { title: 'Build pipeline', route: 'project/build', source: 'project/build.md' },
      { title: 'Version control', route: 'project/version-control', source: 'project/version-control.md' },
    ],
  },
  {
    title: 'Agents',
    items: [{ title: 'Skills', route: 'agents/skills', source: 'agents/skills.md' }],
  },
];

const docPath = (route) => (route === '' ? '/docs' : `/docs/${route}`);
const docMdPath = (route) => (route === '' ? '/docs/index.md' : `/docs/${route}.md`);

// --------------------------------------------------------------------- docs

const pages = [];
for (const group of docsNavigation) {
  for (const item of group.items) {
    const file = path.join(docsRoot, item.source);
    if (!fs.existsSync(file)) throw new Error(`docsNavigation references a missing source: ${item.source}`);
    const markdown = stripFrontmatter(fs.readFileSync(file, 'utf8'));
    pages.push({
      ...item,
      group: group.title,
      path: docPath(item.route),
      md: docMdPath(item.route),
      github: `${REPO}/blob/main/docs/${item.source}`,
      description: firstProseLine(markdown),
      headings: collectHeadings(markdown),
      markdown,
    });
  }
}

const routeToMd = new Map(pages.map((page) => [page.path, `${SITE}${page.md}`]));

// Raw mirrors: same prose, with in-site links pointed at the Markdown twin so an agent that
// follows a link lands on Markdown again rather than the SPA shell.
fs.rmSync(path.join(publicRoot, 'docs'), { recursive: true, force: true });
for (const page of pages) {
  writeFile(path.join(publicRoot, page.md), agentMarkdown(page.markdown));
}

function agentMarkdown(markdown) {
  return ensureTrailingNewline(
    markdown.replace(/\]\((\/docs[^)#\s]*)(#[^)\s]*)?\)/g, (whole, route, hash) => {
      const md = routeToMd.get(route);
      return md ? `](${md}${hash ?? ''})` : whole;
    }),
  );
}

// ------------------------------------------------------------------- skills

const catalogFile = path.join(skillsRoot, 'index.json');
if (!fs.existsSync(catalogFile)) throw new Error('skills/index.json is missing; run `node scripts/sync-skills.mjs` in the repository root');
const catalog = JSON.parse(fs.readFileSync(catalogFile, 'utf8'));
if (catalog.version !== version) {
  throw new Error(`skills/index.json is at ${catalog.version} but version.json is ${version}; run \`node scripts/sync-skills.mjs\``);
}

fs.rmSync(path.join(publicRoot, 'skills'), { recursive: true, force: true });
const skills = catalog.skills.map((skill) => {
  const raw = fs.readFileSync(path.join(repoRoot, skill.source), 'utf8');
  const body = stripFrontmatter(raw);
  writeFile(path.join(publicRoot, 'skills', `${skill.name}.md`), ensureTrailingNewline(raw));
  return {
    ...skill,
    path: `/skills/${skill.name}`,
    md: `/skills/${skill.name}.md`,
    github: `${REPO}/blob/main/${skill.source}`,
    headings: collectHeadings(body),
    body,
  };
});
writeFile(path.join(publicRoot, 'skills', 'index.json'), `${JSON.stringify(catalog, null, 2)}\n`);

const skillsIndexMd = [
  '# Unity Control Protocol agent skills',
  '',
  `Version ${version}. Each skill follows the Agent Skills specification (https://agentskills.io/specification).`,
  'Download the raw URL into `<skills dir>/<name>/SKILL.md` to install by hand.',
  '',
  '| skill | kind | commands | raw |',
  '|---|---|---|---|',
  ...skills.map(
    (s) => `| ${s.name} | ${s.kind} | ${s.commands.map((c) => `\`${c}\``).join(', ')} | ${SITE}${s.md} |`,
  ),
  '',
  '## Install',
  '',
  '```text',
  catalog.install.claudeCode.marketplace,
  catalog.install.claudeCode.omni,
  catalog.install.claudeCode.surfaces,
  '```',
  '',
  '```bash',
  catalog.install.skillsSh,
  catalog.install.githubCli,
  catalog.install.manual,
  '```',
  '',
  ...skills.flatMap((s) => [`## ${s.name}`, '', s.description, '', `- page: ${SITE}${s.path}`, `- raw: ${SITE}${s.md}`, '']),
];
writeFile(path.join(publicRoot, 'skills', 'index.md'), ensureTrailingNewline(skillsIndexMd.join('\n')));

// ------------------------------------------------------------ agent indexes

const tagline =
  'A Rust CLI and an in-editor bridge that expose the Unity Editor as commands: scenes, objects, assets, materials, prefabs, UI Toolkit, play mode, tests, profiler, builds, screenshots and video. Built for humans and AI agents.';

const llms = [
  '# Unity Control Protocol (UCP)',
  '',
  `> ${tagline}`,
  '',
  `Version ${version}. Install the CLI with \`npm install -g @mflrevan/ucp\`, then \`ucp install\` inside a Unity project and \`ucp open\`.`,
  `Every page below is also served as raw Markdown; the full documentation in one file is ${SITE}/llms-full.txt.`,
  '',
];
for (const group of docsNavigation) {
  llms.push(`## ${group.title}`, '');
  for (const item of group.items) {
    const page = pages.find((p) => p.route === item.route);
    llms.push(`- [${page.title}](${SITE}${page.md})${page.description ? `: ${page.description}` : ''}`);
  }
  llms.push('');
}
llms.push('## Skills', '', `- [Skill catalog](${SITE}/skills/index.md): every skill with install commands (JSON: ${SITE}/skills/index.json)`);
for (const skill of skills) {
  llms.push(`- [${skill.name}](${SITE}${skill.md}): ${truncate(skill.description, 220)}`);
}
llms.push('', '## Optional', '', `- [README](${REPO}#readme)`, `- [Changelog](${REPO}/blob/main/CHANGELOG.md)`, `- [Releases](${REPO}/releases)`);
writeFile(path.join(publicRoot, 'llms.txt'), ensureTrailingNewline(llms.join('\n')));

const full = [`# Unity Control Protocol (UCP) ${version}`, '', tagline, '', `Canonical index: ${SITE}/llms.txt`, ''];
for (const page of pages) {
  full.push('', '---', '', `<!-- ${SITE}${page.md} -->`, '', demoteHeadings(agentMarkdown(page.markdown)).trim(), '');
}
full.push('', '---', '', '# Skills', '');
for (const skill of skills) {
  full.push('', '---', '', `<!-- ${SITE}${skill.md} -->`, '', demoteHeadings(skill.body).trim(), '');
}
writeFile(path.join(publicRoot, 'llms-full.txt'), ensureTrailingNewline(full.join('\n')));

const indexMd = [
  '# Unity Control Protocol',
  '',
  tagline,
  '',
  '```bash',
  'npm install -g @mflrevan/ucp',
  'cd path/to/UnityProject',
  'ucp install      # adds the com.ucp.bridge package to the project',
  'ucp open         # launches the editor and waits for the bridge',
  'ucp scene list',
  '```',
  '',
  '## For agents',
  '',
  `- ${SITE}/llms.txt: index of every documentation page and skill`,
  `- ${SITE}/llms-full.txt: the entire documentation in one file`,
  `- ${SITE}/docs/<page>.md: raw Markdown of any documentation page`,
  `- ${SITE}/skills/index.md and ${SITE}/skills/index.json: the skill catalog`,
  `- ${SITE}/skills/<name>.md: any skill, ready to save as SKILL.md`,
  '',
  '## Documentation',
  '',
  ...pages.map((p) => `- [${p.title}](${SITE}${p.md})`),
  '',
  '## Links',
  '',
  `- Repository: ${REPO}`,
  '- npm: https://www.npmjs.com/package/@mflrevan/ucp',
  '- Discord: https://discord.gg/F4RjhdVTbz',
];
writeFile(path.join(publicRoot, 'index.md'), ensureTrailingNewline(indexMd.join('\n')));

// ----------------------------------------------------------- sitemap/robots

const urls = [`${SITE}/`, `${SITE}/skills`, ...pages.map((p) => `${SITE}${p.path}`), ...skills.map((s) => `${SITE}${s.path}`)];
const sitemap = [
  '<?xml version="1.0" encoding="UTF-8"?>',
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
  ...urls.map((loc) => `  <url>\n    <loc>${escapeXml(loc)}</loc>\n  </url>`),
  '</urlset>',
].join('\n');
writeFile(path.join(publicRoot, 'sitemap.xml'), ensureTrailingNewline(sitemap));
writeFile(path.join(publicRoot, 'robots.txt'), ['User-agent: *', 'Allow: /', '', `Sitemap: ${SITE}/sitemap.xml`, ''].join('\n'));

// ------------------------------------------------------------ SPA manifest

const manifest = {
  version,
  site: SITE,
  repo: REPO,
  nav: docsNavigation.map((group) => ({
    title: group.title,
    items: group.items.map((item) => {
      const { markdown, group: _group, source: _source, ...page } = pages.find((p) => p.route === item.route);
      return page;
    }),
  })),
  skills: skills.map(({ body, ...skill }) => skill),
  install: catalog.install,
};
fs.rmSync(generatedRoot, { recursive: true, force: true });
writeFile(path.join(generatedRoot, 'content.json'), `${JSON.stringify(manifest, null, 2)}\n`);

console.log(
  `Content synced at ${version}: ${pages.length} docs, ${skills.length} skills, llms.txt, llms-full.txt, index.md, sitemap.xml.`,
);

// ------------------------------------------------------------------ helpers

function stripFrontmatter(markdown) {
  const match = /^---\r?\n[\s\S]*?\r?\n---\r?\n?/.exec(markdown);
  return match ? markdown.slice(match[0].length).replace(/^\s*\n/, '') : markdown;
}

function collectHeadings(markdown) {
  const slugger = new GithubSlugger();
  const headings = [];
  let inFence = false;
  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trimEnd();
    if (/^(```|~~~)/.test(line.trim())) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    const match = /^(#{1,3})\s+(.+?)\s*#*$/.exec(line);
    if (!match) continue;
    const text = collapseInline(match[2]);
    headings.push({ depth: match[1].length, text, id: slugger.slug(text) });
  }
  return headings;
}

function demoteHeadings(markdown) {
  let inFence = false;
  return markdown
    .split(/\r?\n/)
    .map((line) => {
      if (/^(```|~~~)/.test(line.trim())) inFence = !inFence;
      if (inFence) return line;
      return /^#{1,5}\s/.test(line) ? `#${line}` : line;
    })
    .join('\n');
}

function firstProseLine(markdown) {
  let inFence = false;
  let firstHeading = '';
  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    if (line.startsWith('```') || line.startsWith('~~~')) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    if (line.startsWith('#')) {
      if (!firstHeading) firstHeading = line.replace(/^#+\s*/, '').trim();
      continue;
    }
    if (line.startsWith('>') || line.startsWith('|') || line.startsWith('<') || line.startsWith('-')) continue;
    return collapseInline(line);
  }
  return collapseInline(firstHeading);
}

function collapseInline(text) {
  return text
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/\*(.+?)\*/g, '$1')
    .replace(/`(.+?)`/g, '$1')
    .replace(/\[(.+?)\]\((.+?)\)/g, '$1')
    .trim();
}

function truncate(text, max) {
  return text.length <= max ? text : `${text.slice(0, max - 1).trimEnd()}…`;
}

function ensureTrailingNewline(text) {
  return text.endsWith('\n') ? text : `${text}\n`;
}

function escapeXml(value) {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
}

function writeFile(file, content) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content);
}
