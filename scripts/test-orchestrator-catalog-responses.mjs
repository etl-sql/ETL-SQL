import assert from 'node:assert/strict';
import { readSchedules, readNotifications, readCatalogJson, readDispatchOutcome }
  from '../src/ETL-SQL.Portal/wwwroot/js/orchestrator-catalog-responses.js';

const schedule = { id: 's-1', name: 'nightly_utc', cron: '0 2 * * *', timeZone: 'UTC', isEnabled: true,
  displayName: 'Nightly', description: null, createdBy: 'user:ops', version: 2, tenantId: 'portal-host', options: null };
const notification = { id: 'n-1', name: 'ops_mail', connectionName: 'smtp_ops', recipient: null, isEnabled: false,
  displayName: null, description: null, createdBy: null, version: 1 };

assert.deepEqual(readSchedules([]), []);
assert.deepEqual(readSchedules([schedule]), [{ name: 'nightly_utc', cron: '0 2 * * *', timeZone: 'UTC', isEnabled: true,
  displayName: 'Nightly', description: null, createdBy: 'user:ops', version: 2 }]);
for (const invalid of [null, {}, 'none', [null], [{ ...schedule, name: '' }], [{ ...schedule, cron: '' }],
  [{ ...schedule, timeZone: null }], [{ ...schedule, isEnabled: 'true' }], [{ ...schedule, version: 0 }],
  [{ ...schedule, version: '2' }], [{ ...schedule, displayName: 7 }], [schedule, { ...schedule, name: 'NIGHTLY_UTC' }]])
  assert.throws(() => readSchedules(invalid), /Invalid schedule catalog response/);

assert.equal(readNotifications([notification])[0].connectionName, 'smtp_ops');
assert.equal('id' in readNotifications([notification])[0], false);
for (const invalid of [null, [{}], [{ ...notification, connectionName: '' }], [{ ...notification, recipient: 5 }],
  [{ ...notification, version: 1.5 }], [notification, notification]])
  assert.throws(() => readNotifications(invalid), /Invalid notification catalog response/);

const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
assert.deepEqual(await readCatalogJson(json([schedule])), [schedule]);
await assert.rejects(readCatalogJson(json({ error: 'Schedules are temporarily unavailable. Please retry.' }, 503)),
  /Schedules are temporarily unavailable/);
await assert.rejects(readCatalogJson(json(JSON.stringify({ Error: 'Relayed refusal.' }), 403)), /Relayed refusal/);
await assert.rejects(readCatalogJson(new Response('<html>', { status: 502 })), /did not answer \(HTTP 502\)/);
// A 204 or 206 carries no catalog, so it cannot be read as an empty one.
await assert.rejects(readCatalogJson(new Response(null, { status: 204 })), /did not answer \(HTTP 204\)/);

const result = { notificationName: 'ops_mail', delivered: true, skipped: false, message: null };
assert.deepEqual(await readDispatchOutcome(json(result)), { delivered: true, skipped: false, detail: '' });
assert.deepEqual(await readDispatchOutcome(json({ ...result, delivered: false, skipped: true, message: ' Endpoint disabled. ' }, 202)),
  { delivered: false, skipped: true, detail: 'Endpoint disabled.' });
assert.deepEqual(await readDispatchOutcome(json({ ...result, delivered: false, message: 'Relay refused.' }, 502)),
  { delivered: false, skipped: false, detail: 'Relay refused.' });
// A status that disagrees with its body is not a confirmed delivery.
await assert.rejects(readDispatchOutcome(json({ ...result, delivered: false, skipped: true }, 200)), /not confirmed/);
await assert.rejects(readDispatchOutcome(json(result, 202)), /not confirmed/);
await assert.rejects(readDispatchOutcome(json({ ...result, skipped: true })), /not confirmed/);
await assert.rejects(readDispatchOutcome(new Response(null, { status: 200 })), /not confirmed/);
await assert.rejects(readDispatchOutcome(json({ ...result, message: 4 })), /not confirmed/);
await assert.rejects(readDispatchOutcome(json({ error: 'Notification was not found.' }, 404)), /Notification was not found/);
await assert.rejects(readDispatchOutcome(new Response(null, { status: 403 })), /Unexpected response \(403\)/);

console.log('orchestrator catalog response checks passed');
