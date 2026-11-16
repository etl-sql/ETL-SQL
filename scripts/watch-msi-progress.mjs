import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const normalNames = ['MSI certification (workstation)', 'MSI certification (portal)'];
const nameMatches = (actual, expected) => actual === expected || actual.endsWith(` / ${expected}`);
const timestamp = value => { const n = Date.parse(value); if (!Number.isFinite(n)) throw new Error('Invalid progress timestamp.'); return n; };

export async function readProgressApi({ repository, token, route, fetcher = fetch, sleep }) {
  for (let attempt = 0; attempt < 3; attempt++) {
    let response;
    try {
      response = await fetcher(`https://api.github.com/repos/${repository}/${route}`, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'ETL-SQL-MSI-progress' },
        signal: AbortSignal.timeout(10000), redirect: 'error',
      });
    } catch {
      if (attempt === 2) throw new Error('MSI progress API unavailable after bounded retries.');
      await sleep(2000 * (attempt + 1)); continue;
    }
    if (!response.ok) {
      if ([408, 429, 500, 502, 503, 504].includes(response.status) && attempt < 2) {
        await sleep(2000 * (attempt + 1)); continue;
      }
      throw new Error(`MSI progress API returned HTTP ${response.status}.`);
    }
    const body = await response.text();
    if (body.length > 5 * 1024 * 1024) throw new Error('MSI progress API response exceeds its bound.');
    return JSON.parse(body);
  }
}

// An API outage, malformed record or wrong attempt must never become a cancellation decision.
export function evaluateProgress({ jobs, annotations = {}, runId, attempt, headSha, now, diagnostic = false }) {
  if (!Array.isArray(jobs) || !Number.isFinite(now)) throw new Error('Invalid progress inventory.');
  const names = diagnostic ? ['MSI Portal diagnostic only'] : normalNames;
  const profiles = [];
  for (const name of names) {
    const matches = jobs.filter(job => nameMatches(job.name, name));
    if (matches.length > 1) throw new Error('Ambiguous MSI job inventory.');
    if (!matches.length) { profiles.push({ name, status: 'waiting' }); continue; }
    const job = matches[0];
    if (String(job.run_id) !== runId || job.head_sha !== headSha) throw new Error('Foreign MSI job.');
    // Successful jobs can be carried forward from an earlier attempt without executing again.
    if (job.status === 'completed') { profiles.push({ name, status: job.conclusion === 'success' ? 'passed' : 'failed', jobId: job.id }); continue; }
    if (String(job.run_attempt) !== attempt) throw new Error('Active MSI job belongs to another attempt.');
    const stepName = diagnostic ? 'Diagnose the Portal profile without rebuilding' : 'Certify installer profile with bounded supervision';
    const steps = (job.steps ?? []).filter(step => step.name === stepName && step.status === 'in_progress');
    if (steps.length > 1) throw new Error('Ambiguous active certification step.');
    if (!steps.length) { profiles.push({ name, status: 'waiting', jobId: job.id }); continue; }
    let lastProgress = timestamp(steps[0].started_at);
    for (const annotation of annotations[job.id] ?? []) {
      if (annotation.title !== 'MSI checkpoint') continue;
      let record;
      try { record = JSON.parse(annotation.message); } catch { throw new Error('Malformed MSI checkpoint.'); }
      if (record.scope !== 'diagnostic-only' || record.run !== runId || record.attempt !== attempt || record.runHead !== headSha) throw new Error('Foreign MSI checkpoint.');
      const observed = timestamp(record.utc);
      if (observed > now + 60000) throw new Error('MSI checkpoint is in the future.');
      lastProgress = Math.max(lastProgress, observed);
    }
    if (lastProgress > now + 60000) throw new Error('Certification step starts in the future.');
    profiles.push({ name, jobId: job.id, status: now - lastProgress >= 300000 ? 'stalled' : 'running', lastProgress: new Date(lastProgress).toISOString(), silenceSeconds: Math.max(0, (now - lastProgress) / 1000) });
  }
  const status = profiles.some(p => p.status === 'stalled') ? 'stalled'
    : profiles.every(p => p.status === 'passed') ? 'passed'
      : profiles.every(p => ['passed', 'failed'].includes(p.status)) ? 'failed' : 'waiting';
  return { status, profiles };
}

export async function watchProgress({ api, context, sleep, now = Date.now, save, maxPolls = 130 }) {
  for (let poll = 0; poll < maxPolls; poll++) {
    const run = await api(`actions/runs/${context.runId}`);
    if (String(run.id) !== context.runId || String(run.run_attempt) !== context.attempt || run.head_sha !== context.headSha || run.repository?.full_name !== context.repository) throw new Error('MSI monitor run identity changed.');
    const jobs = [];
    for (let page = 1; ; page++) {
      if (page > 10) throw new Error('MSI job inventory exceeds its bound.');
      const listing = await api(`actions/runs/${context.runId}/jobs?filter=latest&per_page=100&page=${page}`);
      if (!Array.isArray(listing.jobs) || !Number.isSafeInteger(listing.total_count)) throw new Error('Invalid MSI job response.');
      jobs.push(...listing.jobs);
      if (jobs.length === listing.total_count) break;
      if (!listing.jobs.length || jobs.length > listing.total_count) throw new Error('Incomplete MSI job inventory.');
    }
    const names = context.diagnostic ? ['MSI Portal diagnostic only'] : normalNames;
    const annotations = {};
    for (const job of jobs.filter(j => j.status === 'in_progress' && names.some(name => nameMatches(j.name, name)))) {
      const prefix = `https://api.github.com/repos/${context.repository}/check-runs/`;
      if (!job.check_run_url?.startsWith(prefix) || !/^[1-9]\d*$/.test(job.check_run_url.slice(prefix.length))) throw new Error('Foreign MSI check run.');
      const records = [];
      for (let page = 1; ; page++) {
        if (page > 10) throw new Error('MSI annotation inventory exceeds its bound.');
        const batch = await api(`check-runs/${job.check_run_url.slice(prefix.length)}/annotations?per_page=100&page=${page}`);
        if (!Array.isArray(batch)) throw new Error('Invalid MSI annotation response.');
        records.push(...batch);
        if (batch.length < 100) break;
      }
      annotations[job.id] = records;
    }
    const result = evaluateProgress({ ...context, jobs, annotations, now: now() });
    const receipt = { schemaVersion: 1, scope: 'MsiRunnerProgressOnly', releaseEligible: false, ...context, observedAt: new Date(now()).toISOString(), ...result };
    save(receipt);
    if (result.status !== 'waiting') return receipt;
    await sleep(30000);
  }
  throw new Error('MSI progress monitor exceeded its own deadline.');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const context = { repository: process.env.GITHUB_REPOSITORY, runId: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
      headSha: process.env.MSI_MONITOR_HEAD_SHA, diagnostic: process.env.MSI_MONITOR_DIAGNOSTIC === 'true' };
    if (!/^[\w.-]+\/[\w.-]+$/.test(context.repository ?? '') || context.repository.split('/').some(p => ['.', '..'].includes(p)) || !/^[1-9]\d*$/.test(context.runId ?? '') || !/^[1-9]\d*$/.test(context.attempt ?? '') || !/^[a-f0-9]{40}$/.test(context.headSha ?? '') || !process.env.GH_TOKEN || !process.env.GITHUB_OUTPUT) throw new Error('Missing MSI monitor identity.');
    const directory = 'release-validation/msi-progress';
    mkdirSync(directory, { recursive: true });
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const api = route => readProgressApi({ repository: context.repository, token: process.env.GH_TOKEN, route, sleep });
    const receipt = await watchProgress({ api, context, sleep,
      save: value => writeFileSync(`${directory}/receipt.json`, JSON.stringify(value, null, 2)) });
    appendFileSync(process.env.GITHUB_OUTPUT, `stalled=${receipt.status === 'stalled'}\n`);
    console.log(`MSI progress: ${receipt.status}`);
    process.exitCode = receipt.status === 'failed' ? 1 : 0;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
