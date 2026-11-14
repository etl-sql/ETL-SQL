import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gateFlatness, peakMemory, renderMarkdown, scenarios, summarize } from '../billion-row-evidence.mjs';

// Real scenario results from Scale Measurement runs 43617518622 (10M rows) and 43617518644 (100M rows),
// both under a 4 GB GC hard limit.
const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'billion-row');
const recorded = {
  ColumnarCore: [null, 123.9, null, 114.6], TempTableRoundTrip: [null, 99.9, null, 105.1],
  AllocProfile: [81.7, null, 88, 175], ExternalSort: [183.1, null, 183.2, null], ExternalJoin: [291.6, null, 435.4, null],
  HighCardinalityGrouping: [187.8, null, 167.9, null], EligibleWindowRowNumber: [151.9, null, 172.9, null],
};
const summaries = [];
for (const scenario of scenarios) {
  for (const [size, rows] of [['10m', 10_000_000], ['100m', 100_000_000]]) {
    const summary = summarize({ runDirectory: path.join(fixtures, size, scenario), scenario, rows, memoryBoundMB: 4096, passed: true, elapsedMinutes: 1 });
    const [live, managed] = size === '10m' ? recorded[scenario].slice(0, 2) : recorded[scenario].slice(2);
    assert.equal(summary.peakLiveHeapMB, live, `${scenario} ${size} live heap`);
    if (managed !== null) assert.equal(summary.peakManagedHeapMB, managed, `${scenario} ${size} managed heap`);
    summaries.push(summary);
  }
}

const measured = gateFlatness(summaries);
assert.equal(measured.passed, true, renderMarkdown(measured));
assert.deepEqual(measured.rows.map(row => row.metric), ['peakManagedHeapMB', 'peakManagedHeapMB', 'peakLiveHeapMB', 'peakLiveHeapMB',
  'peakLiveHeapMB', 'peakLiveHeapMB', 'peakLiveHeapMB']);
assert.equal(measured.rows.find(row => row.scenario === 'ExternalJoin').limitMB, 4096);

const replace = (scenario, rows, values) => summaries.map(summary =>
  summary.scenario === scenario && summary.rows === rows ? { ...summary, ...values } : summary);
const failing = (list, pattern) => {
  const result = gateFlatness(list);
  assert.equal(result.passed, false);
  assert.match(result.failures.join('\n'), pattern);
};
failing(replace('ExternalSort', 100_000_000, { peakLiveHeapMB: 300 }), /ExternalSort: peakLiveHeapMB grew from 183\.1 MB/);
failing(replace('ExternalJoin', 100_000_000, { peakLiveHeapMB: 4096 }), /ExternalJoin: .*limit 4096 MB/);
failing(replace('ColumnarCore', 10_000_000, { passed: false }), /ColumnarCore: a run failed/);
failing(summaries.filter(summary => !(summary.scenario === 'AllocProfile' && summary.rows === 10_000_000)), /AllocProfile: needs exactly two runs/);
failing(replace('EligibleWindowRowNumber', 100_000_000, { memoryBoundMB: 8192 }), /different memory bounds/);
failing(replace('HighCardinalityGrouping', 100_000_000, { peakLiveHeapMB: null, peakManagedHeapMB: null }), /no comparable heap measurement/);
// A run with live heap in only one size falls back to the managed peak both runs report.
const mixed = gateFlatness(replace('AllocProfile', 10_000_000, { peakLiveHeapMB: null, peakManagedHeapMB: 160 }));
assert.equal(mixed.rows.find(row => row.scenario === 'AllocProfile').metric, 'peakManagedHeapMB');
assert.throws(() => summarize({ runDirectory: fixtures, scenario: 'Unknown', rows: 1, memoryBoundMB: 1, passed: true }), /Unknown billion-row scenario/);
assert.deepEqual(peakMemory(path.join(fixtures, '10m', 'ExternalSort')), { peakLiveHeapMB: 183.1, peakManagedHeapMB: 357.7 });
console.log('Billion-row flatness passes the recorded 10M/100M runs and rejects growth, failures, missing runs, mixed bounds and missing measurements.');
