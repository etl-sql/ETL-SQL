#!/usr/bin/env node
// HA soak certification on a real Compose topology: PostgreSQL, two Portal and two Orchestrator
// nodes behind HAProxy, built from this commit. It runs three gates and writes the evidence that
// `etl-sql admin ha-soak validate --required-gate All` checks:
//
//   1. Sustained load through the load balancer (scripts/test-service-capacity.mjs), gated on
//      errors only; latency is reported.
//   2. Large jobs: concurrent spilling jobs whose scripts check their own results, and four
//      cancellations through the load balancer at a named point (scan, spill write, spill read,
//      repartition).
//   3. Faults injected for real: Portal and Orchestrator node loss, a killed worker, a PostgreSQL
//      outage, and disk faults on a size-limited spill volume (low space, full disk, slow disk,
//      corrupt extent, crash with extents on disk, temp-root exhaustion).
//
// Linux only: the spill volume is a loop-mounted ext4 image, and slow disk throttles it through
// the container's cgroup. It needs Docker and passwordless sudo, as on GitHub-hosted runners.
//
// Usage: node scripts/run-ha-soak-certification.mjs --run-id <id> [--image-tag <tag>] [--skip-build]
//          [--phases sustained,large-job,faults] [--keep]
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const composeFile = path.join(root, 'deploy/docker/docker-compose.ha.yml');
const RUNNER_KIND = 'HostedComposeTopology';
const LEASE_SECONDS = 30;
const CANCEL_POLL_SECONDS = 2;
const SPILL_VOLUME_MB = 6144;
const SPILL_RESERVE_MB = 256;

// ── Process helpers ──────────────────────────────────────────────────────────

function sh(command, args, { input, allowFail = false, sudo = false, timeoutMs = 600000 } = {}) {
  const [file, argv] = sudo ? ['sudo', ['-n', command, ...args]] : [command, args];
  const result = spawnSync(file, argv, { cwd: root, input, encoding: 'utf8', timeout: timeoutMs, maxBuffer: 64 * 1024 * 1024 });
  if (result.error) throw result.error;
  if (result.status !== 0 && !allowFail)
    throw new Error(`${command} ${args.slice(0, 3).join(' ')} exited with ${result.status}: ${(result.stderr || result.stdout).trim().split('\n').slice(-5).join(' ')}`);
  return { code: result.status, out: result.stdout.trim(), err: result.stderr.trim() };
}

function runLogged(command, args, log, { env = {}, timeoutMs = 3600000 } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: root, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    child.stdout.on('data', chunk => { output += chunk; });
    child.stderr.on('data', chunk => { output += chunk; });
    const timer = setTimeout(() => child.kill('SIGKILL'), timeoutMs);
    child.on('error', reject);
    child.on('exit', code => {
      clearTimeout(timer);
      writeFileSync(log, output);
      if (code === 0) resolve(output);
      else reject(new Error(`${path.basename(args[0] ?? command)} exited with ${code}; see ${path.relative(root, log)}`));
    });
  });
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function until(what, probe, { timeoutMs, intervalMs = 1000 }) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await probe();
      if (last) return last;
    } catch (error) {
      last = error;
    }
    await sleep(intervalMs);
  }
  throw new Error(`Timed out after ${timeoutMs / 1000} s waiting for ${what}${last instanceof Error ? `: ${last.message}` : ''}.`);
}

function readEnv(file) {
  return Object.fromEntries(readFileSync(file, 'utf8').split(/\r?\n/).filter(line => line.includes('=') && !line.startsWith('#'))
    .map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]));
}

function writeJson(file, value) {
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, JSON.stringify(value, null, 2) + '\n');
}

// ── Topology ─────────────────────────────────────────────────────────────────

class Topology {
  constructor(runRoot) {
    this.runRoot = runRoot;
    this.env = readEnv(path.join(runRoot, 'postgres-ha-soak.env'));
    this.project = this.env.COMPOSE_PROJECT_NAME;
    this.override = path.join(runRoot, 'ha-certification.override.yml');
    this.spillMount = path.join(runRoot, 'spill');
    this.spillParent = path.join(this.spillMount, 'ETL-SQL-Spill');
    this.portalUrl = `http://127.0.0.1:${this.env.PORT_PORTAL}`;
    this.orchestratorUrl = `http://127.0.0.1:${this.env.PORT_ORCH}`;
    this.token = null;
    this.startedJobs = new Set(); // names of jobs the current scenario created
    this.assertion = null;
  }

  compose(args, options) {
    return sh('docker', ['compose', '--env-file', path.join(this.runRoot, 'postgres-ha-soak.env'), '-f', composeFile, '-f', this.override, ...args], options);
  }

  containers(service) {
    return this.compose(['ps', '-q', service]).out.split('\n').filter(Boolean);
  }

  allContainers(service) {
    return this.compose(['ps', '-aq', service]).out.split('\n').filter(Boolean);
  }

  hostname(container) {
    return sh('docker', ['inspect', '-f', '{{.Config.Hostname}}', container]).out;
  }

  hostPid(container) {
    return Number(sh('docker', ['inspect', '-f', '{{.State.Pid}}', container]).out);
  }

  running(container) {
    return sh('docker', ['inspect', '-f', '{{.State.Running}}', container], { allowFail: true }).out === 'true';
  }

  psql(sql, database = this.env.PG_DB_ORCH) {
    return this.compose(['exec', '-T', 'postgres', 'psql', '-U', this.env.PG_USER, '-d', database, '-At', '-F', '\t', '-c', sql]).out;
  }

  // The container that holds a job's execution lease: lease owners are hostname:pid:guid.
  leaseOwner(jobName) {
    const owner = this.psql(`SELECT COALESCE(lease_owner, '') FROM jobs WHERE name = '${jobName.replace(/'/g, "''")}'`);
    return owner || null;
  }

  containerForOwner(owner) {
    if (!owner) return null;
    const host = owner.split(':')[0];
    return this.allContainers('orchestrator').find(container => this.hostname(container) === host) ?? null;
  }

  async login() {
    const response = await fetch(new URL('/api/auth/login', this.portalUrl), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: this.env.PORTAL_ADMIN_USERNAME ?? 'admin', password: this.env.PORTAL_ADMIN_PASSWORD }),
    });
    if (!response.ok) throw new Error(`Portal login through the load balancer returned ${response.status}.`);
    this.token = (await response.json()).token;
    this.assertion = null;
  }

  async orchestratorHeaders() {
    if (!this.assertion || this.assertion.expiresAt < Date.now() + 30000) {
      const response = await fetch(new URL('/api/auth/orchestrator-assertion', this.portalUrl), {
        method: 'POST', headers: { Authorization: `Bearer ${this.token}` },
      });
      if (response.status === 401) { await this.login(); return this.orchestratorHeaders(); }
      if (!response.ok) throw new Error(`Orchestrator assertion exchange returned ${response.status}.`);
      const issued = await response.json();
      this.assertion = { value: issued.assertion, expiresAt: Date.parse(issued.expiresAt) };
    }
    return { 'X-Orchestrator-Key': this.env.ORCH_API_KEY, 'X-Orchestrator-Identity': this.assertion.value };
  }

  async orchestrator(method, route, body, { allowStatus = [], headers: extra = {} } = {}) {
    const headers = { ...(await this.orchestratorHeaders()), ...extra };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(new URL(route, this.orchestratorUrl), {
      method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000),
    });
    const text = await response.text();
    if (!response.ok && !allowStatus.includes(response.status))
      throw new Error(`${method} ${route} returned ${response.status}: ${text.slice(0, 300)}`);
    return { status: response.status, json: text ? JSON.parse(text) : null };
  }

  async portal(method, route, { cookie, body } = {}) {
    const headers = { Authorization: `Bearer ${this.token}` };
    if (cookie) headers.Cookie = cookie;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    try {
      const response = await fetch(new URL(route, this.portalUrl), {
        method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(30000),
      });
      await response.arrayBuffer();
      return { status: response.status, setCookie: response.headers.get('set-cookie') ?? '' };
    } catch (error) {
      return { status: 0, error: String(error) };
    }
  }

  // ── Jobs ──

  // A new job is due at once, so creating it starts its first run on whichever node claims the lease.
  async startJob(name, scriptText) {
    this.startedJobs.add(name);
    await this.orchestrator('POST', '/api/scheduled-jobs', { name, scriptText, interval: 1, unit: 'DAY', maxRetries: 0 });
    return this.waitForRun(name);
  }

  async trigger(name) {
    const before = (await this.history(name)).map(run => run.id);
    await this.orchestrator('POST', `/api/scheduled-jobs/${encodeURIComponent(name)}/trigger`, undefined, { allowStatus: [202] });
    return this.waitForRun(name, before);
  }

  async history(name, limit = 20) {
    const { json } = await this.orchestrator('GET', `/api/scheduled-jobs/${encodeURIComponent(name)}/history?limit=${limit}`);
    return (json ?? []).map(run => ({
      id: run.id ?? run.Id, status: run.status ?? run.Status, startTime: run.startTime ?? run.StartTime,
      endTime: run.endTime ?? run.EndTime, errorMessage: run.errorMessage ?? run.ErrorMessage ?? '',
      rowsProcessed: run.rowsProcessed ?? run.RowsProcessed ?? 0,
    }));
  }

  async waitForRun(name, excluding = []) {
    return until(`a run of ${name} to start`, async () =>
      (await this.history(name)).find(run => !excluding.includes(run.id)), { timeoutMs: 120000 });
  }

  async waitForEnd(name, runId, timeoutMs = 900000) {
    return until(`run ${runId} of ${name} to end`, async () => {
      const run = (await this.history(name, 50)).find(entry => entry.id === runId);
      return run && run.status !== 'RUNNING' && run.endTime ? run : null;
    }, { timeoutMs, intervalMs: 2000 });
  }

  async kill(name) {
    return this.orchestrator('POST', `/api/scheduled-jobs/${encodeURIComponent(name)}/kill`, undefined, { allowStatus: [202] });
  }

  // Deleting a job needs If-Match with its current version (428 without it). A run finishing
  // between the read and the delete bumps the version, so a conflict is read again and retried.
  async deleteJob(name) {
    for (let attempt = 0; attempt < 5; attempt++) {
      const { json } = await this.orchestrator('GET', '/api/scheduled-jobs?limit=1000');
      const job = (json ?? []).find(entry => (entry.name ?? entry.Name) === name);
      if (!job) return;
      const version = job.version ?? job.Version;
      const { status } = await this.orchestrator('DELETE', `/api/scheduled-jobs/${encodeURIComponent(name)}`, undefined,
        { allowStatus: [204, 404, 409], headers: { 'If-Match': `"${version}"` } });
      if (status !== 409) return;
    }
    throw new Error(`Job ${name} kept changing; it could not be deleted.`);
  }

  // A scenario that fails part-way leaves its job running. That job keeps writing spill and holds
  // a worker, so every later scenario would fail on its leftovers; stop it before the next begins.
  async stopScenarioJobs() {
    const stopped = [];
    for (const name of this.startedJobs) {
      const running = (await this.history(name, 50).catch(() => [])).filter(run => run.status === 'RUNNING' || !run.endTime);
      if (running.length === 0) continue;
      await this.kill(name).catch(() => {});
      for (const run of running) await this.waitForEnd(name, run.id, 300000).catch(() => {});
      stopped.push(name);
    }
    this.startedJobs.clear();
    return stopped;
  }

  // ── Spill volume ──

  spillRoots() {
    if (!existsSync(this.spillParent)) return [];
    return sh('ls', ['-1', this.spillParent], { sudo: true }).out.split('\n').filter(Boolean);
  }

  spillBytes() {
    if (!existsSync(this.spillParent)) return 0;
    return Number(sh('du', ['-sb', this.spillParent], { sudo: true }).out.split(/\s+/)[0]);
  }

  spillFiles() {
    if (!existsSync(this.spillParent)) return [];
    return sh('find', [this.spillParent, '-type', 'f'], { sudo: true }).out.split('\n').filter(Boolean);
  }

  freeBytes() {
    return Number(sh('df', ['--output=avail', '-B1', this.spillMount]).out.split('\n')[1]);
  }

  fillUntilFree(freeBytes, name = 'ballast') {
    const file = path.join(this.spillMount, name);
    const size = Math.max(0, this.freeBytes() - freeBytes);
    sh('fallocate', ['-l', String(size), file], { sudo: true });
    return size;
  }

  removeBallast() {
    for (const name of ['ballast', 'ballast-2']) sh('rm', ['-f', path.join(this.spillMount, name)], { sudo: true });
  }

  processIo(container) {
    const text = sh('cat', [`/proc/${this.hostPid(container)}/io`], { sudo: true }).out;
    return Object.fromEntries(text.split('\n').map(line => line.split(':').map(part => part.trim())).map(([key, value]) => [key, Number(value)]));
  }
}

// ── Setup ────────────────────────────────────────────────────────────────────

function requireLinuxHost() {
  if (process.platform !== 'linux')
    throw new Error('The HA certification needs a Linux Docker host: it loop-mounts the spill volume and throttles it through cgroups.');
  sh('docker', ['version', '--format', '{{.Server.Version}}']);
  sh('true', [], { sudo: true });
  if (!existsSync('/sys/fs/cgroup/cgroup.controllers')) throw new Error('cgroup v2 is required to throttle the spill volume.');
}

async function buildImages(tag, logs) {
  await Promise.all([
    runLogged('docker', ['build', '-f', 'src/ETL-SQL.Orchestrator.Service/Dockerfile', '-t', `etl-sql/orchestrator:${tag}`, '.'], path.join(logs, 'build-orchestrator.log')),
    runLogged('docker', ['build', '-f', 'src/ETL-SQL.Portal/Dockerfile', '-t', `etl-sql/portal:${tag}`, '.'], path.join(logs, 'build-portal.log')),
  ]);
}

function cli(args, log) {
  const binary = path.join(root, 'src/ETL-SQL.App/bin/Release/net10.0/ETL-SQL');
  if (!existsSync(binary)) throw new Error('Build the solution in Release first: the ha-soak commands come from src/ETL-SQL.App.');
  const result = sh(binary, args, { allowFail: true });
  if (log) writeFileSync(log, `${result.out}\n${result.err}`);
  if (result.code !== 0) throw new Error(`etl-sql ${args.slice(0, 3).join(' ')} failed: ${(result.err || result.out).split('\n').slice(-3).join(' ')}`);
  return result.out;
}

function mountSpillVolume(topology) {
  const image = path.join(topology.runRoot, 'spill.img');
  mkdirSync(topology.spillMount, { recursive: true });
  sh('truncate', ['-s', `${SPILL_VOLUME_MB}M`, image]);
  sh('mkfs.ext4', ['-q', '-F', image]);
  sh('mount', ['-o', 'loop', image, topology.spillMount], { sudo: true });
  // The spill parent must not be writable by others, or the sweep ignores owner markers.
  sh('chmod', ['0755', topology.spillMount], { sudo: true });
  const device = sh('findmnt', ['-n', '-o', 'SOURCE', topology.spillMount]).out;
  topology.spillDevice = sh('lsblk', ['-n', '-o', 'MAJ:MIN', device]).out.trim();
}

function unmountSpillVolume(topology) {
  sh('umount', [topology.spillMount], { sudo: true, allowFail: true });
}

function writeOverride(topology) {
  // Short leases so failover is measured in seconds; the spill volume replaces the container temp
  // directory; adaptive execution is on so slow storage is reported as storage pressure.
  const override = `services:
  orchestrator:
    environment:
      - Scheduler__JobLeaseSeconds=${LEASE_SECONDS}
      - Scheduler__CancelPollSeconds=${CANCEL_POLL_SECONDS}
      - Engine__SpillMinimumFreeDiskMB=${SPILL_RESERVE_MB}
      - Engine__Adaptive__Enabled=true
      - TMPDIR=/spill
    volumes:
      - ${topology.spillMount}:/spill
`;
  writeFileSync(topology.override, override);
}

async function waitForTopology(topology) {
  await until('both Orchestrators and both Portals to serve through the load balancer', async () => {
    if (topology.containers('orchestrator').length !== 2 || topology.containers('portal').length !== 2) return false;
    const portal = await fetch(new URL('/healthz', topology.portalUrl)).catch(() => null);
    const orchestrator = await fetch(new URL('/health', topology.orchestratorUrl)).catch(() => null);
    return portal?.ok && orchestrator?.ok;
  }, { timeoutMs: 300000, intervalMs: 3000 });
  await until('the admin to log in', async () => { await topology.login(); return true; }, { timeoutMs: 120000, intervalMs: 3000 });
  // Both Orchestrators must be in rotation: haproxy marks a node up after its health checks pass.
  await until('both Orchestrators to register', () => {
    const live = Number(topology.psql(`SELECT COUNT(*) FROM nodes WHERE last_heartbeat > '${new Date(Date.now() - 60000).toISOString()}'`) || 0);
    return live >= 2;
  }, { timeoutMs: 180000, intervalMs: 3000 }).catch(() => { /* node registry is optional; the VIP health check above is the gate */ });
}

// ── Job scripts ──────────────────────────────────────────────────────────────
// Every script checks its own result and THROWs on a mismatch, so a run recorded SUCCESS has
// produced the right answer. SET PERSIST OFF puts the spill under the swept temp root.
// GENERATE_SERIES materializes at most a million values, so rows come from a streamed cross join
// that yields 1..rows exactly (rows is a multiple of 1000).

const header = 'SET PERSIST OFF;\n';

export function ids(rows) {
  if (rows % 1000 !== 0 || rows / 1000 > 1_000_000) throw new Error(`Row counts are multiples of 1000 up to 1e9: ${rows}`);
  return `(SELECT (a.value - 1) * 1000 + b.value AS value FROM GENERATE_SERIES(1, ${rows / 1000}) a CROSS JOIN GENERATE_SERIES(1, 1000) b) s`;
}

function sumOf(n) { return BigInt(n) * BigInt(n + 1) / 2n; }

export const scripts = {
  scan: rows => `${header}DECLARE @n BIGINT = (SELECT COUNT(*) FROM ${ids(rows)} WHERE value % 7 = 3);
IF @n <> ${Math.floor((rows - 3) / 7) + 1} THROW 50001, 'scan count mismatch', 1;`,
  tempSpill: rows => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SELECT value AS id, value % 1000 AS grp INTO #t FROM ${ids(rows)};
DECLARE @s BIGINT = (SELECT SUM(id) FROM #t);
IF @s <> ${sumOf(rows)} THROW 50002, 'temp spill checksum mismatch', 1;`,
  externalSort: rows => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SET EXTERNAL_SORT_CHUNK_SIZE = 20000;
SET WINDOW_SPILL_THRESHOLD = 10000;
SELECT value AS id INTO #s FROM ${ids(rows)};
SELECT id, ROW_NUMBER() OVER (ORDER BY id DESC) AS rn INTO #ranked FROM #s;
DECLARE @first BIGINT = (SELECT id FROM #ranked WHERE rn = 1);
DECLARE @last BIGINT = (SELECT rn FROM #ranked WHERE id = 1);
IF @first <> ${rows} OR @last <> ${rows} THROW 50003, 'external sort order mismatch', 1;`,
  externalJoin: rows => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SET JOIN_SPILL_THRESHOLD = 10000;
SELECT value AS id, value * 2 AS v INTO #l FROM ${ids(rows)};
SELECT value AS id INTO #r FROM ${ids(rows)};
DECLARE @j BIGINT = (SELECT COUNT(*) FROM #l l JOIN #r r ON l.id = r.id);
IF @j <> ${rows} THROW 50004, 'external join count mismatch', 1;`,
  aggregate: rows => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SELECT value AS id, value % 200000 AS k INTO #a FROM ${ids(rows)};
DECLARE @g BIGINT = (SELECT COUNT(*) FROM (SELECT k, COUNT(*) AS c FROM #a GROUP BY k) x);
IF @g <> ${Math.min(rows, 200000)} THROW 50005, 'aggregate group count mismatch', 1;`,
  // Spills, then reads back without writing: the read phase is a pure scan of the spilled table.
  spillThenRead: (rows, pauseSeconds = 0) => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SELECT value AS id, CONCAT('row-', value) AS pad INTO #t FROM ${ids(rows)};
${pauseSeconds ? `WAITFOR DELAY '00:00:${String(pauseSeconds).padStart(2, '0')}';
` : ''}DECLARE @s BIGINT = (SELECT SUM(id) FROM #t);
IF @s <> ${sumOf(rows)} THROW 50006, 'spill read checksum mismatch', 1;`,
  slowDisk: rows => `${header}SET TEMP_TABLE_SPILL_THRESHOLD = 10000;
SET SPILL_COMPRESSION OFF;
SELECT value AS id, CONCAT('row-', value) AS pad INTO #t FROM ${ids(rows)};
DECLARE @s BIGINT = (SELECT SUM(id) FROM #t);
IF @s <> ${sumOf(rows)} THROW 50007, 'slow disk checksum mismatch', 1;
DECLARE @cost FLOAT = (SELECT MAX(spill_write_ms_per_mb) FROM eng.profile);
DECLARE @pressure INT = (SELECT MAX(storage_pressure_events) FROM eng.profile);
PRINT CONCAT('spill_write_ms_per_mb=', @cost, ' storage_pressure_events=', @pressure);
IF @pressure < 1 THROW 50008, 'slow spill storage was not reported as storage pressure', 1;`,
  wait: seconds => `WAITFOR DELAY '00:00:${String(seconds).padStart(2, '0')}';
SELECT 1 AS done INTO #done;`,
};

// ── Report helpers ───────────────────────────────────────────────────────────

function check(evidence, name, passed, detail) {
  evidence.checks.push({ name, passed: Boolean(passed), detail });
  return Boolean(passed);
}

function finish(evidence) {
  evidence.passed = evidence.checks.length > 0 && evidence.checks.every(item => item.passed) && !evidence.error;
  evidence.status = evidence.passed ? 'Passed' : 'Failed';
  return evidence;
}

async function scenario(id, body, topology) {
  const evidence = { id, startedAt: new Date().toISOString(), checks: [], measurements: {} };
  const started = Date.now();
  try {
    await body(evidence);
  } catch (error) {
    evidence.error = error.message;
  } finally {
    const stopped = await topology?.stopScenarioJobs();
    if (stopped?.length) evidence.measurements.jobsStoppedAfterScenario = stopped;
  }
  evidence.elapsedMs = Date.now() - started;
  evidence.completedAt = new Date().toISOString();
  return finish(evidence);
}

// The spill volume holds only what the certified jobs write; after every scenario it must be empty.
async function assertSpillClean(topology, evidence, label = 'spill volume is empty after cleanup') {
  const files = await until('spill files to be removed', () => {
    const remaining = topology.spillFiles();
    return remaining.length === 0 ? remaining : null;
  }, { timeoutMs: 60000, intervalMs: 2000 }).catch(() => topology.spillFiles());
  evidence.measurements.remainingSpillFiles = files.length;
  return check(evidence, label, files.length === 0, files.slice(0, 5).map(file => path.relative(topology.spillMount, file)));
}

// ── Gate 1: sustained load ───────────────────────────────────────────────────

async function sustained(topology, runId, logs) {
  const workload = path.join(topology.runRoot, 'postgres-ha-sustained.workload.local.json');
  cli(['admin', 'ha-soak', 'workload', '--run-root', topology.runRoot, '--force'], path.join(logs, 'ha-soak-workload.log'));
  const config = JSON.parse(readFileSync(workload, 'utf8'));
  // The load balancer address the CLI wrote is localhost; the containers publish on 127.0.0.1.
  writeJson(workload, config);
  const outDir = path.join(root, 'certification-results/postgres-ha-soak', runId);
  mkdirSync(outDir, { recursive: true });
  await runLogged(process.execPath, ['scripts/test-service-capacity.mjs', '--config', workload, '--out-dir', outDir], path.join(logs, 'sustained.log'), { timeoutMs: 3600000 });
  cli(['admin', 'ha-soak', 'metrics', '--run-root', topology.runRoot, '--output', path.join(outDir, 'postgres-ha-metrics.json'), '--force'], path.join(logs, 'ha-soak-metrics.log'));
  rmSync(workload, { force: true }); // holds the generated admin password and API key
  const report = JSON.parse(readFileSync(path.join(outDir, 'capacity-report.json'), 'utf8'));
  return { outDir, report };
}

// ── Gate 2: large jobs ───────────────────────────────────────────────────────

const mixedWorkloads = [
  ['StreamingScanFilterProjection', scripts.scan(60_000_000)],
  ['TempTableSpillRoundTrip', scripts.tempSpill(8_000_000)],
  ['ExternalSort', scripts.externalSort(6_000_000)],
  ['ExternalEquiJoin', scripts.externalJoin(4_000_000)],
  ['HighCardinalityExternalAggregate', scripts.aggregate(6_000_000)],
];

async function mixedLargeJobs(topology, plan) {
  const concurrent = plan.concurrentJobs ?? 3;
  return scenario(plan.scenarioId, async evidence => {
    const results = [];
    // Waves of `concurrent` jobs, each started through the load balancer.
    for (let index = 0; index < mixedWorkloads.length; index += concurrent) {
      const wave = mixedWorkloads.slice(index, index + concurrent);
      const started = await Promise.all(wave.map(async ([name, script]) => {
        const jobName = `ha-large-${name}-${Date.now()}`;
        const requested = Date.now();
        const run = await topology.startJob(jobName, script);
        return { name, jobName, run, requested };
      }));
      for (const job of started) {
        const end = await topology.waitForEnd(job.jobName, job.run.id, 1800000);
        results.push({
          workload: job.name, status: end.status, errorMessage: end.errorMessage.slice(0, 300),
          rowsProcessed: end.rowsProcessed, jobStartDelayMs: Date.parse(job.run.startTime) - job.requested,
          elapsedMs: Date.parse(end.endTime) - Date.parse(end.startTime),
        });
      }
    }
    evidence.measurements.jobs = results;
    evidence.measurements.concurrentJobs = concurrent;
    for (const result of results)
      check(evidence, `${result.workload} succeeded with its checksum`, result.status === 'SUCCESS', result.errorMessage || undefined);
    await assertSpillClean(topology, evidence);
  }, topology);
}

// Waits until the running job reaches `point`, then cancels it through the load balancer.
async function cancelAt(topology, plan) {
  const point = plan.cancellationPoint;
  const scriptFor = {
    scan: scripts.scan(1_000_000_000),
    'spill-write': scripts.tempSpill(80_000_000),
    'spill-read': scripts.spillThenRead(6_000_000),
    repartition: scripts.externalJoin(10_000_000),
  }[point];
  return scenario(plan.scenarioId, async evidence => {
    const jobName = `ha-cancel-${point}-${Date.now()}`;
    const run = await topology.startJob(jobName, scriptFor);
    const owner = await until('the run to hold its lease', () => topology.leaseOwner(jobName), { timeoutMs: 60000 });
    const container = topology.containerForOwner(owner);
    evidence.measurements.leaseOwner = owner;

    let reached;
    if (point === 'scan') {
      // A streamed scan holds only the small cross-join input; its spill does not grow.
      let previous = -1;
      reached = await until('the scan to run', async () => {
        await sleep(5000);
        const bytes = topology.spillBytes();
        const steady = bytes === previous && bytes < 64 * 1024 * 1024;
        previous = bytes;
        return steady ? { spillBytes: bytes } : null;
      }, { timeoutMs: 120000 });
    } else if (point === 'spill-write') {
      let previous = 0;
      reached = await until('spill writes', () => {
        const bytes = topology.spillBytes();
        const growing = bytes > 32 * 1024 * 1024 && bytes > previous;
        previous = bytes;
        return growing ? { spillBytes: bytes } : null;
      }, { timeoutMs: 300000 });
    } else if (point === 'spill-read') {
      // Reading back: the spill has stopped growing while the process keeps reading.
      let previous = -1, readStart = null;
      reached = await until('the spill to be read back', () => {
        const bytes = topology.spillBytes();
        const io = topology.processIo(container);
        if (bytes > 0 && bytes === previous) {
          readStart ??= io.rchar;
          if (io.rchar - readStart > 16 * 1024 * 1024) return { spillBytes: bytes, readBytesSinceWriteStopped: io.rchar - readStart };
        } else {
          readStart = null;
        }
        previous = bytes;
        return null;
      }, { timeoutMs: 600000, intervalMs: 500 });
    } else {
      // Join partitions are named <guid>_<side>_<n>.tmp; temp-table extents are not.
      reached = await until('join partition files', () => {
        const partitions = topology.spillFiles().filter(file => /^[0-9a-f]{32}_.+_\d+\.tmp$/.test(path.basename(file)));
        return partitions.length >= 4 ? { spillBytes: topology.spillBytes(), partitionFiles: partitions.length } : null;
      }, { timeoutMs: 600000, intervalMs: 500 });
    }
    evidence.measurements.atCancel = reached;
    check(evidence, `the run reached ${point} before the cancel`, Boolean(reached));

    const requestedAt = Date.now();
    const response = await topology.kill(jobName);
    evidence.measurements.cancelResponseStatus = response.status;
    evidence.measurements.cancelRequestedAt = new Date(requestedAt).toISOString();
    const end = await topology.waitForEnd(jobName, run.id, 300000);
    evidence.measurements.cancelObservedAt = end.endTime;
    evidence.measurements.cancelLatencyMs = Date.parse(end.endTime) - requestedAt;
    evidence.measurements.finalStatus = end.status;
    check(evidence, 'the cancelled run is not recorded as a success', end.status !== 'SUCCESS', end.status);
    check(evidence, 'the run stopped within 60 s of the cancel', Date.parse(end.endTime) - requestedAt < 60000);
    await assertSpillClean(topology, evidence);
  }, topology);
}

// ── Gate 3: faults ───────────────────────────────────────────────────────────

async function runToEnd(topology, jobName, script, timeoutMs) {
  const run = await topology.startJob(jobName, script);
  return topology.waitForEnd(jobName, run.id, timeoutMs);
}

const faults = {
  async DiskLowSpaceBeforeExtentWrite(topology, evidence) {
    topology.fillUntilFree((SPILL_RESERVE_MB - 64) * 1024 * 1024);
    try {
      evidence.measurements.freeDiskBytesBefore = topology.freeBytes();
      evidence.measurements.admissionRequiredBytes = SPILL_RESERVE_MB * 1024 * 1024;
      const end = await runToEnd(topology, `ha-fault-low-space-${Date.now()}`, scripts.tempSpill(4_000_000), 600000);
      evidence.measurements.jobFinalState = end.status;
      evidence.measurements.exceptionCategory = /low on space/i.test(end.errorMessage) ? 'SpillStorageLowOnSpace' : end.errorMessage.slice(0, 200);
      check(evidence, 'the run failed', end.status === 'FAILURE', end.status);
      check(evidence, 'it failed on the free-space reserve before writing an extent', /Spill storage is low on space/.test(end.errorMessage), end.errorMessage.slice(0, 300));
      check(evidence, 'the error does not name the spill path', !end.errorMessage.includes('/spill/ETL-SQL-Spill/'));
    } finally {
      topology.removeBallast();
    }
    await assertSpillClean(topology, evidence);
  },

  async DiskFullDuringExtentWrite(topology, evidence) {
    const jobName = `ha-fault-disk-full-${Date.now()}`;
    const run = await topology.startJob(jobName, scripts.tempSpill(80_000_000));
    try {
      // Another writer takes the rest of the volume while an extent is being written.
      const bytes = await until('the job to be writing extents', () => {
        const spilled = topology.spillBytes();
        return spilled > 64 * 1024 * 1024 ? spilled : null;
      }, { timeoutMs: 300000, intervalMs: 500 });
      evidence.measurements.bytesWrittenBeforeFault = bytes;
      topology.fillUntilFree(0);
      const end = await topology.waitForEnd(jobName, run.id, 300000);
      evidence.measurements.jobFinalState = end.status;
      evidence.measurements.error = end.errorMessage.slice(0, 300);
      check(evidence, 'the run failed', end.status === 'FAILURE', end.status);
      check(evidence, 'it failed on the full volume', /no space|disk full|low on space/i.test(end.errorMessage), end.errorMessage.slice(0, 300));
    } finally {
      topology.removeBallast();
    }
    const clean = await assertSpillClean(topology, evidence, 'the partial extent and the run root were removed');
    evidence.measurements.partialExtentRemoved = clean;
  },

  async SlowDiskWriteAndRead(topology, evidence) {
    const limit = 2 * 1024 * 1024;
    const containers = topology.containers('orchestrator');
    const cgroups = containers.map(container => {
      const id = sh('docker', ['inspect', '-f', '{{.Id}}', container]).out;
      const candidates = [`/sys/fs/cgroup/system.slice/docker-${id}.scope`, `/sys/fs/cgroup/docker/${id}`];
      const found = candidates.find(existsSync);
      if (!found) throw new Error(`No cgroup found for container ${container}.`);
      return found;
    });
    evidence.measurements.configuredBytesPerSecond = limit;
    sh('mount', ['-o', 'remount,sync', topology.spillMount], { sudo: true });
    try {
      for (const cgroup of cgroups)
        sh('tee', [path.join(cgroup, 'io.max')], { sudo: true, input: `${topology.spillDevice} wbps=${limit} rbps=${limit}\n` });
      const end = await runToEnd(topology, `ha-fault-slow-disk-${Date.now()}`, scripts.slowDisk(1_000_000), 1800000);
      evidence.measurements.jobFinalState = end.status;
      evidence.measurements.elapsedMs = Date.parse(end.endTime) - Date.parse(end.startTime);
      evidence.measurements.rowsProcessed = end.rowsProcessed;
      check(evidence, 'the run completed without a timeout and reported storage pressure', end.status === 'SUCCESS', end.errorMessage.slice(0, 300) || end.status);
    } finally {
      for (const cgroup of cgroups)
        sh('tee', [path.join(cgroup, 'io.max')], { sudo: true, input: `${topology.spillDevice} wbps=max rbps=max\n`, allowFail: true });
      sh('mount', ['-o', 'remount,async', topology.spillMount], { sudo: true });
    }
    await assertSpillClean(topology, evidence);
  },

  async CorruptExtentBeforeRead(topology, evidence) {
    const jobName = `ha-fault-corrupt-${Date.now()}`;
    const run = await topology.startJob(jobName, scripts.spillThenRead(3_000_000, 30));
    // The script pauses between writing and reading its spill; corrupt one extent in that window.
    let previous = -1;
    await until('the spill to be written', () => {
      const bytes = topology.spillBytes();
      const stable = bytes > 0 && bytes === previous;
      previous = bytes;
      return stable;
    }, { timeoutMs: 300000, intervalMs: 3000 });
    const extents = topology.spillFiles().filter(file => !file.endsWith('.owner'));
    const target = extents.sort((a, b) => Number(sh('stat', ['-c', '%s', b], { sudo: true }).out) - Number(sh('stat', ['-c', '%s', a], { sudo: true }).out))[0];
    if (!target) throw new Error('No spill extent was written.');
    const checksum = () => sh('sha256sum', [target], { sudo: true }).out.split(' ')[0];
    evidence.measurements.corruptedExtentPath = path.relative(topology.spillMount, target);
    evidence.measurements.checksumBefore = checksum();
    const size = Number(sh('stat', ['-c', '%s', target], { sudo: true }).out);
    sh('dd', ['if=/dev/urandom', `of=${target}`, 'bs=4096', 'count=4', `seek=${Math.floor(size / 2 / 4096)}`, 'conv=notrunc', 'status=none'], { sudo: true });
    evidence.measurements.checksumAfter = checksum();
    check(evidence, 'the extent was changed', evidence.measurements.checksumBefore !== evidence.measurements.checksumAfter);
    const end = await topology.waitForEnd(jobName, run.id, 600000);
    evidence.measurements.jobFinalState = end.status;
    evidence.measurements.exceptionCategory = end.errorMessage.slice(0, 200);
    evidence.measurements.resultMarkedFailed = end.status === 'FAILURE';
    check(evidence, 'the run failed instead of returning damaged rows', end.status === 'FAILURE', end.status);
    check(evidence, 'the failure does not name the spill path', !end.errorMessage.includes('/spill/ETL-SQL-Spill/'));
    await assertSpillClean(topology, evidence);
  },

  async IncompleteExtentAfterCrash(topology, evidence) {
    const jobName = `ha-fault-crash-extent-${Date.now()}`;
    await topology.startJob(jobName, scripts.tempSpill(80_000_000));
    const owner = await until('the run to hold its lease', () => topology.leaseOwner(jobName), { timeoutMs: 60000 });
    const container = topology.containerForOwner(owner);
    await until('extents on disk', () => topology.spillBytes() > 64 * 1024 * 1024, { timeoutMs: 300000, intervalMs: 500 });
    evidence.measurements.killedProcessId = topology.hostPid(container);
    sh('docker', ['kill', '--signal', 'KILL', container]);
    evidence.measurements.orphanExtentCountBeforeRestart = topology.spillFiles().filter(file => !file.endsWith('.owner')).length;
    check(evidence, 'the crash left extents behind', evidence.measurements.orphanExtentCountBeforeRestart > 0);
    const restarted = Date.now();
    if (!topology.running(container)) sh('docker', ['start', container]);
    // The restarted Orchestrator sweeps roots whose owner process is gone.
    const swept = await until('the restarted node to sweep the orphaned root', () => topology.spillFiles().length === 0, { timeoutMs: 180000, intervalMs: 1000 }).catch(() => false);
    evidence.measurements.restartCleanupDurationMs = Date.now() - restarted;
    evidence.measurements.orphanExtentCountAfterCleanup = topology.spillFiles().length;
    evidence.measurements.sessionRecoveryState = 'disposable temp root swept on restart';
    check(evidence, 'the restarted node removed the orphaned root', swept);
    await waitForTopology(topology);
    const runs = await topology.history(jobName);
    evidence.measurements.runStatuses = runs.map(run => run.status);
    check(evidence, 'no run of the crashed job is recorded as a success', runs.every(run => run.status !== 'SUCCESS'));
  },

  async WorkerProcessCrashMidJob(topology, evidence) {
    // Due every minute, so after the crash the survivor runs the job once the lease expires.
    const jobName = `ha-fault-worker-crash-${Date.now()}`;
    await topology.orchestrator('POST', '/api/scheduled-jobs', { name: jobName, scriptText: scripts.wait(40), interval: 1, unit: 'MINUTE', maxRetries: 0 });
    const first = await topology.waitForRun(jobName);
    const owner = await until('the run to hold its lease', () => topology.leaseOwner(jobName), { timeoutMs: 60000 });
    const container = topology.containerForOwner(owner);
    evidence.measurements.leaseOwnerBeforeCrash = owner;
    await sleep(5000);
    sh('docker', ['kill', '--signal', 'KILL', container]);
    const crashedAt = Date.now();
    let recovered;
    try {
      recovered = await until('another node to run the job', async () => {
        const current = topology.leaseOwner(jobName);
        const runs = await topology.history(jobName);
        const success = runs.find(run => run.id !== first.id && run.status === 'SUCCESS');
        return current && current !== owner && success ? { current, success } : null;
      }, { timeoutMs: 300000, intervalMs: 3000 });
      evidence.measurements.leaseOwnerAfterRecovery = recovered.current;
      evidence.measurements.recoveryMs = Date.parse(recovered.success.startTime) - crashedAt;
    } finally {
      // Whatever failed above, the next fault needs both Orchestrators back.
      if (!topology.running(container)) sh('docker', ['start', container]);
    }
    // Read the history before deleting the job: a deleted job has none (404).
    const runs = await topology.history(jobName, 50);
    await topology.deleteJob(jobName);
    const crashed = runs.find(run => run.id === first.id);
    evidence.measurements.jobFinalState = crashed?.status;
    const successes = runs.filter(run => run.status === 'SUCCESS')
      .map(run => [Date.parse(run.startTime), Date.parse(run.endTime)]).sort((a, b) => a[0] - b[0]);
    const overlaps = successes.filter((span, index) => index > 0 && span[0] < successes[index - 1][1]).length;
    evidence.measurements.duplicateMutationCount = overlaps;
    check(evidence, 'the crashed run is not recorded as a success', crashed?.status !== 'SUCCESS', crashed?.status);
    check(evidence, 'a different node took the lease', recovered.current.split(':')[0] !== owner.split(':')[0]);
    check(evidence, 'no two successful runs overlapped', overlaps === 0);
    await waitForTopology(topology);
  },

  async PortalNodeLossWithActiveSession(topology, evidence) {
    // The login response pins this client to one Portal node through the affinity cookie.
    const login = await fetch(new URL('/api/auth/login', topology.portalUrl), {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: topology.env.PORTAL_ADMIN_USERNAME ?? 'admin', password: topology.env.PORTAL_ADMIN_PASSWORD }),
    });
    const cookie = (login.headers.get('set-cookie') ?? '').split(/,(?=\s*\w+=)/).map(part => part.split(';')[0].trim())
      .find(part => part.startsWith('ETLSQL_PORTAL_AFFINITY='));
    evidence.measurements.affinityCookieName = 'ETLSQL_PORTAL_AFFINITY';
    check(evidence, 'the Portal issued an affinity cookie', Boolean(cookie));
    // The cookie value is the Portal node id, `<hostname>-<pid>-<guid>`, so it names the container.
    // (Pausing the other nodes to find it does not work: a paused node fails haproxy's health check
    // and the stick table re-learns the cookie from whichever node answers instead.)
    const nodeId = cookie ? decodeURIComponent(cookie.slice('ETLSQL_PORTAL_AFFINITY='.length)) : '';
    const lost = topology.containers('portal').find(container => nodeId.startsWith(`${topology.hostname(container)}-`)) ?? null;
    evidence.measurements.affinityNodeId = nodeId;
    evidence.measurements.lostNodeId = lost ? topology.hostname(lost) : null;
    check(evidence, 'the sticky node was identified', Boolean(lost));
    if (!lost) return;
    sh('docker', ['stop', '--time', '5', lost]);
    const stoppedAt = Date.now();
    try {
      await sleep(15000); // haproxy marks the node down after its failed health checks
      const stateless = [];
      for (let index = 0; index < 20; index++) stateless.push((await topology.portal('GET', '/api/folders')).status);
      const health = await topology.portal('GET', '/healthz', { cookie });
      const interactive = await topology.portal('GET', '/api/folders', { cookie });
      evidence.measurements.healthzStatusAfterStop = health.status;
      evidence.measurements.statelessRequestSuccess = `${stateless.filter(status => status === 200).length}/${stateless.length}`;
      evidence.measurements.interactiveSessionOutcome = interactive.status === 200
        ? 'served by a surviving node; node-local session state was not carried over'
        : `request with the lost node's cookie returned ${interactive.status}`;
      evidence.measurements.failoverObservedAfterMs = Date.now() - stoppedAt;
      check(evidence, 'stateless requests succeed through the surviving nodes', stateless.every(status => status === 200), stateless);
      check(evidence, 'a client pinned to the lost node is served by a survivor', health.status === 200 && interactive.status === 200);
    } finally {
      sh('docker', ['start', lost]);
    }
    await waitForTopology(topology);
  },

  async OrchestratorLeaderLossDuringSchedule(topology, evidence) {
    // A dense schedule: due every minute and running for 20 s.
    const jobName = `ha-fault-leader-${Date.now()}`;
    await topology.orchestrator('POST', '/api/scheduled-jobs', { name: jobName, scriptText: scripts.wait(20), interval: 1, unit: 'MINUTE', maxRetries: 0 });
    const first = await topology.waitForRun(jobName);
    const owner = await until('the run to hold its lease', () => topology.leaseOwner(jobName), { timeoutMs: 60000 });
    const leader = topology.containerForOwner(owner);
    evidence.measurements.leaderBeforeStop = owner;
    sh('docker', ['stop', '--time', '10', leader]);
    const stoppedAt = Date.now();
    try {
      const next = await until('the survivor to run the next occurrence', async () => {
        const runs = await topology.history(jobName);
        const current = topology.leaseOwner(jobName);
        const run = runs.find(entry => entry.id !== first.id && current && current.split(':')[0] !== owner.split(':')[0]);
        return run ? { run, current } : null;
      }, { timeoutMs: 240000, intervalMs: 2000 });
      evidence.measurements.leaderAfterFailover = next.current;
      evidence.measurements.failoverDurationMs = Date.parse(next.run.startTime) - stoppedAt;
      await sleep(70000); // one more occurrence on the survivor
      const history = await topology.history(jobName, 50); // before the delete, which drops it
      await topology.deleteJob(jobName);
      const runs = history.filter(run => run.startTime)
        .map(run => [Date.parse(run.startTime), run.endTime ? Date.parse(run.endTime) : Date.now()]).sort((a, b) => a[0] - b[0]);
      const duplicates = runs.filter((span, index) => index > 0 && span[0] < runs[index - 1][1]).length;
      evidence.measurements.scheduledFireCount = runs.length;
      evidence.measurements.duplicateFireCount = duplicates;
      check(evidence, 'the survivor took over the schedule', Boolean(next));
      check(evidence, 'no occurrence fired twice', duplicates === 0);
    } finally {
      sh('docker', ['start', leader]);
    }
    await waitForTopology(topology);
  },

  async PostgresOutageBrief(topology, evidence) {
    const jobsBefore = (await topology.orchestrator('GET', '/api/scheduled-jobs')).json.length;
    const postgres = topology.containers('postgres')[0];
    sh('docker', ['stop', postgres]);
    const stoppedAt = Date.now();
    const portalDuring = [], orchestratorDuring = [];
    try {
      while (Date.now() - stoppedAt < 20000) {
        portalDuring.push((await topology.portal('GET', '/api/folders')).status);
        orchestratorDuring.push(await fetch(new URL('/health/ready', topology.orchestratorUrl), { signal: AbortSignal.timeout(10000) }).then(r => r.status).catch(() => 0));
        await sleep(2000);
      }
    } finally {
      sh('docker', ['start', postgres]);
    }
    evidence.measurements.outageDurationMs = Date.now() - stoppedAt;
    evidence.measurements.portalHealthDuringOutage = portalDuring;
    evidence.measurements.orchestratorHealthDuringOutage = orchestratorDuring;
    check(evidence, 'the Portal did not report success while its database was down', portalDuring.every(status => status !== 200), portalDuring);
    check(evidence, 'Orchestrator health failed closed during the outage', orchestratorDuring.some(status => status !== 200), orchestratorDuring);
    await waitForTopology(topology);
    const restarts = topology.containers('orchestrator').map(container => Number(sh('docker', ['inspect', '-f', '{{.RestartCount}}', container]).out));
    evidence.measurements.orchestratorRestartsDuringOutage = restarts;
    const logs = topology.compose(['logs', '--since', `${Math.ceil((Date.now() - stoppedAt) / 1000) + 5}s`, 'orchestrator'], { allowFail: true }).out;
    evidence.measurements.jobBackoffObserved = /transient|retry|backing off|failed to|exception/i.test(logs);
    const jobsAfter = (await topology.orchestrator('GET', '/api/scheduled-jobs')).json.length;
    const end = await runToEnd(topology, `ha-fault-after-outage-${Date.now()}`, scripts.wait(2), 300000);
    evidence.measurements.stateConsistencyCheck = { jobsBefore, jobsAfter, runAfterRecovery: end.status };
    check(evidence, 'no job definition was lost', jobsAfter >= jobsBefore);
    check(evidence, 'jobs run again after recovery', end.status === 'SUCCESS', end.errorMessage || end.status);
  },

  async TempRootExhaustion(topology, evidence) {
    // Room for a few extents, not the whole spill: the reserve stops the job before the disk fills.
    // 64 MB above the reserve; the compressed spill passes 64 MB early (see DiskFullDuringExtentWrite),
    // while 1 GB held the whole run and it succeeded.
    topology.fillUntilFree((SPILL_RESERVE_MB + 64) * 1024 * 1024);
    try {
      evidence.measurements.configuredTempRootLimitBytes = topology.freeBytes() - SPILL_RESERVE_MB * 1024 * 1024;
      evidence.measurements.expectedSpillBytes = 80_000_000 * 40;
      const end = await runToEnd(topology, `ha-fault-temp-root-${Date.now()}`, scripts.tempSpill(80_000_000), 900000);
      evidence.measurements.jobFinalState = end.status;
      evidence.measurements.admissionFailureObserved = /Spill storage is low on space/.test(end.errorMessage);
      evidence.measurements.minimumFreeBytesDuringRun = topology.freeBytes();
      check(evidence, 'the run failed', end.status === 'FAILURE', end.status);
      check(evidence, 'the reserve stopped it before the volume filled', evidence.measurements.admissionFailureObserved, end.errorMessage.slice(0, 300));
    } finally {
      topology.removeBallast();
    }
    evidence.measurements.runRootDeletedAfterCleanup = await assertSpillClean(topology, evidence);
  },
};

// ── Evidence files ───────────────────────────────────────────────────────────

function writeReport(dir, file, report, title, items, idKey) {
  writeJson(path.join(dir, `${file}.json`), report);
  const lines = [`# ${title}`, '', `Run: \`${report.runId}\`  Runner: \`${report.runnerKind}\`  Status: **${report.status}**`, '',
    '| Item | Result | Seconds | Failed checks |', '| :--- | :---: | ---: | :--- |'];
  for (const item of items)
    lines.push(`| ${item[idKey]} | ${item.status} | ${Math.round(item.elapsedMs / 1000)} | ${[...item.checks.filter(c => !c.passed).map(c => c.name), item.error].filter(Boolean).join('; ').replace(/\|/g, '/')} |`);
  writeFileSync(path.join(dir, `${file}.md`), lines.join('\n') + '\n');
  for (const item of items) writeJson(path.join(dir, item[idKey], 'result.json'), item);
}

function reportShell(runId, mode, kind) {
  return {
    schemaVersion: 1, phase: 'High availability soak', runId, mode, runnerKind: RUNNER_KIND,
    certificationLevel: 'HostedTopologyEvidence', commit: sh('git', ['rev-parse', 'HEAD']).out, kind, nonSecret: true,
  };
}

// ── Main ─────────────────────────────────────────────────────────────────────

export async function certify({ runId, imageTag = 'ha-cert', skipBuild = false, phases = ['sustained', 'large-job', 'faults'], keep = false }) {
  requireLinuxHost();
  const runsRoot = path.join(root, '.ha-soak-runs');
  const runRoot = path.join(runsRoot, runId);
  if (existsSync(runRoot)) throw new Error(`Run ${runId} already exists; use a new run id.`);
  const logs = path.join(root, 'certification-results/ha-soak-logs', runId);
  mkdirSync(logs, { recursive: true });
  const summary = { runId, startedAt: new Date().toISOString(), phases: {} };

  if (!skipBuild) await buildImages(imageTag, logs);
  cli(['admin', 'ha-soak', 'prepare', '--run-id', runId, '--image-tag', imageTag, '--portal-scale', '2', '--orchestrator-scale', '2', '--output-root', runsRoot], path.join(logs, 'prepare.log'));
  const topology = new Topology(runRoot);
  try {
    mountSpillVolume(topology);
    writeOverride(topology);
    topology.compose(['up', '-d', '--scale', 'portal=2', '--scale', 'orchestrator=2'], { timeoutMs: 900000 });
    await waitForTopology(topology);

    if (phases.includes('sustained')) {
      const started = Date.now();
      const { report } = await sustained(topology, runId, logs);
      summary.phases.sustained = { minutes: Math.round((Date.now() - started) / 60000), steps: [...report.portal, ...report.orchestrator].length };
    }

    if (phases.includes('large-job')) {
      cli(['admin', 'ha-soak', 'large-job-plan', '--run-root', runRoot, '--mode', 'CiSmoke', '--force'], path.join(logs, 'large-job-plan.log'));
      const plan = JSON.parse(readFileSync(path.join(runRoot, 'ha-large-job-soak-plan.json'), 'utf8'));
      const dir = path.join(root, 'certification-results/ha-large-job-soak', runId);
      mkdirSync(dir, { recursive: true });
      for (const name of ['ha-large-job-soak-plan.json', 'ha-large-job-soak-plan.md'])
        writeFileSync(path.join(dir, name), readFileSync(path.join(runRoot, name)));
      const started = Date.now();
      const results = [];
      for (const item of plan.scenarios) {
        const result = item.cancellationPoint ? await cancelAt(topology, item) : await mixedLargeJobs(topology, item);
        results.push({ scenarioId: item.scenarioId, ...result });
        console.log(`large-job ${item.scenarioId}: ${result.status}`);
      }
      const passed = results.every(result => result.passed);
      writeReport(dir, 'soak-report', { ...reportShell(runId, plan.mode, 'large-job'), status: passed ? 'Passed' : 'Failed', passed, scenarios: results },
        'HA large-job soak', results, 'scenarioId');
      summary.phases.largeJob = { minutes: Math.round((Date.now() - started) / 60000), passed };
    }

    if (phases.includes('faults')) {
      cli(['admin', 'ha-soak', 'fault-plan', '--run-root', runRoot, '--mode', 'CiSmoke', '--force'], path.join(logs, 'fault-plan.log'));
      const plan = JSON.parse(readFileSync(path.join(runRoot, 'ha-fault-injection-plan.json'), 'utf8'));
      const dir = path.join(root, 'certification-results/ha-fault-injection', runId);
      mkdirSync(dir, { recursive: true });
      for (const name of ['ha-fault-injection-plan.json', 'ha-fault-injection-plan.md'])
        writeFileSync(path.join(dir, name), readFileSync(path.join(runRoot, name)));
      const started = Date.now();
      const results = [];
      for (const fault of plan.faults) {
        const inject = faults[fault.faultId];
        const result = inject
          ? await scenario(fault.faultId, evidence => inject(topology, evidence), topology)
          : finish({ id: fault.faultId, checks: [], error: 'No injection is implemented for this fault.', elapsedMs: 0 });
        const entry = { faultId: fault.faultId, category: fault.category, injectionPoint: fault.injectionPoint, ...result };
        results.push(entry);
        writeJson(path.join(dir, fault.faultId, 'result.json'), entry); // kept even if a later step aborts
        console.log(`fault ${fault.faultId}: ${result.status}${result.error ? ` (${result.error})` : ''}`);
        try {
          await waitForTopology(topology); // the next fault starts from a healthy topology
        } catch (error) {
          for (const skipped of plan.faults.slice(plan.faults.indexOf(fault) + 1))
            results.push(finish({ faultId: skipped.faultId, id: skipped.faultId, checks: [], elapsedMs: 0,
              error: `Not run: the topology did not recover after ${fault.faultId} (${error.message}).` }));
          break;
        }
      }
      const passed = results.every(result => result.passed);
      writeReport(dir, 'fault-report', { ...reportShell(runId, plan.mode, 'fault-injection'), status: passed ? 'Passed' : 'Failed', passed, faults: results },
        'HA fault injection', results, 'faultId');
      summary.phases.faults = { minutes: Math.round((Date.now() - started) / 60000), passed };
    }

    // The validator reads the run root, which is removed below; keep its non-secret metadata.
    cli(['admin', 'ha-soak', 'evidence', '--run-root', runRoot, '--force'], path.join(logs, 'evidence.log'));
    mkdirSync(path.join(logs, 'run-root'), { recursive: true });
    for (const name of ['topology-metadata.json', 'ha-soak-evidence-plan.json'])
      writeFileSync(path.join(logs, 'run-root', name), readFileSync(path.join(runRoot, name)));
    if (['sustained', 'large-job', 'faults'].every(phase => phases.includes(phase))) {
      const binary = path.join(root, 'src/ETL-SQL.App/bin/Release/net10.0/ETL-SQL');
      const validation = sh(binary, ['admin', 'ha-soak', 'validate', '--run-root', runRoot, '--required-gate', 'All',
        '--required-commit', sh('git', ['rev-parse', 'HEAD']).out, '--allow-dirty', '--markdown-report', path.join(logs, 'validation.md')], { allowFail: true });
      writeFileSync(path.join(logs, 'validation.log'), `${validation.out}
${validation.err}`);
      summary.validation = validation.code === 0 ? 'Passed' : 'Failed';
    }
  } finally {
    cli(['admin', 'ha-soak', 'diagnostics', '--run-root', runRoot, '--output-root', path.join(logs, 'diagnostics'), '--force'], path.join(logs, 'diagnostics.log'));
    if (!keep) {
      topology.compose(['down', '-v', '--remove-orphans'], { allowFail: true, timeoutMs: 300000 });
      unmountSpillVolume(topology);
      sh('rm', ['-rf', runRoot], { sudo: true, allowFail: true });
    }
  }

  summary.completedAt = new Date().toISOString();
  summary.passed = summary.validation === 'Passed'
    || (!summary.validation && Object.values(summary.phases).every(phase => phase.passed !== false));
  writeJson(path.join(logs, 'summary.json'), summary);
  return summary;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const option = name => { const index = args.indexOf(`--${name}`); return index >= 0 ? args[index + 1] : undefined; };
  const runId = option('run-id');
  if (!runId) {
    console.error('Usage: node scripts/run-ha-soak-certification.mjs --run-id <id> [--image-tag <tag>] [--skip-build] [--phases sustained,large-job,faults] [--keep]');
    process.exit(2);
  }
  const summary = await certify({
    runId, imageTag: option('image-tag') ?? 'ha-cert', skipBuild: args.includes('--skip-build'),
    phases: (option('phases') ?? 'sustained,large-job,faults').split(','), keep: args.includes('--keep'),
  });
  console.log(JSON.stringify(summary, null, 2));
  process.exit(summary.passed ? 0 : 1);
}
