import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { windowsHosts, createWindowsPayload, verifyWindowsPayload, copyWindowsPayload } from '../windows-payload.mjs';
import { selectWindowsPayload, waitForWindowsPayload, downloadWindowsPayload } from '../collect-windows-payload.mjs';
import { checkWindowsPackageOrigin } from '../check-windows-package-origin.mjs';

const sha = 'a'.repeat(40), version = '0.22.0';
const temporary = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'shared-windows-payload-')));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const msi = { sourceSha: sha, windowsPayload: { sourceSha: sha, version, manifestSha256: 'd'.repeat(64) } };
const native = { sourceSha: sha, version, windowsPayloadSha256: 'd'.repeat(64) };
checkWindowsPackageOrigin(msi, native, sha, version);
for (const changed of [{ ...native, windowsPayloadSha256: 'e'.repeat(64) }, { ...native, sourceSha: 'b'.repeat(40) },
    { ...native, windowsPayloadSha256: undefined }, { ...native, version: '0.21.0' }]) {
    assert.throws(() => checkWindowsPackageOrigin(msi, changed, sha, version), /same shared payload/);
}
assert.throws(() => checkWindowsPackageOrigin({ sourceSha: sha }, native, sha, version), /same shared payload/);
try {
    const bundle = path.join(temporary, 'bundle'), bin = path.join(bundle, 'bin');
    await fs.mkdir(bin, { recursive: true });
    for (const host of windowsHosts) await fs.writeFile(path.join(bin, host.executable), `synthetic ${host.executable}`);
    await fs.writeFile(path.join(bin, 'appsettings.json'), '{}');
    await fs.writeFile(path.join(bin, '.public-license'), 'synthetic public attribution');
    const manifestHash = await createWindowsPayload(bundle, sha, version, '10.0.100');
    await verifyWindowsPayload(bundle, sha, version, manifestHash);
    const destination = path.join(temporary, 'copy/bin');
    await fs.mkdir(destination, { recursive: true });
    await copyWindowsPayload(bundle, sha, version, manifestHash, destination);
    for (const host of windowsHosts) assert.deepEqual(await fs.readFile(path.join(destination, host.executable)), await fs.readFile(path.join(bin, host.executable)));
    await assert.rejects(copyWindowsPayload(bundle, sha, version, manifestHash, destination), /empty destination/);
    await assert.rejects(verifyWindowsPayload(bundle, 'b'.repeat(40), version, manifestHash), /identity/);
    await assert.rejects(verifyWindowsPayload(bundle, sha, '0.21.0', manifestHash), /identity/);
    const original = await fs.readFile(path.join(bundle, 'windows-payload.json'));
    const altered = JSON.parse(original); altered.options.readyToRun = false;
    const alteredBytes = Buffer.from(JSON.stringify(altered));
    await fs.writeFile(path.join(bundle, 'windows-payload.json'), alteredBytes);
    await assert.rejects(verifyWindowsPayload(bundle, sha, version, hash(alteredBytes)), /options/);
    await fs.writeFile(path.join(bundle, 'windows-payload.json'), original);
    await fs.writeFile(path.join(bin, 'ETL-SQL.exe'), 'changed');
    await assert.rejects(verifyWindowsPayload(bundle, sha, version, manifestHash), /files changed/);
    await fs.writeFile(path.join(bin, 'ETL-SQL.exe'), 'synthetic ETL-SQL.exe');
    await fs.writeFile(path.join(bin, 'appsettings.secrets.json'), 'synthetic private state');
    await assert.rejects(createWindowsPayload(bundle, sha, version, '10.0.100'), /Runtime state/);
    assert.deepEqual(await fs.readFile(path.join(bundle, 'windows-payload.json')), original, 'Rejected publication must preserve the prior manifest.');
    await fs.unlink(path.join(bin, 'appsettings.secrets.json'));
    await fs.symlink(temporary, path.join(bin, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(verifyWindowsPayload(bundle, sha, version, manifestHash), /Linked/);
    await fs.unlink(path.join(bin, 'linked'));

    const now = Date.parse('2026-11-01T12:00:00Z'), time = offset => new Date(now + offset).toISOString();
    const root = 'repos/owner/repo/actions';
    const run = { id: 100, head_sha: sha, head_branch: 'fix/payload', repository: { full_name: 'owner/repo' },
        head_repository: { full_name: 'owner/repo' }, event: 'pull_request', pull_requests: [{ number: 51 }],
        run_attempt: 1, status: 'in_progress', conclusion: null, created_at: time(-60000) };
    const job = { id: 110, run_id: run.id, head_sha: sha, name: 'Publish Windows payload', status: 'completed', conclusion: 'success', started_at: time(-50000), completed_at: time(-10000) };
    const artifact = { id: 120, name: 'windows-payload-100-1', expired: false, digest: `sha256:${'c'.repeat(64)}`,
        size_in_bytes: 100, created_at: time(-12000), workflow_run: { id: 100, head_sha: sha } };
    const page = (key, rows) => [{ total_count: rows.length, [key]: structuredClone(rows) }];
    const data = {
        [`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`]: page('workflow_runs', [run]),
        [`${root}/runs/100/jobs?filter=latest&per_page=100`]: page('jobs', [job]),
        [`${root}/runs/100/attempts/1/jobs?per_page=100`]: page('jobs', [job]),
        [`${root}/runs/100/artifacts?per_page=100`]: page('artifacts', [artifact]),
        [`${root}/artifacts/120`]: [artifact],
    };
    const context = { repository: 'owner/repo', sha, branch: 'fix/payload', event: 'pull_request', pullRequest: 51 };
    const exercise = async (mutate, overrides = {}) => {
        const copy = structuredClone(data); mutate?.(copy);
        return selectWindowsPayload({ ...context, ...overrides }, { now, query: async endpoint => {
            assert.ok(Object.hasOwn(copy, endpoint), endpoint); return copy[endpoint];
        } });
    };
    assert.equal((await exercise()).artifactId, 120, 'Portal may still be running; only the completed payload producer is required.');
    assert.equal((await exercise(copy => {
        copy[`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0].event = 'push';
    }, { event: 'workflow_dispatch' })).artifactId, 120, 'Manual native certification may consume the full MSI branch producer.');
    assert.equal((await exercise(copy => {
        copy[`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0].head_repository.full_name = 'fork/repo';
    })).status, 'Waiting', 'A different source repository must not satisfy the request.');
    assert.equal((await exercise(copy => {
        copy[`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0].head_repository.full_name = 'fork/repo';
    }, { sourceRepository: 'fork/repo' })).artifactId, 120, 'A PR may consume its own matching fork producer, never another fork.');
    const carried = await exercise(copy => {
        copy[`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0].run_attempt = 2;
        copy[`${root}/runs/100/jobs?filter=latest&per_page=100`][0].jobs[0].id = 210;
    });
    assert.equal(carried.jobId, 110); assert.equal(carried.executionAttempt, 1);
    for (const field of ['head_sha', 'head_branch']) assert.equal((await exercise(copy => {
        copy[`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`][0].workflow_runs[0][field] = 'foreign';
    })).status, 'Waiting');
    await assert.rejects(exercise(copy => { copy[`${root}/runs/100/jobs?filter=latest&per_page=100`][0].jobs[0].conclusion = 'failure'; }), /publication failed/);
    for (const field of ['run_id', 'head_sha']) {
        await assert.rejects(exercise(copy => { copy[`${root}/runs/100/jobs?filter=latest&per_page=100`][0].jobs[0][field] = 'foreign'; }), /identity mismatch/);
        await assert.rejects(exercise(copy => { copy[`${root}/runs/100/attempts/1/jobs?per_page=100`][0].jobs[0][field] = 'foreign'; }), /identity mismatch/);
    }
    await assert.rejects(exercise(copy => { copy[`${root}/runs/100/artifacts?per_page=100`][0].artifacts[0].expired = true; }), /artifact origin/);
    await assert.rejects(exercise(copy => { copy[`${root}/runs/100/artifacts?per_page=100`][0].total_count = 2; }), /Incomplete/);
    await assert.rejects(exercise(copy => { copy[`${root}/runs/100/attempts/1/jobs?per_page=100`] = page('jobs', []); }), /original/);
    let clock = now;
    await assert.rejects(waitForWindowsPayload(context, { query: async () => page('workflow_runs', []), registrationMs: 2,
        clock: () => clock, sleep: async () => { clock++; }, intervalMs: 1, report: () => {} }), /No matching/);

    const archive = path.join(temporary, 'source.zip');
    const zip = spawnSync('pwsh', ['-NoProfile', '-Command', '[IO.Compression.ZipFile]::CreateFromDirectory($env:PAYLOAD_SOURCE,$env:PAYLOAD_ZIP,[IO.Compression.CompressionLevel]::NoCompression,$true)'],
        { env: { ...process.env, PAYLOAD_SOURCE: bundle, PAYLOAD_ZIP: archive }, encoding: 'utf8', windowsHide: true, timeout: 10000 });
    assert.equal(zip.status, 0, zip.stdout + zip.stderr);
    const archiveBytes = await fs.readFile(archive);
    const selection = { ...(await exercise()), artifactBytes: archiveBytes.length, artifactDigest: `sha256:${hash(archiveBytes)}` };
    const output = path.join(temporary, 'download'); await fs.mkdir(output);
    const accepted = await downloadWindowsPayload(selection, version, output, {
        download: async (_selection, target) => fs.copyFile(archive, target),
        query: async () => [{ ...artifact, digest: selection.artifactDigest }],
    });
    assert.equal(accepted.manifestSha256, manifestHash);
    await verifyWindowsPayload(accepted.payloadDirectory, sha, version, manifestHash);
    const badOutput = path.join(temporary, 'bad-download'); await fs.mkdir(badOutput);
    await assert.rejects(downloadWindowsPayload({ ...selection, artifactDigest: `sha256:${'0'.repeat(64)}` }, version, badOutput,
        { download: async (_selection, target) => fs.copyFile(archive, target), query: async () => [] }), /digest mismatch/);
    console.log('Shared Windows payload: exact bytes/options, clean inventory, immutable original-attempt selection, independent profile progress and bounded verified download passed.');
} finally {
    assert.equal(path.dirname(temporary), await fs.realpath(os.tmpdir()));
    await fs.rm(temporary, { recursive: true, force: true });
}
const packaging = spawnSync('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL('./Test-SharedWindowsPackaging.ps1', import.meta.url))],
    { encoding: 'utf8', timeout: 30000, windowsHide: true });
assert.ifError(packaging.error);
assert.equal(packaging.status, 0, packaging.stdout + packaging.stderr);
assert.match(packaging.stdout, /eight publishes feed MSI, ZIP and VSIX unchanged/);
