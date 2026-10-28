// A manual checkpoint never acquires GitHub Actions provenance.
import { createHash } from 'node:crypto';
import { closeSync, cpSync, existsSync, lstatSync, mkdirSync, openSync, readFileSync, readSync, readdirSync, realpathSync, unlinkSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { controlledCacheSnapshot } from './hosted-local-resume.mjs';
import { candidateFindings, requiredLocalPhases } from './check-release-evidence.mjs';

const json = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const hash = value => createHash('sha256').update(value).digest('hex');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const digest = value => hash(JSON.stringify(canonical(value)));
const fresh = value => Number.isFinite(Date.parse(value)) && Date.parse(value) <= Date.now() && Date.now() - Date.parse(value) <= 7 * 86400000;
function fileHash(file) {
  const descriptor = openSync(file, 'r'), value = createHash('sha256'), buffer = Buffer.alloc(1024 * 1024);
  try {
    let length;
    while ((length = readSync(descriptor, buffer, 0, buffer.length, null)) > 0) value.update(buffer.subarray(0, length));
    return value.digest('hex');
  } finally { closeSync(descriptor); }
}

function noLinks(file) {
  let current = path.parse(file).root;
  for (const part of file.slice(current.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, part);
    if (lstatSync(current, { throwIfNoEntry: false })?.isSymbolicLink()) throw new Error('Workstation checkpoints cannot traverse links.');
  }
}
function tree(file, exclude = new Set()) {
  noLinks(file);
  const stat = lstatSync(file);
  if (stat.isFile()) return ['file', fileHash(file)];
  if (!stat.isDirectory()) throw new Error('Unsupported workstation evidence input.');
  return ['directory', readdirSync(file).sort().filter(name => !exclude.has(path.join(file, name)))
    .map(name => [name, tree(path.join(file, name), exclude)])];
}
function context(workspace, evidence, identity) {
  workspace = realpathSync(workspace); evidence = path.resolve(evidence); noLinks(evidence);
  if (!/^[a-f0-9]{40}$/.test(identity.sourceSha ?? '') || !identity.toolchain ||
      !/^v22\.\d+\.\d+$/.test(identity.toolchain.nodeVersion ?? '') ||
      !/^10\.\d+\.\d+$/.test(identity.toolchain.dotnetVersion ?? '') ||
      identity.host?.platform !== 'win32' || identity.host.architecture !== 'x64') throw new Error('Invalid independent workstation identity.');
  const { dockerImagesHash, ...toolchain } = identity.toolchain;
  return { workspace, evidence, owner: { workspace, evidence, ...identity, toolchain }, dockerImagesHash,
    stateFile: path.join(evidence, 'validation/latest/state.json') };
}
function inputPath(ctx, input) {
  const file = path.resolve(ctx.workspace, input);
  if (![ctx.workspace, ctx.evidence].some(root => {
    const relative = path.relative(root, file);
    return relative && !path.isAbsolute(relative) && !relative.split(path.sep).includes('..');
  })) throw new Error('Checkpoint phase input escapes the workspace and evidence directory.');
  return file;
}
function snapshot(ctx, attempt) {
  const state = json(ctx.stateFile);
  if (state.evidenceSchemaVersion !== 1 || state.commitHash !== ctx.owner.sourceSha ||
      state.sourceFingerprint !== hash(ctx.owner.sourceSha) || state.configuration !== 'Release' ||
      !['Passed', 'Failed', 'Running'].includes(state.status) || !fresh(state.generatedAt) || state.hostedExecution !== undefined ||
      state.workstationExecution !== undefined ||
      ['quick', 'forceResume', 'skipNode', 'skipScale'].some(key => state.scope?.[key] !== false) ||
      ['includeSlt', 'includeDockerIntegration', 'includeStandardScale'].some(key => state.scope?.[key] !== true) ||
      !Array.isArray(state.plannedPhases) || new Set(state.plannedPhases).size !== state.plannedPhases.length ||
      requiredLocalPhases.some(name => !state.plannedPhases.includes(name)) || !Array.isArray(state.phases) ||
      new Set(state.phases.map(phase => phase.name)).size !== state.phases.length) throw new Error('Checkpoint lacks the original full candidate scope.');
  const inputs = [];
  for (const phase of state.phases) {
    if (!state.plannedPhases.includes(phase.name) || !['Passed', 'Failed', 'Skipped'].includes(phase.status)) throw new Error('Invalid checkpoint phase inventory.');
    if (phase.status === 'Passed' && (!fresh(phase.completedAt) || !phase.command || !phase.log || !Array.isArray(phase.artifacts))) {
      throw new Error('Passed checkpoint phase lacks fresh original execution evidence.');
    }
    for (const input of [phase.log, ...(phase.artifacts ?? [])].filter(Boolean)) {
      const file = inputPath(ctx, input);
      if (phase.status === 'Passed' && !existsSync(file)) throw new Error('Passed checkpoint input is missing.');
      inputs.push([file, existsSync(file) ? digest(tree(file)) : null]);
    }
  }
  const exclude = new Set([path.join(ctx.evidence, 'active.lock'), path.join(ctx.evidence, `attempt-${attempt}.checkpoint.json`)]);
  return digest([tree(ctx.evidence, exclude), inputs]);
}

export async function beginWorkstationAttempt(workspace, evidence, identity, resume) {
  const ctx = context(workspace, evidence, identity);
  let attempt = 1, startedUtc = new Date().toISOString();
  if (resume) {
    if (!existsSync(ctx.evidence)) throw new Error('Original workstation checkpoint is missing; no automatic full rerun.');
    const owner = json(path.join(ctx.evidence, 'owner.json'));
    if (digest(owner) !== digest(ctx.owner)) throw new Error('Candidate, workstation, workspace or toolchain changed; no resume.');
    if (existsSync(path.join(ctx.evidence, 'active.lock'))) throw new Error('An unfinished workstation attempt owns this evidence; inspect its diagnostics.');
    const attempts = readdirSync(ctx.evidence).map(name => Number(name.match(/^attempt-([1-9][0-9]*)\.started\.json$/)?.[1] ?? 0));
    const prior = Math.max(0, ...attempts), checkpointFile = path.join(ctx.evidence, `attempt-${prior}.checkpoint.json`);
    if (!prior || !existsSync(checkpointFile)) throw new Error('Closed workstation checkpoint is missing; no automatic full rerun.');
    const previous = json(checkpointFile);
    startedUtc = json(path.join(ctx.evidence, 'invocation.json')).startedUtc;
    if (!fresh(startedUtc) || !fresh(previous.completedUtc) || previous.exitCode === 0) throw new Error('Checkpoint is stale, future-dated or already successful.');
    if (previous.evidenceHash !== snapshot(ctx, prior) || previous.dockerImagesHash !== ctx.dockerImagesHash ||
        previous.cache.sha256 !== (await controlledCacheSnapshot(ctx.workspace)).sha256) {
      throw new Error('Original state, logs, artifacts or cached outputs changed; no resume.');
    }
    attempt = prior + 1;
  } else {
    if (existsSync(ctx.evidence)) throw new Error('Evidence directory already exists. Preserve it and choose a new directory, or explicitly resume its checkpoint.');
    mkdirSync(ctx.evidence, { recursive: true });
    writeFileSync(path.join(ctx.evidence, 'owner.json'), JSON.stringify(ctx.owner), { flag: 'wx' });
    writeFileSync(path.join(ctx.evidence, 'invocation.json'), JSON.stringify({ executionMode: 'manual-workstation', separateFromHostedCI: true,
      sourceSha: identity.sourceSha, configuration: 'Release', startedUtc }), { flag: 'wx' });
  }
  writeFileSync(path.join(ctx.evidence, 'active.lock'), JSON.stringify({ attempt }), { flag: 'wx' });
  const attemptStartedUtc = new Date().toISOString();
  writeFileSync(path.join(ctx.evidence, `attempt-${attempt}.started.json`), JSON.stringify({ attempt, startedUtc: attemptStartedUtc }), { flag: 'wx' });
  return { attempt, startedUtc, attemptStartedUtc, resume, processDirectory: attempt === 1 ? 'process' : `process-${attempt}`,
    resultFile: attempt === 1 ? 'process-result.json' : `attempt-${attempt}.result.json` };
}

export async function finishWorkstationAttempt(workspace, evidence, identity, plan, exitCode) {
  const ctx = context(workspace, evidence, identity);
  if (digest(json(path.join(ctx.evidence, 'owner.json'))) !== digest(ctx.owner) ||
      json(path.join(ctx.evidence, 'active.lock')).attempt !== plan.attempt || !Number.isInteger(exitCode)) throw new Error('Workstation identity changed before checkpointing.');
  if (exitCode === 0 && candidateFindings(json(ctx.stateFile), identity.sourceSha).length) {
    throw new Error('Successful workstation child lacks complete passing candidate evidence.');
  }
  const result = { executionMode: 'manual-workstation', sourceSha: identity.sourceSha, status: exitCode === 0 ? 'passed' : 'failed',
    exitCode, startedUtc: plan.startedUtc, attemptStartedUtc: plan.attemptStartedUtc, completedUtc: new Date().toISOString() };
  result.attempts = Array.from({ length: plan.attempt }, (_, index) => {
    const attempt = index + 1;
    const previous = attempt === plan.attempt ? result : json(path.join(ctx.evidence, attempt === 1 ? 'process-result.json' : `attempt-${attempt}.result.json`));
    return { attempt, startedAt: previous.attemptStartedUtc, completedAt: previous.completedUtc,
      exitCode: previous.exitCode, status: previous.status };
  });
  writeFileSync(path.join(ctx.evidence, plan.resultFile), JSON.stringify(result, null, 2), { flag: 'wx' });
  writeFileSync(path.join(ctx.evidence, `attempt-${plan.attempt}.state.json`), readFileSync(ctx.stateFile), { flag: 'wx' });
  // Failed producers may reuse their report paths on recovery. Preserve those original payloads
  // before permitting another attempt, as well as the failed state and process diagnostics.
  const retained = [];
  for (const phase of json(ctx.stateFile).phases.filter(phase => phase.status === 'Failed')) {
    for (const input of phase.artifacts ?? []) {
      const file = inputPath(ctx, input);
      if (!existsSync(file) || retained.some(entry => entry.original === file)) continue;
      const sha256 = digest(tree(file));
      const relative = `attempt-${plan.attempt}.failed-artifacts/${retained.length}/${path.basename(file)}`;
      const target = path.join(ctx.evidence, relative);
      // Do not recursively copy an artifact containing this evidence directory into itself.
      const containsEvidence = path.relative(file, ctx.evidence);
      if (!containsEvidence || !path.isAbsolute(containsEvidence) && !containsEvidence.split(path.sep).includes('..')) {
        throw new Error('A failed artifact contains the workstation evidence directory; inspect it before recovery.');
      }
      cpSync(file, target, { recursive: true, errorOnExist: true, force: false });
      if (digest(tree(target)) !== sha256) throw new Error('Failed artifact changed while preserving its original payload.');
      retained.push({ original: file, retained: relative, sha256 });
    }
  }
  writeFileSync(path.join(ctx.evidence, `attempt-${plan.attempt}.failed-artifacts.json`), JSON.stringify(retained, null, 2), { flag: 'wx' });
  // A killed process without valid state cannot be silently converted into a fresh full run.
  const checkpoint = { schemaVersion: 1, attempt: plan.attempt, exitCode, completedUtc: result.completedUtc, dockerImagesHash: ctx.dockerImagesHash,
    evidenceHash: snapshot(ctx, plan.attempt), cache: await controlledCacheSnapshot(ctx.workspace) };
  writeFileSync(path.join(ctx.evidence, `attempt-${plan.attempt}.checkpoint.json`), JSON.stringify(checkpoint, null, 2), { flag: 'wx' });
  unlinkSync(path.join(ctx.evidence, 'active.lock'));
  return checkpoint;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [mode, workspace, evidence, identityFile, planFile, exitCode] = process.argv.slice(2);
    if (mode === 'begin' && process.argv.length === 6) console.log(JSON.stringify(await beginWorkstationAttempt(workspace, evidence, json(identityFile), false)));
    else if (mode === 'resume' && process.argv.length === 6) console.log(JSON.stringify(await beginWorkstationAttempt(workspace, evidence, json(identityFile), true)));
    else if (mode === 'finish' && process.argv.length === 8) console.log(JSON.stringify(await finishWorkstationAttempt(workspace, evidence, json(identityFile), json(planFile), Number(exitCode))));
    else throw new Error('Usage: workstation-resume.mjs begin|resume <workspace> <evidence> <identity.json> | finish <workspace> <evidence> <identity.json> <plan.json> <exit-code>');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
