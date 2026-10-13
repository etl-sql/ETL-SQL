import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const files = name => pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js', `${name}.js`)).href;
const stored = new Map();
globalThis.sessionStorage = {
    getItem: key => stored.get(key) ?? null,
    setItem: (key, value) => stored.set(key, String(value)),
    removeItem: key => stored.delete(key),
};
let payload;
globalThis.fetch = async () => ({ ok: true, status: 200, json: async () => payload });
let destination;
globalThis.window = { fetch: globalThis.fetch, location: {
    origin: 'https://portal.example', href: 'https://portal.example/index.html', replace: value => { destination = value; },
} };
const { applyNavigation, applyNavigationSafely } = await import(files('portal-nav'));
const entry = () => ({ style: { display: 'none' }, dataset: {} });
const entries = new Map([['studioNav', entry()], ['adminNav', entry()], ['docsNav', entry()]]);
const attributes = new Map();
const doc = {
    getElementById: id => entries.get(id),
    body: { setAttribute: (key, value) => attributes.set(key, value) },
};
payload = [
    { id: 'studioNav', visible: true, reason: null },
    { id: 'adminNav', visible: false, reason: 'requires Admin' },
];
assert.deepEqual(await applyNavigation(doc), { studioNav: true, adminNav: false });
assert.equal(entries.get('studioNav').style.display, '');
assert.equal(entries.get('adminNav').style.display, 'none');
assert.equal(entries.get('adminNav').dataset.navHiddenReason, 'requires Admin');
assert.equal(entries.get('docsNav').style.display, 'none', 'Unmentioned destinations stay hidden.');
assert.equal(attributes.get('data-nav-applied'), 'true');
payload = [{ id: 'adminNav', visible: true }, { id: 'docsNav', visible: 'yes' }];
await assert.rejects(applyNavigation(doc), /Invalid Portal navigation/);
assert.equal(entries.get('adminNav').style.display, 'none', 'Validate the entire answer before applying any entry.');
assert.deepEqual(await applyNavigationSafely(doc), {});
payload = { destinations: [] };
assert.deepEqual(await applyNavigationSafely(doc), {});

let scenario = 0;
async function complete(data) {
    stored.clear();
    destination = null;
    globalThis.document = { getElementById: () => data === undefined ? null : { textContent: JSON.stringify(data) } };
    await import(`${files('sso-complete')}?scenario=${scenario++}`);
    return destination;
}
const session = { token: 'test-session', refreshToken: 'test-refresh' };
assert.equal(await complete({ ...session, redirect: '/index.html#reports' }), '/index.html#reports');
assert.equal(stored.get('etlsql_token'), session.token);
assert.equal(stored.get('etlsql_refresh'), session.refreshToken);
for (const redirect of ['//other.example/path', '/\\other.example/path', 'https://other.example', 'javascript:alert(1)', 12]) {
    assert.equal(await complete({ ...session, redirect }), '/index.html', 'SSO must remain on the Portal origin.');
}
for (const data of [undefined, null, {}, { token: 12 }, { token: '' }]) {
    assert.equal(await complete(data), '/login.html?error=sso_failed');
    assert.equal(stored.size, 0, 'Malformed data must not become a stored session.');
}
assert.equal(await complete({ token: session.token, refreshToken: 12 }), '/index.html');
assert.equal(stored.has('etlsql_refresh'), false, 'Only string refresh tokens enter storage.');
console.log('Portal shell: server navigation validation and same-origin SSO hand-off passed.');
