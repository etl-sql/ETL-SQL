import assert from 'node:assert/strict';
import { makeControlPlaneSandbox } from '../../tools/ui-sandbox/stories/control-plane-dashboard.story.js';
import { readControlPlaneOverview, readControlPlaneTenants, readControlPlaneAudit,
  readControlPlaneReceipt } from '../../src/ETL-SQL.Portal/wwwroot/js/control-plane-responses.js';
import { escapeControlPlane } from '../../src/ETL-SQL.Portal/wwwroot/js/control-plane-view.js';

const api = makeControlPlaneSandbox('noisyNeighbor');
const overview = await (await api.fetch('/api/platform/control-plane/overview')).json();
assert.equal(readControlPlaneOverview(overview).totalTenants, 3);
for (const change of [{ totalTenants: '3' }, { auditOutboxFailed: -1 }, { capturedAtUtc: 'bad' },
  { activeExecutions: Number.MAX_SAFE_INTEGER + 1 }]) {
  assert.throws(() => readControlPlaneOverview({ ...overview, ...change }), /Invalid control-plane overview/);
}
const tenants = await (await api.fetch('/api/platform/control-plane/tenants')).json();
assert.ok(readControlPlaneTenants(tenants)[2].quotaUtilizationPercentage > 100);
assert.throws(() => readControlPlaneTenants([tenants[0], tenants[0]]), /Invalid control-plane tenant/);
for (const change of [{ maxStorageMb: '0' }, { quotaUtilizationPercentage: NaN }, { tenantId: '' },
  { fenceEpoch: Number.MAX_SAFE_INTEGER + 1 }, { deletedAtUtc: 'bad' }]) {
  assert.throws(() => readControlPlaneTenants([{ ...tenants[0], ...change }]), /Invalid control-plane tenant/);
}
assert.deepEqual(readControlPlaneTenants([]), []);
assert.throws(() => readControlPlaneTenants(null), /Invalid control-plane tenant/);
const response = await api.fetch('/api/platform/control-plane/tenants/acme-corp/state', {
  method: 'POST', body: JSON.stringify({ state: 'Maintenance', platformOperator: 'operator',
    authorizationReference: 'CHG-fixture', reason: 'Scheduled maintenance' })
});
const receipt = await response.json();
assert.equal(readControlPlaneReceipt(receipt).status, 'Completed');
assert.equal(readControlPlaneAudit([receipt])[0].kind, 'SetState:Maintenance');
assert.equal(readControlPlaneReceipt({ ...receipt, status: 'Pending' }).status, 'Pending');
assert.throws(() => readControlPlaneReceipt(null), /Invalid control-plane audit receipt/);
for (const change of [{ operationId: '' }, { receiptHash: null }, { platformOperator: 7 },
  { authorizationExpiresUtc: 'bad' }, { targetMaxConcurrentJobs: '5' }]) {
  assert.throws(() => readControlPlaneReceipt({ ...receipt, ...change }), /Invalid control-plane audit receipt/);
}
assert.throws(() => readControlPlaneAudit([receipt, receipt]), /Invalid control-plane audit trail/);
assert.throws(() => readControlPlaneAudit({ items: [] }), /Invalid control-plane audit trail/);
assert.equal(escapeControlPlane('<img src=x onerror="attack()">&'), '&lt;img src=x onerror=&quot;attack()&quot;&gt;&amp;');
console.log('Control-plane metadata and receipt response boundaries passed.');
