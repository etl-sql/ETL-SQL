import assert from 'node:assert/strict';
import { readOrchestratorSettings, readPortalBranding, readOrchestratorConnectionStatus } from '../src/ETL-SQL.Portal/wwwroot/js/admin-settings-responses.js';

const settings = { apiUrl: null, hasApiKey: true };
const branding = { displayName: null, footerText: 'Footer', logoUrl: '/logo.svg' };
assert.deepEqual(readOrchestratorSettings(settings), settings);
assert.deepEqual(readPortalBranding(branding), branding);
for (const invalid of [null, {}, { hasApiKey: true }, { apiUrl: null, hasApiKey: 'true' }, { apiUrl: 12, hasApiKey: false }])
  assert.throws(() => readOrchestratorSettings(invalid));
for (const invalid of [null, {}, { ...branding, logoUrl: undefined }, { ...branding, displayName: {} }, { ...branding, footerText: false }])
  assert.throws(() => readPortalBranding(invalid));
const online = { online: true, status: { status: 'Running', uptimeSeconds: 12, processId: 1, startedAt: '2026-10-09T12:00:00Z', version: '0.21.0' } };
for (const valid of [online, { online: false, status: 'Offline' }, { online: true, status: null }])
  assert.deepEqual(readOrchestratorConnectionStatus(valid), valid);
for (const invalid of [null, {}, { online: false }, { online: false, status: {} }, { online: 'true', status: null },
  { ...online, status: {} }, { ...online, status: { ...online.status, processId: -1 } },
  { ...online, status: { ...online.status, uptimeSeconds: Infinity } }, { ...online, status: { ...online.status, startedAt: 'invalid' } }])
  assert.throws(() => readOrchestratorConnectionStatus(invalid));

const calls = [];
let reply = () => Response.json(settings);
globalThis.sessionStorage = { getItem: () => null, removeItem() {}, setItem() {} };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminSettingsApi, AdminSettingsOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-settings-api.js');
const signal = new AbortController();
assert.deepEqual(await adminSettingsApi.orchestrator(signal.signal), settings);
assert.equal(calls.at(-1).options.signal, signal.signal);
for (const apiKey of [null, '', 'replacement-fixture-key']) {
  reply = () => new Response(null, { status: 204 });
  await adminSettingsApi.saveOrchestrator({ apiUrl: null, apiKey });
  assert.equal(calls.at(-1).url, '/api/admin/settings/orchestrator');
  assert.deepEqual(JSON.parse(calls.at(-1).options.body), { apiUrl: null, apiKey });
}
await adminSettingsApi.saveBranding(branding);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), branding);
for (const result of [() => Response.json({}, { status: 200 }), () => new Response(null, { status: 202 }),
  () => Response.json({ error: 'Unavailable' }, { status: 503 }), () => { throw new Error('Connection lost'); }]) {
  reply = result;
  await assert.rejects(() => adminSettingsApi.saveOrchestrator({ apiUrl: null, apiKey: null }), AdminSettingsOutcomeUnconfirmed);
  await assert.rejects(() => adminSettingsApi.saveBranding(branding), AdminSettingsOutcomeUnconfirmed);
}
reply = () => Response.json({ error: 'Rejected' }, { status: 400 });
await assert.rejects(() => adminSettingsApi.saveBranding(branding), error => !(error instanceof AdminSettingsOutcomeUnconfirmed));
reply = () => Response.json({});
await assert.rejects(() => adminSettingsApi.status());
reply = () => Response.json({ online: false, status: 'Offline' }, { status: 403 });
await assert.rejects(() => adminSettingsApi.status());
reply = () => Response.json(branding, { status: 201 });
await assert.rejects(() => adminSettingsApi.branding());
signal.abort(); const before = calls.length;
await assert.rejects(() => adminSettingsApi.orchestrator(signal.signal));
assert.equal(calls.length, before);
console.log('admin settings: complete metadata/status contracts, key preservation/clearing, exact receipts, rejected and unconfirmed writes, and read cancellation passed');
