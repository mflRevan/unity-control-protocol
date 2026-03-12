import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const metadataPath = path.join(root, 'version.json');

const rawArgs = process.argv.slice(2);
const isCheck = rawArgs.includes('--check');
const filteredArgs = rawArgs.filter((arg) => arg !== '--check');
const requestedVersion = filteredArgs[0];
const requestedProtocol = filteredArgs[1] ?? requestedVersion;

const metadata = JSON.parse(fs.readFileSync(metadataPath, 'utf8'));
if (requestedVersion) {
  metadata.version = requestedVersion;
  metadata.protocolVersion = requestedProtocol ?? requestedVersion;
}

const { version, protocolVersion, repository, bridgePackage } = metadata;
const bridgeDependency = `${repository.gitUrl}?path=unity-package/${bridgePackage}#v${version}`;

const replacements = [
  ['cli/Cargo.toml', (content) => replaceOne(content, /^version = ".*"$/m, `version = "${version}"`)],
  [
    'cli/src/config.rs',
    (content) =>
      replaceOne(
        content,
        /^pub const PROTOCOL_VERSION: &str = ".*";$/m,
        `pub const PROTOCOL_VERSION: &str = "${protocolVersion}";`,
      ),
  ],
  ['npm/package.json', (content) => replaceOne(content, /"version": ".*"/, `"version": "${version}"`)],
  [
    'unity-package/com.ucp.bridge/package.json',
    (content) => {
      let next = replaceOne(content, /"version": ".*"/, `"version": "${version}"`);
      next = replaceOne(
        next,
        /"url": "https:\/\/github\.com\/.*?\/unity-control-protocol\.git"/,
        `"url": "${repository.gitUrl}"`,
      );
      next = replaceOne(
        next,
        /"documentationUrl": ".*"/,
        `"documentationUrl": "${repository.url}/blob/main/PROJECT.md"`,
      );
      next = replaceOne(
        next,
        /"changelogUrl": ".*"/,
        `"changelogUrl": "${repository.url}/blob/main/unity-package/com.ucp.bridge/CHANGELOG.md"`,
      );
      return next;
    },
  ],
  [
    'unity-package/com.ucp.bridge/Editor/Bridge/BridgeServer.cs',
    (content) =>
      replaceOne(
        content,
        /private const string ProtocolVersion = ".*";/,
        `private const string ProtocolVersion = "${protocolVersion}";`,
      ),
  ],
  [
    'README.md',
    (content) => {
      let next = replaceOne(content, /Release: `.*`/, `Release: \`${version}\``);
      next = replaceOne(next, /"com\.ucp\.bridge": ".*"/, `"com.ucp.bridge": "${bridgeDependency}"`);
      next = replaceOne(next, /### Advanced editor control in `.*`/, `### Advanced editor control in \`${version}\``);
      next = replaceOne(next, /"protocolVersion":".*"/, `"protocolVersion":"${protocolVersion}"`);
      return next;
    },
  ],
  [
    'npm/README.md',
    (content) => {
      let next = replaceOne(
        content,
        /Version `.*` of the Unity Control Protocol CLI\./,
        `Version \`${version}\` of the Unity Control Protocol CLI.`,
      );
      next = replaceOne(next, /"com\.ucp\.bridge": ".*"/, `"com.ucp.bridge": "${bridgeDependency}"`);
      return next;
    },
  ],
  [
    'docs/getting-started/installation.md',
    (content) => replaceOne(content, /"com\.ucp\.bridge": ".*"/, `"com.ucp.bridge": "${bridgeDependency}"`),
  ],
  [
    'docs/commands/connection.md',
    (content) => replaceOne(content, /  \| Protocol: .*/, `  | Protocol: ${protocolVersion}`),
  ],
  ['docs/commands/settings.md', (content) => replaceOne(content, /  Version: .*/, `  Version: ${version}`)],
  [
    'website/src/components/animated-terminal.tsx',
    (content) =>
      replaceOne(
        content,
        /  \{ text: '  Protocol: v.*', type: 'info', delay: 200 \},/,
        `  { text: '  Protocol: v${protocolVersion}', type: 'info', delay: 200 },`,
      ),
  ],
  [
    'skills/unity-control-protocol/SKILL.md',
    (content) => replaceOne(content, /  version: '.*'/, `  version: '${version}'`),
  ],
  [
    'PROJECT.md',
    (content) => {
      let next = replaceOne(content, /Current release target: `.*`/, `Current release target: \`${version}\``);
      next = replaceOne(next, /Current protocol version: `.*`/, `Current protocol version: \`${protocolVersion}\``);
      next = replaceIfPresent(next, /"serverVersion": ".*"/, `"serverVersion": "${protocolVersion}"`);
      next = replaceIfPresent(next, /"protocolVersion": ".*"/, `"protocolVersion": "${protocolVersion}"`);
      return next;
    },
  ],
  ['version.json', () => `${JSON.stringify(metadata, null, 2)}\n`],
];

const failures = [];
for (const [relativePath, transform] of replacements) {
  const absolutePath = path.join(root, relativePath);
  const current = fs.readFileSync(absolutePath, 'utf8');
  const next = withExistingLineEndings(transform(current), current);
  if (isCheck) {
    if (normalizeLineEndings(next) !== normalizeLineEndings(current)) {
      failures.push(relativePath);
    }
  } else if (next !== current) {
    fs.writeFileSync(absolutePath, next);
  }
}

if (isCheck) {
  if (failures.length > 0) {
    console.error('Version metadata is out of sync in:');
    for (const failure of failures) {
      console.error(`- ${failure}`);
    }
    process.exit(1);
  }
  console.log(`Version metadata is in sync for ${version} / protocol ${protocolVersion}.`);
} else {
  console.log(`Synced repo metadata to ${version} / protocol ${protocolVersion}.`);
}

function replaceOne(content, pattern, replacement) {
  const matches = content.match(pattern);
  if (!matches || matches.length !== 1) {
    throw new Error(`Expected exactly one match for ${pattern}`);
  }
  return content.replace(pattern, replacement);
}

function replaceIfPresent(content, pattern, replacement) {
  const matches = content.match(pattern);
  if (!matches) {
    return content;
  }
  if (matches.length !== 1) {
    throw new Error(`Expected at most one match for ${pattern}`);
  }
  return content.replace(pattern, replacement);
}

function normalizeLineEndings(content) {
  return content.replace(/\r\n/g, '\n');
}

function withExistingLineEndings(next, current) {
  const newline = current.includes('\r\n') ? '\r\n' : '\n';
  return normalizeLineEndings(next).replace(/\n/g, newline);
}
