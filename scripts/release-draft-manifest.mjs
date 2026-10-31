import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { lstat, open, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

async function inventory(directory) {
  const root = await lstat(directory);
  if (!root.isDirectory() || root.isSymbolicLink()) throw new Error('Draft assets need an ordinary directory.');
  const names = (await readdir(directory)).sort();
  if (!names.length || new Set(names.map(name => name.toLowerCase())).size !== names.length) {
    throw new Error('Draft assets are empty or have ambiguous names.');
  }
  const assets = {};
  for (const name of names) {
    if (!/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(name)) throw new Error('Unsafe draft asset name.');
    const file = path.join(directory, name), info = await lstat(file);
    if (!info.isFile() || info.isSymbolicLink() || info.size === 0) throw new Error(`Invalid draft asset: ${name}`);
    const hash = createHash('sha256');
    for await (const chunk of createReadStream(file)) hash.update(chunk);
    assets[name] = { size: info.size, sha256: hash.digest('hex') };
  }
  return assets;
}

// This binds the final draft to bytes already checked by the platform gates. It
// does not certify packages or replace their original build/installation evidence.
export async function draftManifest(mode, directory, manifestPath, sourceSha, tag, expectedHash) {
  if (!['create', 'verify'].includes(mode) || !/^[a-f0-9]{40}$/.test(sourceSha) ||
      !/^v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)$/.test(tag)) throw new Error('Invalid draft candidate identity.');
  const relative = path.relative(path.resolve(directory), path.resolve(manifestPath));
  if (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative)) {
    throw new Error('Keep the draft manifest outside the asset directory.');
  }
  if (mode === 'create') {
    const assets = await inventory(directory);
    await writeFile(manifestPath, `${JSON.stringify({ schemaVersion: 1, sourceSha, tag, assets }, null, 2)}\n`, { flag: 'wx' });
  }
  const handle = await open(manifestPath, 'r');
  let bytes;
  try {
    const info = await handle.stat(), entry = await lstat(manifestPath);
    if (!info.isFile() || !entry.isFile() || entry.isSymbolicLink() ||
        info.dev !== entry.dev || info.ino !== entry.ino) throw new Error('Invalid draft manifest file.');
    bytes = await handle.readFile();
  } finally { await handle.close(); }
  const digest = createHash('sha256').update(bytes).digest('hex');
  if (mode === 'verify') {
    if (!/^[a-f0-9]{64}$/.test(expectedHash ?? '') || digest !== expectedHash) throw new Error('Draft manifest hash mismatch.');
    const manifest = JSON.parse(bytes);
    if (manifest.schemaVersion !== 1 || manifest.sourceSha !== sourceSha || manifest.tag !== tag) throw new Error('Draft candidate identity mismatch.');
    const actual = await inventory(directory);
    if (!manifest.assets || Array.isArray(manifest.assets) ||
        JSON.stringify(Object.keys(actual).sort()) !== JSON.stringify(Object.keys(manifest.assets).sort()) ||
        Object.entries(actual).some(([name, entry]) => manifest.assets[name]?.size !== entry.size || manifest.assets[name]?.sha256 !== entry.sha256)) {
      throw new Error('Draft asset inventory or hashes changed after verification.');
    }
  }
  return digest;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const mode = process.argv[2];
    if ((mode === 'create' && process.argv.length !== 7) || (mode === 'verify' && process.argv.length !== 8) || !['create', 'verify'].includes(mode)) {
      throw new Error('Usage: release-draft-manifest.mjs create|verify <assets> <manifest> <source-sha> <tag> [manifest-sha256]');
    }
    console.log(await draftManifest(...process.argv.slice(2)));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
