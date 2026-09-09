import assert from 'node:assert/strict';
import { detectPlaintextSecrets, secureStudioScriptForSave } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-security.js';

for (const value of [undefined, null, '', 42, {}]) {
    assert.deepEqual(detectPlaintextSecrets(value), []);
}
const script = `PASSWORD = 'first-fixture', api_key = "second-fixture", TOKEN = 'first-fixture'`;
const findings = detectPlaintextSecrets(script);
assert.equal(findings.length, 3);
assert.deepEqual(findings.map(finding => script.slice(finding.start, finding.end)),
    ['first-fixture', 'second-fixture', 'first-fixture']);
assert.deepEqual(findings.map(finding => finding.label),
    ['Plaintext Password', 'Plaintext Secret / API Key', 'Plaintext Secret / API Key']);
assert.deepEqual(detectPlaintextSecrets(`PASSWORD = 'ENC:fixture', TOKEN = 'SECRET:fixture', API_KEY = 'SHARED:fixture'`), []);

const calls = [];
const secured = await secureStudioScriptForSave(script, 'fixture-passphrase', async (value, passphrase) => {
    calls.push({ value, passphrase });
    return `ENC:replacement-${calls.length}`;
});
assert.equal(secured, `PASSWORD = 'ENC:replacement-3', api_key = "ENC:replacement-2", TOKEN = 'ENC:replacement-1'`);
assert.deepEqual(calls.map(call => call.value), ['first-fixture', 'second-fixture', 'first-fixture']);
assert.ok(calls.every(call => call.passphrase === 'fixture-passphrase'));
assert.equal(await secureStudioScriptForSave('unchanged', 'fixture', async () => assert.fail('Unexpected encryption')), 'unchanged');

for (const passphrase of [undefined, null, '', ' \t ']) {
    await assert.rejects(secureStudioScriptForSave(script, passphrase, async () => assert.fail('Unexpected encryption')),
        /A passphrase is required/);
}
for (const result of [undefined, null, '', 'plaintext']) {
    await assert.rejects(secureStudioScriptForSave(script, 'fixture', async () => result),
        /Credential encryption is unavailable/);
}
let attempted = 0;
await assert.rejects(secureStudioScriptForSave(script, 'fixture', async () => {
    if (++attempted === 2) throw new Error('Fixture encryption failure');
    return 'ENC:fixture';
}), /Fixture encryption failure/);
assert.equal(attempted, 2);
assert.equal(script, `PASSWORD = 'first-fixture', api_key = "second-fixture", TOKEN = 'first-fixture'`);
console.log('Studio security: detection, replacement offsets, passphrase and encryption failures passed.');
