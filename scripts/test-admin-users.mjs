import assert from 'node:assert/strict';
import { readAdminUserPage, readAdminRoles, readAdminUserReceipt, readAdminUserBulkReceipt } from '../src/ETL-SQL.Portal/wwwroot/js/admin-users-responses.js';

const user = { id: 2, username: 'steward', email: 'steward@example.test', firstName: null, lastName: null,
  isActive: true, mustChangePassword: false, createdAt: '2026-10-08T12:00:00Z', roles: ['DataSteward'], groups: [], provider: 'Local', version: 2 };
const page = { items: [user], total: 1, page: 1, pageSize: 25 };
assert.equal(readAdminUserPage(page, 1, 25).items[0].username, 'steward');
assert.equal(readAdminUserPage({ ...page, items: [], total: 0 }, 1, 25).total, 0);
for (const bad of [null, {}, { ...page, total: 2 }, { ...page, page: 2 }, { ...page, pageSize: 100 },
  { ...page, items: [{ ...user, version: null }] }, { ...page, items: [{ ...user, createdAt: 'unknown' }] },
  { ...page, items: [{ ...user, roles: null }] }, { ...page, items: [user, user], total: 2 }])
  assert.throws(() => readAdminUserPage(bad, 1, 25));
assert.deepEqual(readAdminRoles([{ name: 'DataSteward' }, { name: 'CustomRole' }]).map(role => role.name), ['DataSteward', 'CustomRole']);
for (const bad of [null, {}, [{ name: '' }], [{ name: 2 }], [{ name: 'Viewer' }, { name: 'VIEWER' }]])
  assert.throws(() => readAdminRoles(bad));
const request = { username: user.username, email: user.email, role: 'DataSteward' };
assert.equal(readAdminUserReceipt(user, null, request).id, 2);
assert.equal(readAdminUserReceipt(user, 2, { email: user.email }, 1).version, 2);
assert.deepEqual(readAdminUserReceipt({ ...user, roles: [] }, 2, { email: user.email }, 1).roles, []);
for (const bad of [{ ...user, id: 3 }, { ...user, version: 1 }, { ...user, email: 'another@example.test' }])
  assert.throws(() => readAdminUserReceipt(bad, 2, { email: user.email }, 1));
assert.throws(() => readAdminUserReceipt({ ...user, roles: ['Admin'] }, null, request));
assert.throws(() => readAdminUserReceipt({ ...user, username: 'another' }, null, request));
const targets = [{ id: 2, version: 1 }, { id: 3, version: 1 }];
const bulk = { updated: 1, results: [{ id: 2, status: 'Updated', currentVersion: 2, error: null },
  { id: 3, status: 'Conflict', currentVersion: 2, error: null }] };
assert.equal(readAdminUserBulkReceipt(bulk, targets).updated, 1);
for (const bad of [{ ...bulk, updated: 2 }, { ...bulk, results: [bulk.results[0]] },
  { ...bulk, results: [bulk.results[0], bulk.results[0]] },
  { ...bulk, results: [{ ...bulk.results[0], currentVersion: 1 }, bulk.results[1]] },
  { ...bulk, results: [bulk.results[0], { ...bulk.results[1], id: 99 }] },
  { ...bulk, results: [bulk.results[0], { ...bulk.results[1], status: 'Completed' }] }])
  assert.throws(() => readAdminUserBulkReceipt(bad, targets));

const calls = [];
let reply = () => new Response(null, { status: 204 });
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminUsersApi, AdminUserOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-users-api.js');
await adminUsersApi.resetPassword(2, 'temporary-test-password', 1);
assert.equal(new Headers(calls[0].options.headers).get('If-Match'), '"1"');
assert.equal(JSON.parse(calls[0].options.body).newPassword, 'temporary-test-password');
for (const response of [() => Response.json({ success: true }), () => new Response(null, { status: 202 }),
  () => new Response(null, { status: 500 }), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminUsersApi.resetPassword(2, 'temporary-test-password', 1), AdminUserOutcomeUnconfirmed);
}
reply = () => Response.json({ error: 'Version conflict' }, { status: 409 });
await assert.rejects(() => adminUsersApi.resetPassword(2, 'temporary-test-password', 1), error => !(error instanceof AdminUserOutcomeUnconfirmed) && error.status === 409);
reply = () => Response.json({ ...user, roles: ['Admin'] }, { status: 201 });
await assert.rejects(() => adminUsersApi.create({ ...request, password: 'temporary-test-password' }), AdminUserOutcomeUnconfirmed);
reply = () => Response.json(user);
const updated = await adminUsersApi.update(2, { email: user.email }, 1);
reply = () => new Response(null, { status: 204 });
await adminUsersApi.resetPassword(2, 'temporary-test-password', updated.version);
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
console.log('admin users response and mutation checks passed');
