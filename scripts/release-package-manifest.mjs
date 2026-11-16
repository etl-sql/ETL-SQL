import { createReadStream } from 'node:fs';
import { lstat, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

async function hashPackage(directory, name) {
  const file = path.join(directory, name);
  const info = await lstat(file);
  if (!info.isFile() || info.size === 0) throw new Error(`Missing or empty package: ${name}`);
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

// The caller owns the allowed filenames. Manifests cannot introduce paths or package types.
export async function packageManifest(mode, directory, sourceSha, version,
  { platform, manifestName, required, optional = [], includeOptional = false }, manifestSha256) {
  if (!['create', 'verify'].includes(mode) || !/^[a-f0-9]{40}$/.test(sourceSha) ||
      !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) throw new Error('Invalid package identity.');
  const manifestPath = path.join(directory, manifestName);
  let names = [...required, ...(includeOptional ? optional : [])];
  let actual;
  if (mode === 'verify') {
    const contents = await readFile(manifestPath);
    if (!/^[a-f0-9]{64}$/.test(manifestSha256 ?? '') || createHash('sha256').update(contents).digest('hex') !== manifestSha256) {
      throw new Error('Package manifest hash mismatch.');
    }
    actual = JSON.parse(contents.toString('utf8'));
    if (actual.schemaVersion !== 1 || actual.sourceSha !== sourceSha || actual.version !== version || actual.platform !== platform) {
      throw new Error('Package candidate identity mismatch.');
    }
    if (!actual.packages || typeof actual.packages !== 'object' || Array.isArray(actual.packages)) throw new Error('Missing package hashes.');
    names = [...required, ...optional.filter(name => Object.hasOwn(actual.packages, name))];
    if (Object.keys(actual.packages).length !== names.length) throw new Error('Unexpected package hashes.');
  }
  for (const name of optional.filter(name => !names.includes(name))) {
    const info = await lstat(path.join(directory, name)).catch(error => {
      if (error.code !== 'ENOENT') throw error;
      return null;
    });
    if (info) throw new Error(`Unrecorded optional package: ${name}`);
  }
  const packages = Object.fromEntries(await Promise.all(names.map(async name => [name, await hashPackage(directory, name)])));
  if (mode === 'create') {
    await writeFile(manifestPath, `${JSON.stringify({ schemaVersion: 1, sourceSha, version, platform, packages }, null, 2)}\n`);
  } else if (names.some(name => actual.packages[name] !== packages[name])) {
    throw new Error('Package hashes mismatch.');
  }
}
