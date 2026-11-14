#!/usr/bin/env node
// Service-capacity release evidence (release reliability W8). Starts the Portal and Orchestrator
// from this checkout's Release build with throwaway state, provisions the reference data, runs the
// checked-in capacity workloads with scripts/test-service-capacity.mjs and gates the reports.
//
// Owner decision 2026-11-14: on a shared hosted runner latency is noise, so the gate is errors:
// an unexpected 5xx or transport failure, an error rate above the workload's limit, or SQLite
// contention fails. Latency, throughput and queue depth stay in the report for comparison only.
//
//   node scripts/run-service-capacity-certification.mjs --profile reference --out-dir <new dir>
//   node scripts/run-service-capacity-certification.mjs --profile full --out-dir <new dir>
//
// "reference" is the conditional service-capacity certification (the workstation reference
// workload, about 4 minutes of load). "full" is the full baseline: reference plus the Portal
// cache-cold/export profile and the Orchestrator real-script, schedule-density and
// process-spawning profiles.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, openSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const portalUrl = 'http://127.0.0.1:5110';
const orchestratorUrl = 'http://127.0.0.1:5111';

export const profiles = {
  reference: ['capacity-results/reference-local/workload.sanitized.json'],
  full: [
    'capacity-results/reference-local/workload.sanitized.json',
    'capacity-results/workloads/portal-cache-cold-exports.workload.json',
    'capacity-results/workloads/orchestrator-real-scripts.workload.json',
    'capacity-results/workloads/orchestrator-schedule-density.workload.json',
    'capacity-results/workloads/orchestrator-process-spawning.workload.json',
  ],
};

// Gate one capacity report. Returns the breaches that fail the certification.
export function errorBreaches(report, criteria = report.breachCriteria) {
  const breaches = [];
  for (const step of [...(report.portal ?? []), ...(report.orchestrator ?? [])]) {
    const label = `${step.service} at concurrency ${step.concurrency}`;
    // Statuses a workload declares for a request (409 already running, 503 overloaded) are its
    // API contract under load; every other failure counts.
    const counts = step.unexpectedStatusCounts;
    if (!counts || typeof counts !== 'object') { breaches.push(`${label}: the report does not classify failed requests`); continue; }
    const serverErrors = Object.entries(counts).filter(([status]) => Number(status) >= 500).reduce((sum, [, count]) => sum + count, 0);
    if (serverErrors > 0) breaches.push(`${label}: ${serverErrors} unexpected server error(s)`);
    if ((counts['0'] ?? 0) > 0) breaches.push(`${label}: ${counts['0']} request(s) failed without a response`);
    if (step.errorRatePct > criteria.maxErrorRatePct) breaches.push(`${label}: error rate ${step.errorRatePct}% > ${criteria.maxErrorRatePct}%`);
    if (step.sqliteContentionCount > criteria.maxSqliteContentionCount) breaches.push(`${label}: SQLite contention ${step.sqliteContentionCount} > ${criteria.maxSqliteContentionCount}`);
    if (!Number.isInteger(step.requestCount) || step.requestCount < 1) breaches.push(`${label}: no requests completed`);
  }
  if (!(report.portal?.length || report.orchestrator?.length)) breaches.push('The report has no measured steps.');
  return breaches;
}

// Point a checked-in workload at the services started here and fill in what provisioning created.
export function materializeWorkload(workload, { portal, orchestrator, apiKey, passwords, variables }) {
  const origins = new Map();
  if (workload.portal?.baseUrl) origins.set(new URL(workload.portal.baseUrl).origin, portal);
  if (workload.orchestrator?.baseUrl) origins.set(new URL(workload.orchestrator.baseUrl).origin, orchestrator);
  const retarget = value => {
    if (Array.isArray(value)) return value.map(retarget);
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, child]) => [key, retarget(child)]));
    if (typeof value === 'string' && /^https?:\/\//.test(value)) {
      // Swap the origin as text: re-serializing the URL would percent-encode {{variable}} templates.
      const origin = new URL(value).origin;
      const target = origins.get(origin);
      if (!target || !value.startsWith(origin)) return value;
      const rest = value.slice(origin.length);
      return rest === '' || rest === '/' ? target : `${target}${rest}`;
    }
    return value;
  };
  const result = retarget(structuredClone(workload));
  if (result.portal) {
    for (const [role, credentials] of Object.entries(result.portal.roles ?? {})) {
      if (!passwords[role]) throw new Error(`No provisioned password for Portal role ${role}.`);
      credentials.password = passwords[role];
    }
  }
  if (result.orchestrator) result.orchestrator.apiKey = apiKey;
  for (const name of Object.keys(result.variables ?? {})) {
    if (variables[name] !== undefined) result.variables[name] = String(variables[name]);
  }
  return result;
}

function secret(bytes = 24) { return randomBytes(bytes).toString('base64url'); }

function startService(name, project, dll, env, logDirectory) {
  const out = openSync(path.join(logDirectory, `${name}.log`), 'a');
  const child = spawn('dotnet', [dll], { cwd: path.join(root, project), env: { ...process.env, ...env }, stdio: ['ignore', out, out] });
  child.on('exit', code => { child.exitCodeSeen = code ?? -1; });
  return child;
}

async function stopService(child) {
  if (!child || child.exitCodeSeen !== undefined) return;
  const exited = new Promise(resolve => child.once('exit', resolve));
  child.kill('SIGTERM');
  const timer = setTimeout(() => child.kill('SIGKILL'), 20000);
  await exited;
  clearTimeout(timer);
}

// A service left over from an interrupted run would answer health checks for the one started here.
async function assertPortFree(url, name) {
  try {
    await fetch(new URL('/health', url), { signal: AbortSignal.timeout(2000) });
  } catch {
    return;
  }
  throw new Error(`Something already answers on ${url}; stop it before certifying the ${name}.`);
}

async function waitHealthy(url, child, name, timeoutMs = 180000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCodeSeen !== undefined) throw new Error(`${name} exited with code ${child.exitCodeSeen} before becoming healthy.`);
    try {
      const response = await fetch(new URL('/health', url));
      if (response.ok) return;
    } catch { /* not listening yet */ }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`${name} did not become healthy within ${timeoutMs / 1000} s.`);
}

function run(command, args, { env = {}, log, timeoutMs }) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: root, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    const timer = setTimeout(() => child.kill('SIGKILL'), timeoutMs);
    child.on('error', reject);
    child.on('exit', code => {
      clearTimeout(timer);
      if (log) writeFileSync(log, stdout + (stderr ? `\n--- stderr\n${stderr}` : ''));
      if (code === 0) resolve(stdout);
      else reject(new Error(`${path.basename(args[0] ?? command)} exited with ${code}: ${stderr.trim().split('\n').slice(-5).join(' ')}`));
    });
  });
}

async function api(baseUrl, method, route, body, token) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(new URL(route, baseUrl), { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${route} failed with ${response.status}: ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : {};
}

// The cache-cold workload refreshes a dataset-backed report and reads the dataset it registers.
async function provisionCacheCold(passwords, folderId) {
  const { token } = await api(portalUrl, 'POST', '/api/auth/login', { username: 'admin', password: passwords.admin });
  const report = await api(portalUrl, 'POST', '/api/reports', {
    folderId, name: 'Capacity Cache-Cold Report', description: 'Dataset-backed capacity report', scriptPath: 'capacity-cache-cold.rptsql',
  }, token);
  const execution = await api(portalUrl, 'POST', `/api/reports/${report.id}/execute`, { parameters: {} }, token);
  const deadline = Date.now() + 120000;
  for (;;) {
    const job = await api(portalUrl, 'GET', `/api/jobs/${execution.jobId}`, undefined, token);
    if (job.status === 'Completed') break;
    if (job.status === 'Failed' || job.status === 'Cancelled') throw new Error(`Cache-cold report execution ended ${job.status}: ${job.error ?? ''}`);
    if (Date.now() > deadline) throw new Error('Cache-cold report execution did not finish within 120 s.');
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  // A script error completes the job and is recorded only in the snapshot manifest.
  const manifest = await api(portalUrl, 'GET', `/api/reports/${report.id}/snapshot/manifest`, undefined, token);
  if (manifest.error ?? manifest.Error) throw new Error(`The cache-cold report failed: ${manifest.error ?? manifest.Error}`);
  const datasets = await api(portalUrl, 'GET', '/api/datasets', undefined, token);
  const list = Array.isArray(datasets) ? datasets : datasets.items ?? datasets.datasets ?? [];
  const dataset = list.find(item => String(item.name ?? '').toLowerCase().includes('capacity_cold'));
  if (!dataset) throw new Error(`The cache-cold report registered no capacity_cold dataset (found ${list.length}).`);
  return { cacheColdReportId: report.id, datasetId: dataset.id };
}

export async function certify({ profile, outDir, configuration = 'Release' }) {
  if (!profiles[profile]) throw new Error(`Unknown capacity profile: ${profile}`);
  if (existsSync(outDir) && readdirSync(outDir).length) throw new Error('Use a new, empty output directory.');
  await assertPortFree(portalUrl, 'Portal');
  await assertPortFree(orchestratorUrl, 'Orchestrator');
  mkdirSync(outDir, { recursive: true });
  const logs = path.join(outDir, 'logs');
  mkdirSync(logs);
  // Not under the OS temp folder: on Windows that is AppData, which the engine's path guard refuses.
  // The state holds disposable credentials and databases; it is removed when the run ends.
  mkdirSync(path.join(root, 'artifacts'), { recursive: true });
  const state = mkdtempSync(path.join(root, 'artifacts', 'capacity-state-'));
  const bin = () => path.join('bin', configuration, 'net10.0');
  const cli = path.join(root, 'src/ETL-SQL.App', bin(), process.platform === 'win32' ? 'ETL-SQL.exe' : 'ETL-SQL');
  for (const required of [path.join(root, 'src/ETL-SQL.Portal', bin(), 'ETL-SQL-Portal.dll'),
    path.join(root, 'src/ETL-SQL.Orchestrator.Service', bin(), 'ETL-SQL-Service.dll'), cli]) {
    if (!existsSync(required)) throw new Error(`Build the solution in ${configuration} first: missing ${path.relative(root, required)}.`);
  }

  const apiKey = secret(32), signing = secret(32), initialAdmin = `Init-${secret(12)}1!`;
  const passwords = { admin: `Adm-${secret(12)}1!`, viewer: `View-${secret(12)}1!`, publisher: `Pub-${secret(12)}1!` };
  const orchestratorEnv = spawning => ({
    ASPNETCORE_URLS: orchestratorUrl, ASPNETCORE_ENVIRONMENT: 'Production',
    Orchestrator__ApiKey: apiKey, Orchestrator__IdentitySigningSecret: signing, Orchestrator__RequireFederatedIdentity: 'false',
    Orchestrator__DatabasePath: path.join(state, 'orchestrator.db'), Session__Root: path.join(state, 'orchestrator-sessions'),
    Scheduler__SleepIntervalSeconds: '1', Jobs__UseProcessSpawning: spawning ? 'true' : 'false', Jobs__ExecutablePath: cli,
  });
  const portalEnv = {
    ASPNETCORE_URLS: portalUrl, ASPNETCORE_ENVIRONMENT: 'Production',
    Portal__ScriptRootPath: path.join(root, 'capacity-results/reference-local'),
    Portal__DatabasePath: path.join(state, 'portal.db'), Portal__SnapshotDirectory: path.join(state, 'snapshots'),
    Portal__DatasetRootPath: path.join(state, 'datasets'), Portal__Storage__KeyRingPath: path.join(state, 'portal-keys'),
    Portal__Jwt__Secret: secret(48), Portal__Dataset__AtRestKey: randomBytes(32).toString('base64'),
    Portal__FirstRun__AdminUsername: 'admin', Portal__FirstRun__AdminPassword: initialAdmin, Portal__FirstRun__MustChangePassword: 'true',
    // Registering the reference reports goes through the Studio publishing route.
    Portal__Studio__Mode: 'SourceControlled', Portal__Studio__RoleCapabilities__Admin__0: 'StudioAccess',
    Portal__Studio__RoleCapabilities__Admin__1: 'ReportPublish',
    Session__Root: path.join(state, 'portal-sessions'),
  };

  const summary = { schemaVersion: 1, profile, configuration, startedAt: new Date().toISOString(), host: { platform: `${os.platform()} ${os.release()}`, cpus: os.cpus().length, memoryGB: Math.round(os.totalmem() / 2 ** 30) }, workloads: [] };
  let orchestrator = startService('orchestrator', 'src/ETL-SQL.Orchestrator.Service', path.join(bin(), 'ETL-SQL-Service.dll'), orchestratorEnv(false), logs);
  const portal = startService('portal', 'src/ETL-SQL.Portal', path.join(bin(), 'ETL-SQL-Portal.dll'), portalEnv, logs);
  try {
    await waitHealthy(orchestratorUrl, orchestrator, 'Orchestrator');
    await waitHealthy(portalUrl, portal, 'Portal');
    const provisioned = JSON.parse(await run(process.execPath, ['capacity-results/reference-local/provision-reference.mjs'], {
      log: path.join(logs, 'provision-reference.log'), timeoutMs: 300000,
      env: {
        CAPACITY_PORTAL_URL: portalUrl, CAPACITY_ORCHESTRATOR_URL: orchestratorUrl, CAPACITY_ORCHESTRATOR_API_KEY: apiKey,
        CAPACITY_INITIAL_ADMIN_PASSWORD: initialAdmin, CAPACITY_ADMIN_PASSWORD: passwords.admin,
        CAPACITY_VIEWER_PASSWORD: passwords.viewer, CAPACITY_PUBLISHER_PASSWORD: passwords.publisher,
      },
    }));
    const variables = { ...provisioned };
    if (profile === 'full') Object.assign(variables, await provisionCacheCold(passwords, provisioned.folderId));
    summary.provisioned = variables;

    for (const workloadPath of profiles[profile]) {
      const name = path.basename(workloadPath).replace(/\.(workload\.)?json$/, '').replace(/^workload\./, 'reference.');
      const spawning = name.includes('process-spawning');
      if (spawning) {
        await stopService(orchestrator);
        orchestrator = startService('orchestrator-spawning', 'src/ETL-SQL.Orchestrator.Service', path.join(bin(), 'ETL-SQL-Service.dll'), orchestratorEnv(true), logs);
        await waitHealthy(orchestratorUrl, orchestrator, 'Orchestrator (process spawning)');
      }
      const workload = materializeWorkload(JSON.parse(readFileSync(path.join(root, workloadPath), 'utf8')), {
        portal: portalUrl, orchestrator: orchestratorUrl, apiKey, passwords, variables,
      });
      const workloadDir = path.join(outDir, name);
      mkdirSync(workloadDir);
      // The materialized config holds disposable credentials; keep it outside the retained evidence.
      const config = path.join(state, `${name}.json`);
      writeFileSync(config, JSON.stringify(workload, null, 2));
      const started = Date.now();
      let failure = null;
      try {
        await run(process.execPath, ['scripts/test-service-capacity.mjs', '--config', config, '--out-dir', workloadDir], { log: path.join(logs, `${name}.log`), timeoutMs: 3600000 });
      } catch (error) { failure = error.message; }
      const reportPath = path.join(workloadDir, 'capacity-report.json');
      const report = existsSync(reportPath) ? JSON.parse(readFileSync(reportPath, 'utf8')) : null;
      // The report records the config path it ran with; it points into the discarded state directory.
      if (report) { report.configPath = workloadPath; writeFileSync(reportPath, JSON.stringify(report, null, 2)); }
      const breaches = failure ? [failure] : errorBreaches(report);
      summary.workloads.push({ name, workload: workloadPath, minutes: Math.round((Date.now() - started) / 6000) / 10, passed: breaches.length === 0, breaches });
      console.log(`${name}: ${breaches.length ? 'FAIL' : 'pass'}${breaches.length ? `\n  ${breaches.join('\n  ')}` : ''}`);
    }
  } finally {
    await stopService(portal);
    await stopService(orchestrator);
    rmSync(state, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
    summary.completedAt = new Date().toISOString();
    summary.passed = summary.workloads.length === profiles[profile].length && summary.workloads.every(item => item.passed);
    writeFileSync(path.join(outDir, 'service-capacity-certification.json'), JSON.stringify(summary, null, 2) + '\n');
    writeFileSync(path.join(outDir, 'service-capacity-certification.md'), renderMarkdown(summary));
  }
  return summary;
}

export function renderMarkdown(summary) {
  const lines = [`## Service capacity (${summary.profile})`, '',
    'Gated on errors: an unexpected 5xx or transport failure, error rate above the workload limit, or SQLite contention. Latency and throughput are in each capacity-report.md for comparison only.', '',
    '| Workload | Minutes | Result |', '| :--- | ---: | :---: |'];
  for (const item of summary.workloads) lines.push(`| ${item.name} | ${item.minutes} | ${item.passed ? 'Pass' : 'FAIL'} |`);
  for (const item of summary.workloads.filter(entry => !entry.passed)) lines.push('', `**${item.name}**`, ...item.breaches.map(breach => `- ${breach}`));
  return lines.join('\n') + '\n';
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const option = name => { const index = args.indexOf(`--${name}`); return index >= 0 ? args[index + 1] : undefined; };
  const profile = option('profile'), outDir = option('out-dir');
  if (!profile || !outDir) {
    console.error('Usage: node scripts/run-service-capacity-certification.mjs --profile reference|full --out-dir <new directory> [--configuration Release]');
    process.exit(2);
  }
  const summary = await certify({ profile, outDir: path.resolve(outDir), configuration: option('configuration') ?? 'Release' });
  console.log(renderMarkdown(summary));
  process.exit(summary.passed ? 0 : 1);
}
