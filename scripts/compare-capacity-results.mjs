#!/usr/bin/env node

import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const [baselineArg, currentArg, thresholdArg = '15'] = process.argv.slice(2);
if (!baselineArg || !currentArg) {
  console.error('Usage: node scripts/compare-capacity-results.mjs <baseline.json> <current.json> [thresholdPct]');
  process.exit(2);
}

const thresholdPct = Number(thresholdArg);
const baseline = JSON.parse(await fs.readFile(path.resolve(baselineArg), 'utf8'));
const current = JSON.parse(await fs.readFile(path.resolve(currentArg), 'utf8'));
const rows = [];
let failed = false;

for (const service of ['portal', 'orchestrator']) {
  const baselineSteps = new Map((baseline[service] ?? []).map(x => [x.concurrency, x]));
  for (const step of current[service] ?? []) {
    const previous = baselineSteps.get(step.concurrency);
    if (!previous) continue;
    const latencyChange = percentChange(previous.latencyMs.p95, step.latencyMs.p95);
    const throughputChange = percentChange(previous.requestsPerMinute, step.requestsPerMinute);
    const regressed = latencyChange > thresholdPct || throughputChange < -thresholdPct || step.errorRatePct > previous.errorRatePct;
    failed ||= regressed;
    rows.push({ service, concurrency: step.concurrency, latencyChange, throughputChange, errorRate: step.errorRatePct, status: regressed ? 'REGRESSED' : 'OK' });
  }
}

console.table(rows);
process.exit(failed ? 1 : 0);

function percentChange(before, after) {
  if (!before) return after ? 100 : 0;
  return Math.round(((after - before) / before) * 10000) / 100;
}
