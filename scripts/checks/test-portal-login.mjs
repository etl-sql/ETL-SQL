import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const storage = new Map();
globalThis.sessionStorage = {
    getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, String(value)),
    removeItem: key => storage.delete(key),
};
const jwt = claims => `header.${Buffer.from(JSON.stringify(claims)).toString('base64url')}.signature`;
const login = mustChangePassword => ({ token: jwt({ mustChangePassword: String(mustChangePassword), name: 'test-user' }),
    refreshToken: 'test-refresh', expiresAt: '2026-01-01T00:00:00Z' });
let loginResponse = login(true);
let loginStatus = 200;
const requests = [];
globalThis.fetch = async (url, options = {}) => {
    requests.push({ url, options });
    const status = url === '/api/auth/login' ? loginStatus : 200;
    return { ok: status === 200, status, statusText: 'Error', json: async () => {
        if (url === '/api/auth/login') return loginResponse;
        if (url === '/api/auth/providers') return { local: true, oidcEnabled: true, oidcLoginUrl: '/api/auth/oidc/login' };
        return {};
    } };
};
globalThis.window = { fetch: globalThis.fetch, location: { search: '', href: 'https://portal.example/login.html', origin: 'https://portal.example' } };
function element() {
    const classes = new Set();
    const listeners = new Map();
    return { style: {}, value: '', disabled: false, textContent: '', listeners, attributes: new Map(),
        classList: { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value) },
        addEventListener: (name, callback) => listeners.set(name, callback),
        setAttribute(key, value) { this.attributes.set(key, value); }, focus() {},
    };
}
const ids = ['username', 'password', 'newPwd', 'confirmPwd', 'currentPwd', 'loginBtn', 'changeBtn', 'loginForm',
    'changeForm', 'mustChangeBanner', 'errorMsg', 'ssoSection', 'ssoBtn'];
const nodes = new Map(ids.map(id => [id, element()]));
globalThis.document = { title: 'ETL-SQL Portal', getElementById: id => nodes.get(id),
    querySelector: selector => nodes.get(selector.slice(1)), querySelectorAll: () => [] };
await import(pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/pages/login.js')).href);
await new Promise(resolve => setImmediate(resolve));
assert.equal(nodes.get('ssoBtn').attributes.get('href'), '/api/auth/oidc/login');
const click = id => nodes.get(id).listeners.get('click')();
await click('loginBtn');
assert.equal(nodes.get('errorMsg').textContent, 'Username and password are required.');
nodes.get('username').value = 'test-user';
nodes.get('password').value = 'test-password';
await click('loginBtn');
assert.equal(nodes.get('loginForm').style.display, 'none', 'A token requiring a password change must open the change form.');
assert.equal(nodes.get('changeForm').style.display, '');
assert.equal(nodes.get('loginBtn').disabled, false);
assert.equal(window.location.href, 'https://portal.example/login.html', 'A forced password change must not navigate into the app.');

nodes.get('newPwd').value = 'test-new-password';
nodes.get('confirmPwd').value = 'test-new-password';
loginResponse = login(false);
await click('changeBtn');
assert.equal(window.location.href, '/index.html');
assert.equal(storage.get('etlsql_token'), loginResponse.token, 'Password changes exchange the invalidated session for a new one.');
assert.ok(requests.some(request => request.url === '/api/auth/change-password'));

storage.clear();
window.location.href = 'https://portal.example/login.html';
loginResponse = { token: 12 };
await click('loginBtn');
assert.equal(storage.size, 0, 'Malformed login responses must not create a session.');
assert.equal(nodes.get('errorMsg').textContent, 'Invalid Portal sign-in response.');
assert.equal(window.location.href, 'https://portal.example/login.html');
loginStatus = 429;
loginResponse = { error: 'Account locked' };
await click('loginBtn');
assert.equal(nodes.get('errorMsg').textContent, 'Account locked. Try again in 15 minutes.');
console.log('Portal sign-in: required inputs, provider link, forced password change, fresh session, malformed response, and lockout passed.');
