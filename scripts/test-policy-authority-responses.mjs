import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { makeFakeApi } from '../tools/ui-sandbox/stories/policy-authority-admin.story.js';
import { readPolicyAuthorityStatus, readPolicyValidation, readPolicyVersions, readPolicyMachines,
  readPolicyVersionReceipt, readPolicyMachineReceipt } from '../src/ETL-SQL.Portal/wwwroot/js/policy-authority-responses.js';
import { defaultPolicyExpiry, parsePolicyExpiry, escapePolicy, renderPolicyVersions } from '../src/ETL-SQL.Portal/wwwroot/js/policy-authority-view.js';

const api = makeFakeApi(), scope = { tenant: 'acme', environment: 'prod' };
assert.equal(readPolicyAuthorityStatus(await api.status()).configured, true);
assert.equal(readPolicyAuthorityStatus(await makeFakeApi({ configured: false }).status()).configured, false);
for (const bad of [null, {}, { configured: 'false' }, { configured: true }, { configured: false, error: 7 }]) {
  assert.throws(() => readPolicyAuthorityStatus(bad), /unavailable/);
}
assert.equal(readPolicyValidation(await api.validate('{"schemaVersion":"1.0"}')).isValid, true);
assert.equal(readPolicyValidation(await api.validate('{ bad')).isValid, false);
for (const bad of [{ isValid: true }, { isValid: true, errors: ['schema failure'] },
  { isValid: false, errors: [] }, { isValid: false, errors: [7] }]) assert.throws(() => readPolicyValidation(bad), /unavailable/);
const versions = readPolicyVersions(await api.versions(scope.tenant, scope.environment), scope);
assert.equal(versions.length, 4);
assert.deepEqual(readPolicyVersions([], scope), []);
for (const bad of [null, {}, [versions[0], versions[0]], [{ ...versions[0], tenant: 'other' }]]) assert.throws(() => readPolicyVersions(bad, scope), /unavailable/);
for (const change of [{ policyHash: '' }, { expiresAtUtc: 'invalid' }, { publishedAtUtc: null },
  { rolloutState: 'active' }, { author: null }, { canaryPercentage: 1.5 }, { canaryGroup: '', canaryPercentage: null }]) {
  assert.throws(() => readPolicyVersions([{ ...versions[0], ...change }], scope), /unavailable/);
}
const canary = versions.find(version => version.rolloutState === 'Canary');
assert.throws(() => readPolicyVersions([{ ...canary, canaryGroup: 'ring0' }], scope), /unavailable/);
assert.throws(() => readPolicyVersions([{ ...canary, canaryPercentage: null }], scope), /unavailable/);
const cohort = { ...canary, canaryGroup: '<img src=x onerror="attack()">', canaryPercentage: null };
assert.match(renderPolicyVersions(readPolicyVersions([cohort], scope)), /&lt;img/);
assert.equal(escapePolicy("<'&\""), '&lt;&#39;&amp;&quot;');
assert.equal(readPolicyVersionReceipt(versions[1], scope, 'Active', versions[1].policyVersion).policyHash, versions[1].policyHash);
assert.throws(() => readPolicyVersionReceipt(versions[1], scope, 'Staged'), /unconfirmed/);
assert.throws(() => readPolicyVersionReceipt(versions[1], scope, 'Active', 'different'), /unconfirmed/);
const machines = readPolicyMachines(await api.machines(scope.tenant, scope.environment), scope);
assert.equal(machines.length, 2);
assert.deepEqual(readPolicyMachines([], scope), []);
for (const bad of [null, [machines[0], machines[0]], [{ ...machines[0], machineId: 'bad' }],
  [{ ...machines[0], revoked: 'false' }], [{ ...machines[0], enrollmentId: null }],
  [{ ...machines[0], environment: 'staging' }], [{ ...machines[0], lastSeenAtUtc: 'invalid' }]]) {
  assert.throws(() => readPolicyMachines(bad, scope), /unavailable/);
}
assert.equal(readPolicyMachineReceipt(machines[0], scope, machines[0].machineId, false).requiresClientCertificate, true);
assert.throws(() => readPolicyMachineReceipt(machines[0], scope, machines[1].machineId, false), /unconfirmed/);
assert.throws(() => readPolicyMachineReceipt(machines[0], scope, machines[0].machineId, true), /unconfirmed/);
const reissued = await api.haltCanary(scope.tenant, scope.environment, canary.policyVersion);
assert.equal(readPolicyVersionReceipt(reissued, scope, 'Active').supersededVersion, versions[1].policyVersion);
assert.equal(reissued.policyHash, versions[1].policyHash);
const expiry = defaultPolicyExpiry(new Date('2026-10-07T17:30:00Z'));
assert.ok(Number.isFinite(Date.parse(parsePolicyExpiry(expiry, 0))));
assert.throws(() => parsePolicyExpiry('bad'), /future expiration/);
assert.throws(() => parsePolicyExpiry('2000-01-01T12:00'), /future expiration/);
// Chicago crosses DST during this interval. The displayed default must preserve local wall time.
const result = spawnSync(process.execPath, ['--input-type=module', '-e', `
  import assert from 'node:assert/strict';
  import { defaultPolicyExpiry } from './src/ETL-SQL.Portal/wwwroot/js/policy-authority-view.js';
  const before = new Date('2026-10-07T17:30:00Z');
  const after = new Date(defaultPolicyExpiry(before));
  assert.equal(after.getHours(), before.getHours()); assert.equal(after.getMinutes(), before.getMinutes());
  assert.equal(after.getDate(), 6); assert.equal(after.getMonth(), 10);
`], { cwd: process.cwd(), env: { ...process.env, TZ: 'America/Chicago' }, encoding: 'utf8' });
assert.equal(result.status, 0, result.stderr);
console.log('Policy status, validation, scope, version/machine receipts, escaped cohorts, and local expiry boundaries passed.');
