import assert from 'node:assert/strict';
import { connectionFixtures, makeFakeApi } from '../../tools/ui-sandbox/stories/connections-admin.story.js';
import { readConnectionSummaries, readConnectionDetail, readConnectionExports, readConnectionVerification,
  readSharedConnectionDiagnostic, readConnectionImport, readConnectionImpact, readConnectionHelp,
  readParsedConnection, parseConnectionOptions } from '../../src/ETL-SQL.Portal/wwwroot/js/connection-responses.js';
import { renderConnectionTable, renderConnectionDetail, renderSharedConnectionDiagnostic } from '../../src/ETL-SQL.Portal/wwwroot/js/connections-view.js';

const api = makeFakeApi(connectionFixtures);
const rows = readConnectionSummaries(await api.list());
assert.equal(rows.length, 4);
assert.deepEqual(readConnectionSummaries([]), []);
for (const invalid of [null, {}, [rows[0], rows[0]], [{ ...rows[0], disabled: 'false' }],
  [{ ...rows[0], version: 0 }], [{ ...rows[0], updatedAtUtc: 'invalid' }], [{ ...rows[0], gateway: {} }]]) {
  assert.throws(() => readConnectionSummaries(invalid), /unavailable or incomplete/);
}
const gateway = readConnectionDetail(await api.detail('gateway_sales'), 'gateway_sales');
assert.equal(gateway.gateway.resourceId, 'sales_read');
assert.match(renderConnectionDetail(gateway), /Gateway Binding/);
assert.throws(() => readConnectionDetail({ ...gateway, gateway: null }, 'gateway_sales'), /unavailable/);
assert.throws(() => readConnectionDetail(gateway, 'other'), /unavailable/);
for (const change of [{ options: null }, { sensitiveFields: undefined }, { options: { SERVER: 3 } }]) {
  assert.throws(() => readConnectionDetail({ ...gateway, ...change }, 'gateway_sales'), /unavailable/);
}
const masked = readConnectionDetail(await api.detail('archive_s3'), 'archive_s3');
assert.equal(masked.options.BUCKET, '********');
const exports = readConnectionExports(await api.exportAll());
assert.equal(exports.find(entry => entry.alias === 'archive_s3').options.BUCKET, 'archive-bucket');
assert.equal(exports[0].gateway.resourceId, 'sales_read');
assert.throws(() => readConnectionExports([{ alias: 'partial', connectorType: 'MSSQL' }]), /complete/);
assert.throws(() => readConnectionExports([exports[0], exports[0]]), /unique/);
assert.equal(readConnectionVerification(await api.verify('sales_dw'), 'sales_dw').secretReferences, 1);
for (const invalid of [{ alias: 'other', status: 'ok', secretReferences: 2 }, { alias: 'sales_dw', status: 'ok', secretReferences: '2' }, {}]) {
  assert.throws(() => readConnectionVerification(invalid, 'sales_dw'), /unavailable/);
}
const diagnostic = readSharedConnectionDiagnostic(await api.test('sales_dw'), 'sales_dw');
assert.match(renderSharedConnectionDiagnostic(diagnostic), /SKIPPED/);
for (const change of [{ steps: undefined }, { steps: [] }, { succeeded: false }, { alias: 'other' },
  { steps: [{ layer: 'DNS', status: 'Ok', detail: 'Wrong casing', remedy: null }] },
  { steps: [{ layer: 'TCP', status: 'failed', detail: 'Failed', remedy: null }] }]) {
  assert.throws(() => readSharedConnectionDiagnostic({ ...diagnostic, ...change }, 'sales_dw'), /unavailable/);
}
assert.equal(readSharedConnectionDiagnostic({ ...diagnostic, succeeded: false,
  steps: [{ layer: 'POLICY', status: 'denied', detail: 'Denied', remedy: null }] }, 'sales_dw').succeeded, false);
assert.equal(readConnectionImpact(await api.impact('sales_dw'), 'sales_dw').consumerCount, 3);
assert.throws(() => readConnectionImpact({ reference: 'SHARED:other', consumerCount: 0, consumers: [] }, 'sales_dw'), /unavailable/);
assert.throws(() => readConnectionImpact({ reference: 'SHARED:sales_dw', consumerCount: 1, consumers: [] }, 'sales_dw'), /unavailable/);
assert.equal(readConnectionImport({ created: 1, updated: 3 }, 4).updated, 3);
for (const invalid of [{ created: 0, updated: 0 }, { created: '4', updated: 0 }, {}]) {
  assert.throws(() => readConnectionImport(invalid, 4), /unconfirmed/);
}
assert.equal(readConnectionHelp(await api.getHelp('MSSQL')).content.startsWith('# MSSQL'), true);
assert.throws(() => readConnectionHelp({ content: null }), /unavailable/);
assert.deepEqual(readParsedConnection({ detectedProvider: 'MSSQL', options: { SERVER: 'sql01' },
  extractedCredential: null, suggestedSecretKey: null }).options, { SERVER: 'sql01' });
assert.throws(() => readParsedConnection({ options: null }), /unavailable/);
assert.deepEqual({ ...parseConnectionOptions('server=sql01\nPASSWORD=reference=with=equals') },
  { SERVER: 'sql01', PASSWORD: 'reference=with=equals' });
assert.throws(() => parseConnectionOptions('SERVER=one\nserver=two'), /repeats/);
assert.throws(() => parseConnectionOptions('__gateway_id=one'), /Wizard/);
assert.throws(() => parseConnectionOptions('private-fixture-token'), error => error.message === 'Option line 1 must use KEY=VALUE.');
assert.doesNotMatch(renderConnectionTable([{ ...rows[0], alias: '<img onerror="fixture">' }]), /<img/);
const imported = { ...exports[0], alias: 'disabled_gateway_copy', disabled: true };
readConnectionImport(await api.importAll([imported]), 1);
assert.equal(readConnectionDetail(await api.detail(imported.alias), imported.alias).summary.disabled, true);
assert.equal((await api.exportAll()).find(entry => entry.alias === imported.alias).gateway.gatewayId, 'hq-gateway');
console.log('Connection catalog, gateway preservation, masking, diagnostics, impact, import/export, and option boundaries passed.');
