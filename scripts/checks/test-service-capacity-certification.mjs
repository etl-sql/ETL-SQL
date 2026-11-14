import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { errorBreaches, materializeWorkload, profiles, renderMarkdown } from '../run-service-capacity-certification.mjs';

const root = new URL('../../', import.meta.url);
const criteria = { maxErrorRatePct: 5, maxSqliteContentionCount: 0 };
const step = overrides => ({ service: 'orchestrator', concurrency: 4, requestCount: 100, errorRatePct: 0, sqliteContentionCount: 0, unexpectedStatusCounts: {}, ...overrides });
const report = (...steps) => ({ breachCriteria: criteria, orchestrator: steps });

// Declared statuses (409 already running, 503 overloaded) are counted by the load tool as expected
// and never reach unexpectedStatusCounts; latency never gates.
assert.deepEqual(errorBreaches(report(step({ p95Ms: 99999, throughputRps: 0.01 }))), []);
assert.match(errorBreaches(report(step({ unexpectedStatusCounts: { 500: 2, 502: 1 } })))[0], /3 unexpected server error/);
assert.deepEqual(errorBreaches(report(step({ unexpectedStatusCounts: { 404: 1 } }))), [], 'A 4xx counts through the error rate only.');
assert.match(errorBreaches(report(step({ unexpectedStatusCounts: { 0: 4 } })))[0], /4 request\(s\) failed without a response/);
assert.match(errorBreaches(report(step({ errorRatePct: 5.1 })))[0], /error rate 5.1% > 5%/);
assert.match(errorBreaches(report(step({ sqliteContentionCount: 1 })))[0], /SQLite contention 1 > 0/);
assert.match(errorBreaches(report(step({ requestCount: 0 })))[0], /no requests completed/);
assert.match(errorBreaches(report(step({ unexpectedStatusCounts: undefined })))[0], /does not classify failed requests/,
  'A report from a load tool that predates status classification must fail, not pass vacuously.');
assert.deepEqual(errorBreaches({ breachCriteria: criteria }), ['The report has no measured steps.']);

const workload = {
  portal: { baseUrl: 'http://127.0.0.1:5000', roles: { admin: { username: 'admin', password: '<set>' } },
    requests: [{ url: 'http://127.0.0.1:5000/api/reports/{{reportId}}?x=1' }] },
  orchestrator: { baseUrl: 'http://127.0.0.1:5001/', apiKey: '<set>', requests: [{ url: 'http://127.0.0.1:5001' }] },
  external: 'https://example.invalid/kept',
  variables: { reportId: '<set>', unknown: 'kept' },
};
const materialized = materializeWorkload(workload, { portal: 'http://127.0.0.1:5110', orchestrator: 'http://127.0.0.1:5111',
  apiKey: 'key', passwords: { admin: 'pw' }, variables: { reportId: 7 } });
assert.equal(materialized.portal.baseUrl, 'http://127.0.0.1:5110');
assert.equal(materialized.portal.requests[0].url, 'http://127.0.0.1:5110/api/reports/{{reportId}}?x=1', 'Templates survive retargeting unencoded.');
assert.equal(materialized.orchestrator.baseUrl, 'http://127.0.0.1:5111');
assert.equal(materialized.orchestrator.requests[0].url, 'http://127.0.0.1:5111');
assert.equal(materialized.orchestrator.apiKey, 'key');
assert.equal(materialized.portal.roles.admin.password, 'pw');
assert.equal(materialized.external, 'https://example.invalid/kept');
assert.deepEqual(materialized.variables, { reportId: '7', unknown: 'kept' });
assert.equal(workload.portal.roles.admin.password, '<set>', 'The checked-in workload must not be mutated.');
assert.throws(() => materializeWorkload({ portal: { baseUrl: 'http://x', roles: { viewer: {} } } },
  { portal: 'p', orchestrator: 'o', apiKey: 'k', passwords: {}, variables: {} }), /No provisioned password for Portal role viewer/);

// Every profile workload exists and declares the trigger statuses its API returns under load.
for (const file of new Set(Object.values(profiles).flat())) {
  const url = new URL(file, root);
  assert.ok(existsSync(url), `Missing capacity workload ${file}.`);
  const text = readFileSync(url, 'utf8');
  if (/\/trigger/.test(text)) assert.match(text, /"expectedStatuses"/, `${file} triggers jobs without declaring 409/503 as expected.`);
}
assert.ok(profiles.full.includes(profiles.reference[0]), 'The full baseline includes the reference workload.');

const markdown = renderMarkdown({ profile: 'reference', workloads: [{ name: 'a', minutes: 1, passed: true, breaches: [] }, { name: 'b', minutes: 2, passed: false, breaches: ['x'] }] });
assert.match(markdown, /\| b \| 2 \| FAIL \|/);
assert.match(markdown, /\*\*b\*\*\n- x/);
console.log('Service-capacity certification gate: error gating, workload materialization and profiles verified.');
