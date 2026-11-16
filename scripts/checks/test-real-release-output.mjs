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
  // The release policy has since gained Engine Certification (W4b), which that candidate's full run
  // predates, so the same real data no longer certifies under the current policy.
  await assert.rejects(collectHostedEvidence(recorded.repository, recorded.sha, {
    query: replay(recorded.responses), now: Date.parse(recorded.now), policy: hostedPolicy, history }),
  error => error.code === 'INCOMPLETE_COVERAGE' && /Engine Certification/.test(error.message));
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

console.log('Real release output: hosted CI/CodeQL evidence and the DEB selection for dry run 42699535102 reproduced exactly from their recorded GitHub responses; its real MSI and native Windows selections pass the shared-payload check and match the draft.');
