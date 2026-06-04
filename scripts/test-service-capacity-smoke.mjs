#!/usr/bin/env node

import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';

const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'etl-sql-capacity-smoke-'));
const server = http.createServer((request, response) => {
  response.setHeader('Content-Type', 'application/json');
  if (request.url === '/api/auth/login') return response.end(JSON.stringify({ token: 'smoke-token' }));
  if (request.url === '/metrics') return response.end(JSON.stringify({ active_jobs: 1, queued_jobs: 0, max_jobs: 4 }));
  response.end(JSON.stringify({ ok: true }));
});

await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const configPath = path.join(tempDir, 'workload.json');
const outDir = path.join(tempDir, 'results');

await fs.writeFile(configPath, JSON.stringify({
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
    metricsPath: '/metrics',
    steps: [{ concurrency: 1, durationSeconds: 0.2 }],
    workload: [{ name: 'orchestrator-smoke', method: 'GET', path: '/health', service: 'orchestrator' }]
  }
}, null, 2));

try {
  await runNode(['scripts/test-service-capacity.mjs', '--config', configPath, '--out-dir', outDir]);
  const report = JSON.parse(await fs.readFile(path.join(outDir, 'capacity-report.json'), 'utf8'));
  if (report.portal.length !== 1 || report.orchestrator.length !== 1) throw new Error('Expected one result step for each service.');
  if (!report.portal[0].passed || !report.orchestrator[0].passed) throw new Error('Expected smoke workload steps to pass.');
  if (report.portal[0].requestCount > 10) throw new Error('Expected Portal think time to pace request volume.');
  if (report.orchestrator[0].serviceMetricMaxima.queued_jobs !== 0) throw new Error('Expected queued_jobs metric sample.');
  await runNode([
    'scripts/compare-capacity-results.mjs',
    path.join(outDir, 'capacity-report.json'),
    path.join(outDir, 'capacity-report.json'),
    '15'
  ]);
  console.log('Capacity harness smoke test passed.');
} finally {
  server.close();
  await fs.rm(tempDir, { recursive: true, force: true });
}

function runNode(argumentsList) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, argumentsList, { cwd: path.resolve('.'), stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(`Child process exited with code ${code}.`)));
  });
}
