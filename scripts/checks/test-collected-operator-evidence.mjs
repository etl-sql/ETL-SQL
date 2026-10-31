import assert from 'node:assert/strict';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { collectOperatorEvidence, operatorAssetName, verifyOperatorAttachment } from '../collect-operator-release-evidence.mjs';
import { createOperatorEvidence } from '../operator-release-evidence.mjs';
import { operatorFixture } from './fixtures/operator-bundle.mjs';

const root = mkdtempSync(path.join(os.tmpdir(), 'collected-operator-'));
const sha = 'a'.repeat(40), base = 'b'.repeat(40), version = '1.2.3', now = Date.now(), time = offset => new Date(now + offset).toISOString();
const options = { now, semanticValidator: () => {} };
const previous = { id: 120, tag_name: 'v1.2.2', draft: false, prerelease: false, assets: [] };
const asset = { id: 456, name: operatorAssetName, state: 'uploaded', size: 100, digest: `sha256:${'c'.repeat(64)}`, created_at: time(-500), updated_at: time(-500) };
const release = { id: 123, tag_name: `operator-check-${sha}`, draft: true, target_commitish: sha, created_at: time(-900), assets: [asset] };
const releasesUrl = 'repos/owner/repo/releases?per_page=100', assetsUrl = 'repos/owner/repo/releases/123/assets?per_page=100';
const branchUrl = 'repos/owner/repo/branches/release%2Fv1.2.3', alertsUrl = 'repos/owner/repo/code-scanning/alerts?state=open&ref=refs%2Fheads%2Frelease%2Fv1.2.3&per_page=100';
let next = 0;
async function collect({ mutate = () => {}, transform = (_, value) => value, change = () => {}, expectedManifestSha256, comparisonReader = () => ({ comparisonBase: base, changedPathsSha256: 'c'.repeat(64) }) } = {}) {
  const input = path.join(root, `input-${next}`), directory = path.join(root, `bundle-${next++}`);
  operatorFixture(input, { sha, version, now, comparisonBase: base }); createOperatorEvidence(input, directory, sha, version, options); change(directory);
  const data = { [releasesUrl]: [[structuredClone(release), structuredClone(previous)]], [assetsUrl]: [[structuredClone(asset)]], [branchUrl]: [{ commit: { sha } }], [alertsUrl]: [[]] };
  mutate(data);
  const selected = await collectOperatorEvidence('owner/repo', sha, version, { ...options, expectedManifestSha256, comparisonReader,
    query: endpoint => { assert.ok(endpoint in data); return structuredClone(transform(endpoint, data[endpoint])); },
    readBundle: () => ({ directory, archiveSha256: 'c'.repeat(64) }) });
  return { selected, directory };
}
try {
  const first = await collect(); assert.equal(first.selected.assetId, 456); assert.equal(first.selected.comparisonBase, base);
  assert.equal(first.selected.originChecks.codeqlOpenAlerts, 0);
  assert.equal((await collect({ expectedManifestSha256: first.selected.manifestSha256 })).selected.manifestSha256, first.selected.manifestSha256);
  for (const mutate of [data => { data[releasesUrl][0][0].draft = false; }, data => { data[releasesUrl][0][0].target_commitish = 'd'.repeat(40); },
    data => { data[releasesUrl][0].push(release); }, data => { data[assetsUrl] = [[]]; },
    data => { data[assetsUrl][0][0].digest = `sha256:${'d'.repeat(64)}`; }, data => { data[assetsUrl][0][0].created_at = time(-8 * 86400000); },
    data => { data[releasesUrl][0].splice(1); }, data => { data[assetsUrl][0][0].state = 'new'; },
    data => { data[branchUrl][0].commit.sha = 'd'.repeat(40); }, data => { data[alertsUrl] = [[{ state: 'open' }]]; },
    data => { data[alertsUrl] = []; }]) await assert.rejects(collect({ mutate }));
  await assert.rejects(collect({ expectedManifestSha256: '0'.repeat(64) }));
  await assert.rejects(collect({ comparisonReader: () => ({ comparisonBase: 'd'.repeat(40), changedPathsSha256: 'c'.repeat(64) }) }));
  let comparisons = 0;
  await assert.rejects(collect({ comparisonReader: () => ({ comparisonBase: ++comparisons > 1 ? 'd'.repeat(40) : base, changedPathsSha256: 'c'.repeat(64) }) }), /changed during collection/);
  let reads = 0;
  await assert.rejects(collect({ transform: (url, value) => url === assetsUrl && ++reads > 1 ? [[{ ...asset, updated_at: time(-100) }]] : value }), /changed during collection/);
  let branchReads = 0;
  await assert.rejects(collect({ transform: (url, value) => url === branchUrl && ++branchReads > 1 ? [{ commit: { sha: 'd'.repeat(40) } }] : value }), /branch no longer/);
  await assert.rejects(collect({ change: directory => writeFileSync(path.join(directory, 'evidence/reviews/source-and-security.md'), 'Changed after sealing') }));
  const attachment = path.join(root, 'attachment'); mkdirSync(attachment); cpSync(first.directory, path.join(attachment, 'bundle'), { recursive: true });
  const selectionFile = path.join(attachment, 'selection.json'); writeFileSync(selectionFile, JSON.stringify(first.selected));
  assert.equal(verifyOperatorAttachment(attachment, first.selected, options), true);
  for (const field of ['assetId', 'version', 'policySha256', 'comparisonBase', 'changedPathsSha256', 'reviewedBy', 'previousStableTag', 'candidateRef', 'originChecks']) {
    writeFileSync(selectionFile, JSON.stringify({ ...first.selected, [field]: field === 'assetId' ? 999 : 'changed' }));
    assert.throws(() => verifyOperatorAttachment(attachment, first.selected, options));
  }
  writeFileSync(selectionFile, JSON.stringify(first.selected));
  writeFileSync(path.join(attachment, 'bundle/evidence/reviews/source-and-security.md'), 'Replaced draft review');
  assert.throws(() => verifyOperatorAttachment(attachment, first.selected, options));
  assert.ok(readFileSync(path.join(first.directory, 'manifest.json')).length);
  console.log('Operator transport: exact candidate/draft/digest, current predecessor diff, concurrent changes, retained selection and actual attachment tampering reject safely.');
} finally { rmSync(root, { recursive: true, force: true }); }
