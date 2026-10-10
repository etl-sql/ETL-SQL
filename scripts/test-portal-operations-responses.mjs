import assert from 'node:assert/strict';
import { makeOperationsApi } from '../tools/ui-sandbox/stories/portal-operations.story.js';
import * as read from '../src/ETL-SQL.Portal/wwwroot/js/operations-responses.js';
import { collectAdminUsers } from '../src/ETL-SQL.Portal/wwwroot/js/admin-user-directory.js';

const api = makeOperationsApi();
const metrics = await api.operationalMetrics();
assert.equal(read.readMetrics(metrics).queuedExecutions, 12);
for (const change of [{ queuedExecutions: '0' }, { queuedExecutions: -1 },
  { datasetStorageBytes: Number.MAX_SAFE_INTEGER + 1 }, { auditOutboxOldestPendingAgeSeconds: NaN },
  { schemaUpToDate: 'true' }, { nodeId: null }]) {
  assert.throws(() => read.readMetrics({ ...metrics, ...change }), /Invalid operational metrics/);
}
const fleet = await api.fleetStatus();
assert.equal(read.readFleet(fleet).inventory.upgradeReadiness.ready, true);
assert.equal(read.readFleet({ ...fleet, inventory: null }).inventory, null);
assert.throws(() => read.readFleet({ ...fleet, inventory: { ...fleet.inventory,
  upgradeReadiness: { ready: 'true', findings: [] } } }), /Invalid fleet/);
const [write] = await api.gatewayAmbiguousWrites();
assert.equal(read.readWriteCases([write])[0].events[0].eventType, 'Detected');
assert.throws(() => read.readWriteCases([{ ...write, events: [{ ...write.events[0], createdAtUtc: 'invalid' }] }]), /Invalid ambiguous/);
assert.throws(() => read.readWriteCases({ items: [] }), /Invalid ambiguous/);
const [account] = await api.listServiceAccounts();
assert.equal(read.readAccounts([account])[0].version, 1);
assert.throws(() => read.readAccounts([{ ...account, scopes: 'portal.read' }]), /Invalid service accounts/);
assert.throws(() => read.readAccounts([{ ...account, version: 0 }]), /Invalid service accounts/);
assert.throws(() => read.readAccounts([{ ...account, lastUsedAt: 'invalid' }]), /Invalid service accounts/);
assert.equal(read.readCredential({ account, clientSecret: 'fixture-one-time-value' }).account.id, account.id);
assert.throws(() => read.readCredential({ account, clientSecret: null }), /Invalid service-account credential/);
assert.throws(() => read.readCredential({ account: { ...account, clientId: null }, clientSecret: 'fixture-one-time-value' }), /Invalid service-account credential/);
const services = await api.listAdminServices();
assert.equal(read.readServices(services)[0].recipients, 'ops@example.test;oncall@example.test');
assert.throws(() => read.readServices([{ ...services[0], recipients: [] }]), /Invalid administrative services/);
assert.throws(() => read.readRuns([{ ...services[0].lastRun, attempts: '1' }]), /Invalid administrative service history/);
assert.equal(read.readRuns(await api.adminServiceHistory())[0].outcome, 'Sent');
const approvals = await api.pendingAccessRequests();
assert.equal(read.readApprovals(approvals).length, 1);
assert.throws(() => read.readApprovals([{ ...approvals[0], createdAt: null }]), /Invalid pending/);
assert.equal(read.readOwners(await api.listUsers())[0].username, 'admin');
assert.throws(() => read.readOwners([{ id: 1, username: 'owner', isActive: 'true' }]), /Invalid account owners/);
assert.deepEqual(read.readAccess([]), []);
assert.throws(() => read.readAccess(null), /Invalid anonymous/);
assert.deepEqual(read.readAudit(await api.auditLog()), []);
assert.throws(() => read.readAudit({ items: [{ timestamp: 'bad-date', action: 'CREATE' }] }), /Invalid service-account audit/);

const user = id => ({ id, username: `owner-${id}`, isActive: true, mustChangePassword: false,
  createdAt: '2026-10-07T00:00:00Z', roles: ['Viewer'], groups: [], version: 1 });
const directory = Array.from({ length: 101 }, (_, index) => user(index + 1));
const calls = [];
assert.equal((await collectAdminUsers(async (page, pageSize) => {
  calls.push([page, pageSize]);
  return { items: directory.slice((page - 1) * pageSize, page * pageSize), total: 101, page, pageSize };
}))[100].id, 101);
assert.deepEqual(calls, [[1, 100], [2, 100]]);
await assert.rejects(collectAdminUsers(async (page, pageSize) => ({ items: [], total: 1, page, pageSize })), /incomplete page/);
await assert.rejects(collectAdminUsers(async (page, pageSize) => ({ items: [user(1), user(1)], total: 2, page, pageSize })), /duplicate entries/);
await assert.rejects(collectAdminUsers(async (page, pageSize) => ({ items: [{ ...user(1), isActive: 'true' }], total: 1, page, pageSize })), /Invalid user-directory/);
await assert.rejects(collectAdminUsers(async (page, pageSize) => ({
  items: page === 1 ? directory.slice(0, 100) : [user(101)], total: page === 1 ? 101 : 102, page, pageSize
})), /directory changed/);
const savedFetch = globalThis.fetch, savedStorage = globalThis.sessionStorage, savedWindow = globalThis.window;
try {
  globalThis.sessionStorage = { getItem: () => null };
  globalThis.fetch = async url => {
    const query = new URL(url, 'http://localhost').searchParams;
    const page = Number(query.get('page')), pageSize = Number(query.get('pageSize'));
    return new Response(JSON.stringify({ items: directory.slice((page - 1) * pageSize, page * pageSize), total: 101, page, pageSize }));
  };
  globalThis.window = { fetch: globalThis.fetch, location: { origin: 'http://localhost', href: 'http://localhost/admin.html' } };
  const { adminApi } = await import('../src/ETL-SQL.Portal/wwwroot/js/api.js');
  assert.equal((await adminApi.listUsers())[100].username, 'owner-101');
} finally {
  globalThis.fetch = savedFetch;
  if (savedStorage === undefined) delete globalThis.sessionStorage;
  else globalThis.sessionStorage = savedStorage;
  if (savedWindow === undefined) delete globalThis.window;
  else globalThis.window = savedWindow;
}
console.log('Portal operations response boundaries passed.');
