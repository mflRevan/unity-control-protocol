import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const metadata = JSON.parse(fs.readFileSync(path.join(root, 'version.json'), 'utf8'));
const skillSlug = 'unity-control-protocol';
const skillName = 'Unity Control Protocol';
const skillDir = path.join(root, 'skills', skillSlug);
const stageRoot = path.join(root, 'artifacts', 'clawhub');
const changelogPath = path.join(root, 'CHANGELOG.md');
const skillPath = path.join(skillDir, 'SKILL.md');
const command = process.argv[2];
const options = parseOptions(process.argv.slice(3));

if (!command || !['validate', 'stage', 'publish', 'changelog'].includes(command)) {
  printUsage();
  process.exit(command ? 1 : 0);
}

switch (command) {
  case 'validate':
    validateSkillBundle();
    console.log(`ClawHub skill bundle is valid for ${metadata.version}.`);
    break;
  case 'stage': {
    validateSkillBundle();
    const outDir = options['out-dir'] ? path.resolve(options['out-dir']) : stageRoot;
    const stagedDir = stageSkillBundle(outDir);
    console.log(`Staged ClawHub bundle at ${stagedDir}`);
    break;
  }
  case 'publish': {
    validateSkillBundle();
    const outDir = options['out-dir'] ? path.resolve(options['out-dir']) : stageRoot;
    const stagedDir = stageSkillBundle(outDir);
    const changelog = options.changelog ?? getReleaseChangelog(metadata.version);
    publishSkill(stagedDir, changelog, options.tags ?? 'latest');
    break;
  }
  case 'changelog':
    process.stdout.write(`${getReleaseChangelog(metadata.version)}\n`);
    break;
  default:
    printUsage();
    process.exit(1);
}

function validateSkillBundle() {
  const errors = [];

  requireFile(skillPath, errors);

  if (errors.length > 0) {
    throwValidationErrors(errors);
  }

  const skillContent = fs.readFileSync(skillPath, 'utf8');
  const frontmatter = extractFrontmatter(skillContent);
  const relativeEntries = fs
    .readdirSync(skillDir, { withFileTypes: true })
    .map((entry) => entry.name)
    .sort();
  const allowedEntries = new Set(['SKILL.md']);

  if (!frontmatter.includes(`name: ${skillSlug}`)) {
    errors.push('SKILL.md frontmatter must include the canonical skill name.');
  }

  if (!frontmatter.includes(`homepage: ${metadata.repository.url}`)) {
    errors.push('SKILL.md frontmatter must include the repository homepage.');
  }

  if (!frontmatter.includes(`version: '${metadata.version}'`)) {
    errors.push(`SKILL.md version must match version.json (${metadata.version}).`);
  }

  if (!frontmatter.includes('clawdbot:')) {
    errors.push('SKILL.md frontmatter must include metadata.clawdbot.');
  }

  if (!/emoji:\s*['"]🎮['"]/.test(frontmatter)) {
    errors.push('SKILL.md frontmatter must include metadata.clawdbot.emoji.');
  }

  for (const heading of ['## External Endpoints', '## Security & Privacy', '## Model Invocation Note', '## Trust Statement']) {
    if (!skillContent.includes(heading)) {
      errors.push(`SKILL.md must include the "${heading.replace('## ', '')}" section.`);
    }
  }

  const scriptsDir = path.join(skillDir, 'scripts');
  const hasScriptsDir = fs.existsSync(scriptsDir);
  if (hasScriptsDir && !frontmatter.includes('files:')) {
    errors.push('SKILL.md frontmatter must declare metadata.clawdbot.files when the skill ships scripts/.');
  }

  for (const entry of relativeEntries) {
    if (!allowedEntries.has(entry)) {
      errors.push(`Unexpected entry in ${path.relative(root, skillDir)}: ${entry}`);
    }
  }

  if (errors.length > 0) {
    throwValidationErrors(errors);
  }
}

function stageSkillBundle(outRoot) {
  const outDir = path.join(outRoot, skillSlug);
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });

  copyFile(skillPath, path.join(outDir, 'SKILL.md'));

  return outDir;
}

function publishSkill(bundleDir, changelog, tags) {
  const token = process.env.CLAWHUB_TOKEN?.trim();
  const env = { ...process.env };

  if (token) {
    runClawhub(['login', '--token', token, '--label', 'unity-control-protocol release', '--no-browser'], env);
  } else {
    console.warn('CLAWHUB_TOKEN is not set; relying on existing clawhub auth state.');
  }

  runClawhub(
    [
      '--no-input',
      'publish',
      bundleDir,
      '--slug',
      skillSlug,
      '--name',
      skillName,
      '--version',
      metadata.version,
      '--changelog',
      changelog,
      '--tags',
      tags,
    ],
    env,
  );

  console.log(`Published ${skillSlug}@${metadata.version} to ClawHub.`);
}

function getReleaseChangelog(version) {
  const content = fs.readFileSync(changelogPath, 'utf8');
  const headerPattern = new RegExp(`^## \\[${escapeRegExp(version)}\\] - .*$`, 'm');
  const headerMatch = content.match(headerPattern);
  if (!headerMatch || headerMatch.index == null) {
    throw new Error(`Could not find changelog entry for version ${version} in CHANGELOG.md.`);
  }

  const sectionStart = headerMatch.index + headerMatch[0].length;
  const remaining = content.slice(sectionStart);
  const nextHeaderIndex = remaining.search(/\r?\n## \[/);
  const section = nextHeaderIndex === -1 ? remaining : remaining.slice(0, nextHeaderIndex);
  return section.trim();
}

function extractFrontmatter(content) {
  const match = content.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!match) {
    throw new Error('SKILL.md is missing YAML frontmatter.');
  }
  return match[1];
}

function requireFile(filePath, errors) {
  if (!fs.existsSync(filePath)) {
    errors.push(`Missing required file: ${path.relative(root, filePath)}`);
  }
}

function throwValidationErrors(errors) {
  console.error('ClawHub skill validation failed:');
  for (const error of errors) {
    console.error(`- ${error}`);
  }
  process.exit(1);
}

function copyFile(source, destination) {
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
}

function runClawhub(args, env) {
  const executable = process.platform === 'win32' ? 'clawhub.cmd' : 'clawhub';
  const result = spawnSync(executable, args, {
    cwd: root,
    stdio: 'inherit',
    env,
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

function parseOptions(args) {
  const parsed = {};

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (!arg.startsWith('--')) {
      continue;
    }

    const key = arg.slice(2);
    const next = args[index + 1];
    if (!next || next.startsWith('--')) {
      parsed[key] = true;
      continue;
    }

    parsed[key] = next;
    index += 1;
  }

  return parsed;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function printUsage() {
  console.log(`Usage:
  node scripts/clawhub-skill.mjs validate
  node scripts/clawhub-skill.mjs stage [--out-dir <dir>]
  node scripts/clawhub-skill.mjs changelog
  node scripts/clawhub-skill.mjs publish [--out-dir <dir>] [--changelog <text>] [--tags <tags>]
`);
}
