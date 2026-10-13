import assert from 'node:assert/strict';
import { readLineageHistory, readStewardship, readProtectedRows, readSuggestions, readLineageImpact,
  readLineageAudit, readMetrics, readSavedLineageViews } from '../../src/ETL-SQL.Portal/wwwroot/js/lineage-catalog-responses.js';
import { createLineageMock } from '../../tools/ui-sandbox/stories/lineage-catalog.story.js';

const api = createLineageMock();
const rows = readLineageHistory(await api.lineage('table', { name: 'mart.SalesSummary' }));
assert.equal(rows.length, 4);
for (const value of [null, {}, [...rows, rows[0]], [{ ...rows[0], reportId: '42' }], [{ ...rows[0], sourceColumns: null }]]) {
  assert.throws(() => readLineageHistory(value));
}
assert.deepEqual(readLineageHistory([]), []);
const inventory = readStewardship(await api.stewardship({ view: 'all' }));
assert.equal(inventory.items.length, 6);
for (const value of [null, {}, { ...inventory, stewards: null }, { ...inventory, summary: { totalAssets: 6 } },
  { ...inventory, items: [{ ...inventory.items[0], isSensitive: 'false' }] }]) assert.throws(() => readStewardship(value));
const protectedRows = readProtectedRows(await api.protectedData());
assert.equal(protectedRows.length, 1);
assert.equal(Object.hasOwn(protectedRows[0], 'isSensitive'), false);
assert.throws(() => readProtectedRows([{ ...protectedRows[0], protectionTags: [] }]));
assert.throws(() => readProtectedRows(null));
const suggestions = readSuggestions(await api.protectedDataSuggestions());
assert.equal(suggestions.length, 1);
assert.throws(() => readSuggestions([{ ...suggestions[0], confidence: 2 }]));
const request = { kind: 'table', name: 'edw.Sales', column: '', direction: 'downstream', depth: 4, limit: 100 };
const impact = readLineageImpact(await api.impact(request), request);
assert.equal(impact.alerts.length, 1);
for (const value of [{ ...impact, alerts: undefined }, { ...impact, summary: { ...impact.summary, reports: 0 } },
  { ...impact, request: { ...impact.request, name: 'other-target' } }, { ...impact, request: { ...impact.request, depth: 8 } }]) {
  assert.throws(() => readLineageImpact(value, request));
}
const audit = await api.auditLog(1, 200);
assert.equal(readLineageAudit(audit, 200).length, 2);
assert.throws(() => readLineageAudit(audit, 50));
assert.throws(() => readLineageAudit({ ...audit, items: [] }, 200));
assert.equal(readMetrics(await api.operationalMetrics()).auditOutboxPending, 3);
assert.throws(() => readMetrics({ auditOutboxPending: 3 }));
const saved = { name: 'Sales', kind: 'table', query: 'mart.SalesSummary', column: '', tagValue: '', from: '', to: '' };
assert.deepEqual(readSavedLineageViews([saved, saved, { name: 'partial' }, { ...saved, name: 'bad-kind', kind: '__proto__' }]), [saved]);
assert.deepEqual(readSavedLineageViews({}), []);

const calls = [];
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/index.html' },
  fetch: async (url, options) => { calls.push({ url: String(url), options }); return Response.json([]); } };
const { catalogApi, adminApi } = await import('../../src/ETL-SQL.Portal/wwwroot/js/api.js');
const controller = new AbortController();
await catalogApi.stewardship({}, controller.signal);
await catalogApi.protectedData({}, controller.signal);
await catalogApi.protectedDataSuggestions({}, controller.signal);
await catalogApi.impact(request, controller.signal);
await adminApi.auditLog(1, 200, 'STEWARD_LINEAGE_IMPACT', '', '', '', controller.signal);
await adminApi.operationalMetrics(controller.signal);
assert.equal(calls.length, 6);
assert.ok(calls.every(call => call.options.signal === controller.signal));
console.log('lineage catalog response and transport contracts passed');
