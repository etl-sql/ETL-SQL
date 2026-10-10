import assert from 'node:assert/strict';
import { todaysRuns, renderDayStrip } from '../src/ETL-SQL.Portal/wwwroot/js/orchestrator-timeline.js';
import { readScriptChoices, readBundleChoices } from '../src/ETL-SQL.Portal/wwwroot/js/orchestrator-create-job.js';

// Today's strip: placement is a fraction of the local calendar day, and only today's runs are placed.
const now = new Date(2026, 9, 10, 9, 0, 0);
const at = (day, hour, minute = 0) => new Date(2026, 9, day, hour, minute, 0).toISOString();
const jobs = [
  { name: 'evening', interval: 1, unit: 'DAY', atTime: null, nextRun: at(10, 18), isEnabled: true },
  { name: 'midnight', interval: 1, unit: 'DAY', atTime: null, nextRun: at(10, 0), isEnabled: false },
  { name: 'tomorrow', interval: 1, unit: 'DAY', atTime: null, nextRun: at(11, 0), isEnabled: true },
  { name: 'yesterday', interval: 1, unit: 'DAY', atTime: null, nextRun: at(9, 23, 59), isEnabled: true },
  { name: 'manual', interval: 0, unit: 'HOUR', atTime: null, nextRun: null, isEnabled: true },
  { name: 'unparseable', interval: 1, unit: 'HOUR', atTime: null, nextRun: 'soon', isEnabled: true }
];
const runs = todaysRuns(jobs, now);
assert.deepEqual(runs.map(run => run.job.name), ['midnight', 'evening']);
assert.equal(runs[0].offset, 0);
assert.equal(runs[1].offset, 0.75);

const strip = renderDayStrip(jobs, now);
assert.match(strip, /role="list"/);
assert.equal((strip.match(/class="orch-day-run/g) ?? []).length, 2);
assert.match(strip, /style="left:75\.00%" data-job="evening"/);
assert.match(strip, /orch-day-run disabled-job"\s+style="left:0\.00%" data-job="midnight"/);
assert.doesNotMatch(strip, /tomorrow|yesterday|manual|unparseable/);
// A run in the last minutes of the day stays inside the track instead of hanging off its edge.
assert.match(renderDayStrip([{ ...jobs[0], nextRun: at(10, 23, 59) }], now), /style="left:94\.00%"/);
assert.match(renderDayStrip([], now), /No runs are scheduled for the rest of today/);
assert.match(renderDayStrip([{ ...jobs[0], name: '<img src=x onerror=1>' }], now), /&lt;img src=x onerror=1&gt;/);
assert.doesNotMatch(renderDayStrip([{ ...jobs[0], name: '"><script>' }], now), /<script>/);

assert.deepEqual(readScriptChoices({ files: ['a.etlsql'] }), [{ value: 'a.etlsql', label: 'a.etlsql' }]);
assert.deepEqual(readScriptChoices({ files: [] }), []);
for (const invalid of [null, [], {}, { files: 'a' }, { files: [''] }, { files: [1] }])
  assert.throws(() => readScriptChoices(invalid), /Invalid script list response/);

// The service lists published versions; each bundle is offered once, labelled with its newest version.
assert.deepEqual(readBundleChoices([{ bundleName: 'sales', version: 1 }, { bundleName: 'ops', version: 4 }, { bundleName: 'sales', version: 3 }]),
  [{ value: 'bundle://sales/latest', label: 'sales (latest: v3)' }, { value: 'bundle://ops/latest', label: 'ops (latest: v4)' }]);
for (const invalid of [null, {}, [{ bundleName: 'sales' }], [{ bundleName: '', version: 1 }], [{ bundleName: 'sales', version: 0 }],
  [{ bundleName: 'sales', version: '2' }], [{ bundleName: 'sales', latestVersion: 2 }]])
  assert.throws(() => readBundleChoices(invalid), /Invalid bundle list response/);

console.log('orchestrator page owner checks passed');
