// Summarizes one billion-row scenario run and gates memory flatness across row counts (release
// reliability W8). Peak memory must not grow with input: each scenario runs at two sizes under the
// same DOTNET_GCHeapHardLimit, and the larger run's peak may exceed the smaller one's only by the
// noise allowance below. Throughput is never gated.
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const scenarios = ['ColumnarCore', 'TempTableRoundTrip', 'AllocProfile', 'ExternalSort', 'ExternalJoin',
  'HighCardinalityGrouping', 'EligibleWindowRowNumber'];
// Owner decision 2026-11-14: the join's live heap grows with build partitions (about 0.6 MB each, up to
// the 1,024-partition cap), bounded well under the cap. It passes by completing under the bound.
export const boundedOnly = new Set(['ExternalJoin']);
// Measured spread at 10M vs 100M rows (runs 43617518622, 43617518644): -11% to +14%.
export const flatness = { maximumRatio: 1.25, allowanceMB: 32 };

function jsonFiles(directory) {
  const found = [];
  for (const name of readdirSync(directory).sort()) {
    const current = path.join(directory, name);
    if (statSync(current).isDirectory()) found.push(...jsonFiles(current));
    else if (name.endsWith('.json')) found.push(current);
  }
  return found;
}

function collect(value, key, into) {
  if (Array.isArray(value)) for (const item of value) collect(item, key, into);
  else if (value && typeof value === 'object') {
    for (const [name, child] of Object.entries(value)) {
      if (name === key && typeof child === 'number' && Number.isFinite(child)) into.push(child);
      else collect(child, key, into);
    }
  }
  return into;
}

// Live heap (bytes surviving a full collection) is the flatness metric. A run with no full
// collection reports none; its managed heap peak then bounds the live heap from above.
export function peakMemory(runDirectory) {
  const live = [], managed = [];
  for (const file of jsonFiles(runDirectory)) {
    let document;
    try { document = JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, '')); } catch { continue; }
    collect(document, 'peakLiveHeapMB', live);
    collect(document, 'peakManagedHeapMB', managed);
  }
  return {
    peakLiveHeapMB: live.length ? Math.max(...live) : null,
    peakManagedHeapMB: managed.length ? Math.max(...managed) : null,
  };
}

export function summarize({ runDirectory, scenario, rows, memoryBoundMB, passed, elapsedMinutes }) {
  if (!scenarios.includes(scenario)) throw new Error(`Unknown billion-row scenario: ${scenario}`);
  if (!Number.isSafeInteger(rows) || rows < 1 || !Number.isFinite(memoryBoundMB) || memoryBoundMB <= 0) throw new Error('Invalid row count or memory bound.');
  return { schemaVersion: 1, scenario, rows, memoryBoundMB, passed: passed === true, elapsedMinutes, ...peakMemory(runDirectory) };
}

function metricFor(small, large) {
  if (small.peakLiveHeapMB !== null && large.peakLiveHeapMB !== null) return 'peakLiveHeapMB';
  if (small.peakManagedHeapMB !== null && large.peakManagedHeapMB !== null) return 'peakManagedHeapMB';
  return null;
}

export function gateFlatness(summaries) {
  const rows = [], failures = [];
  for (const scenario of scenarios) {
    const runs = summaries.filter(summary => summary.scenario === scenario).sort((a, b) => a.rows - b.rows);
    if (runs.length !== 2 || runs[0].rows === runs[1].rows) {
      failures.push(`${scenario}: needs exactly two runs at different row counts, found ${runs.length}.`);
      continue;
    }
    const [small, large] = runs;
    const row = { scenario, smallRows: small.rows, largeRows: large.rows, metric: metricFor(small, large), smallMB: null, largeMB: null, limitMB: null, passed: false };
    rows.push(row);
    if (!small.passed || !large.passed) { failures.push(`${scenario}: a run failed (rows, checksum or memory bound).`); continue; }
    if (small.memoryBoundMB !== large.memoryBoundMB) { failures.push(`${scenario}: runs used different memory bounds.`); continue; }
    if (!row.metric) { failures.push(`${scenario}: no comparable heap measurement in both runs.`); continue; }
    row.smallMB = small[row.metric];
    row.largeMB = large[row.metric];
    if (boundedOnly.has(scenario)) {
      row.limitMB = large.memoryBoundMB;
      row.passed = row.largeMB < row.limitMB;
    } else {
      row.limitMB = Math.round((row.smallMB * flatness.maximumRatio + flatness.allowanceMB) * 10) / 10;
      row.passed = row.largeMB <= row.limitMB;
    }
    if (!row.passed) failures.push(`${scenario}: ${row.metric} grew from ${row.smallMB} MB at ${small.rows} rows to ${row.largeMB} MB at ${large.rows} rows (limit ${row.limitMB} MB).`);
  }
  return { passed: failures.length === 0, rows, failures };
}

export function renderMarkdown(result) {
  const lines = ['## Billion-row memory flatness', '',
    `Larger run may use at most ${flatness.maximumRatio}x the smaller run's peak plus ${flatness.allowanceMB} MB. ` +
    `${[...boundedOnly].join(', ')} only has to stay under the memory bound (owner decision 2026-11-14).`, '',
    '| Scenario | Rows | Metric | Small MB | Large MB | Limit MB | Result |', '| :--- | :--- | :--- | ---: | ---: | ---: | :---: |'];
  for (const row of result.rows) {
    lines.push(`| ${row.scenario} | ${row.smallRows} / ${row.largeRows} | ${row.metric ?? '-'} | ${row.smallMB ?? '-'} | ${row.largeMB ?? '-'} | ${row.limitMB ?? '-'} | ${row.passed ? 'Pass' : 'FAIL'} |`);
  }
  if (result.failures.length) lines.push('', ...result.failures.map(failure => `- ${failure}`));
  return lines.join('\n') + '\n';
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'summarize' && args.length === 7) {
    const [runDirectory, scenario, rows, memoryBoundMB, passed, elapsedMinutes, output] = args;
    const summary = summarize({ runDirectory, scenario, rows: Number(rows), memoryBoundMB: Number(memoryBoundMB),
      passed: passed === 'true', elapsedMinutes: Number(elapsedMinutes) });
    writeFileSync(output, JSON.stringify(summary, null, 2) + '\n');
    console.log(JSON.stringify(summary));
  } else if (command === 'flatness' && args.length === 3) {
    const [directory, jsonOutput, markdownOutput] = args;
    const summaries = jsonFiles(directory).filter(file => path.basename(file) === 'summary.json')
      .map(file => JSON.parse(readFileSync(file, 'utf8')));
    const result = gateFlatness(summaries);
    writeFileSync(jsonOutput, JSON.stringify(result, null, 2) + '\n');
    writeFileSync(markdownOutput, renderMarkdown(result));
    console.log(renderMarkdown(result));
    if (!result.passed) process.exit(1);
  } else {
    console.error('Usage: node scripts/billion-row-evidence.mjs summarize <run-dir> <scenario> <rows> <memory-bound-mb> <passed> <elapsed-minutes> <summary.json>\n' +
      '       node scripts/billion-row-evidence.mjs flatness <summaries-dir> <result.json> <result.md>');
    process.exit(2);
  }
}
