import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createOperatorEvidence, verifyOperatorEvidence, operatorPolicy } from '../operator-release-evidence.mjs';
import { operatorComparison, previousOperatorRelease } from '../operator-release-comparison.mjs';
import { operatorFixture } from './fixtures/operator-bundle.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'operator-bundle-'));
const sha = 'a'.repeat(40), version = '1.2.3', now = Date.now(), options = { now, semanticValidator: () => {} };
const script = fileURLToPath(new URL('../operator-release-evidence.mjs', import.meta.url));
let next = 0;
function input() { const directory = path.join(root, `input-${next++}`); operatorFixture(directory, { sha, version, now }); return directory; }
function change(directory, mutate) { const file = path.join(directory, 'checklist.json'), value = JSON.parse(readFileSync(file)); mutate(value); writeFileSync(file, JSON.stringify(value)); }
try {
  const original = input(), bundle = path.join(root, 'bundle');
  const receipt = createOperatorEvidence(original, bundle, sha, version, options);
  assert.equal(receipt.scope, 'operator-submitted-release-checklist'); assert.equal(receipt.fullReleaseCertification, false);
  assert.deepEqual(verifyOperatorEvidence(bundle, sha, version, options), receipt);
  assert.throws(() => createOperatorEvidence(original, bundle, sha, version, options));
  for (const mutate of [c => { c.sourceSha = 'd'.repeat(40); }, c => { c.dirty = 'false'; }, c => { c.version = '1.2.4'; },
    c => { c.sourceFingerprint = '0'.repeat(64); }, c => { c.reviewedBy = ''; }, c => { c.comparisonBase = sha; },
    c => { c.changedPathsSha256 = ''; }, c => { c.reviewedAt = 'invalid'; }, c => { c.generatedAt = new Date(now + 1000).toISOString(); },
    c => { c.reviewedAt = new Date(now - 8 * 86400000).toISOString(); }, c => { c.generatedAt = new Date(now - 3000).toISOString(); },
    c => { c.uncovered = ['No gaps']; }, c => { c.claims.sharedSaas = true; }, c => { c.claims.ha = true; }, c => { c.claims.billionRow = true; },
    c => { c.reports[0].path = '../escape.json'; }, c => { c.reports[2].cliVersionLog = ''; }, c => { c.reviews[0].status = 'Pending'; },
    c => { c.reviews[0].evidence = []; }, c => { c.reviews[0].reason = ''; }, c => { c.certifications[0].status = 'Skipped'; },
    c => { c.certifications[0].status = 'Passed'; }, c => { c.certifications[0].evidence = ['evidence/missing.log']; },
    c => { c.certifications[0].topology = ''; }, c => { c.certifications[0].command = ''; }]) {
    const directory = input(); change(directory, mutate);
    assert.throws(() => createOperatorEvidence(directory, path.join(root, `reject-${next}`), sha, version, options));
  }
  for (const [key, ids] of [['reports', operatorPolicy.requiredReports], ['reviews', operatorPolicy.requiredReviews], ['certifications', operatorPolicy.conditionalCertifications]]) {
    for (const id of ids) {
      const directory = input(); change(directory, c => { c[key] = c[key].filter(row => row.id !== id); });
      assert.throws(() => createOperatorEvidence(directory, path.join(root, `missing-${next}`), sha, version, options));
    }
    for (const duplicate of [true, false]) {
      const directory = input(); change(directory, c => { if (duplicate) c[key][1] = c[key][0]; else c[key][0].id = 'unknown'; });
      assert.throws(() => createOperatorEvidence(directory, path.join(root, `inventory-${next}`), sha, version, options));
    }
  }
  for (const mutation of [null, report => { report.status = 'Failed'; }, report => { report.passed = 'true'; },
    report => { report.commit = 'd'.repeat(40); }, report => { report.dirty = true; }, report => { report.configuration = 'Debug'; }]) {
    const directory = input();
    change(directory, c => {
      const row = c.certifications.find(row => row.id === 'stress-scale'); row.status = 'Passed'; row.evidence = ['evidence/stress.json'];
      row.execution = { sourceSha: sha, configuration: 'Release', exitCode: 0, host: 'fixture-host', startedAt: new Date(now - 6000).toISOString(), completedAt: new Date(now - 5000).toISOString() };
    });
    const report = { status: 'Passed', commit: sha, dirty: false, configuration: 'Release', generatedAt: new Date(now - 4000).toISOString() };
    if (mutation) mutation(report); writeFileSync(path.join(directory, 'evidence/stress.json'), JSON.stringify(report));
    const execute = () => createOperatorEvidence(directory, path.join(root, `conditional-${next}`), sha, version, options);
    if (mutation) assert.throws(execute); else execute();
  }
  for (const name of ['evidence/extra.log', 'checklist.json']) {
    const changed = path.join(root, `changed-${next++}`); cpSync(bundle, changed, { recursive: true });
    writeFileSync(path.join(changed, name), 'changed after review'); assert.throws(() => verifyOperatorEvidence(changed, sha, version, options));
  }
  for (const [name, bytes] of [['evidence/raw.zip', 'backup'], ['evidence/runtime.db', 'state'], ['evidence/key.log', 'SECRET:fixture-value'],
    ['evidence/binary.log', Buffer.from([0, 1])], ['evidence/appsettings.secrets.json', '{}']]) {
    const directory = input(); writeFileSync(path.join(directory, name), bytes);
    assert.throws(() => createOperatorEvidence(directory, path.join(root, `unsafe-${next}`), sha, version, options));
  }
  const linked = path.join(root, 'linked'); symlinkSync(original, linked, process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => createOperatorEvidence(linked, path.join(root, 'linked-output'), sha, version, options));
  const existing = readFileSync(path.join(bundle, 'manifest.json'));
  assert.throws(() => createOperatorEvidence(original, path.join(root, 'semantic-failure'), sha, version, { ...options, semanticValidator: () => { throw new Error('Invalid detailed reports'); } }));
  assert.throws(() => createOperatorEvidence(original, path.join(original, 'nested-output'), sha, version, options));
  const drifting = input();
  assert.throws(() => createOperatorEvidence(drifting, path.join(root, 'drifting-output'), sha, version, { ...options,
    semanticValidator: directory => writeFileSync(path.join(directory, 'evidence/reviews/source-and-security.md'), 'Changed while validating reports') }));
  assert.deepEqual(readFileSync(path.join(bundle, 'manifest.json')), existing);
  // A structurally sealed synthetic payload cannot bypass real report validation through the public CLI.
  const cli = spawnSync(process.execPath, [script, 'verify', bundle, sha, version], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.ifError(cli.error); assert.notEqual(cli.status, 0);

  const published = [{ tag_name: 'v1.1.0', draft: false, prerelease: false }, { tag_name: 'v1.2.2', draft: false, prerelease: false },
    { tag_name: 'v1.2.3', draft: false, prerelease: false }, { tag_name: 'v1.2.9', draft: false, prerelease: false },
    { tag_name: 'v1.2.2-rc.1', draft: false, prerelease: true }, { tag_name: 'v1.2.2', draft: true, prerelease: false }];
  assert.equal(previousOperatorRelease(published, version), 'v1.2.2');
  assert.throws(() => previousOperatorRelease([], version)); assert.throws(() => previousOperatorRelease(published, '01.2.3'));
  const repository = path.join(root, 'checkout [候補]'); mkdirSync(repository);
  const git = args => { const result = spawnSync('git', args, { cwd: repository, encoding: 'utf8', windowsHide: true }); assert.equal(result.status, 0, result.stderr); return result.stdout.trim(); };
  git(['init', '-q']); git(['config', 'user.name', 'Operator fixture']); git(['config', 'user.email', 'fixture@example.invalid']); git(['config', 'commit.gpgsign', 'false']); git(['config', 'core.hooksPath', path.join(root, 'no-hooks')]);
  writeFileSync(path.join(repository, 'source [é].cs'), 'original'); git(['add', '.']); git(['commit', '-qm', 'base']); const base = git(['rev-parse', 'HEAD']);
  git(['mv', 'source [é].cs', 'renamed source.cs']); git(['commit', '-qm', 'candidate']); const candidate = git(['rev-parse', 'HEAD']);
  const execute = args => spawnSync('git', args, { cwd: repository, windowsHide: true });
  git(['tag', 'v1.2.2', base]);
  const comparison = operatorComparison(candidate, 'v1.2.2', { execute }); assert.equal(comparison.comparisonBase, base);
  assert.throws(() => operatorComparison(candidate, candidate, { execute }));
  const template = path.join(root, 'pending [é]');
  const result = spawnSync(process.execPath, [script, 'template', candidate, version, 'v1.2.2', template], { cwd: repository, encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, result.stderr); const pending = JSON.parse(readFileSync(path.join(template, 'checklist.json')));
  assert.ok(pending.reviews.every(row => row.status === 'Pending') && pending.certifications.every(row => row.status === 'Pending'));
  assert.deepEqual({ comparisonBase: pending.comparisonBase, changedPathsSha256: pending.changedPathsSha256, previousStableTag: pending.previousStableTag }, comparison);
  assert.throws(() => createOperatorEvidence(template, path.join(root, 'pending-output'), candidate, version, options));
  console.log('Operator bundle: candidate/freshness/scope, every required row, applicability, source comparison, immutable inventory, unsafe inputs and public-CLI semantic rejection pass.');
} finally { rmSync(root, { recursive: true, force: true }); }
