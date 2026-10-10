import assert from 'node:assert/strict';
import { readAdminSubscription, readAdminSubscriptionPage, readSubscriptionParameters, readSubscriptionHistory,
  readSubscriptionReceipt, readSubscriptionBulkReceipt } from '../src/ETL-SQL.Portal/wwwroot/js/admin-subscriptions-responses.js';

const subscription = { id: 60, reportId: 40, reportName: 'Finance', name: null, schedule: 'Daily', atTime: null,
  deliverOnRefresh: false, format: 'PDF', smtpAlias: 'mail', recipients: 'finance@example.test',
  lastSentAt: null, nextRunAt: null, failCount: 0, isActive: true, parameters: { '@Region': 'North' },
  parameterSummary: '@Region=North', version: 1 };
const page = { items: [subscription], total: 1, page: 1, pageSize: 25 };
assert.deepEqual(readAdminSubscriptionPage(page, 1, 25), page);
assert.throws(() => readAdminSubscriptionPage({ ...page, page: 0 }, 0, 25));
assert.throws(() => readAdminSubscriptionPage({ ...page, pageSize: 0 }, 1, 0));
for (const invalid of [null, {}, { ...page, total: 2 }, { ...page, items: [] }, { ...page, page: 2 },
  { ...page, pageSize: 50 }, { ...page, total: -1 }, { ...page, items: [subscription, subscription], total: 2 }])
  assert.throws(() => readAdminSubscriptionPage(invalid, 1, 25));
for (const invalid of [{ ...subscription, id: 0 }, { ...subscription, reportId: null }, { ...subscription, version: '1' },
  { ...subscription, failCount: -1 }, { ...subscription, nextRunAt: 'invalid' }, { ...subscription, parameters: { '@Region': 42 } },
  { ...subscription, isActive: 'true' }, { ...subscription, reportName: null }])
  assert.throws(() => readAdminSubscription(invalid));
assert.throws(() => readAdminSubscription(subscription, 61));
const parameter = { name: '@Region', type: 'STRING', required: true, default: null, description: 'Region' };
assert.deepEqual(readSubscriptionParameters([parameter]), [parameter]);
for (const invalid of [null, {}, [parameter, parameter], [{ ...parameter, required: 'true' }], [{ ...parameter, default: 42 }]])
  assert.throws(() => readSubscriptionParameters(invalid));
const history = { id: 1, jobName: 'Subscription trigger', startTime: '2026-10-09T12:00:00Z', endTime: null,
  status: 'RUNNING', errorMessage: null, rowsProcessed: 0, peakMemoryBytes: 0, cpuTimeSeconds: 0,
  scriptHashAtRunTime: null, hashMatched: null, rowsQuarantined: 0, rowsWarned: 0, dataQualityFailures: null,
  checkpointLabel: null, jobId: null, tenantId: 'portal-host', hasResumeSession: false };
assert.deepEqual(readSubscriptionHistory([history]), [history]);
for (const invalid of [null, {}, [history, history], [{ ...history, startTime: 'invalid' }],
  [{ ...history, rowsProcessed: '0' }], [{ ...history, cpuTimeSeconds: NaN }], [{ ...history, hasResumeSession: null }]])
  assert.throws(() => readSubscriptionHistory(invalid));
const receipt = { ...subscription, isActive: false, version: 2 };
assert.equal(readSubscriptionReceipt(receipt, subscription, { isActive: false }).version, 2);
for (const invalid of [{ ...receipt, id: 61 }, { ...receipt, reportId: 41 }, { ...receipt, version: 1 },
  { ...receipt, isActive: true }]) assert.throws(() => readSubscriptionReceipt(invalid, subscription, { isActive: false }));
assert.equal(readSubscriptionReceipt({ ...receipt, parameters: null }, subscription, { parameters: {} }).parameters, null);
assert.throws(() => readSubscriptionReceipt(receipt, subscription, { parameters: {} }));
const special = JSON.parse('{"__proto__":"kept","@Region":"East"}');
assert.deepEqual(readSubscriptionReceipt({ ...receipt, parameters: special }, subscription, { parameters: special }).parameters, special);
const targets = [{ id: 60, version: 1 }, { id: 61, version: 2 }];
const bulk = { updated: 1, results: [{ id: 60, status: 'Updated', currentVersion: 2, error: null },
  { id: 61, status: 'Conflict', currentVersion: 3, error: null }] };
assert.deepEqual(readSubscriptionBulkReceipt(bulk, targets), bulk);
for (const invalid of [{ ...bulk, updated: 2 }, { ...bulk, results: [bulk.results[0]] },
  { ...bulk, results: [bulk.results[0], bulk.results[0]] },
  { ...bulk, results: [{ ...bulk.results[0], currentVersion: 1 }, bulk.results[1]] },
  { ...bulk, results: [bulk.results[0], { ...bulk.results[1], status: 'Unknown' }] }])
  assert.throws(() => readSubscriptionBulkReceipt(invalid, targets));

const calls = [];
let reply = () => Response.json(page);
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminSubscriptionsApi, AdminSubscriptionOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-subscriptions-api.js');
const pending = new AbortController();
await adminSubscriptionsApi.catalog(1, 'Finance & East', 'active', pending.signal);
assert.equal(new URL(calls.at(-1).url, window.location.origin).searchParams.get('q'), 'Finance & East');
assert.equal(calls.at(-1).options.signal, pending.signal);
reply = () => Response.json(receipt);
await adminSubscriptionsApi.update(subscription, { isActive: false });
assert.equal(calls.at(-1).options.headers.get('If-Match'), '"1"');
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { isActive: false });
for (const bad of [() => Response.json(receipt, { status: 201 }), () => Response.json({ error: 'Unavailable' }, { status: 503 }),
  () => Response.json({ ...receipt, version: 1 }), () => new Response('not JSON'), () => { throw new Error('Connection lost'); }]) {
  reply = bad;
  await assert.rejects(() => adminSubscriptionsApi.update(subscription, { isActive: false }),
    error => error instanceof AdminSubscriptionOutcomeUnconfirmed && error.subscriptionIds[0] === 60);
}
reply = () => Response.json({ error: 'Version changed.' }, { status: 409 });
await assert.rejects(() => adminSubscriptionsApi.update(subscription, { isActive: false }), error => !(error instanceof AdminSubscriptionOutcomeUnconfirmed));
reply = () => new Response(null, { status: 204 });
await adminSubscriptionsApi.delete(subscription);
reply = () => Response.json({});
await assert.rejects(() => adminSubscriptionsApi.delete(subscription), AdminSubscriptionOutcomeUnconfirmed);
reply = () => Response.json(bulk);
await adminSubscriptionsApi.bulkStatus(targets, false);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { subscriptions: targets, isActive: false });
reply = () => new Response(null, { status: 404 });
assert.equal(await adminSubscriptionsApi.get(60, pending.signal), null);
reply = () => Response.json({ items: [] });
await assert.rejects(() => adminSubscriptionsApi.parameters(40));
await assert.rejects(() => adminSubscriptionsApi.history(60));
pending.abort(); const count = calls.length;
await assert.rejects(() => adminSubscriptionsApi.catalog(1, '', '', pending.signal));
assert.equal(calls.length, count);
console.log('admin subscription contracts: complete catalogs, parameters/history, versioned receipts, clear overrides, partial bulk results, and uncertain writes passed');
