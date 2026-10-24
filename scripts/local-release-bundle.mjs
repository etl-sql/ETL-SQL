// Portable local phase evidence. Hosted/operator/package certification remains separate.
import { createHash } from 'node:crypto';
import { lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { candidateFindings } from './check-release-evidence.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const readJson = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));

function requireCandidate(state, sha, now) {
  const errors = candidateFindings(state, sha, now);
  if (errors.length) throw new Error(errors.join('\n'));
}

function safeFile(root, name) {
  if (typeof name !== 'string' || !/^[a-zA-Z0-9_./-]+$/.test(name) ||
      name.split('/').some(part => !part || part === '.' || part === '..') || path.isAbsolute(name)) {
    throw new Error('Invalid bundle-relative path.');
  }
  let resolved = root;
  for (const part of name.split('/')) {
    resolved = path.join(resolved, part);
    if (lstatSync(resolved).isSymbolicLink()) throw new Error('Bundle links are not evidence.');
  }
  if (!lstatSync(resolved).isFile()) throw new Error('Evidence must be a regular file.');
  return resolved;
}

export function createBundle(stateFile, sha, directory, { now = Date.now() } = {}) {
  const state = readJson(stateFile);
  requireCandidate(state, sha, now);
  // Never overwrite an earlier attempt or a caller's directory.
  mkdirSync(directory);
  const files = [];
  function retain(source, name) {
    if (!lstatSync(source).isFile() || lstatSync(source).isSymbolicLink()) throw new Error('Evidence input must be a regular file.');
    const bytes = readFileSync(source);
    mkdirSync(path.dirname(path.join(directory, name)), { recursive: true });
    writeFileSync(path.join(directory, name), bytes, { flag: 'wx' });
    files.push({ path: name, sha256: hash(bytes), bytes: bytes.length });
    return bytes.length;
  }
  function retainArtifact(source, prefix) {
    const stat = lstatSync(source);
    if (stat.isSymbolicLink()) throw new Error('Artifact links cannot be retained.');
    if (stat.isFile()) { retain(source, prefix); return; }
    if (!stat.isDirectory()) throw new Error('Unsupported evidence artifact.');
    const children = readdirSync(source).sort();
    // Directory markers preserve empty artifact directories without relying on archive behavior.
    const marker = `${prefix}/directory.json`;
    const bytes = Buffer.from(JSON.stringify({ entries: children.map((name, i) => ({ name, path: `${prefix}/${i}` })) }));
    mkdirSync(path.dirname(path.join(directory, marker)), { recursive: true });
    writeFileSync(path.join(directory, marker), bytes, { flag: 'wx' });
    files.push({ path: marker, sha256: hash(bytes), bytes: bytes.length });
    children.forEach((child, i) => retainArtifact(path.join(source, child), `${prefix}/${i}`));
  }
  const portable = structuredClone(state);
  portable.phases.forEach((phase, i) => {
    const log = `logs/${i}.log`;
    if (!retain(phase.log, log)) throw new Error(`Empty phase log: ${phase.name}`);
    phase.log = log;
    phase.artifactNames = (phase.artifacts ?? []).map(artifact => path.basename(artifact));
    phase.artifacts = (phase.artifacts ?? []).map((artifact, j) => {
      const name = `artifacts/${i}/${j}`;
      retainArtifact(artifact, name);
      return name;
    });
  });
  const stateBytes = Buffer.from(JSON.stringify(portable, null, 2));
  writeFileSync(path.join(directory, 'state.json'), stateBytes, { flag: 'wx' });
  files.push({ path: 'state.json', sha256: hash(stateBytes), bytes: stateBytes.length });
  const manifest = { schemaVersion: 1, scope: 'local-phases', candidateSha: sha, files };
  writeFileSync(path.join(directory, 'manifest.json'), JSON.stringify(manifest, null, 2), { flag: 'wx' });
  verifyBundle(directory, sha, { now });
  return manifest;
}

export function verifyBundle(directory, sha, { now = Date.now() } = {}) {
  if (lstatSync(directory).isSymbolicLink()) throw new Error('Bundle root cannot be a link.');
  const manifest = readJson(safeFile(directory, 'manifest.json'));
  if (manifest.schemaVersion !== 1 || manifest.scope !== 'local-phases' || manifest.candidateSha !== sha ||
      !Array.isArray(manifest.files) || !manifest.files.length) throw new Error('Invalid local evidence manifest.');
  const paths = new Set();
  for (const file of manifest.files) {
    if (paths.has(file.path)) throw new Error('Duplicate evidence path.');
    paths.add(file.path);
    const bytes = readFileSync(safeFile(directory, file.path));
    if (hash(bytes) !== file.sha256 || bytes.length !== file.bytes) throw new Error(`Evidence hash mismatch: ${file.path}`);
  }
  if (!paths.has('state.json')) throw new Error('State is not covered by the manifest.');
  const directories = new Set();
  for (const name of paths) {
    const parts = name.split('/');
    for (let i = 1; i < parts.length; i++) directories.add(parts.slice(0, i).join('/'));
  }
  function checkInventory(folder, prefix = '') {
    for (const name of readdirSync(folder)) {
      const relative = prefix ? `${prefix}/${name}` : name;
      const stat = lstatSync(path.join(folder, name));
      if (stat.isSymbolicLink()) throw new Error('Bundle links are not evidence.');
      if (stat.isDirectory()) {
        if (!directories.has(relative)) throw new Error('Unlisted bundle directory.');
        checkInventory(path.join(folder, name), relative);
      }
      else if (!stat.isFile() || (relative !== 'manifest.json' && !paths.has(relative))) throw new Error('Unlisted bundle file.');
    }
  }
  checkInventory(directory);
  const referenced = new Set(['state.json']);
  function visitArtifact(prefix) {
    if (paths.has(prefix)) { referenced.add(prefix); return; }
    const markerPath = `${prefix}/directory.json`;
    if (!paths.has(markerPath)) throw new Error('Phase artifact is missing.');
    referenced.add(markerPath);
    const marker = readJson(safeFile(directory, markerPath));
    if (!Array.isArray(marker.entries)) throw new Error('Invalid artifact directory inventory.');
    const names = new Set();
    marker.entries.forEach((entry, i) => {
      if (typeof entry?.name !== 'string' || !entry.name || /[\0/\\]/.test(entry.name) ||
          entry.name === '.' || entry.name === '..' || names.has(entry.name) || entry.path !== `${prefix}/${i}`) {
        throw new Error('Invalid artifact directory inventory.');
      }
      names.add(entry.name);
      visitArtifact(entry.path);
    });
  }
  const state = readJson(safeFile(directory, 'state.json'));
  requireCandidate(state, sha, now);
  for (const [i, phase] of state.phases.entries()) {
    if (phase.log !== `logs/${i}.log`) throw new Error('Missing canonical phase log reference.');
    if (!paths.has(phase.log) || !readFileSync(safeFile(directory, phase.log)).length) throw new Error('Phase log is missing or empty.');
    referenced.add(phase.log);
    if (!Array.isArray(phase.artifacts)) throw new Error('Phase artifact inventory is missing.');
    if (!Array.isArray(phase.artifactNames) || phase.artifactNames.length !== phase.artifacts.length ||
        phase.artifactNames.some(name => typeof name !== 'string' || !name || /[/\\]/.test(name) || name === '.' || name === '..')) {
      throw new Error('Original artifact names are missing or invalid.');
    }
    for (const [j, artifact] of phase.artifacts.entries()) {
      if (artifact !== `artifacts/${i}/${j}`) throw new Error('Missing canonical phase artifact reference.');
      visitArtifact(artifact);
    }
  }
  if (Array.from(paths).some(name => !referenced.has(name))) throw new Error('Unreferenced bundle evidence.');
  return manifest;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [mode, input, sha, output] = process.argv.slice(2);
    if (mode === 'create' && process.argv.length === 6) createBundle(input, sha, output);
    else if (mode === 'verify' && process.argv.length === 5) verifyBundle(input, sha);
    else throw new Error('Usage: local-release-bundle.mjs create <state.json> <sha> <new-directory> | verify <directory> <sha>');
    console.log('Local phase evidence bundle verified; operator and package gates remain separate.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
