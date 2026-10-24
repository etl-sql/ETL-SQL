import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const lock = JSON.parse(readFileSync(path.join(root, 'scripts/actionlint-lock.json'), 'utf8'));
const platform = `${process.platform}-${process.arch}`;
const directory = path.join(root, 'scratch', 'actionlint', lock.version, platform);
export const executable = path.join(directory, process.platform === 'win32' ? 'actionlint.exe' : 'actionlint');

export async function setup() {
  const entry = lock.archives[platform];
  if (!entry) throw new Error(`Unsupported actionlint platform: ${platform}`);
  mkdirSync(directory, { recursive: true });
  const name = `actionlint_${lock.version}_${entry[0]}`;
  const archive = path.join(directory, name);
  const digest = bytes => createHash('sha256').update(bytes).digest('hex');
  if (!existsSync(archive)) {
    const response = await fetch(`https://github.com/rhysd/actionlint/releases/download/v${lock.version}/${name}`, { signal: AbortSignal.timeout(60000) });
    if (!response.ok) throw new Error(`actionlint download failed: HTTP ${response.status}`);
    const bytes = Buffer.from(await response.arrayBuffer());
    if (digest(bytes) !== entry[1]) throw new Error('actionlint archive checksum mismatch.');
    writeFileSync(archive, bytes);
  }
  if (digest(readFileSync(archive)) !== entry[1]) throw new Error(`actionlint archive checksum mismatch: ${archive}`);
  const result = process.platform === 'win32'
    ? spawnSync('pwsh', ['-NoProfile', '-Command', 'Expand-Archive -LiteralPath $env:ACTIONLINT_ARCHIVE -DestinationPath $env:ACTIONLINT_DIRECTORY -Force'], {
      env: { ...process.env, ACTIONLINT_ARCHIVE: archive, ACTIONLINT_DIRECTORY: directory }, stdio: 'inherit', windowsHide: true, timeout: 30000 })
    : spawnSync('tar', ['-xzf', archive, '-C', directory], { stdio: 'inherit', timeout: 30000 });
  if (result.error || result.status !== 0) throw new Error(`actionlint extraction failed: ${result.error?.message ?? result.status}`);
  console.log(`Prepared actionlint ${lock.version} (${platform}).`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await setup();
