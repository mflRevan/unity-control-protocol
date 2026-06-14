import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const websiteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(websiteRoot, '..');
const generatedRoot = path.join(websiteRoot, '.generated');
const publicRoot = path.join(websiteRoot, 'public');

// Canonical site origin used in generated machine-readable artifacts.
const SITE_ORIGIN = 'https://unityctl.dev';

// ---------------------------------------------------------------------------
// 1. Mirror canonical Markdown from <repo>/docs and <repo>/skills into
//    website/.generated, which Vite bundles into the SPA via ?raw imports.
// ---------------------------------------------------------------------------
syncDirectory(path.join(repoRoot, 'docs'), path.join(generatedRoot, 'docs'));
syncDirectory(path.join(repoRoot, 'skills'), path.join(generatedRoot, 'skills'));

// Note: the agent-surface generator is invoked at the bottom of this file,
// after `docsNavigation` (a `const`, so not hoisted) is declared.

// ===========================================================================
// Directory sync helpers
// ===========================================================================

function syncDirectory(sourceDir, destinationDir) {
  if (!fs.existsSync(sourceDir)) {
    if (!fs.existsSync(destinationDir)) {
      throw new Error(`Missing source content at ${sourceDir} and no bundled fallback at ${destinationDir}`);
    }
    return;
  }

  fs.rmSync(destinationDir, { recursive: true, force: true });
  copyRecursive(sourceDir, destinationDir);
}

function copyRecursive(source, destination) {
  const stats = fs.statSync(source);
  if (stats.isDirectory()) {
    if (path.basename(source) === 'qa' && path.basename(path.dirname(source)) === 'docs') {
      return;
    }

    fs.mkdirSync(destination, { recursive: true });
    for (const entry of fs.readdirSync(source)) {
      copyRecursive(path.join(source, entry), path.join(destination, entry));
    }
    return;
  }

  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
}

// ===========================================================================
// Agent surface (machine-readable docs)
// ===========================================================================

/**
 * Navigation model. This MUST stay in sync with `src/lib/docs-content.ts`
 * (`docsNavigation` + the content-key map). Each item ties together:
 *   - title:  human label shown in the SPA nav.
 *   - route:  the human URL path under the site origin (e.g. /docs/authoring/scenes).
 *             '' is the docs index (/docs).
 *   - source: the .md file under .generated/docs that backs the route.
 *
 * Driving every generated artifact (mirrors, llms.txt, sitemap) from this one
 * table means the agent surface cannot drift from the rendered site: adding a
 * page in docs-content.ts means adding one row here.
 *
 * Note that the Getting Started + CLI Overview routes are flattened in the SPA
 * (e.g. /docs -> getting-started/introduction.md), so route != source there;
 * the rest mirror their source path 1:1.
 */
const docsNavigation = [
  {
    title: 'Getting Started',
    items: [
      { title: 'Introduction', route: '', source: 'getting-started/introduction.md' },
      { title: 'Installation', route: 'installation', source: 'getting-started/installation.md' },
      { title: 'Quick Start', route: 'quickstart', source: 'getting-started/quickstart.md' },
    ],
  },
  {
    title: 'Overview',
    items: [
      { title: 'CLI Overview', route: 'overview', source: 'overview/overview.md' },
      { title: 'Project Setup & Bridge', route: 'overview/project-setup', source: 'overview/project-setup.md' },
      { title: 'Editor Lifecycle', route: 'overview/editor-lifecycle', source: 'overview/editor-lifecycle.md' },
    ],
  },
  {
    title: 'Authoring',
    items: [
      { title: 'Scenes', route: 'authoring/scenes', source: 'authoring/scenes.md' },
      { title: 'Objects & Components', route: 'authoring/objects', source: 'authoring/objects.md' },
      { title: 'Prefabs', route: 'authoring/prefabs', source: 'authoring/prefabs.md' },
      { title: 'Assets', route: 'authoring/assets', source: 'authoring/assets.md' },
      { title: 'Materials', route: 'authoring/materials', source: 'authoring/materials.md' },
      { title: 'Reference Search', route: 'authoring/references', source: 'authoring/references.md' },
      { title: 'Files', route: 'authoring/files', source: 'authoring/files.md' },
      { title: 'Scripting', route: 'authoring/scripting', source: 'authoring/scripting.md' },
    ],
  },
  {
    title: 'Runtime & Diagnostics',
    items: [
      { title: 'Play Mode & Compilation', route: 'runtime/play-mode', source: 'runtime/play-mode.md' },
      { title: 'Screenshots & Logs', route: 'runtime/logs-and-media', source: 'runtime/logs-and-media.md' },
      { title: 'Testing', route: 'runtime/testing', source: 'runtime/testing.md' },
      { title: 'Profiler', route: 'runtime/profiler', source: 'runtime/profiler.md' },
    ],
  },
  {
    title: 'Project Operations',
    items: [
      { title: 'Packages', route: 'project/packages', source: 'project/packages.md' },
      { title: 'Settings', route: 'project/settings', source: 'project/settings.md' },
      { title: 'Build Pipeline', route: 'project/build', source: 'project/build.md' },
      { title: 'Version Control', route: 'project/version-control', source: 'project/version-control.md' },
    ],
  },
  {
    title: 'Agents',
    items: [{ title: 'Skills', route: 'agents/skills', source: 'agents/skills.md' }],
  },
];

function generateAgentSurface() {
  const docsSourceRoot = path.join(generatedRoot, 'docs');
  const publicDocsRoot = path.join(publicRoot, 'docs');

  // Start from a clean public/docs so deleted pages don't linger as stale mirrors.
  fs.rmSync(publicDocsRoot, { recursive: true, force: true });

  // Public .md path for a given route. '' (the docs index) becomes
  // /docs/index.md so it has a clean, fetchable extension URL.
  const publicMdPath = (route) => `docs/${route === '' ? 'index' : route}.md`;
  // Public URL an agent fetches for the raw Markdown of a route.
  const mdUrl = (route) => `${SITE_ORIGIN}/${publicMdPath(route)}`;
  // Human route URL (the SPA page).
  const routeUrl = (route) => `${SITE_ORIGIN}/docs${route === '' ? '' : `/${route}`}`;

  // --- 1. Per-page Markdown mirrors -------------------------------------
  // Copy each backing .md (frontmatter stripped) to its route-aligned path so
  // GET /docs/<route>.md returns clean raw prose.
  for (const group of docsNavigation) {
    for (const item of group.items) {
      const sourcePath = path.join(docsSourceRoot, item.source);
      if (!fs.existsSync(sourcePath)) {
        throw new Error(`docsNavigation references missing source: ${item.source}`);
      }
      const markdown = stripFrontmatter(fs.readFileSync(sourcePath, 'utf8'));
      const destPath = path.join(publicRoot, publicMdPath(item.route));
      fs.mkdirSync(path.dirname(destPath), { recursive: true });
      fs.writeFileSync(destPath, ensureTrailingNewline(markdown));
    }
  }

  // --- 2. llms.txt (https://llmstxt.org convention) ---------------------
  const llmsLines = [
    '# Unity Control Protocol (UCP)',
    '',
    '> A cross-platform CLI and Unity Editor bridge for programmatic control of Unity projects over a WebSocket/JSON-RPC 2.0 connection.',
    '',
  ];
  for (const group of docsNavigation) {
    llmsLines.push(`## ${group.title}`, '');
    for (const item of group.items) {
      const sourcePath = path.join(docsSourceRoot, item.source);
      const summary = firstProseLine(fs.readFileSync(sourcePath, 'utf8'));
      const suffix = summary ? `: ${summary}` : '';
      llmsLines.push(`- [${item.title}](${mdUrl(item.route)})${suffix}`);
    }
    llmsLines.push('');
  }
  fs.writeFileSync(path.join(publicRoot, 'llms.txt'), ensureTrailingNewline(llmsLines.join('\n')));

  // --- 3. robots.txt ----------------------------------------------------
  const robots = ['User-agent: *', 'Allow: /', '', `Sitemap: ${SITE_ORIGIN}/sitemap.xml`].join('\n');
  fs.writeFileSync(path.join(publicRoot, 'robots.txt'), ensureTrailingNewline(robots));

  // --- 4. sitemap.xml (human routes) ------------------------------------
  const urls = [`${SITE_ORIGIN}/`];
  for (const group of docsNavigation) {
    for (const item of group.items) {
      urls.push(routeUrl(item.route));
    }
  }
  const sitemap = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ...urls.map((loc) => `  <url>\n    <loc>${escapeXml(loc)}</loc>\n  </url>`),
    '</urlset>',
  ].join('\n');
  fs.writeFileSync(path.join(publicRoot, 'sitemap.xml'), ensureTrailingNewline(sitemap));

  const pageCount = docsNavigation.reduce((n, g) => n + g.items.length, 0);
  console.log(
    `Generated agent surface: ${pageCount} Markdown mirrors under public/docs, llms.txt, robots.txt, sitemap.xml`,
  );
}

// ===========================================================================
// Markdown helpers
// ===========================================================================

/** Remove a leading YAML frontmatter block (--- ... ---) if present. */
function stripFrontmatter(markdown) {
  if (!markdown.startsWith('---')) return markdown;
  const match = markdown.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n?/);
  return match ? markdown.slice(match[0].length).replace(/^\s*\n/, '') : markdown;
}

/**
 * First meaningful prose line for a doc summary: the first non-empty,
 * non-heading line; falls back to the first heading's text if the doc is
 * heading-only. Returns '' when nothing usable is found.
 */
function firstProseLine(markdown) {
  const body = stripFrontmatter(markdown);
  const lines = body.split(/\r?\n/);
  let firstHeading = '';
  let inFence = false;
  for (const raw of lines) {
    const line = raw.trim();
    if (!line) continue;
    // Track fenced code blocks and skip their entire body, not just the fences.
    if (line.startsWith('```') || line.startsWith('~~~')) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    if (line.startsWith('#')) {
      if (!firstHeading) firstHeading = line.replace(/^#+\s*/, '').trim();
      continue;
    }
    // Skip blockquote markers so the summary stays clean prose.
    if (line.startsWith('>')) continue;
    return collapseInline(line);
  }
  return collapseInline(firstHeading);
}

/** Strip basic inline Markdown so summaries read as plain text. */
function collapseInline(text) {
  return text
    .replace(/\*\*(.+?)\*\*/g, '$1') // bold
    .replace(/\*(.+?)\*/g, '$1') // italic
    .replace(/`(.+?)`/g, '$1') // inline code
    .replace(/\[(.+?)\]\((.+?)\)/g, '$1') // links -> link text
    .trim();
}

function ensureTrailingNewline(text) {
  return text.endsWith('\n') ? text : `${text}\n`;
}

function escapeXml(value) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

// ---------------------------------------------------------------------------
// Generate the machine-readable surface for AI agents (per-page .md mirrors,
// llms.txt, robots.txt, sitemap.xml) under website/public so a plain HTTP GET
// returns prose, not an empty SPA shell. Invoked here, after `docsNavigation`
// is initialized.
// ---------------------------------------------------------------------------
generateAgentSurface();
