import assert from 'node:assert/strict';
import { readAdminAuditPage } from '../src/ETL-SQL.Portal/wwwroot/js/admin-audit-responses.js';

const entry = { id: 1, userId: null, username: null, action: 'RUN_REPORT', resourceType: 'Report',
  resourceId: '7', timestamp: '2026-10-09T12:00:00Z', detail: 'Evidence', correlationId: null, studioCapability: null };
const page = { items: [entry], total: 1, page: 1, pageSize: 50 };
assert.deepEqual(readAdminAuditPage(page, 1, 50), page);
assert.deepEqual(readAdminAuditPage(page, 999, 50), page);
assert.deepEqual(readAdminAuditPage({ ...page, total: 0, items: [] }, 999, 50).items, []);
const last = { ...page, total: 51, page: 2 };
assert.equal(readAdminAuditPage(last, 999, 50).page, 2);
for (const bad of [null, [], {}, { ...page, total: '1' }, { ...page, total: -1 }, { ...page, total: 1.5 },
  { ...page, page: 2 }, { ...page, pageSize: 25 }, { ...page, total: 2 },
  { ...page, items: [] }, { ...page, items: [entry, entry], total: 2 }])
  assert.throws(() => readAdminAuditPage(bad, 1, 50));
for (const bad of [{ ...entry, id: 0 }, { ...entry, userId: '1' }, { ...entry, username: 42 },
  { ...entry, action: ' ' }, { ...entry, timestamp: 'invalid' }, { ...entry, resourceId: {} },
  { ...entry, resourceType: 42 }, { ...entry, detail: {} }, { ...entry, correlationId: 42 },
  { ...entry, studioCapability: [] }])
  assert.throws(() => readAdminAuditPage({ ...page, items: [bad] }, 1, 50));
assert.throws(() => readAdminAuditPage(page, 0, 50));
assert.throws(() => readAdminAuditPage(page, 1, 0));

const calls = [];
let reply = () => Response.json(page);
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminAuditApi } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-audit-api.js');
const request = new AbortController();
await adminAuditApi.catalog(1, 'ACTION & other', request.signal);
assert.equal(new URL(calls.at(-1).url, window.location.origin).searchParams.get('action'), 'ACTION & other');
assert.equal(calls.at(-1).options.signal, request.signal);
reply = () => Response.json(last);
assert.equal((await adminAuditApi.catalog(999, '')).page, 2);
for (const response of [() => Response.json({ items: [] }),
  () => Response.json(page, { status: 201 }), () => Response.json({ error: 'Unavailable' }, { status: 503 }),
  () => new Response('not JSON'), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminAuditApi.catalog(1, ''));
}
reply = () => new Response('Id,Action\n1,RUN_REPORT', { headers: { 'Content-Type': 'text/csv; charset=utf-8' } });
assert.equal(await (await adminAuditApi.exportCsv('RUN_REPORT', request.signal)).text(), 'Id,Action\n1,RUN_REPORT');
assert.equal(new URL(calls.at(-1).url, window.location.origin).searchParams.get('action'), 'RUN_REPORT');
assert.equal(calls.at(-1).options.signal, request.signal);
for (const response of [() => Response.json({ error: 'Export unavailable' }, { status: 503 }),
  () => Response.json({ error: 'Wrong body' }), () => new Response('', { headers: { 'Content-Type': 'text/csv' } }),
  () => new Response('Id', { status: 201, headers: { 'Content-Type': 'text/csv' } }),
  () => new Response('<html>Login</html>', { headers: { 'Content-Type': 'text/html' } }),
  () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminAuditApi.exportCsv(''));
}
request.abort();
const before = calls.length;
await assert.rejects(() => adminAuditApi.exportCsv('', request.signal));
assert.equal(calls.length, before);
console.log('admin audit contracts: page clamps, complete evidence, scoped requests, and CSV response validation passed');
