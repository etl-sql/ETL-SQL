import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function timestamp(value) { const n = value ? Date.parse(value) : NaN; return Number.isFinite(n) ? n : null; }
function seconds(start, end) {
  const a = timestamp(start), b = timestamp(end);
  return a === null || b === null || b < a ? null : (b - a) / 1000;
}
export function stepCategory(name) {
  const matches = [
    ['upload', /\b(upload|attest|retain)\b/i],
    ['setup', /\b(checkout|set up|setup|install|restore|download|prepare)\b/i],
    ['build', /\b(build|compile|package|packaging|publish)\b/i],
    ['test', /\b(test|tests|validate|validation|certification|coverage|lint|audit|verify|check|checks)\b/i],
  ].filter(([, pattern]) => pattern.test(name ?? ''));
  // Ambiguous names remain other; these are labels inferred from names, not runner telemetry.
  return matches.length === 1 ? matches[0][0] : 'other';
}

export function jobStepTiming(job) {
  const totals = { setup: 0, build: 0, test: 0, upload: 0, other: 0 };
  for (const step of job.steps) if (step.durationSeconds !== null) totals[step.category] += step.durationSeconds;
  const intervals = job.steps.map(step => [timestamp(step.startedAt), timestamp(step.completedAt)]).sort((a, b) => a[0] - b[0]);
  const start = timestamp(job.startedAt), end = timestamp(job.completedAt);
  const measured = job.durationSeconds !== null && intervals.length > 0 && intervals.every(([a, b], i) =>
    a !== null && b !== null && b >= a && a >= start && b <= end && (i === 0 || a >= intervals[i - 1][1]));
  return { inferredStepSeconds: totals, unmeasuredSteps: job.steps.filter(step => step.durationSeconds === null).length,
    unattributedSeconds: measured ? job.durationSeconds - Object.values(totals).reduce((sum, value) => sum + value, 0) : null };
}
function api(endpoint, paginate = false) {
  const result = spawnSync('gh', ['api', ...(paginate ? ['--paginate', '--slurp'] : []), endpoint], {
    encoding: 'utf8', timeout: 60000, maxBuffer: 32 * 1024 * 1024, windowsHide: true,
  });
  if (result.error || result.status !== 0) throw new Error(`Cannot read GitHub timeline endpoint: ${endpoint}`);
  return JSON.parse(result.stdout);
}
export async function collectTimeline(repository, sha, runIds, query = api) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository) || !/^[a-f0-9]{40}$/i.test(sha) ||
      !runIds.length || runIds.some(id => !/^[1-9][0-9]*$/.test(String(id))) || new Set(runIds.map(String)).size !== runIds.length) throw new Error('Supply repository, exact candidate SHA and distinct positive run IDs.');
  const runs = [];
  for (const id of runIds) {
    const base = `repos/${repository}/actions/runs/${id}`;
    const run = await query(base);
    if (String(run.id) !== String(id) || run.head_sha !== sha || run.repository?.full_name?.toLowerCase() !== repository.toLowerCase() ||
        !Number.isSafeInteger(run.run_attempt) || run.run_attempt < 1) throw new Error(`Run ${id} does not match the candidate/repository or has invalid attempts.`);
    const attempts = [];
    for (let number = 1; number <= run.run_attempt; number++) {
      const attempt = await query(`${base}/attempts/${number}`);
      if (String(attempt.id) !== String(id) || attempt.head_sha !== sha || attempt.run_attempt !== number) throw new Error(`Invalid attempt ${id}/${number}.`);
      const pages = await query(`${base}/attempts/${number}/jobs?per_page=100`, true);
      if (!Array.isArray(pages) || !pages.length || pages.some(p => !Array.isArray(p.jobs))) throw new Error('Invalid paginated jobs response.');
      const rawJobs = pages.flatMap(p => p.jobs);
      if (pages.some(p => p.total_count !== rawJobs.length) || new Set(rawJobs.map(j => j.id)).size !== rawJobs.length) throw new Error('Incomplete or duplicate job pages. Retry collection after the run settles.');
      const jobs = rawJobs.map(job => {
        if (String(job.run_id) !== String(id) || job.head_sha !== sha) throw new Error('Job belongs to another candidate or run.');
        // GitHub can assign a new job ID when carrying a prior success into a retry response.
        // Match execution timestamps and name, not the copied API record's identity.
        const previous = attempts.flatMap(a => a.jobs).find(j => j.name === job.name &&
          j.startedAt === job.started_at && j.completedAt === job.completed_at &&
          j.conclusion === job.conclusion && j.durationSeconds !== null);
        const executionAttempt = previous?.executionAttempt ?? number;
        const result = { id: job.id, name: job.name, status: job.status, conclusion: job.conclusion,
          executionAttempt, carriedForward: executionAttempt !== number,
          startedAt: job.started_at ?? null, completedAt: job.completed_at ?? null,
          durationSeconds: seconds(job.started_at, job.completed_at),
          steps: (job.steps ?? []).map(step => ({ number: step.number, name: step.name, category: stepCategory(step.name), status: step.status,
            conclusion: step.conclusion, startedAt: step.started_at ?? null, completedAt: step.completed_at ?? null,
            durationSeconds: seconds(step.started_at, step.completed_at) })) };
        return { ...result, ...jobStepTiming(result) };
      });
      const executed = jobs.filter(j => !j.carriedForward);
      const starts = executed.map(j => timestamp(j.startedAt)).filter(n => n !== null);
      const ends = executed.map(j => timestamp(j.completedAt)).filter(n => n !== null);
      const first = starts.length ? new Date(Math.min(...starts)).toISOString() : null;
      const last = ends.length ? new Date(Math.max(...ends)).toISOString() : null;
      const complete = attempt.status === 'completed' && jobs.length > 0 && jobs.every(j => j.status === 'completed');
      const fullyMeasured = complete && executed.length > 0 && executed.every(j => j.durationSeconds !== null);
      attempts.push({ number, status: attempt.status, conclusion: attempt.conclusion,
        startedAt: attempt.run_started_at ?? null, firstJobAt: first, lastCompletedJobAt: last,
        startDelaySeconds: seconds(attempt.run_started_at, first),
        observedJobSpanSeconds: fullyMeasured ? seconds(first, last) : null,
        complete,
        measuredJobSeconds: executed.reduce((sum, j) => sum + (j.durationSeconds ?? 0), 0),
        inferredStepSeconds: Object.fromEntries(['setup', 'build', 'test', 'upload', 'other'].map(category =>
          [category, executed.reduce((sum, job) => sum + job.inferredStepSeconds[category], 0)])),
        carriedForwardJobs: jobs.length - executed.length,
        unmeasuredJobs: executed.filter(j => j.durationSeconds === null).length, jobs });
    }
    runs.push({ id: String(id), workflow: run.path, createdAt: run.created_at, attempts });
  }
  return { schemaVersion: 1, repository, sourceSha: sha, collectedAt: new Date().toISOString(), runs };
}
export function timelineMarkdown(timeline) {
  const cell = value => String(value ?? 'unknown').replaceAll('|', '\\|').replace(/[\r\n]/g, ' ');
  const minutes = value => value === null ? 'unknown' : (value / 60).toFixed(2);
  const lines = ['# Release run timeline', '', `Candidate: ${timeline.sourceSha}`, '',
    'Job spans are observed elapsed time; summed job durations include parallel work. Both exclude executions carried forward from previous attempts, identified by matching job name and execution timestamps even when GitHub changes their IDs. Start delay is from attempt start to first new job start, not an exact runner-queue measurement. Missing timestamps remain unknown. This report is timing evidence, not certification.', '',
    '| Run / attempt | Result | Complete | Start delay (min) | Job span (min) | Summed jobs (min) | Unmeasured jobs |',
    '| :--- | :--- | :--- | ---: | ---: | ---: | ---: |'];
  for (const run of timeline.runs) for (const a of run.attempts) {
    lines.push(`| ${run.id} / ${a.number} | ${cell(a.conclusion ?? a.status)} | ${a.complete} | ${minutes(a.startDelaySeconds)} | ${minutes(a.observedJobSpanSeconds)} | ${minutes(a.measuredJobSeconds)} | ${a.unmeasuredJobs} |`);
  }
  lines.push('', '## Step time by inferred category', '',
    'Categories are inferred from step names; ambiguous names remain other. Values sum measured step durations across new executions, including parallel work. They are not elapsed critical-path time. Unattributed time includes gaps outside steps, not a proven runner-queue delay.', '',
    '| Run / attempt | Setup (min) | Build (min) | Test (min) | Upload (min) | Other (min) |',
    '| :--- | ---: | ---: | ---: | ---: | ---: |');
  for (const run of timeline.runs) for (const a of run.attempts) {
    lines.push(`| ${run.id} / ${a.number} | ${['setup', 'build', 'test', 'upload', 'other'].map(category => minutes(a.inferredStepSeconds[category])).join(' | ')} |`);
  }
  lines.push('', '## Jobs and steps', '', '| Run / attempt | Job or step | Result | Minutes | Executed in attempt |', '| :--- | :--- | :--- | ---: | ---: |');
  for (const run of timeline.runs) for (const a of run.attempts) for (const job of a.jobs) {
    lines.push(`| ${run.id} / ${a.number} | ${cell(job.name)} | ${cell(job.conclusion ?? job.status)} | ${minutes(job.durationSeconds)} | ${job.executionAttempt} |`);
    for (const step of job.steps) lines.push(`| ${run.id} / ${a.number} | ${cell(job.name)}: ${cell(step.name)} | ${cell(step.conclusion ?? step.status)} | ${minutes(step.durationSeconds)} | ${job.executionAttempt} |`);
  }
  return lines.join('\n') + '\n';
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 5) throw new Error('Usage: node scripts/collect-release-timeline.mjs owner/repo <candidate-SHA> <run-id,run-id>');
    const [, , repository, sha, ids] = process.argv;
    const timeline = await collectTimeline(repository, sha, ids.split(','));
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    const directory = path.join(root, 'release-validation', 'timelines', `${Date.now()}-${process.pid}`);
    mkdirSync(directory, { recursive: true });
    writeFileSync(path.join(directory, 'timeline.json'), JSON.stringify(timeline, null, 2));
    writeFileSync(path.join(directory, 'timeline.md'), timelineMarkdown(timeline));
    console.log(`Timeline saved: ${directory}`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
