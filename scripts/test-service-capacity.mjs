#!/usr/bin/env node

import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';

const args = parseArgs(process.argv.slice(2));
const configPath = path.resolve(args.config ?? 'capacity-results/workload.example.json');
const validateOnly = Boolean(args['validate-only']);
const config = JSON.parse(await fs.readFile(configPath, 'utf8'));
validateConfig(config);

if (validateOnly) {
  console.log(`Capacity workload configuration is valid: ${configPath}`);
  process.exit(0);
}

const generatedAt = new Date();
const outDir = path.resolve(args['out-dir'] ?? `capacity-results/${stamp(generatedAt)}`);
await fs.mkdir(outDir, { recursive: true });

const context = {
  tokens: {},
  apiKey: config.orchestrator?.apiKey ?? '',
  variables: { ...(config.variables ?? {}) },
};

await authenticateRoles(config.portal, context);
await runLifecycleRequests('setup', config.setupRequests ?? [], context);

let report;
try {
  const portal = config.portal?.steps?.length
    ? await runServiceSteps('portal', config.portal, context)
    : [];
  const orchestrator = config.orchestrator?.steps?.length
    ? await runServiceSteps('orchestrator', config.orchestrator, context)
    : [];

  report = {
    generatedAt: generatedAt.toISOString(),
    configPath,
    environment: collectEnvironment(config),
    breachCriteria: config.breachCriteria,
    portal,
    orchestrator,
  };
} finally {
  await runLifecycleRequests('cleanup', config.cleanupRequests ?? [], context);
}

await fs.writeFile(path.join(outDir, 'capacity-report.json'), JSON.stringify(report, null, 2));
await fs.writeFile(path.join(outDir, 'capacity-report.md'), renderMarkdown(report));
console.log(`Capacity report written to ${outDir}`);

function parseArgs(values) {
  const parsed = {};
  for (let i = 0; i < values.length; i++) {
    const value = values[i];
    if (!value.startsWith('--')) continue;
    const key = value.slice(2);
    if (i + 1 < values.length && !values[i + 1].startsWith('--')) parsed[key] = values[++i];
    else parsed[key] = true;
  }
  return parsed;
}

function validateConfig(value) {
  if (!value || typeof value !== 'object') throw new Error('Configuration must be a JSON object.');
  if (!value.breachCriteria) throw new Error('breachCriteria is required.');
  for (const serviceName of ['portal', 'orchestrator']) {
    const service = value[serviceName];
    if (!service) continue;
    if (!service.baseUrl) throw new Error(`${serviceName}.baseUrl is required.`);
    validateThinkTime(service.thinkTimeMs, `${serviceName}.thinkTimeMs`);
    for (const step of service.steps ?? []) {
      if (!Number.isInteger(step.concurrency) || step.concurrency < 1) {
        throw new Error(`${serviceName}.steps concurrency must be a positive integer.`);
      }
      if (!Number.isFinite(step.durationSeconds) || step.durationSeconds <= 0) {
        throw new Error(`${serviceName}.steps durationSeconds must be positive.`);
      }
    }
    for (const request of service.workload ?? []) validateRequest(request, `${serviceName}.workload`);
  }
  for (const request of [...(value.setupRequests ?? []), ...(value.cleanupRequests ?? [])]) {
    validateRequest(request, 'lifecycle request');
  }
}

function validateRequest(request, owner) {
  if (!request.method || !request.path) throw new Error(`${owner} entries require method and path.`);
  if (request.weight !== undefined && (!Number.isFinite(request.weight) || request.weight <= 0)) {
    throw new Error(`${owner} weight must be positive.`);
  }
  validateThinkTime(request.thinkTimeMs, `${owner} thinkTimeMs`);
}

function validateThinkTime(value, owner) {
  if (value !== undefined && (!Number.isFinite(value) || value < 0)) {
    throw new Error(`${owner} must be a non-negative number.`);
  }
}

async function authenticateRoles(portal, context) {
  if (!portal?.roles) return;
  for (const [role, credentials] of Object.entries(portal.roles)) {
    const response = await sendRequest({
      baseUrl: portal.baseUrl,
      method: 'POST',
      path: '/api/auth/login',
      body: credentials,
      name: `login-${role}`,
    }, context);
    if (!response.ok || !response.json?.token) {
      throw new Error(`Portal login failed for role '${role}' with status ${response.status}.`);
    }
    context.tokens[role] = response.json.token;
  }
}

async function runLifecycleRequests(label, requests, context) {
  for (const request of requests) {
    const result = await sendRequest(request, context);
    if (!result.ok && request.required !== false) {
      throw new Error(`${label} request ${request.method} ${request.path} failed with status ${result.status}.`);
    }
    if (request.capture && result.json) {
      for (const [name, jsonPath] of Object.entries(request.capture)) {
        context.variables[name] = getJsonPath(result.json, jsonPath);
      }
    }
  }
}

async function runServiceSteps(serviceName, service, context) {
  const results = [];
  for (const step of service.steps) {
    console.log(`${serviceName}: concurrency=${step.concurrency}, duration=${step.durationSeconds}s`);
    await warmService(service, context);
    results.push(await runStep(serviceName, service, step, context));
  }
  return results;
}

async function warmService(service, context) {
  const warmupRequests = service.warmupRequests ?? service.workload?.slice(0, 1) ?? [];
  for (let i = 0; i < (service.warmupIterations ?? 1); i++) {
    for (const request of warmupRequests) await sendRequest(request, context);
  }
}

async function runStep(serviceName, service, step, context) {
  const startedAt = new Date();
  const deadline = Date.now() + step.durationSeconds * 1000;
  const observations = [];
  const metricSamples = [];
  const workers = Array.from({ length: step.concurrency }, () => workerLoop(service, context, deadline, observations));
  const sampler = sampleLoop(service, context, deadline, metricSamples);
  await Promise.all([...workers, sampler]);

  const endedAt = new Date();
  const summary = summarizeObservations(observations, step.durationSeconds, metricSamples);
  const breaches = evaluateBreaches(summary, config.breachCriteria);
  return {
    service: serviceName,
    concurrency: step.concurrency,
    durationSeconds: step.durationSeconds,
    startedAt: startedAt.toISOString(),
    endedAt: endedAt.toISOString(),
    ...summary,
    breaches,
    passed: breaches.length === 0,
  };
}

async function workerLoop(service, context, deadline, observations) {
  while (Date.now() < deadline) {
    const request = weightedChoice(service.workload);
    observations.push(await sendRequest(request, context));
    const thinkTimeMs = request.thinkTimeMs ?? service.thinkTimeMs ?? 0;
    if (thinkTimeMs > 0 && Date.now() < deadline) await sleep(thinkTimeMs);
  }
}

async function sampleLoop(service, context, deadline, samples) {
  const intervalMs = service.metricSampleIntervalMs ?? 1000;
  while (Date.now() < deadline) {
    const sample = { timestamp: new Date().toISOString() };
    if (service.metricsPath) {
      const response = await sendRequest({ method: 'GET', path: service.metricsPath, baseUrl: service.baseUrl }, context);
      sample.serviceMetrics = response.json ?? null;
    }
    if (service.processId) sample.process = readProcessSample(service.processId);
    samples.push(sample);
    await sleep(intervalMs);
  }
}

async function sendRequest(request, context) {
  const started = performance.now();
  const baseUrl = substitute(request.baseUrl ?? inferBaseUrl(request), context.variables);
  const url = new URL(substitute(request.path, context.variables), baseUrl);
  const headers = { ...(request.headers ?? {}) };
  if (request.role && context.tokens[request.role]) headers.Authorization = `Bearer ${context.tokens[request.role]}`;
  if (request.useApiKey && context.apiKey) headers['X-Orchestrator-Key'] = context.apiKey;
  if (request.body !== undefined) headers['Content-Type'] = 'application/json';

  try {
    const response = await fetch(url, {
      method: request.method,
      headers,
      body: request.body === undefined ? undefined : JSON.stringify(substituteObject(request.body, context.variables)),
    });
    const text = await response.text();
    return {
      name: request.name ?? `${request.method} ${request.path}`,
      ok: response.ok,
      status: response.status,
      latencyMs: performance.now() - started,
      sqliteContention: /database is locked|database is busy|sqlite_busy|sqlite_locked/i.test(text),
      json: tryParseJson(text),
    };
  } catch (error) {
    return {
      name: request.name ?? `${request.method} ${request.path}`,
      ok: false,
      status: 0,
      latencyMs: performance.now() - started,
      sqliteContention: /database is locked|database is busy|sqlite_busy|sqlite_locked/i.test(String(error)),
      error: String(error),
    };
  }
}

function inferBaseUrl(request) {
  if (request.service === 'orchestrator') return config.orchestrator?.baseUrl;
  return config.portal?.baseUrl;
}

function weightedChoice(workload) {
  const total = workload.reduce((sum, item) => sum + (item.weight ?? 1), 0);
  let value = Math.random() * total;
  for (const item of workload) {
    value -= item.weight ?? 1;
    if (value <= 0) return item;
  }
  return workload[workload.length - 1];
}

function summarizeObservations(observations, durationSeconds, metricSamples) {
  const latencies = observations.map(x => x.latencyMs).sort((a, b) => a - b);
  const failures = observations.filter(x => !x.ok);
  const flattenedMetrics = metricSamples.map(x => flattenNumeric(x.serviceMetrics)).filter(x => Object.keys(x).length);
  return {
    requestCount: observations.length,
    requestsPerMinute: round(observations.length / durationSeconds * 60),
    errorCount: failures.length,
    errorRatePct: round(observations.length ? failures.length / observations.length * 100 : 0),
    sqliteContentionCount: observations.filter(x => x.sqliteContention).length,
    latencyMs: {
      p50: percentile(latencies, 0.50),
      p95: percentile(latencies, 0.95),
      p99: percentile(latencies, 0.99),
      max: latencies.length ? round(latencies[latencies.length - 1]) : 0,
    },
    statusCounts: countBy(observations, x => String(x.status)),
    requestCounts: countBy(observations, x => x.name),
    serviceMetricMaxima: numericMaxima(flattenedMetrics),
    metricSamples,
  };
}

function evaluateBreaches(summary, criteria) {
  const breaches = [];
  if (summary.errorRatePct > criteria.maxErrorRatePct) breaches.push(`error rate ${summary.errorRatePct}% > ${criteria.maxErrorRatePct}%`);
  if (summary.latencyMs.p95 > criteria.maxP95LatencyMs) breaches.push(`p95 ${summary.latencyMs.p95}ms > ${criteria.maxP95LatencyMs}ms`);
  if (summary.sqliteContentionCount > criteria.maxSqliteContentionCount) breaches.push(`SQLite contention ${summary.sqliteContentionCount} > ${criteria.maxSqliteContentionCount}`);
  const queued = summary.serviceMetricMaxima['queued_jobs'] ?? summary.serviceMetricMaxima['metrics.queued_jobs'];
  if (criteria.maxQueuedWork !== undefined && queued !== undefined && queued > criteria.maxQueuedWork) {
    breaches.push(`queued work ${queued} > ${criteria.maxQueuedWork}`);
  }
  return breaches;
}

function collectEnvironment(value) {
  return {
    hostname: os.hostname(),
    platform: `${os.platform()} ${os.release()} ${os.arch()}`,
    cpuModel: os.cpus()[0]?.model ?? 'unknown',
    cpuCount: os.cpus().length,
    totalMemoryBytes: os.totalmem(),
    nodeVersion: process.version,
    dotnetVersion: value.environment?.dotnetVersion ?? null,
    diskType: value.environment?.diskType ?? null,
    deploymentMode: value.environment?.deploymentMode ?? null,
    databaseLocation: value.environment?.databaseLocation ?? null,
    notes: value.environment?.notes ?? null,
  };
}

function readProcessSample(processId) {
  try {
    const stat = process.getActiveResourcesInfo ? process.getActiveResourcesInfo() : [];
    return { processId, runnerActiveResources: stat.length };
  } catch {
    return { processId };
  }
}

function renderMarkdown(report) {
  const lines = [
    '# ETL-SQL Capacity Test Report',
    '',
    `Generated: ${report.generatedAt}`,
    '',
    '## Reference Environment',
    '',
    '| Field | Value |',
    '| :--- | :--- |',
    ...Object.entries(report.environment).map(([key, value]) => `| ${key} | ${value ?? ''} |`),
    '',
  ];
  for (const serviceName of ['portal', 'orchestrator']) {
    lines.push(`## ${titleCase(serviceName)} Results`, '', '| Concurrency | Requests/min | Error % | p50 ms | p95 ms | p99 ms | SQLite contention | Pass |', '| ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |');
    for (const step of report[serviceName]) {
      lines.push(`| ${step.concurrency} | ${step.requestsPerMinute} | ${step.errorRatePct} | ${step.latencyMs.p50} | ${step.latencyMs.p95} | ${step.latencyMs.p99} | ${step.sqliteContentionCount} | ${step.passed ? 'OK' : 'FAIL'} |`);
    }
    lines.push('');
  }
  return `${lines.join('\n').trimEnd()}\n`;
}

function substitute(value, variables) {
  return String(value).replace(/\{\{([^}]+)\}\}/g, (_, name) => {
    if (!(name in variables)) throw new Error(`Missing workload variable '${name}'.`);
    return variables[name];
  });
}

function substituteObject(value, variables) {
  if (typeof value === 'string') return substitute(value, variables);
  if (Array.isArray(value)) return value.map(x => substituteObject(x, variables));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, substituteObject(v, variables)]));
  return value;
}

function getJsonPath(value, jsonPath) {
  return jsonPath.split('.').reduce((current, part) => current?.[part], value);
}

function tryParseJson(text) {
  if (!text) return null;
  try { return JSON.parse(text); } catch { return null; }
}

function percentile(sorted, p) {
  if (!sorted.length) return 0;
  return round(sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * p) - 1)]);
}

function countBy(values, selector) {
  const result = {};
  for (const value of values) {
    const key = selector(value);
    result[key] = (result[key] ?? 0) + 1;
  }
  return result;
}

function flattenNumeric(value, prefix = '', result = {}) {
  if (!value || typeof value !== 'object') return result;
  for (const [key, child] of Object.entries(value)) {
    const name = prefix ? `${prefix}.${key}` : key;
    if (typeof child === 'number' && Number.isFinite(child)) result[name] = child;
    else if (child && typeof child === 'object') flattenNumeric(child, name, result);
  }
  return result;
}

function numericMaxima(values) {
  const result = {};
  for (const value of values) {
    for (const [key, number] of Object.entries(value)) result[key] = Math.max(result[key] ?? Number.NEGATIVE_INFINITY, number);
  }
  return result;
}

function round(value) {
  return Math.round(value * 100) / 100;
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function stamp(date) {
  return date.toISOString().replace(/[-:]/g, '').replace(/\..+/, '').replace('T', '-');
}

function titleCase(value) {
  return value[0].toUpperCase() + value.slice(1);
}
