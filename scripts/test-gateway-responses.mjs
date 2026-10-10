import assert from 'node:assert/strict';
import { gatewayFixtures, makeFakeApi, makeWizardApi } from '../tools/ui-sandbox/stories/gateways-admin.story.js';
import { readGatewayFleet, currentGatewayFleet, readIssuedGateway, readGatewayRevocation,
  readGatewayResources, readDiscoveredGateways, readConnectorSchemas, readConnectionDiagnostic } from '../src/ETL-SQL.Portal/wwwroot/js/gateway-responses.js';
import { gatewaySetupCommand, escapeGateway, renderGatewayNodes } from '../src/ETL-SQL.Portal/wwwroot/js/gateways-view.js';

const fleet = readGatewayFleet(gatewayFixtures.healthy);
assert.equal(fleet.length, 4);
assert.deepEqual(readGatewayFleet([]), []);
for (const invalid of [null, {}, [fleet[0], fleet[0]]]) assert.throws(() => readGatewayFleet(invalid), /unavailable or incomplete/);
for (const change of [{ enrollmentId: '' }, { activeNodes: '2' }, { totalNodes: 3 }, { nodes: null },
  { isOnline: false }, { state: 'consumed' }, { createdUtc: 'invalid' }, { publishedResources: [{}] }]) {
  assert.throws(() => readGatewayFleet([{ ...fleet[0], ...change }]), /unavailable or incomplete/);
}
for (const nodeChange of [{ gatewayId: 'other-cluster' }, { tenantId: 'other-tenant' },
  { isActive: 'true' }, { connectedUtc: 'invalid' }, { workloadPublicKeyThumbprint: null }]) {
  const gateway = structuredClone(fleet[0]); Object.assign(gateway.nodes[0], nodeChange);
  assert.throws(() => readGatewayFleet([gateway]), /unavailable or incomplete/);
}
const newerPending = { ...fleet[0], enrollmentId: 'newer-pending', createdUtc: '2026-10-07T12:00:00Z', state: 'Pending' };
assert.equal(currentGatewayFleet(readGatewayFleet([newerPending, ...fleet]))[0].enrollmentId, fleet[0].enrollmentId);
const newerConsumed = { ...newerPending, state: 'Consumed' };
assert.equal(currentGatewayFleet(readGatewayFleet([...fleet, newerConsumed]))[0].enrollmentId, newerConsumed.enrollmentId);
assert.equal(currentGatewayFleet(readGatewayFleet([...fleet, { ...newerPending, gatewayId: 'CORP-ONPREM-GW', nodes: [], activeNodes: 0, totalNodes: 0, isOnline: false }])).length, 5);
const inactive = structuredClone(fleet[0]); inactive.nodes[0].isActive = false; inactive.activeNodes = 1;
assert.match(renderGatewayNodes(readGatewayFleet([inactive])[0]), /Disconnected/);
assert.equal(escapeGateway('<img onerror="x">&\''), '&lt;img onerror=&quot;x&quot;&gt;&amp;&#39;');

const api = makeFakeApi(gatewayFixtures.healthy);
const receipt = await api.enroll("branch'; Write-Host injected; '", 60);
assert.equal(readIssuedGateway(receipt, receipt.gatewayId).tenantId, 'portal-host');
assert.match(gatewaySetupCommand('https://portal', receipt), /--gateway-id 'branch''; Write-Host injected; '''/);
for (const change of [{ oneTimeToken: undefined }, { gatewayId: 'other' }, { tenantId: '' }, { expiresUtc: '2000-01-01T00:00:00Z' }]) {
  assert.throws(() => readIssuedGateway({ ...receipt, ...change }, receipt.gatewayId), /unconfirmed/);
}
assert.equal(readGatewayRevocation(await api.revoke(receipt.gatewayId), receipt.gatewayId).success, true);
for (const invalid of [null, {}, { success: 'true', gatewayId: receipt.gatewayId }, { success: true, gatewayId: 'other' }]) {
  assert.throws(() => readGatewayRevocation(invalid, receipt.gatewayId), /unconfirmed/);
}
const wizard = makeWizardApi(api);
const schemas = await wizard.schemas(); assert.equal(readConnectorSchemas(schemas)[0].connectorType, 'MSSQL');
assert.throws(() => readConnectorSchemas([{ ...schemas[0], options: [{ name: 'PASSWORD', type: '3' }] }]), /unavailable/);
const gateways = await wizard.gateways();
assert.equal(readDiscoveredGateways([gateways[0], gateways[0]]).length, 1);
assert.throws(() => readDiscoveredGateways([{ ...gateways[0], totalNodes: -1 }]), /unavailable/);
const resources = await wizard.resources('corp-onprem-gw');
assert.equal(readGatewayResources(resources)[0].state, 'Approved');
assert.throws(() => readGatewayResources([{ ...resources[0], state: 'Pending' }]), /unavailable/);
const diagnostic = await wizard.test({ alias: 'fixture', connectorType: 'MSSQL' });
assert.equal(readConnectionDiagnostic(diagnostic).succeeded, true);
assert.throws(() => readConnectionDiagnostic({ ...diagnostic, succeeded: false }), /incomplete/);
assert.throws(() => readConnectionDiagnostic({ ...diagnostic, steps: [{}] }), /incomplete/);
assert.equal(readConnectionDiagnostic({ ...diagnostic, succeeded: false, steps: [{ layer: 'Egress', status: 'Denied', detail: 'Policy denied', remedy: null }] }).succeeded, false);
console.log('Gateway fleet, history, credential, revocation, wizard metadata, and diagnostic boundaries passed.');
