import { createHash } from 'node:crypto';
import { closeSync, existsSync, lstatSync, mkdirSync, openSync, readFileSync, readSync, readdirSync, realpathSync, writeFileSync } from 'node:fs';
import { isMainThread, parentPort, Worker, workerData } from 'node:worker_threads';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { candidateFindings, requiredLocalPhases } from './check-release-evidence.mjs';

const hash = value => createHash('sha256').update(value).digest('hex');
const digest = value => hash(JSON.stringify(value));
const json = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hashBuffer = Buffer.alloc(1024 * 1024);
function fileHash(file) {
  const descriptor = openSync(file, 'r'), value = createHash('sha256');
  try {
    let length;
    while ((length = readSync(descriptor, hashBuffer, 0, hashBuffer.length, null)) > 0) value.update(hashBuffer.subarray(0, length));
    return value.digest('hex');
  } finally { closeSync(descriptor); }
}

function owned(workspace, input) {
  const resolved = path.resolve(workspace, input), relative = path.relative(workspace, resolved);
  if (!relative || path.isAbsolute(relative) || relative.split(path.sep).includes('..')) throw new Error('Resume inputs must stay inside the controlled workspace.');
  let current = workspace;
  for (const part of relative.split(path.sep)) {
    current = path.join(current, part);
    if (lstatSync(current, { throwIfNoEntry: false })?.isSymbolicLink()) throw new Error('Resume inputs cannot traverse links.');
  }
  return resolved;
}

function tree(file, stats) {
  const stat = lstatSync(file);
  if (stat.isSymbolicLink()) throw new Error('Resume inputs cannot contain links.');
  if (stat.isFile()) {
    if (stats) { stats.files++; stats.bytes += stat.size; }
    return digest(['file', fileHash(file)]);
  }
  if (!stat.isDirectory()) throw new Error('Unsupported resume input.');
  return digest(['directory', readdirSync(file).sort().map(name => [name, tree(path.join(file, name), stats)])]);
}

function phaseDigest(phase, logHash, artifacts) {
  // PowerShell can reserialize a UTC JSON timestamp with the local offset on resume.
  // Preserve the instant and sub-millisecond digits rather than its zone spelling.
  const fraction = String(phase.completedAt).match(/\.(\d+)(?:Z|[+-]\d{2}:\d{2})$/)?.[1] ?? '';
  const completed = [Date.parse(phase.completedAt), fraction.padEnd(9, '0').slice(3)];
  return digest([phase.name, phase.command, completed, logHash, artifacts]);
}

export function localPhaseFingerprint(workspace, phase) {
  return phaseDigest(phase, fileHash(owned(workspace, phase.log)), phase.artifacts.map(file =>
    [path.basename(file), tree(owned(workspace, file))]));
}

export function portablePhaseFingerprint(directory, phase, manifest) {
  const files = new Map(manifest.files.map(file => [file.path, file.sha256]));
  function artifact(prefix) {
    if (files.has(prefix)) return digest(['file', files.get(prefix)]);
    const marker = json(path.join(directory, `${prefix}/directory.json`));
    return digest(['directory', marker.entries.map(entry => [entry.name, artifact(entry.path)])]);
  }
  return phaseDigest(phase, files.get(phase.log), phase.artifacts.map((file, i) => [phase.artifactNames[i], artifact(file)]));
}

export async function controlledCacheSnapshot(workspace, report = () => {}) {
  const started = Date.now(), stats = { files: 0, bytes: 0 }, roots = new Set();
  for (const top of ['src', 'tests']) {
    const folder = path.join(workspace, top);
    if (!existsSync(folder)) continue;
    for (const name of readdirSync(folder)) for (const output of ['bin', 'obj']) roots.add(`${top}/${name}/${output}`);
  }
  for (const name of ['scripts/typecheck/node_modules', 'src/etl-sql-vscode/node_modules', 'src/etl-sql-vscode/ui/node_modules',
    'src/etl-sql-vscode/out', 'src/etl-sql-vscode/dist', 'src/etl-sql-vscode/ui/dist',
    'artifacts/browser-assets.json', 'artifacts/browser-assets.props', 'artifacts/browser-assets-built']) roots.add(name);
  const inventory = path.join(workspace, 'artifacts/browser-assets.json');
  if (existsSync(inventory)) for (const output of json(owned(workspace, inventory)).outputs ?? []) roots.add(output.path);
  const entries = [...roots].sort().map(name => {
    const file = owned(workspace, name);
    return [name, existsSync(file) ? file : null];
  });
  const active = entries.map(([name, file], index) => ({ name, file, index })).filter(entry => entry.file);
  active.sort((a, b) => Number(!a.name.endsWith('/bin')) - Number(!b.name.endsWith('/bin')));
  const workers = [], count = Math.min(4, active.length);
  try {
    await Promise.all(Array.from({ length: count }, (_, index) => new Promise((resolve, reject) => {
      const worker = new Worker(new URL(import.meta.url), { workerData: { mode: 'controlled-cache', entries: active.filter((_, i) => i % count === index) } });
      workers.push(worker);
      let received = false;
      worker.on('message', result => {
        if (result.error) { reject(new Error(result.error)); return; }
        if (result.entry) {
          entries[result.entry.index][1] = result.entry.sha256;
          stats.files += result.entry.files; stats.bytes += result.entry.bytes;
          report({ files: stats.files, bytes: stats.bytes, elapsedMilliseconds: Date.now() - started });
        } else { received = true; resolve(); }
      });
      worker.on('error', reject);
      worker.on('exit', code => { if (!received) reject(new Error(`Cache fingerprint worker exited before completion (${code}).`)); });
    })));
  } finally { await Promise.all(workers.map(worker => worker.terminate())); }
  return { sha256: digest(entries), ...stats, elapsedMilliseconds: Date.now() - started };
}

function context(workspace, identity) {
  workspace = realpathSync(workspace);
  if (!/^[\w.-]+\/[\w.-]+$/.test(identity.repository ?? '') || !/^[a-f0-9]{40}$/.test(identity.sha ?? '') ||
      !Number.isSafeInteger(identity.runId) || identity.runId < 1 || !Number.isSafeInteger(identity.attempt) || identity.attempt < 1 ||
      typeof identity.runnerName !== 'string' || !identity.runnerName.trim() ||
      !/^v22\.\d+\.\d+$/.test(identity.toolchain?.nodeVersion ?? '') || !/^10\.\d+\.\d+$/.test(identity.toolchain?.dotnetVersion ?? '') ||
      !/^[a-f0-9]{64}$/.test(identity.toolchain?.dockerImagesHash ?? '')) throw new Error('Invalid controlled-host resume identity or toolchain.');
  const { dockerImagesHash, ...basic } = identity.toolchain;
  const owner = { repository: identity.repository, sha: identity.sha, runId: identity.runId, runnerName: identity.runnerName,
    workspace, toolchainHash: digest(canonical(basic)) };
  const relative = `release-validation/hosted-local-state/${identity.runId}`;
  const root = owned(workspace, relative), validation = `${relative}/validation`;
  return { workspace, owner, root, validation, stateFile: owned(workspace, `${validation}/latest/state.json`), dockerImagesHash };
}

function stateSnapshot(ctx) {
  if (!existsSync(ctx.stateFile)) return { state: null, fingerprint: digest(null), phases: [], logs: [] };
  const state = json(owned(ctx.workspace, ctx.stateFile));
  if (state.evidenceSchemaVersion !== 1 || state.configuration !== 'Release' || state.commitHash !== ctx.owner.sha ||
      state.sourceFingerprint !== hash(ctx.owner.sha) || !['Passed', 'Failed', 'Running'].includes(state.status) ||
      ['quick', 'forceResume', 'skipNode', 'skipScale'].some(key => state.scope?.[key] !== false) ||
      ['includeSlt', 'includeDockerIntegration', 'includeStandardScale'].some(key => state.scope?.[key] !== true) ||
      !Array.isArray(state.plannedPhases) || new Set(state.plannedPhases).size !== state.plannedPhases.length ||
      requiredLocalPhases.some(name => !state.plannedPhases.includes(name)) || !Array.isArray(state.phases) ||
      new Set(state.phases.map(phase => phase.name)).size !== state.phases.length) throw new Error('Resume state does not describe this candidate and full release scope.');
  const phases = [], logs = [], payloads = [];
  for (const phase of state.phases) {
    if (!state.plannedPhases.includes(phase.name) || !['Passed', 'Failed', 'Skipped'].includes(phase.status)) throw new Error('Invalid resume phase inventory.');
    const log = owned(ctx.workspace, phase.log);
    if (existsSync(log)) logs.push({ path: log, sha256: hash(readFileSync(log)) });
    payloads.push([phase.name, (phase.artifacts ?? []).map(file => {
      const artifact = owned(ctx.workspace, file);
      return [file, existsSync(artifact) ? tree(artifact) : null];
    })]);
    if (phase.status !== 'Passed') continue;
    const completed = Date.parse(phase.completedAt);
    if (!Number.isFinite(completed) || completed > Date.now() || Date.now() - completed > 7 * 86400000 ||
        typeof phase.command !== 'string' || !phase.command || !Array.isArray(phase.artifacts)) throw new Error('Passed resume phase lacks valid original execution evidence.');
    phases.push({ name: phase.name, fingerprint: localPhaseFingerprint(ctx.workspace, phase) });
  }
  return { state, phases, logs, fingerprint: digest([hash(readFileSync(ctx.stateFile)), phases, logs, payloads]) };
}

function previousCheckpoint(ctx, attempt) {
  const started = readdirSync(ctx.root).map(name => name.match(/^attempt-([1-9][0-9]*)\.started\.json$/)?.[1]).filter(Boolean).map(Number);
  const prior = Math.max(0, ...started.filter(number => number < attempt));
  if (!prior) throw new Error('Original controlled-host checkpoint is missing; no automatic full rerun.');
  const file = owned(ctx.workspace, path.join(ctx.root, `attempt-${prior}.checkpoint.json`));
  if (!existsSync(file)) throw new Error('Previous controlled attempt has no closed checkpoint; inspect its retained diagnostics.');
  return json(file);
}

export async function beginHostedLocalAttempt(workspace, identity) {
  const ctx = context(workspace, identity), ownerFile = path.join(ctx.root, 'owner.json');
  if (!existsSync(ownerFile)) {
    if (identity.attempt !== 1) throw new Error('Original controlled-host workspace is missing; no automatic full rerun.');
    if (existsSync(ctx.stateFile)) throw new Error('A fresh controlled run cannot inherit workstation state.');
    mkdirSync(ctx.root, { recursive: true });
    writeFileSync(ownerFile, JSON.stringify(ctx.owner), { flag: 'wx' });
  }
  if (digest(canonical(json(owned(ctx.workspace, ownerFile)))) !== digest(canonical(ctx.owner))) throw new Error('Candidate, workflow run, runner, workspace or toolchain changed; no resume.');
  if (readdirSync(ctx.root).some(name => Number(name.match(/^attempt-([1-9][0-9]*)\.started\.json$/)?.[1]) >= identity.attempt)) {
    throw new Error('This controlled attempt is already claimed or superseded.');
  }
  let resume = false;
  if (identity.attempt > 1) {
    const first = json(owned(ctx.workspace, path.join(ctx.root, 'attempt-1.started.json')));
    const began = Date.parse(first.startedAt);
    if (!Number.isFinite(began) || began > Date.now() || Date.now() - began > 7 * 86400000) throw new Error('Controlled workflow checkpoint is stale or future-dated; start a new explicit certification.');
    const previous = previousCheckpoint(ctx, identity.attempt), current = stateSnapshot(ctx);
    if (previous.evidenceFingerprint !== current.fingerprint || previous.dockerImagesHash !== ctx.dockerImagesHash) {
      throw new Error('Controlled state, logs, artifacts, cached outputs or Docker inputs changed; no resume.');
    }
    const cache = await controlledCacheSnapshot(ctx.workspace);
    if (previous.cache.sha256 !== cache.sha256) throw new Error('Controlled cached outputs changed; no resume.');
    for (const name of readdirSync(ctx.root).filter(name => /^attempt-[1-9][0-9]*\.checkpoint\.json$/.test(name))) {
      for (const log of json(owned(ctx.workspace, path.join(ctx.root, name))).logs) {
        if (hash(readFileSync(owned(ctx.workspace, log.path))) !== log.sha256) throw new Error('An original attempt log changed; no resume.');
      }
    }
    resume = current.state !== null;
  }
  writeFileSync(path.join(ctx.root, `attempt-${identity.attempt}.started.json`), JSON.stringify({ attempt: identity.attempt, startedAt: new Date().toISOString() }), { flag: 'wx' });
  return { resume, validationDirectory: ctx.validation };
}

export async function finishHostedLocalAttempt(workspace, identity, exitCode) {
  const ctx = context(workspace, identity);
  if (!Number.isInteger(exitCode) || digest(canonical(json(owned(ctx.workspace, path.join(ctx.root, 'owner.json'))))) !== digest(canonical(ctx.owner))) throw new Error('Controlled attempt identity changed before checkpointing.');
  json(owned(ctx.workspace, path.join(ctx.root, `attempt-${identity.attempt}.started.json`)));
  const snapshot = stateSnapshot(ctx), previous = identity.attempt > 1 ? previousCheckpoint(ctx, identity.attempt) : null;
  if (exitCode === 0 && snapshot.state?.status !== 'Passed') throw new Error('Successful controlled execution did not retain Passed state.');
  if (exitCode === 0 && candidateFindings(snapshot.state, ctx.owner.sha).length) throw new Error('Successful controlled execution did not retain the complete required candidate scope.');
  const phases = snapshot.phases.map(phase => ({ ...phase, attempt:
    previous?.phases.find(old => old.name === phase.name && old.fingerprint === phase.fingerprint)?.attempt ?? identity.attempt }));
  if (snapshot.state) {
    snapshot.state.hostedExecution = { schemaVersion: 1, runId: identity.runId, attempt: identity.attempt, runnerName: identity.runnerName, phases };
    writeFileSync(ctx.stateFile, JSON.stringify(snapshot.state, null, 2));
  }
  const checkpoint = { schemaVersion: 1, attempt: identity.attempt, exitCode, finishedAt: new Date().toISOString(), phases, logs: snapshot.logs,
    evidenceFingerprint: stateSnapshot(ctx).fingerprint, dockerImagesHash: ctx.dockerImagesHash, cache: await controlledCacheSnapshot(ctx.workspace) };
  writeFileSync(path.join(ctx.root, `attempt-${identity.attempt}.checkpoint.json`), JSON.stringify(checkpoint, null, 2), { flag: 'wx' });
  return checkpoint;
}

if (!isMainThread && workerData?.mode === 'controlled-cache') {
  try {
    for (const entry of workerData.entries) {
      const stats = { files: 0, bytes: 0 }, sha256 = tree(entry.file, stats);
      parentPort.postMessage({ entry: { index: entry.index, sha256, ...stats } });
    }
    parentPort.postMessage({ complete: true });
  } catch (error) { parentPort.postMessage({ error: error.message }); }
} else if (isMainThread && process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [mode, workspace, identityFile, exitCode] = process.argv.slice(2);
    if (mode === 'inspect-cache' && process.argv.length === 4) console.log(JSON.stringify(await controlledCacheSnapshot(realpathSync(workspace), progress => console.error(JSON.stringify(progress)))));
    else if (mode === 'begin' && process.argv.length === 5) console.log(JSON.stringify(await beginHostedLocalAttempt(workspace, json(identityFile))));
    else if (mode === 'finish' && process.argv.length === 6) console.log(JSON.stringify(await finishHostedLocalAttempt(workspace, json(identityFile), Number(exitCode))));
    else throw new Error('Usage: hosted-local-resume.mjs begin|finish <workspace> <identity.json> [exit-code] | inspect-cache <workspace>');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
