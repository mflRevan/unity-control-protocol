#!/usr/bin/env node
// Keeps every consumer of the agent skills in lockstep with the ground truth under `skills/`.
//
// Ground truth: `skills/<name>/SKILL.md`, hand-written, Agent Skills spec frontmatter
// (https://agentskills.io/specification). Everything else is derived:
//
//   plugins/ucp-surfaces/skills/<name>/SKILL.md   Claude Code plugin with the focused surface skills
//   plugins/ucp-surfaces/.claude-plugin/plugin.json version stamped from version.json
//   skills/index.json                              catalog for the website, llms.txt, and agents
//
// Usage: node scripts/sync-skills.mjs [--check]
//   --check  exit 1 when any derived file or frontmatter version is stale (CI / release gate)

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const skillsRoot = path.join(root, 'skills');
const pluginRoot = path.join(root, 'plugins', 'ucp-surfaces');
const pluginSkillsRoot = path.join(pluginRoot, 'skills');
const pluginManifestPath = path.join(pluginRoot, '.claude-plugin', 'plugin.json');
const catalogPath = path.join(skillsRoot, 'index.json');
const OMNI = 'unity-control-protocol';
const SITE = 'https://unityctl.dev';

const isCheck = process.argv.includes('--check');
const { version } = JSON.parse(fs.readFileSync(path.join(root, 'version.json'), 'utf8'));

// ---------------------------------------------------------------- frontmatter

function parseFrontmatter(markdown, file) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/.exec(markdown);
  if (!match) throw new Error(`${file}: missing frontmatter block`);
  const [, block, body] = match;
  const data = {};
  let current = null;
  let folded = null;
  for (const raw of block.split(/\r?\n/)) {
    if (folded && /^ {2}\S/.test(raw)) {
      data[folded] = (data[folded] ? data[folded] + ' ' : '') + raw.trim();
      continue;
    }
    folded = null;
    const nested = /^ {2}([A-Za-z0-9_-]+): (.*)$/.exec(raw);
    if (nested && current) {
      data[current][nested[1]] = stripQuotes(nested[2]);
      continue;
    }
    const top = /^([A-Za-z0-9_-]+):(?: (.*))?$/.exec(raw);
    if (!top) throw new Error(`${file}: unparseable frontmatter line: ${raw}`);
    const [, key, value] = top;
    if (value === undefined || value === '') {
      data[key] = {};
      current = key;
    } else if (value === '>-' || value === '>') {
      data[key] = '';
      folded = key;
      current = null;
    } else {
      data[key] = stripQuotes(value);
      current = null;
    }
  }
  return { data, body };
}

function stripQuotes(value) {
  const trimmed = value.trim();
  if (/^'.*'$/.test(trimmed) || /^".*"$/.test(trimmed)) return trimmed.slice(1, -1);
  return trimmed;
}

const ALLOWED_TOP = new Set(['name', 'description', 'license', 'compatibility', 'metadata', 'allowed-tools']);

function validate(name, data, file) {
  const problems = [];
  if (data.name !== name) problems.push(`name '${data.name}' must equal directory name '${name}'`);
  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(name) || name.length > 64) problems.push('name must be lowercase kebab-case, at most 64 chars');
  if (!data.description || data.description.length > 1024) problems.push('description is required and at most 1024 chars');
  if (data.compatibility && data.compatibility.length > 500) problems.push('compatibility is at most 500 chars');
  for (const key of Object.keys(data)) {
    if (!ALLOWED_TOP.has(key)) problems.push(`unknown frontmatter key '${key}' (spec keys: ${[...ALLOWED_TOP].join(', ')})`);
  }
  if (typeof data.metadata !== 'object') problems.push('metadata block with author/version/homepage is required');
  if (problems.length) throw new Error(`${file}:\n  - ${problems.join('\n  - ')}`);
}

// ------------------------------------------------------------------- collect

const entries = fs
  .readdirSync(skillsRoot, { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => entry.name)
  .sort();

const stale = [];
const skills = [];

for (const name of entries) {
  const file = path.join(skillsRoot, name, 'SKILL.md');
  if (!fs.existsSync(file)) throw new Error(`${file}: every directory under skills/ must contain SKILL.md`);
  let markdown = fs.readFileSync(file, 'utf8');
  const { data, body } = parseFrontmatter(markdown, file);
  validate(name, data, file);

  // Stamp the release version into the source of truth.
  const stamped = markdown.replace(/^( {2}version: )'.*'$/m, `$1'${version}'`);
  if (stamped !== markdown) {
    if (isCheck) stale.push(path.relative(root, file));
    else fs.writeFileSync(file, stamped);
    markdown = stamped;
  }

  const title = (/^# (.+)$/m.exec(body) || [])[1] || name;
  // Top-level ucp commands the skill teaches: fenced example lines plus inline code.
  const commands = [...body.matchAll(/(?:^|`)ucp ([a-z][a-z-]*)(?=[\s`])/gm)]
    .map((m) => m[1])
    .filter((c) => c !== 'help')
    .filter((c, i, all) => all.indexOf(c) === i)
    .sort();
  skills.push({
    name,
    title,
    description: data.description,
    compatibility: data.compatibility || '',
    version,
    kind: name === OMNI ? 'omni' : 'surface',
    source: `skills/${name}/SKILL.md`,
    raw: `${SITE}/skills/${name}.md`,
    page: `${SITE}/skills/${name}`,
    commands,
    markdown,
  });
}

// ------------------------------------------------------------ derived files

function writeIfChanged(file, content) {
  const current = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null;
  if (current === content) return false;
  if (isCheck) {
    stale.push(path.relative(root, file));
    return false;
  }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content);
  return true;
}

// Plugin skills mirror: exactly the surface skills, nothing else.
const surfaceNames = new Set(skills.filter((s) => s.kind === 'surface').map((s) => s.name));
if (fs.existsSync(pluginSkillsRoot)) {
  for (const entry of fs.readdirSync(pluginSkillsRoot, { withFileTypes: true })) {
    if (entry.isDirectory() && !surfaceNames.has(entry.name)) {
      if (isCheck) stale.push(path.relative(root, path.join(pluginSkillsRoot, entry.name)));
      else fs.rmSync(path.join(pluginSkillsRoot, entry.name), { recursive: true, force: true });
    }
  }
}
for (const skill of skills) {
  if (skill.kind !== 'surface') continue;
  writeIfChanged(path.join(pluginSkillsRoot, skill.name, 'SKILL.md'), skill.markdown);
}

// Plugin manifest version follows the release; Claude Code pins users to this value.
const manifest = JSON.parse(fs.readFileSync(pluginManifestPath, 'utf8'));
manifest.version = version;
manifest.description = `Focused Unity Control Protocol skills, one per command surface (${
  skills.filter((s) => s.kind === 'surface').map((s) => s.name).join(', ')
}). Install instead of, or next to, the single omni skill.`;
writeIfChanged(pluginManifestPath, `${JSON.stringify(manifest, null, 2)}\n`);

// Catalog for the website, llms.txt, and agents that want a machine-readable list.
const catalog = {
  version,
  generatedFrom: 'skills/*/SKILL.md',
  install: {
    claudeCode: {
      marketplace: '/plugin marketplace add mflRevan/unity-control-protocol',
      omni: '/plugin install ucp@unity-control-protocol',
      surfaces: '/plugin install ucp-surfaces@unity-control-protocol',
    },
    skillsSh: 'npx skills add mflRevan/unity-control-protocol',
    githubCli: 'gh skill install mflRevan/unity-control-protocol <skill-name>',
    manual: `curl -fsSL ${SITE}/skills/<skill-name>.md -o .agents/skills/<skill-name>/SKILL.md`,
  },
  skills: skills.map(({ markdown, ...rest }) => rest),
};
writeIfChanged(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`);

// ------------------------------------------------------------------- report

if (isCheck) {
  if (stale.length) {
    console.error('Skill artifacts are out of sync (run `node scripts/sync-skills.mjs`):');
    for (const item of stale) console.error(`- ${item}`);
    process.exit(1);
  }
  console.log(`Skills in sync: ${skills.length} skills (${surfaceNames.size} surface + omni) at ${version}.`);
} else {
  console.log(
    `Synced ${skills.length} skills (${surfaceNames.size} surface + omni) at ${version}: plugin mirror, plugin.json, skills/index.json.`,
  );
}
