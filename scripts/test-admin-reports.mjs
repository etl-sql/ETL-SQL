import assert from 'node:assert/strict';
import { readAdminReportCatalog, readAdminReportIdentity, readAdminReportReceipt,
  readAdminReportValidation, readAdminReportScripts, matchesReportScript } from '../src/ETL-SQL.Portal/wwwroot/js/admin-reports-responses.js';

const report = { id: 40, folderId: 20, folderName: 'Reports', folderPath: '/Reports', version: 2,
  name: 'Finance', description: null, scriptPath: '/scripts/reports/finance.rptsql',
  createdAt: '2026-10-09T12:00:00Z', updatedAt: '2026-10-09T12:00:00Z' };
const body = { name: 'Updated', folderId: 20, description: 'Draft', scriptPath: 'reports/finance.rptsql' };
const updated = { ...report, ...body, scriptPath: report.scriptPath, version: 3 };
assert.equal(readAdminReportCatalog([report])[0].id, 40);
for (const bad of [null, {}, [report, report], [{ ...report, id: 0 }], [{ ...report, version: '2' }],
  [{ ...report, folderId: null }], [{ ...report, folderPath: 'Reports' }], [{ ...report, createdAt: 'invalid' }]])
  assert.throws(() => readAdminReportCatalog(bad));
assert.equal(readAdminReportIdentity(updated, 40).version, 3);
assert.throws(() => readAdminReportIdentity(updated, 41));
assert.equal(readAdminReportReceipt(updated, body, report).version, 3);
assert.equal(readAdminReportReceipt({ ...updated, version: 1 }, body, null).version, 1);
// A blank description can be populated from the script's own metadata by the server.
assert.equal(readAdminReportReceipt({ ...updated, description: 'Script description' }, { ...body, description: '' }, report).description, 'Script description');
for (const bad of [{ ...updated, id: 41 }, { ...updated, folderId: 21 }, { ...updated, name: 'Other' },
  { ...updated, scriptPath: '/scripts/other.rptsql' }, { ...updated, description: 'Other' }, { ...updated, version: 2 }])
  assert.throws(() => readAdminReportReceipt(bad, body, report));
assert.throws(() => readAdminReportReceipt(updated, body, null));
assert.ok(matchesReportScript('C:\\Scripts\\Reports\\Finance.rptsql', 'reports/finance.rptsql'));
assert.ok(matchesReportScript('/scripts/reports/finance.rptsql', './reports/finance.rptsql'));
assert.ok(matchesReportScript('/scripts/reports/finance.rptsql', '/scripts/reports/finance.rptsql'));
assert.equal(matchesReportScript('/scripts/Reports/finance.rptsql', 'reports/finance.rptsql'), false);
assert.equal(matchesReportScript('/scripts/other/reports/finance.rptsql', '/scripts/reports/finance.rptsql'), false);
assert.equal(matchesReportScript('/scripts/reports/finance.rptsql', 'ports/finance.rptsql'), false);

const validation = { isValid: true, scriptPath: report.scriptPath, hash: 'sha256:' + 'a'.repeat(64),
  lastModified: report.updatedAt, metadata: { Title: 'Finance' },
  parameters: [{ name: '@Region', type: 'STRING', required: true, default: null, description: null }], errors: [] };
assert.equal(readAdminReportValidation(validation, body.scriptPath).parameters[0].name, '@Region');
const invalid = { ...validation, isValid: false, hash: null, lastModified: null, errors: ['Invalid script.'] };
assert.equal(readAdminReportValidation(invalid, body.scriptPath).isValid, false);
for (const bad of [null, {}, { ...validation, scriptPath: '/scripts/other.rptsql' },
  { ...validation, hash: 'a'.repeat(64) }, { ...validation, lastModified: 42 },
  { ...validation, errors: ['Unexpected error'] }, { ...validation, metadata: { Title: 42 } },
  { ...validation, parameters: [{ name: '@Region' }] }, { ...validation, parameters: [...validation.parameters, ...validation.parameters] },
  { ...invalid, errors: [] }]) assert.throws(() => readAdminReportValidation(bad, body.scriptPath));
assert.deepEqual(readAdminReportScripts(['reports/finance.rptsql']), ['reports/finance.rptsql']);
for (const bad of [null, {}, [42], ['a.js'], ['a.rptsql', 'a.rptsql']]) assert.throws(() => readAdminReportScripts(bad));

const calls = [];
let reply = () => Response.json([report]);
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminReportsApi, AdminReportOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-reports-api.js');
await adminReportsApi.catalog();
reply = () => Response.json(report);
await adminReportsApi.get(40);
await assert.rejects(() => adminReportsApi.get(41), TypeError);
for (const mode of ['CatalogOnly', 'SourceControlled', 'Disabled']) {
  reply = () => Response.json({ mode, capabilities: [], sourceControlEnabled: false, draftRecovery: false });
  assert.equal((await adminReportsApi.session()).mode, mode);
}
reply = () => Response.json({ mode: 'Unexpected', capabilities: [], sourceControlEnabled: false, draftRecovery: false });
await assert.rejects(() => adminReportsApi.session(), TypeError);
reply = () => Response.json(validation);
assert.ok((await adminReportsApi.validate(body.scriptPath)).isValid);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { scriptPath: body.scriptPath });
reply = () => Response.json(invalid, { status: 400 });
assert.equal((await adminReportsApi.validate(body.scriptPath)).isValid, false);
for (const response of [() => Response.json(invalid), () => Response.json(validation, { status: 400 }),
  () => Response.json({ error: 'Unavailable' }, { status: 503 }), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminReportsApi.validate(body.scriptPath), error => !(error instanceof AdminReportOutcomeUnconfirmed));
}
reply = () => Response.json({ ...updated, version: 1 }, { status: 201 });
await adminReportsApi.create(body);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), body);
reply = () => Response.json(updated);
await adminReportsApi.update(report, body);
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
assert.deepEqual(JSON.parse(calls.at(-1).options.body), body);
reply = () => new Response(null, { status: 204 });
await adminReportsApi.delete(report);
assert.ok(String(calls.at(-1).url).endsWith('/api/reports/40?cascade=true'));
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
for (const response of [() => Response.json({ deleted: true }), () => new Response(null, { status: 202 }),
  () => new Response(null, { status: 500 }), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminReportsApi.delete(report), error => error instanceof AdminReportOutcomeUnconfirmed && error.reportId === 40);
}
reply = () => Response.json({ ...updated, id: 41 });
await assert.rejects(() => adminReportsApi.update(report, body), AdminReportOutcomeUnconfirmed);
reply = () => Response.json({ ...updated, version: 1 });
await assert.rejects(() => adminReportsApi.create(body), error => error instanceof AdminReportOutcomeUnconfirmed && error.reportId === null);
reply = () => Response.json({ error: 'Version conflict' }, { status: 409 });
await assert.rejects(() => adminReportsApi.update(report, body), error => !(error instanceof AdminReportOutcomeUnconfirmed) && error.status === 409);
reply = () => Response.json({ error: 'Report catalog unavailable' }, { status: 503 });
await assert.rejects(() => adminReportsApi.catalog(), error => !(error instanceof AdminReportOutcomeUnconfirmed) && error.status === 503);
console.log('admin report catalog, script validation, private versions, exact receipts, and unconfirmed-outcome checks passed');
