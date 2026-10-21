import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const hostedPolicy = JSON.parse(readFileSync(new URL('./hosted-release-policy.json', import.meta.url), 'utf8'));

function ghApi(endpoint) {
  const result = spawnSync('gh', ['api', '--paginate', '--slurp', endpoint], {
    encoding: 'utf8', timeout: 60000, maxBuffer: 32 * 1024 * 1024, windowsHide: true,
  });
  if (result.error || result.status !== 0) throw new Error(`Cannot read hosted release evidence: ${endpoint}`);
  return JSON.parse(result.stdout);
}

function paged(pages, property) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page[property]))) {
    throw new Error(`Invalid paginated ${property} response.`);
  }
  const records = pages.flatMap(page => page[property]);
  if (pages.some(page => page.total_count !== records.length) ||
      records.some(record => !Number.isSafeInteger(record.id) || record.id < 1) ||
      new Set(records.map(record => record.id)).size !== records.length) {
    throw new Error(`Incomplete or duplicate ${property} response.`);
  }
  return records;
}

function sameRepository(run, repository) {
  return run.repository?.full_name?.toLowerCase() === repository.toLowerCase() &&
    run.head_repository?.full_name?.toLowerCase() === repository.toLowerCase();
}

function missingCoverage(message) {
  return Object.assign(new Error(message), { code: 'INCOMPLETE_COVERAGE' });
}

export async function collectHostedEvidence(repository, sha, {
  query = ghApi, now = Date.now(), policy = hostedPolicy,
} = {}) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository) || !/^[a-f0-9]{40}$/.test(sha)) {
    throw new Error('Supply a repository and exact lowercase candidate SHA.');
  }
  if (policy.schemaVersion !== 1 || !Number.isFinite(policy.maximumAgeHours) || policy.maximumAgeHours <= 0 ||
      !Array.isArray(policy.workflows) || !policy.workflows.length) throw new Error('Invalid hosted evidence policy.');
  const workflows = [];
  for (const configuredRequirement of policy.workflows) {
    let requirement = configuredRequirement;
    if (!/^[\w-]+\.yml$/.test(requirement.file) || !Array.isArray(requirement.jobs) || !requirement.jobs.length ||
        new Set(requirement.jobs).size !== requirement.jobs.length ||
        !Array.isArray(requirement.events) || !requirement.events.length) throw new Error('Invalid workflow requirement.');
    async function findRun(requirement) {
      const endpoint = `repos/${repository}/actions/workflows/${requirement.file}/runs?head_sha=${sha}&per_page=100`;
      const runs = paged(await query(endpoint), 'workflow_runs');
      const eligible = runs.filter(run => run.head_sha === sha && sameRepository(run, repository) &&
        run.path === `.github/workflows/${requirement.file}` && requirement.events.includes(run.event) &&
        (run.head_branch === 'main' || /^release\/.+/.test(run.head_branch ?? '')));
      return eligible.sort((a, b) => b.id - a.id)[0];
    }
    let run = await findRun(requirement);
    // A full regular push may already have paid for every lane. A requested certification run
    // takes precedence even if red; fallback is allowed only when no such run exists.
    if (!run && requirement.fallback) {
      requirement = { ...configuredRequirement, ...configuredRequirement.fallback, fallback: undefined };
      run = await findRun(requirement);
    }
    if (!run) throw new Error(`Missing exact-candidate ${requirement.file} evidence on main or release/**.`);
    // The latest candidate run owns the verdict. An older green run cannot conceal a newer failure.
    if (run.status !== 'completed' || run.conclusion !== 'success') {
      throw new Error(`${requirement.file} run ${run.id} is ${run.status}/${run.conclusion}. Inspect: gh run view ${run.id} --repo ${repository} --log-failed. Retry unchanged inputs: gh run rerun ${run.id} --repo ${repository} --failed`);
    }
    const created = Date.parse(run.created_at);
    if (!Number.isFinite(created) || created > now || now - created > policy.maximumAgeHours * 3600000 ||
        !Number.isSafeInteger(run.run_attempt) || run.run_attempt < 1) throw new Error(`Stale or invalid ${requirement.file} run.`);
    // Enumerate attempts explicitly. A failed-job retry need not include successful earlier
    // jobs, and the job API does not promise a run_attempt property on each job.
    const latestJobs = new Map();
    const history = [];
    for (let attempt = 1; attempt <= run.run_attempt; attempt++) {
      const attemptJobs = paged(await query(`repos/${repository}/actions/runs/${run.id}/attempts/${attempt}/jobs?per_page=100`), 'jobs');
      if (new Set(attemptJobs.map(job => job.name)).size !== attemptJobs.length) throw new Error('Duplicate job names within an attempt.');
      for (const job of attemptJobs) {
        if (job.run_id !== run.id || job.head_sha !== sha) throw new Error('Job belongs to another candidate or run.');
        const previous = latestJobs.get(job.name);
        // Carried successes can receive new API IDs while retaining execution timestamps.
        const copiedExecution = previous && job.started_at && job.completed_at &&
          previous.started_at === job.started_at && previous.completed_at === job.completed_at &&
          previous.conclusion === job.conclusion;
        const originAttempt = previous && (previous.id === job.id || copiedExecution) ? previous.originAttempt : attempt;
        latestJobs.set(job.name, { ...job, originAttempt });
      }
      history.push({ attempt, jobs: attemptJobs.map(job => ({ id: job.id, name: job.name, conclusion: job.conclusion })) });
    }
    const jobs = [...latestJobs.values()];
    const retained = [];
    for (const name of requirement.jobs) {
      const matches = jobs.filter(job => job.name === `${requirement.jobPrefix ?? ''}${name}`);
      if (matches.length === 0 || (matches.length === 1 && matches[0].conclusion === 'skipped')) {
        throw missingCoverage(`Required job did not run: ${requirement.file} / ${name}`);
      }
      if (matches.length !== 1) throw new Error(`Required job must appear once: ${requirement.file} / ${name}`);
      const job = matches[0];
      const completed = Date.parse(job.completed_at);
      if (job.run_id !== run.id || job.head_sha !== sha || job.status !== 'completed' || job.conclusion !== 'success' ||
          !Number.isFinite(completed) || completed > now || completed < created) {
        throw new Error(`Missing, failed, skipped or invalid job evidence: ${requirement.file} / ${name}`);
      }
      retained.push({ id: job.id, name, attempt: job.originAttempt, conclusion: job.conclusion,
        completedAt: job.completed_at, url: job.html_url });
    }
    if (jobs.some(job => job.status !== 'completed' || job.conclusion !== 'success')) {
      throw new Error(`${requirement.file} contains an unsuccessful or skipped job.`);
    }
    // Recheck after fetching jobs so a concurrent retry cannot mix run-attempt snapshots.
    const refreshedPages = await query(`repos/${repository}/actions/runs/${run.id}`);
    if (!Array.isArray(refreshedPages) || refreshedPages.length !== 1) throw new Error('Invalid run identity response.');
    const refreshed = refreshedPages[0];
    if (refreshed.id !== run.id || refreshed.head_sha !== sha || refreshed.run_attempt !== run.run_attempt ||
        refreshed.status !== 'completed' || refreshed.conclusion !== 'success' ||
        refreshed.path !== run.path || !sameRepository(refreshed, repository)) {
      throw new Error('Run changed during evidence collection; inspect and collect again.');
    }
    workflows.push({ workflow: requirement.file, id: run.id, attempt: run.run_attempt, event: run.event,
      branch: run.head_branch, createdAt: run.created_at, url: run.html_url, jobs: retained, history });
  }
  return { schemaVersion: 1, scope: 'hosted-ci-and-codeql', fullReleaseCertification: false,
    repository, sourceSha: sha, collectedAt: new Date(now).toISOString(), workflows };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 5) throw new Error('Usage: node scripts/collect-hosted-release-evidence.mjs <owner/repo> <SHA> <output.json>');
    const manifest = await collectHostedEvidence(process.argv[2], process.argv[3]);
    const output = path.resolve(process.argv[4]);
    mkdirSync(path.dirname(output), { recursive: true });
    writeFileSync(output, JSON.stringify(manifest, null, 2) + '\n');
    console.log(`Hosted CI/CodeQL evidence verified for ${manifest.sourceSha}; saved ${output}. Other release certifications remain required.`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
