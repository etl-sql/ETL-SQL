#!/usr/bin/env node

import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';

const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'etl-sql-capacity-smoke-'));
const apiKey = randomBytes(32).toString('base64');
const assertions = new Set();
const observedAssertions = new Set();
let exchangeCount = 0;
let denyExchange = false;
let expireExchange = false;
let foreignRequests = 0;
const foreignServer = http.createServer((_request, response) => {
  foreignRequests++;
  response.end('{}');
});
const server = http.createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.url === '/api/auth/login') return response.end(JSON.stringify({ token: 'smoke-token' }));
  if (request.url === '/api/auth/orchestrator-assertion') {
    if (denyExchange || request.headers.authorization !== 'Bearer smoke-token') {
      response.statusCode = 403;
      return response.end(JSON.stringify({ error: 'Assertion exchange refused.' }));
    }
    const assertion = randomBytes(32).toString('base64url');
    assertions.add(assertion);
    exchangeCount++;
    return response.end(JSON.stringify({
      assertion,
      headerName: 'X-Orchestrator-Identity',
      audience: 'etl-sql-orchestrator-api',
      // The Portal step takes over a second, forcing a refresh before the Orchestrator step.
      expiresAt: new Date(Date.now() + (expireExchange ? -1000 : exchangeCount === 1 ? 15500 : 120000)).toISOString(),
    }));
  }
  if (request.url === '/api/capacity-redirect') {
    response.writeHead(302, { Location: `http://127.0.0.1:${foreignPort}/probe` });
    return response.end('{}');
  }
  if (request.url === '/metrics') return response.end(JSON.stringify({ active_jobs: 1, queued_jobs: 0, max_jobs: 4 }));
  if (request.url === '/api/capacity-job') {
    const assertion = request.headers['x-orchestrator-identity'] ?? '';
    if (request.headers['x-orchestrator-key'] !== apiKey || !assertions.has(assertion)) {
      response.statusCode = 401;
      return response.end(JSON.stringify({ error: 'Invalid capacity identity.' }));
    }
    observedAssertions.add(assertion);
  }
  response.end(JSON.stringify({ ok: true }));
});

await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
await new Promise(resolve => foreignServer.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const foreignPort = foreignServer.address().port;
const configPath = path.join(tempDir, 'workload.json');
const outDir = path.join(tempDir, 'results');

const workload = {
  environment: { deploymentMode: 'smoke' },
  breachCriteria: { maxErrorRatePct: 1, maxP95LatencyMs: 2000, maxSqliteContentionCount: 0, maxQueuedWork: 10 },
  portal: {
    baseUrl: `http://127.0.0.1:${port}`,
    thinkTimeMs: 50,
    roles: { viewer: { username: 'viewer', password: 'password' } },
    steps: [{ concurrency: 1, durationSeconds: 0.2 }],
    workload: [{ name: 'portal-smoke', method: 'GET', path: '/api/folders', role: 'viewer' }]
  },
  orchestrator: {
    baseUrl: `http://127.0.0.1:${port}`,
    apiKey,
    identityRole: 'viewer',
    metricsPath: '/metrics',
    processId: process.pid,
    steps: [{ concurrency: 1, durationSeconds: 0.2 }],
    workload: [{ name: 'orchestrator-smoke', method: 'GET', path: '/api/capacity-job', service: 'orchestrator', useApiKey: true }]
  },
  setupRequests: [{ name: 'setup', baseUrl: `http://127.0.0.1:${port}`, method: 'POST', path: '/api/capacity-job', useApiKey: true }],
  cleanupRequests: [{ name: 'cleanup', baseUrl: `http://127.0.0.1:${port}`, method: 'DELETE', path: '/api/capacity-job', useApiKey: true }],
};
await fs.writeFile(configPath, JSON.stringify(workload, null, 2));

try {
  await runNode(['scripts/test-service-capacity.mjs', '--config', configPath, '--out-dir', outDir]);
  const report = JSON.parse(await fs.readFile(path.join(outDir, 'capacity-report.json'), 'utf8'));
  if (report.portal.length !== 1 || report.orchestrator.length !== 1) throw new Error('Expected one result step for each service.');
  if (!report.portal[0].passed || !report.orchestrator[0].passed) throw new Error('Expected smoke workload steps to pass.');
  if (report.portal[0].requestCount > 10) throw new Error('Expected Portal think time to pace request volume.');
  if (report.orchestrator[0].serviceMetricMaxima.queued_jobs !== 0) throw new Error('Expected queued_jobs metric sample.');
  if (report.orchestrator[0].processMetricMaxima.processId !== process.pid) throw new Error('Expected OS process metric sample.');
  if (!report.orchestrator[0].processMetricMaxima.workingSetBytes) throw new Error('Expected OS working-set metric sample.');
  if (exchangeCount !== 2 || observedAssertions.size !== 2) throw new Error('Expected the driver to cache and refresh the Portal-issued assertion.');
  const publicReport = await fs.readFile(path.join(outDir, 'capacity-report.json'), 'utf8');
  if (publicReport.includes(apiKey) || [...assertions].some(assertion => publicReport.includes(assertion))) throw new Error('Capacity report exposed private credentials or assertions.');
  denyExchange = true;
  await expectFailure(['scripts/test-service-capacity.mjs', '--config', configPath, '--out-dir', path.join(tempDir, 'denied-results')], 'exchange failed with status 403');
  denyExchange = false;
  expireExchange = true;
  await expectFailure(['scripts/test-service-capacity.mjs', '--config', configPath, '--out-dir', path.join(tempDir, 'expired-results')], 'invalid or expired assertion');
  expireExchange = false;
  const foreign = structuredClone(workload);
  foreign.setupRequests[0].baseUrl = `http://127.0.0.1:${foreignPort}`;
  const foreignConfig = path.join(tempDir, 'foreign-origin.json');
  await fs.writeFile(foreignConfig, JSON.stringify(foreign));
  await expectFailure(['scripts/test-service-capacity.mjs', '--config', foreignConfig, '--out-dir', path.join(tempDir, 'foreign-results')], 'configured Orchestrator origin');
  const redirected = structuredClone(workload);
  redirected.setupRequests[0].path = '/api/capacity-redirect';
  const redirectConfig = path.join(tempDir, 'redirect.json');
  await fs.writeFile(redirectConfig, JSON.stringify(redirected));
  await expectFailure(['scripts/test-service-capacity.mjs', '--config', redirectConfig, '--out-dir', path.join(tempDir, 'redirect-results')], 'failed with status 0');
  if (foreignRequests !== 0) throw new Error('Orchestrator credentials reached a foreign origin.');
  await runNode([
    'scripts/compare-capacity-results.mjs',
    path.join(outDir, 'capacity-report.json'),
    path.join(outDir, 'capacity-report.json'),
    '15'
  ]);
  console.log('Capacity harness smoke test passed.');
} finally {
  server.close();
  foreignServer.close();
  await fs.rm(tempDir, { recursive: true, force: true });
}

function runNode(argumentsList) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, argumentsList, { cwd: path.resolve('.'), stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(`Child process exited with code ${code}.`)));
  });
}

function expectFailure(argumentsList, expectedError) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, argumentsList, { cwd: path.resolve('.'), stdio: ['ignore', 'ignore', 'pipe'] });
    let error = '';
    child.stderr.on('data', chunk => { error += chunk; });
    child.on('error', reject);
    child.on('exit', code => code !== 0 && error.includes(expectedError)
      ? resolve() : reject(new Error('Expected the capacity driver to reject unavailable caller identity.')));
  });
}
