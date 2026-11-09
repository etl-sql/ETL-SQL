import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const lock = JSON.parse(readFileSync(path.join(root, 'scripts/actionlint-lock.json'), 'utf8'));
const platform = `${process.platform}-${process.arch}`;
const directory = path.join(root, 'scratch', 'actionlint', lock.version, platform);
export const executable = path.join(directory, process.platform === 'win32' ? 'actionlint.exe' : 'actionlint');

// One stalled download once failed Source preflight (CI 42476400399, 2026-11-08: "The operation was
// aborted due to timeout"). Retry with a shorter per-attempt limit; three attempts and their pauses
// stay inside the workflow step's 2-minute limit. A truncated body fails its checksum and is retried.
export async function download(url, sha256, { fetchImpl = fetch, attempts = 3, timeoutMs = 25000, pauseMs = 3000, report = console.error } = {}) {
  const failures = [];
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      const response = await fetchImpl(url, { signal: AbortSignal.timeout(timeoutMs) });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const bytes = Buffer.from(await response.arrayBuffer());
      if (createHash('sha256').update(bytes).digest('hex') !== sha256) throw new Error('archive checksum mismatch');
      return bytes;
    } catch (error) {
      failures.push(`attempt ${attempt}: ${error?.message ?? error}`);
      if (attempt < attempts) {
        report(`actionlint download ${failures.at(-1)}; retrying.`);
        await new Promise(resolve => setTimeout(resolve, pauseMs * attempt));
      }
    }
  }
  throw new Error(`actionlint download failed (${failures.join('; ')}).`);
}

export async function setup() {
  const entry = lock.archives[platform];
  if (!entry) throw new Error(`Unsupported actionlint platform: ${platform}`);
  mkdirSync(directory, { recursive: true });
  const name = `actionlint_${lock.version}_${entry[0]}`;
  const archive = path.join(directory, name);
  const digest = bytes => createHash('sha256').update(bytes).digest('hex');
  // Read the cached archive rather than testing for it, so nothing can change between check and use.
  let cached = null;
  try {
    cached = readFileSync(archive);
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
  }
  if (cached === null) {
    const bytes = await download(`https://github.com/rhysd/actionlint/releases/download/v${lock.version}/${name}`, entry[1]);
    writeFileSync(archive, bytes);
    cached = bytes;
  }
  if (digest(cached) !== entry[1]) throw new Error(`actionlint archive checksum mismatch: ${archive}`);
  const result = process.platform === 'win32'
    ? spawnSync('pwsh', ['-NoProfile', '-Command', 'Expand-Archive -LiteralPath $env:ACTIONLINT_ARCHIVE -DestinationPath $env:ACTIONLINT_DIRECTORY -Force'], {
      env: { ...process.env, ACTIONLINT_ARCHIVE: archive, ACTIONLINT_DIRECTORY: directory }, stdio: 'inherit', windowsHide: true, timeout: 30000 })
    : spawnSync('tar', ['-xzf', archive, '-C', directory], { stdio: 'inherit', timeout: 30000 });
  if (result.error || result.status !== 0) throw new Error(`actionlint extraction failed: ${result.error?.message ?? result.status}`);
  console.log(`Prepared actionlint ${lock.version} (${platform}).`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await setup();
