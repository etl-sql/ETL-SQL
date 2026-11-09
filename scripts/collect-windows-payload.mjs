import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream, openSync, closeSync, mkdirSync, readFileSync, writeFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { extractSubmittedArchive } from './lib/submitted-evidence.mjs';
import { verifyWindowsPayload } from './windows-payload.mjs';

function complete(pages, key) {
    if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page[key]))) throw new Error('Incomplete Windows producer inventory.');
    const rows = pages.flatMap(page => page[key]);
    if (rows.length > 10000 || pages.some(page => page.total_count !== rows.length) ||
        rows.some(row => !Number.isSafeInteger(row.id) || row.id < 1) || new Set(rows.map(row => row.id)).size !== rows.length) {
        throw new Error('Incomplete or duplicate Windows producer inventory.');
    }
    return rows;
}
const producerName = 'Publish Windows payload';
const apiRoot = repository => `repos/${repository}/actions`;
const timestamp = value => { const time = Date.parse(value); if (!Number.isFinite(time)) throw new Error('Missing Windows producer timestamp.'); return time; };

export async function selectWindowsPayload(context, { query, now = Date.now() } = {}) {
    const { repository, sha, branch, event, sourceRepository = repository, pullRequest } = context;
    if (!/^[\w.-]+\/[\w.-]+$/.test(repository) || repository.split('/').some(part => part === '.' || part === '..') ||
        !/^[a-f0-9]{40}$/.test(sha) || typeof branch !== 'string' || !branch || /[\x00-\x20]/.test(branch) ||
        !['pull_request', 'workflow_dispatch'].includes(event) || typeof query !== 'function' ||
        (event === 'pull_request' && (!Number.isSafeInteger(pullRequest) || pullRequest < 1)) ||
        (event !== 'pull_request' && sourceRepository !== repository)) throw new Error('Invalid Windows payload request.');
    const root = apiRoot(repository);
    const runs = complete(await query(`${root}/workflows/msi-upgrade.yml/runs?head_sha=${sha}&per_page=100`), 'workflow_runs')
        .filter(run => run.head_sha === sha && run.head_branch === branch && run.repository?.full_name === repository &&
            run.head_repository?.full_name === sourceRepository &&
            (event === 'pull_request' ? run.event === 'pull_request' && run.pull_requests?.some(pr => pr.number === pullRequest)
                : ['push', 'workflow_dispatch'].includes(run.event)))
        .sort((left, right) => right.id - left.id);
    for (const run of runs) {
        if (now - timestamp(run.created_at) > 7 * 86400000 || timestamp(run.created_at) > now + 1000) throw new Error('Windows producer is stale or future-dated.');
        if (!Number.isInteger(run.run_attempt) || run.run_attempt < 1 || run.run_attempt > 100) throw new Error('Invalid Windows producer attempt.');
        const jobs = complete(await query(`${root}/runs/${run.id}/jobs?filter=latest&per_page=100`), 'jobs');
        // A retained-input diagnostic must not hide or replace the real producer.
        if (jobs.some(job => job.name === 'MSI Portal diagnostic only' && job.conclusion !== 'skipped')) continue;
        const matches = jobs.filter(job => job.name === producerName);
        if (matches.length > 1) throw new Error('Ambiguous Windows payload producer.');
        const job = matches[0];
        if (!job || job.conclusion === 'skipped') {
            if (run.status === 'completed' && run.conclusion !== 'success') throw new Error('Windows producer prerequisites failed.');
            return { status: 'Waiting', runId: run.id, reason: 'Waiting for a full MSI workflow to publish the Windows payload.' };
        }
        if (job.run_id !== run.id || job.head_sha !== sha) throw new Error('Windows producer job identity mismatch.');
        if (job.status !== 'completed') return { status: 'Waiting', runId: run.id, reason: 'Windows payload publication is still running.' };
        if (job.conclusion !== 'success') throw new Error('Windows payload publication failed; inspect its original run.');
        let original;
        for (let attempt = 1; attempt <= run.run_attempt; attempt++) {
            const executions = complete(await query(`${root}/runs/${run.id}/attempts/${attempt}/jobs?per_page=100`), 'jobs')
                .filter(candidate => candidate.name === producerName && candidate.conclusion === 'success' && candidate.status === 'completed' &&
                    candidate.started_at === job.started_at && candidate.completed_at === job.completed_at);
            if (executions.length > 1) throw new Error('Ambiguous original Windows payload execution.');
            if (executions.length) { original = { job: executions[0], attempt }; break; }
        }
        if (!original) throw new Error('Missing original Windows payload execution.');
        if (original.job.run_id !== run.id || original.job.head_sha !== sha) throw new Error('Original Windows producer identity mismatch.');
        const start = timestamp(original.job.started_at), end = timestamp(original.job.completed_at);
        if (end < start || end > now + 1000) throw new Error('Invalid Windows payload execution window.');
        const expectedName = `windows-payload-${run.id}-${original.attempt}`;
        const artifacts = complete(await query(`${root}/runs/${run.id}/artifacts?per_page=100`), 'artifacts')
            .filter(artifact => artifact.name === expectedName);
        if (artifacts.length !== 1) throw new Error('Missing or ambiguous Windows payload artifact.');
        const artifact = artifacts[0];
        if (artifact.expired !== false || artifact.workflow_run?.id !== run.id || artifact.workflow_run?.head_sha !== sha ||
            !/^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '') || !Number.isSafeInteger(artifact.size_in_bytes) ||
            artifact.size_in_bytes < 1 || artifact.size_in_bytes > 8 * 1024 ** 3 ||
            timestamp(artifact.created_at) < start || timestamp(artifact.created_at) > end + 999) throw new Error('Invalid Windows payload artifact origin.');
        return { schemaVersion: 1, scope: 'WindowsBuildInputsOnly', status: 'Ready', repository, sourceSha: sha,
            publishReadyToRun: event !== 'pull_request',
            runId: run.id, jobId: original.job.id, executionAttempt: original.attempt, artifactId: artifact.id,
            artifactName: artifact.name, artifactDigest: artifact.digest, artifactBytes: artifact.size_in_bytes };
    }
    return { status: 'Waiting', reason: 'No matching Windows producer registered; start the full MSI workflow on this ref.' };
}

function gh(args, options = {}) {
    const result = spawnSync('gh', args, { windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 ** 2, encoding: 'utf8', ...options });
    if (result.error || result.status !== 0) throw new Error('Windows payload GitHub request failed.');
    return result.stdout;
}
export async function waitForWindowsPayload(context, { query, timeoutMs = 3000000, registrationMs = 300000,
    intervalMs = 20000, report = console.log, clock = Date.now, sleep = ms => new Promise(resolve => setTimeout(resolve, ms)) } = {}) {
    const started = clock(); let lastReport = -Infinity;
    while (clock() - started <= timeoutMs) {
        const selection = await selectWindowsPayload(context, { query, now: clock() });
        if (selection.status === 'Ready') return selection;
        if (!selection.runId && clock() - started >= registrationMs) throw new Error(selection.reason);
        if (clock() - lastReport >= 60000) { report(selection.reason); lastReport = clock(); }
        await sleep(intervalMs);
    }
    throw new Error('Windows payload wait exceeded its budget; retry this consumer after inspecting the producer.');
}

export async function downloadWindowsPayload(selection, version, output, { query, download } = {}) {
    if (typeof selection.publishReadyToRun !== 'boolean') throw new Error('Windows payload optimization profile is missing.');
    const attempt = path.join(output, `${selection.runId}-${selection.artifactId}`);
    mkdirSync(attempt, { recursive: false });
    const archive = path.join(attempt, 'payload.zip');
    await download(selection, archive);
    if (statSync(archive).size !== selection.artifactBytes) throw new Error('Windows payload download size mismatch.');
    const hash = createHash('sha256');
    for await (const chunk of createReadStream(archive)) hash.update(chunk);
    if (`sha256:${hash.digest('hex')}` !== selection.artifactDigest) throw new Error('Windows payload archive digest mismatch.');
    const expanded = path.join(attempt, 'expanded');
    extractSubmittedArchive(archive, expanded, { maximumBytes: 8 * 1024 ** 3, maximumEntries: 10000, timeoutMs: 300000 });
    const payloadDirectory = path.join(expanded, 'bundle');
    const manifestSha256 = createHash('sha256').update(readFileSync(path.join(payloadDirectory, 'windows-payload.json'))).digest('hex');
    await verifyWindowsPayload(payloadDirectory, selection.sourceSha, version, manifestSha256, selection.publishReadyToRun);
    // Recheck the same artifact after download; an altered/deleted origin cannot become accepted input.
    const pages = await query(`${apiRoot(selection.repository)}/artifacts/${selection.artifactId}`);
    const artifact = pages[0];
    if (pages.length !== 1 || artifact.id !== selection.artifactId || artifact.expired !== false ||
        artifact.digest !== selection.artifactDigest || artifact.name !== selection.artifactName ||
        artifact.workflow_run?.id !== selection.runId || artifact.workflow_run?.head_sha !== selection.sourceSha) throw new Error('Windows payload origin changed during download.');
    return { ...selection, version, manifestSha256, payloadDirectory };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        if (process.argv.length !== 5) throw new Error('Usage: collect-windows-payload.mjs <source-sha> <version> <new-output-directory>');
        const [sha, version, output] = process.argv.slice(2);
        const event = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
        const context = { repository: process.env.GITHUB_REPOSITORY, sha,
            event: process.env.GITHUB_EVENT_NAME, branch: process.env.GITHUB_HEAD_REF || process.env.GITHUB_REF_NAME,
            pullRequest: event.pull_request?.number, sourceRepository: event.pull_request?.head?.repo?.full_name || process.env.GITHUB_REPOSITORY };
        const query = endpoint => JSON.parse(gh(['api', '--paginate', '--slurp', endpoint]));
        const selection = await waitForWindowsPayload(context, { query });
        mkdirSync(output, { recursive: false });
        const accepted = await downloadWindowsPayload(selection, version, output, { query, download: async (origin, file) => {
            const fd = openSync(file, 'wx');
            try { gh(['api', `${apiRoot(origin.repository)}/artifacts/${origin.artifactId}/zip`], { timeout: 300000, stdio: ['ignore', fd, 'pipe'] }); }
            finally { closeSync(fd); }
        } });
        writeFileSync(path.join(output, 'selection.json'), JSON.stringify(accepted, null, 2) + '\n');
        console.log(`Reusing Windows payload from run ${selection.runId}, original job ${selection.jobId}, artifact ${selection.artifactId}.`);
    } catch (error) { console.error(error.message); process.exitCode = 1; }
}
