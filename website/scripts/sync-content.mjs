import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const websiteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = path.resolve(websiteRoot, '..');
const generatedRoot = path.join(websiteRoot, '.generated');

syncDirectory(path.join(repoRoot, 'docs'), path.join(generatedRoot, 'docs'));
syncDirectory(path.join(repoRoot, 'skills'), path.join(generatedRoot, 'skills'));

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
    fs.mkdirSync(destination, { recursive: true });
    for (const entry of fs.readdirSync(source)) {
      copyRecursive(path.join(source, entry), path.join(destination, entry));
    }
    return;
  }

  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
}
