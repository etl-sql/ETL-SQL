// Operator-submitted evidence complements hosted CI, workstation and package certification.
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { closeSync, fstatSync, lstatSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { operatorComparison, previousOperatorRelease } from './operator-release-comparison.mjs';

export const operatorPolicy = JSON.parse(readFileSync(new URL('./operator-release-policy.json', import.meta.url), 'utf8'));
function readChecked(file) {
  const descriptor = openSync(file, 'r');
  try {
    const info = fstatSync(descriptor), entry = lstatSync(file);
    if (!info.isFile() || !entry.isFile() || entry.isSymbolicLink() || info.dev !== entry.dev || info.ino !== entry.ino ||
        info.size < 1 || info.size > operatorPolicy.maximumFileBytes) throw new Error('Invalid operator evidence file handle or size.');
    return readFileSync(descriptor);
  } finally { closeSync(descriptor); }
}
const json = file => JSON.parse(readChecked(file).toString('utf8').replace(/^\uFEFF/, ''));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const scope = 'operator-submitted-release-checklist';
const policySha256 = hash(readFileSync(new URL('./operator-release-policy.json', import.meta.url)));
const nonempty = value => typeof value === 'string' && value.trim().length > 0;

function file(root, relative) {
  if (!lstatSync(root).isDirectory() || lstatSync(root).isSymbolicLink()) throw new Error('Operator bundle root must be a real directory.');
  if (typeof relative !== 'string' || !relative || /[\\\0:]/.test(relative) || path.isAbsolute(relative) ||
      relative.split('/').some(part => !part || part === '.' || part === '..')) throw new Error('Invalid operator evidence path.');
  let current = root;
  for (const part of relative.split('/')) {
    current = path.join(current, part);
    if (lstatSync(current).isSymbolicLink()) throw new Error('Operator evidence cannot traverse links.');
  }
  if (!lstatSync(current).isFile()) throw new Error('Operator evidence must reference a regular file.');
  return current;
}

function inventory(directory, prefix = '', counts) {
  if (!lstatSync(directory).isDirectory() || lstatSync(directory).isSymbolicLink()) throw new Error('Operator bundle must use real directories.');
  const files = [];
  const names = readdirSync(directory).sort();
  counts ??= { entries: 0, files: 0, reserve: names.includes('manifest.json') ? 2 : 3, fileReserve: names.includes('manifest.json') ? 0 : 1 };
  for (const name of names) {
    if (++counts.entries + counts.reserve > operatorPolicy.maximumArchiveEntries) throw new Error('Operator evidence archive inventory is too large.');
    const relative = prefix ? `${prefix}/${name}` : name, current = path.join(directory, name), stat = lstatSync(current);
    if (stat.isSymbolicLink()) throw new Error('Operator bundle cannot contain links.');
    if (stat.isDirectory()) {
      const children = inventory(current, relative, counts);
      if (!children.length) throw new Error('Operator bundle cannot contain empty directories.');
      files.push(...children);
    } else {
      file(directory, name);
      files.push(relative);
      if (++counts.files + counts.fileReserve > operatorPolicy.maximumFiles) throw new Error('Operator evidence file inventory is too large.');
    }
  }
  return files;
}

function timestamp(value, now, description) {
  const time = typeof value === 'string' && value.length <= 64 ? Date.parse(value) : NaN;
  if (!Number.isFinite(time) || time > now || now - time > operatorPolicy.maximumAgeDays * 86400000) throw new Error(`Missing, stale or future operator evidence time: ${description}.`);
  return time;
}

function rows(actual, required, description) {
  if (!Array.isArray(actual) || actual.length !== required.length ||
      new Set(actual.map(row => row?.id)).size !== required.length || actual.some(row => !required.includes(row?.id))) {
    throw new Error(`Operator checklist has missing, duplicate or unknown ${description}.`);
  }
  return actual;
}

export function operatorChecklistFindings(checklist, sha, version, { now = Date.now() } = {}) {
  // Throwing before report validation makes all incomplete review records cheap failures.
  if (!/^[a-f0-9]{40}$/.test(sha ?? '') || typeof version !== 'string' || version.length > 64 || !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version) ||
      checklist?.schemaVersion !== 1 || checklist.scope !== scope || checklist.sourceSha !== sha || checklist.version !== version ||
      checklist.dirty !== false || checklist.sourceFingerprint !== hash(sha) || !nonempty(checklist.reviewedBy) || checklist.reviewedBy.length > 128 ||
      !/^[a-f0-9]{40}$/.test(checklist.comparisonBase ?? '') || checklist.comparisonBase === sha ||
      !/^[a-f0-9]{64}$/.test(checklist.changedPathsSha256 ?? '') ||
      !/^refs\/heads\/(?:main|release\/[\w./-]+)$/.test(checklist.candidateRef ?? '') || checklist.candidateRef.length > 1024 || checklist.candidateRef.includes('..')) throw new Error('Invalid operator checklist candidate, review or comparison identity.');
  const reviewed = timestamp(checklist.reviewedAt, now, 'review'), generated = timestamp(checklist.generatedAt, now, 'generation');
  previousOperatorRelease([{ tag_name: checklist.previousStableTag, draft: false, prerelease: false }], version);
  if (generated < reviewed) throw new Error('Operator checklist generation precedes review.');
  if (JSON.stringify(checklist.uncovered) !== JSON.stringify(operatorPolicy.uncovered)) throw new Error('Operator checklist must retain its canonical uncovered scope.');
  if (checklist.claims?.sharedSaas !== false || typeof checklist.claims?.ha !== 'boolean' || typeof checklist.claims?.billionRow !== 'boolean') throw new Error('Unsupported or missing release claim boundaries.');
  const references = [];
  for (const row of rows(checklist.reports, operatorPolicy.requiredReports, 'reports')) {
    if (!nonempty(row.path)) throw new Error('Required operator report is missing.');
    references.push(row.path);
    if (row.id === 'recovery') {
      if (!nonempty(row.cliVersionLog) || !nonempty(row.transcript)) throw new Error('Recovery invocation evidence is missing.');
      references.push(row.cliVersionLog, row.transcript);
    }
  }
  for (const row of rows(checklist.reviews, operatorPolicy.requiredReviews, 'reviews')) {
    if (row.status !== 'Passed' || !nonempty(row.reason) || !Array.isArray(row.evidence) || !row.evidence.length || row.evidence.some(value => !nonempty(value))) throw new Error('Operator review must pass with a reason and retained evidence.');
    references.push(...row.evidence);
  }
  for (const row of rows(checklist.certifications, operatorPolicy.conditionalCertifications, 'conditional certifications')) {
    if (!['Passed', 'NotApplicable'].includes(row.status) || !nonempty(row.reason) || !nonempty(row.topology) || !nonempty(row.command) ||
        !Array.isArray(row.evidence) || row.evidence.some(value => !nonempty(value))) throw new Error('Conditional operator decision is incomplete.');
    if (row.status === 'Passed' && row.evidence.length === 0 || row.status === 'NotApplicable' && row.evidence.length !== 0) throw new Error('Conditional certification evidence does not match its applicability.');
    if (row.status === 'Passed') {
      const execution = row.execution;
      if (execution?.sourceSha !== sha || execution.configuration !== 'Release' || execution.exitCode !== 0 || !nonempty(execution.host)) throw new Error('Operator-recorded certification execution is incomplete.');
      const start = timestamp(execution.startedAt, now, row.id), end = timestamp(execution.completedAt, now, row.id);
      if (end < start || end > reviewed) throw new Error('Conditional certification lies outside its review window.');
    } else if (row.execution !== undefined) throw new Error('Not-applicable certification cannot contain an execution receipt.');
    references.push(...row.evidence);
  }
  for (const [claim, ids] of [['ha', ['ha-fault-recovery', 'sustained-ha-capacity']], ['billionRow', ['billion-row']]]) {
    if (checklist.claims[claim] && ids.some(id => checklist.certifications.find(row => row.id === id).status !== 'Passed')) throw new Error('Release claim lacks its applicable operator certification.');
  }
  return { references: [...new Set(references)], reviewed, generated };
}

function validateReports(directory, sha, version) {
  const script = fileURLToPath(new URL('./Test-OperatorReportInputs.ps1', import.meta.url));
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, '-Directory', directory,
    '-CandidateSha', sha, '-Version', version], { encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 1024 * 1024 });
  // Do not echo report contents or paths from provider diagnostics into the release console.
  if (result.error || result.status !== 0) throw new Error('Operator report semantic validation failed; inspect the retained reports with Test-OperatorReportInputs.ps1.');
}

function payload(root, names) {
  let total = 0;
  return names.map(name => {
    const source = file(root, name), stat = lstatSync(source);
    total += stat.size;
    if (stat.size === 0 || stat.size > operatorPolicy.maximumFileBytes || total > operatorPolicy.maximumPayloadBytes ||
        !/\.(json|md|log|trx|txt|xml|csv)$/i.test(name) || /(?:^|\/)(?:appsettings(?:\..+)?|initial-admin-credentials|.*\.(?:secrets|protected))\./i.test(name)) throw new Error('Operator evidence must contain bounded reports, not runtime state or backups.');
    const bytes = readChecked(source), text = bytes.toString('utf8');
    if (bytes.includes(0) || /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:ENC|SECRET):|\bgh[pousr]_[A-Za-z0-9]{36,}/.test(text)) throw new Error('Operator evidence contains prohibited secret material.');
    return { path: name, sha256: hash(bytes), bytes: bytes.length };
  });
}

function inspect(directory, sha, version, now, semanticValidator) {
  const checklistBytes = readChecked(file(directory, 'checklist.json'));
  const checklist = JSON.parse(checklistBytes.toString('utf8').replace(/^\uFEFF/, ''));
  const checked = operatorChecklistFindings(checklist, sha, version, { now });
  const names = inventory(directory).filter(name => name !== 'manifest.json');
  if (names.some(name => name !== 'checklist.json' && !name.startsWith('evidence/'))) throw new Error('Unexpected operator bundle file.');
  for (const reference of checked.references) {
    if (!reference.startsWith('evidence/') || !names.includes(reference)) throw new Error('Operator checklist references missing evidence.');
  }
  const files = payload(directory, names);
  if (files.find(item => item.path === 'checklist.json')?.sha256 !== hash(checklistBytes)) throw new Error('Operator checklist changed during validation.');
  for (const row of checklist.certifications.filter(row => row.status === 'Passed')) {
    const reports = row.evidence.filter(name => name.endsWith('.json')).map(name => json(file(directory, name)));
    if (!reports.length) throw new Error('Passed operator certification needs its machine-readable report.');
    for (const report of reports) {
      if (!report || typeof report !== 'object' || Array.isArray(report) ||
          ['status', 'result'].some(key => Object.hasOwn(report, key) && !['Passed', 'Pass'].includes(report[key])) ||
          ['testsPassed', 'passed'].some(key => Object.hasOwn(report, key) && report[key] !== true) ||
          Object.hasOwn(report, 'dirty') && report.dirty !== false ||
          report.commit && typeof report.commit === 'object' && Object.hasOwn(report.commit, 'isDirty') && report.commit.isDirty !== false ||
          Object.hasOwn(report, 'configuration') && report.configuration !== 'Release') throw new Error('Retained conditional report contradicts its passed review.');
      for (const candidate of [report.sourceSha, typeof report.commit === 'string' ? report.commit : report.commit?.sha]) {
        if (candidate !== undefined && candidate !== sha) throw new Error('Conditional report names a different candidate.');
      }
      for (const generatedAt of [report.generatedAt, report.generatedUtc].filter(value => value !== undefined)) {
        if (timestamp(generatedAt, now, row.id) > checked.reviewed) throw new Error('Conditional report was generated after its review.');
      }
    }
    if (row.id === 'ha-fault-recovery' && !reports.some(report => report.schemaVersion === 1 && report.status === 'Passed' &&
        report.requiredGate === 'All' && Number.isInteger(report.checkedArtifactCount) && report.checkedArtifactCount > 0 &&
        Array.isArray(report.issues) && !report.issues.some(issue => issue.level === 'Error'))) throw new Error('HA review lacks an All-gate validation report.');
  }
  semanticValidator(directory, sha, version);
  const currentNames = inventory(directory).filter(name => name !== 'manifest.json');
  if (JSON.stringify(names) !== JSON.stringify(currentNames) || JSON.stringify(files) !== JSON.stringify(payload(directory, currentNames))) throw new Error('Operator evidence changed during report validation.');
  return { checklist, files };
}

export function createOperatorEvidence(input, directory, sha, version, { now = Date.now(), semanticValidator = validateReports } = {}) {
  const relativeOutput = path.relative(path.resolve(input), path.resolve(directory));
  if (!relativeOutput || !relativeOutput.startsWith(`..${path.sep}`) && relativeOutput !== '..' && !path.isAbsolute(relativeOutput)) throw new Error('Operator output must be outside its input bundle.');
  const checked = inspect(input, sha, version, now, semanticValidator);
  const manifest = { schemaVersion: 1, scope, sourceSha: sha, version, policySha256, files: checked.files };
  const manifestBytes = Buffer.from(JSON.stringify(manifest, null, 2) + '\n');
  if (manifestBytes.length > operatorPolicy.maximumFileBytes || checked.files.reduce((sum, item) => sum + item.bytes, 0) + manifestBytes.length + 32768 > operatorPolicy.maximumPayloadBytes) throw new Error('Operator bundle exceeds its archive payload budget.');
  // An input or prior accepted bundle is never overwritten, including failed validation.
  mkdirSync(directory);
  for (const item of checked.files) {
    const bytes = readChecked(file(input, item.path));
    if (hash(bytes) !== item.sha256) throw new Error('Operator evidence changed during collection.');
    const destination = path.join(directory, item.path); mkdirSync(path.dirname(destination), { recursive: true });
    writeFileSync(destination, bytes, { flag: 'wx' });
  }
  writeFileSync(path.join(directory, 'manifest.json'), manifestBytes, { flag: 'wx' });
  return verifyOperatorEvidence(directory, sha, version, { now, semanticValidator });
}

export function verifyOperatorEvidence(directory, sha, version, { now = Date.now(), semanticValidator = validateReports } = {}) {
  const manifestBytes = readChecked(file(directory, 'manifest.json'));
  const manifest = JSON.parse(manifestBytes.toString('utf8').replace(/^\uFEFF/, ''));
  if (manifest.schemaVersion !== 1 || manifest.scope !== scope || manifest.sourceSha !== sha || manifest.version !== version || manifest.policySha256 !== policySha256) throw new Error('Operator bundle candidate or policy mismatch.');
  const checked = inspect(directory, sha, version, now, semanticValidator);
  if (checked.files.reduce((sum, item) => sum + item.bytes, 0) + manifestBytes.length + 32768 > operatorPolicy.maximumPayloadBytes) throw new Error('Operator bundle exceeds its archive payload budget.');
  if (JSON.stringify(manifest.files) !== JSON.stringify(checked.files)) throw new Error('Operator evidence inventory or hashes changed.');
  if (hash(readChecked(file(directory, 'manifest.json'))) !== hash(manifestBytes)) throw new Error('Operator manifest changed during validation.');
  return { schemaVersion: 1, scope, fullReleaseCertification: false, sourceSha: sha, version, policySha256,
    manifestSha256: hash(manifestBytes),
    execution: { mode: 'operator-submission', completedAt: checked.checklist.generatedAt },
    comparisonBase: checked.checklist.comparisonBase, changedPathsSha256: checked.checklist.changedPathsSha256, previousStableTag: checked.checklist.previousStableTag,
    candidateRef: checked.checklist.candidateRef,
    reviewedBy: checked.checklist.reviewedBy, reviewedAt: checked.checklist.reviewedAt,
    uncovered: checked.checklist.uncovered };
}

export function operatorTemplate(sha, version, comparison) {
  return { schemaVersion: 1, scope, sourceSha: sha, version, candidateRef: `refs/heads/release/v${version}`, dirty: false, sourceFingerprint: hash(sha), ...comparison,
    reviewedBy: '', reviewedAt: null, generatedAt: null, uncovered: operatorPolicy.uncovered,
    claims: { sharedSaas: false, ha: false, billionRow: false },
    reports: operatorPolicy.requiredReports.map(id => ({ id, path: `evidence/${id}/${id === 'deployment' ? 'claims-index' : id === 'canary' ? 'certification' : 'recovery-report'}.json`,
      ...(id === 'recovery' ? { cliVersionLog: 'evidence/recovery/cli-version.log', transcript: 'evidence/recovery/validation.log' } : {}) })),
    reviews: operatorPolicy.requiredReviews.map(id => ({ id, status: 'Pending', reason: '', evidence: [`evidence/reviews/${id}.md`] })),
    certifications: operatorPolicy.conditionalCertifications.map(id => ({ id, status: 'Pending', reason: '', topology: '', command: '', evidence: [] })) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [mode, input, directoryOrSha, shaOrVersion, version] = process.argv.slice(2);
    if (mode === 'template' && process.argv.length === 7) {
      if (!/^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/.test(shaOrVersion ?? '')) throw new Error('Template requires the previous stable release tag.');
      const comparison = operatorComparison(input, shaOrVersion);
      const template = operatorTemplate(input, directoryOrSha, comparison);
      if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(directoryOrSha)) throw new Error('Invalid release version.');
      mkdirSync(version);
      writeFileSync(path.join(version, 'checklist.json'), JSON.stringify(template, null, 2) + '\n', { flag: 'wx' });
      console.log('Pending operator checklist created; no certification or review is marked passed.');
      process.exit(0);
    } else if (mode === 'create' && process.argv.length === 7) createOperatorEvidence(input, directoryOrSha, shaOrVersion, version);
    else if (mode === 'verify' && process.argv.length === 6) verifyOperatorEvidence(input, directoryOrSha, shaOrVersion);
    else throw new Error('Usage: operator-release-evidence.mjs template <sha> <version> <previous-tag> <new-input> | create <input> <new-bundle> <sha> <version> | verify <bundle> <sha> <version>');
    console.log('Operator checklist completeness, reports and retained hashes verified; submitted reviews do not establish trusted-host execution.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
