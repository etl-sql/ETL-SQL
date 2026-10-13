import assert from 'node:assert/strict';
import { readLoginResponse, readAuthProvidersResponse, requiresPasswordChange } from '../../src/ETL-SQL.Portal/wwwroot/js/auth-responses.js';
import { readDocumentationSearch, readDocumentationDocument } from '../../src/ETL-SQL.Portal/wwwroot/js/documentation-responses.js';
import { readSecretSummaries, readSecretImpact, readSecretKeyRingCheck, secretVerificationError } from '../../src/ETL-SQL.Portal/wwwroot/js/secret-responses.js';

const login = { token: 'test-session', refreshToken: 'test-refresh', expiresAt: '2026-01-01T00:00:00Z' };
assert.deepEqual(readLoginResponse(login), login);
for (const value of [null, [], {}, { ...login, token: 12 }, { ...login, token: '' },
    { ...login, refreshToken: {} }, { ...login, refreshToken: '' }, { ...login, expiresAt: 'invalid' }]) {
    assert.throws(() => readLoginResponse(value), /Invalid Portal sign-in/);
}
const providers = { local: true, oidcEnabled: false, oidcLoginUrl: null };
assert.deepEqual(readAuthProvidersResponse(providers), providers);
assert.throws(() => readAuthProvidersResponse({ ...providers, oidcEnabled: 'true' }), /identity-provider/);
const jwt = claims => `header.${Buffer.from(JSON.stringify(claims)).toString('base64url')}.signature`;
assert.equal(requiresPasswordChange(jwt({ mustChangePassword: 'true' })), true);
assert.equal(requiresPasswordChange(jwt({ mustChangePassword: 'false' })), false);
assert.equal(requiresPasswordChange(jwt({})), false);
assert.throws(() => requiresPasswordChange('malformed'), /session token has no payload/);

const results = [{ path: 'select.md', title: 'SELECT', section: 'Reference', snippet: 'Read rows.', score: 1 }];
assert.equal(readDocumentationSearch(results), results);
assert.deepEqual(readDocumentationSearch([]), []);
for (const value of [null, {}, [results[0], { ...results[0], path: 42 }], [{ ...results[0], score: '1' }]]) {
    assert.throws(() => readDocumentationSearch(value), /documentation search/);
}
const document = { path: 'select.md', title: 'SELECT', markdown: '# SELECT\nRead rows.' };
assert.equal(readDocumentationDocument(document), document);
assert.throws(() => readDocumentationDocument({ ...document, markdown: {} }), /documentation document/);
const secret = { name: 'sandbox_password', disabled: false, createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: '2026-10-07T00:00:00Z', version: 1 };
assert.deepEqual(readSecretSummaries([secret]), [secret]);
assert.deepEqual(readSecretSummaries([]), []);
for (const value of [null, {}, [null], [{ ...secret, disabled: 'false' }], [{ ...secret, createdAtUtc: 'invalid' }],
    [{ ...secret, version: 0 }], [{ ...secret, version: 1.5 }], [{ ...secret, name: {} }]]) {
    assert.throws(() => readSecretSummaries(value), /secret-store list/);
}
const impact = { reference: 'SECRET:sandbox_password', consumerCount: 1,
    consumers: [{ type: 'Report', name: 'Sales', detail: null, lastUsedAtUtc: null }] };
assert.deepEqual(readSecretImpact(impact), impact);
for (const value of [null, { ...impact, consumerCount: 2 }, { ...impact, consumers: {} },
    { ...impact, consumers: [{ ...impact.consumers[0], lastUsedAtUtc: 'invalid' }] }]) {
    assert.throws(() => readSecretImpact(value), /secret-store impact/);
}
assert.deepEqual(readSecretKeyRingCheck({ secretCount: 0, failedCount: 0, firstFailedName: null }),
    { secretCount: 0, failedCount: 0, firstFailedName: null });
assert.equal(readSecretKeyRingCheck({ secretCount: 2, failedCount: 1, firstFailedName: 'sandbox_password' }).failedCount, 1);
for (const value of [null, { secretCount: 2, failedCount: '0' }, { secretCount: 1, failedCount: 2 },
    { secretCount: 1, failedCount: 1, firstFailedName: null }, { secretCount: -1, failedCount: 0 }]) {
    assert.throws(() => readSecretKeyRingCheck(value), /secret-store verification/);
}
assert.equal(secretVerificationError(Object.assign(new Error('missing'), { status: 404 })), 'missing');
assert.equal(secretVerificationError({ status: 409, body: { status: 'disabled' } }), 'disabled');
assert.equal(secretVerificationError({ status: 409, payload: { status: 'undecryptable' } }), 'undecryptable');
assert.equal(secretVerificationError({ body: { status: {} } }), 'error');
assert.equal(secretVerificationError('connection failed'), 'error');
console.log('Portal responses: sign-in, identity providers, token password-change claim, documentation, and secret-store validation passed.');
