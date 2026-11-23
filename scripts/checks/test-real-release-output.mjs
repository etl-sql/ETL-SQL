// Release collectors replayed on real recorded output (TODO W4). Each fixture under
// fixtures/real-release holds the GitHub API responses a collector read for a real candidate, and the
// evidence file the real release run kept. The current collector must reproduce that file exactly.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectHostedEvidence, hostedPolicy } from '../collect-hosted-release-evidence.mjs';
import { collectReleaseEvidence, sameSelection } from '../collect-release-evidence.mjs';
import { findCertifiedDeb } from '../find-certified-deb.mjs';
import { checkWindowsPackageOrigin } from '../check-windows-package-origin.mjs';

const fixture = name => JSON.parse(readFileSync(new URL(`./fixtures/real-release/${name}`, import.meta.url), 'utf8'));
const replay = responses => endpoint => {
  if (!(endpoint in responses)) throw new Error(`Not in the recording: ${endpoint}`);
  return structuredClone(responses[endpoint]);
};

// Hosted CI and CodeQL: release dry run 42699535102 on cd6870b93, 2026-11-09.
{
  const recorded = fixture('hosted-cd6870b93.json');
  const expected = fixture('hosted-cd6870b93.expected.json');
  const history = { isAncestor: () => false, changedPaths: () => ({ paths: [] }) };
  const actual = await collectHostedEvidence(recorded.repository, recorded.sha, {
    query: replay(recorded.responses), now: Date.parse(recorded.now), policy: recorded.policy, history });
  assert.deepEqual(actual, expected);
  // The release policy has since gained jobs that candidate's full run predates (Engine Certification
  // in W4b, the Portal Regression workers in RV02), so the same real data no longer certifies.
  await assert.rejects(collectHostedEvidence(recorded.repository, recorded.sha, {
    query: replay(recorded.responses), now: Date.parse(recorded.now), policy: hostedPolicy, history }),
  error => error.code === 'INCOMPLETE_COVERAGE' && /Engine Certification|Portal Regression/.test(error.message));
}

// DEB selection: the same dry run logged "Reusing certified DEB run 42688144873, artifact 13707941967."
{
  const recorded = fixture('deb-cd6870b93.json');
  const select = (overrides = {}) => findCertifiedDeb({ repository: recorded.repository, sha: recorded.sha, version: recorded.version,
    previousTag: recorded.previousTag, now: Date.parse(recorded.now), query: replay(recorded.responses),
    readReceipt: (run, artifact) => structuredClone(recorded.receipts[`${run.id}/${artifact.id}`]), ...overrides });
  const selected = await select();
  assert.deepEqual(selected, recorded.selected);
  assert.deepEqual([selected.run_id, selected.artifact_id], ['42688144873', '13707941967']);
  // Eight days later the same certification is out of the seven-day window.
  assert.equal(await select({ now: Date.parse(recorded.now) + 8 * 86400000 }), null);
  // A previous release would make it an upgrade candidate that this fresh-install receipt never certified.
  await assert.rejects(select({ previousTag: 'v0.21.0' }), /current predecessor/);
}

// MSI selection: the same dry run logged "Reusing MSI certification run 42688106750, attempt 1,
// artifact 13708086760." Find-CertifiedMsi.ps1 runs with gh answering from the recording.
{
  const recording = fileURLToPath(new URL('./fixtures/real-release/msi-cd6870b93.json', import.meta.url));
  const helper = fileURLToPath(new URL('./fixtures/real-release/Invoke-RecordedMsiFinder.ps1', import.meta.url));
  const recorded = fixture('msi-cd6870b93.json');
  const find = ({ now = recorded.now, previousTag = '' } = {}) => {
    const directory = mkdtempSync(path.join(tmpdir(), 'real-msi-'));
    try {
      const output = path.join(directory, 'github-output.txt');
      const result = spawnSync('pwsh', ['-NoProfile', '-File', helper, '-Recording', recording, '-OutputDirectory', directory,
        '-Now', now, '-PreviousTag', previousTag], { encoding: 'utf8', windowsHide: true, timeout: 120000, env: { ...process.env, GITHUB_OUTPUT: output } });
      assert.equal(result.error, undefined);
      return { status: result.status, text: result.stdout + result.stderr,
        outputs: existsSync(output) ? readFileSync(output, 'utf8').trim().split(/\r?\n/) : [] };
    } finally { rmSync(directory, { recursive: true, force: true }); }
  };
  const selected = find();
  assert.equal(selected.status, 0, selected.text);
  assert.deepEqual(selected.outputs, ['artifact_id=13708086760', 'run_id=42688106750', 'receipt_id=13708321770']);
  assert.match(selected.text, /Reusing MSI certification run 42688106750, attempt 1, artifact 13708086760\./);
  const expired = find({ now: new Date(Date.parse(recorded.now) + 8 * 86400000).toISOString() });
  assert.equal(expired.status, 0, expired.text);
  assert.deepEqual(expired.outputs, []);
  assert.match(expired.text, /No eligible pre-tag MSI certification found/);
  const upgrade = find({ previousTag: 'v0.21.0' });
  assert.deepEqual(upgrade.outputs, [], 'A fresh-install receipt never certifies an upgrade from v0.21.0.');
}

// Windows package origin: the real MSI inputs (candidate.json of MSI run 42688106750) and the real
// native selection the dry run kept name one shared Windows payload, and the published draft carried
// exactly the certified MSI.
{
  const msi = fixture('msi-certification-inputs-cd6870b93.json');
  const native = fixture('certified-windows-selection-cd6870b93.json');
  const draft = fixture('verified-draft-cd6870b93.json');
  const sha = 'cd6870b930f4dbdaecf5aab3f224070ab7afe803';
  checkWindowsPackageOrigin(msi, native, sha, '0.22.0');
  assert.throws(() => checkWindowsPackageOrigin(msi, native, sha, '0.22.1'), /same shared payload/);
  assert.throws(() => checkWindowsPackageOrigin(msi, { ...native, windowsPayloadSha256: 'f'.repeat(64) }, sha, '0.22.0'), /same shared payload/);
  assert.equal(draft.sourceSha, sha);
  assert.equal(draft.assets['ETL-SQL-v0.22.0-x64-Setup.msi'].sha256, msi.files.find(file => file.name === 'current.msi').sha256.toLowerCase());
}

// Release Evidence and Tag Release approval: v0.22.0 candidate 0a1507282 (Release Evidence 43786591392,
// Tag Release 43888292114, Release run 43893010630, and dry run 43871241433 on 2026-11-16).
{
  const recorded = fixture('release-evidence-0a1507282.json');
  const expected = fixture('release-evidence-0a1507282.expected.json');
  const actual = await collectReleaseEvidence(recorded.repository, recorded.sha, {
    query: replay(recorded.responses), now: Date.parse(recorded.now), policy: recorded.policy, requireApproval: true });
  assert.deepEqual(actual, expected);
  sameSelection(expected, actual);

  const rehearsed = await collectReleaseEvidence(recorded.repository, recorded.sha, {
    query: replay(recorded.responses), now: Date.parse(recorded.now), policy: recorded.policy, rehearsalBranch: recorded.branch });
  assert.equal(rehearsed.run.id, 43786591392);
  assert.equal(rehearsed.run.attempt, 2);
  assert.equal(rehearsed.run.jobs.length, 40);
  assert.equal(rehearsed.run.jobs.find(job => job.name === 'Scale Huge ScalarSubqueryCache')?.attempt, 2);
  assert.equal(rehearsed.artifacts.length, 39);
  assert.equal(rehearsed.codeql.openAlerts, 0);
  assert.equal(rehearsed.approval, null);

  const alertsEndpoint = `repos/${recorded.repository}/code-scanning/alerts?state=open&ref=${encodeURIComponent(`refs/heads/${recorded.branch}`)}&per_page=100`;
  const withOpenAlert = structuredClone(recorded.responses);
  withOpenAlert[alertsEndpoint] = [[{ number: 586 }]];
  await assert.rejects(collectReleaseEvidence(recorded.repository, recorded.sha, {
    query: replay(withOpenAlert), now: Date.parse(recorded.now), policy: recorded.policy, requireApproval: true }), /open alert/);
  await assert.rejects(collectReleaseEvidence(recorded.repository, recorded.sha, {
    query: replay(withOpenAlert), now: Date.parse(recorded.now), policy: recorded.policy, rehearsalBranch: recorded.branch }), /open alert/);

  const withExpiredArtifact = structuredClone(recorded.responses);
  withExpiredArtifact[`repos/${recorded.repository}/actions/runs/43786591392/artifacts?per_page=100`][0].artifacts[0].expired = true;
  await assert.rejects(collectReleaseEvidence(recorded.repository, recorded.sha, {
    query: replay(withExpiredArtifact), now: Date.parse(recorded.now), policy: recorded.policy, requireApproval: true }), /expired/);
}

console.log('Real release output: hosted CI/CodeQL evidence, Release Evidence selection (0a1507282) and the DEB selection for dry run 42699535102 reproduced exactly from their recorded GitHub responses; its real MSI and native Windows selections pass the shared-payload check and match the draft.');
