import assert from 'node:assert/strict';
import { setImmediate } from 'node:timers/promises';

const saved = Object.fromEntries(['window', 'sessionStorage', 'fetch'].map(key => [key,
  Object.getOwnPropertyDescriptor(globalThis, key)]));
const storage = new Map();
let handler;
let href = 'https://portal.test/index.html';
const redirects = [];
const location = { origin: 'https://portal.test', get href() { return href; },
  set href(value) { href = new URL(value, href).href; redirects.push(value); } };
globalThis.window = { location, fetch: (...args) => handler(...args) };
globalThis.sessionStorage = { getItem: key => storage.get(key) ?? null,
  setItem: (key, value) => storage.set(key, String(value)), removeItem: key => storage.delete(key) };
globalThis.fetch = (...args) => window.fetch(...args);

const json = (value, status = 200) => new Response(JSON.stringify(value), { status,
  headers: { 'Content-Type': 'application/json' } });
const headers = (input, init) => new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
const login = { token: 'fixture-new-access', refreshToken: 'fixture-new-refresh', expiresAt: '2035-01-01T00:00:00Z' };
function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

const failures = [];
try {
  const { auth, apiFetch, authApi, catalogApi } = await import('../src/ETL-SQL.Portal/wwwroot/js/api.js');
  async function check(name, run) {
    auth.clear();
    href = 'https://portal.test/index.html';
    redirects.length = 0;
    handler = () => { throw new Error('Unexpected request.'); };
    try { await run(); console.log(`PASS ${name}`); }
    catch (error) { failures.push(name); console.error(`FAIL ${name}: ${error.message}`); }
  }

  await check('Headers and tuple headers survive authenticated requests', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    handler = async (input, init) => {
      assert.equal(headers(input, init).get('If-Match'), '"7"');
      assert.equal(headers(input, init).get('Authorization'), 'Bearer fixture-access');
      return json({});
    };
    await apiFetch('/api/example', { headers: new Headers({ 'If-Match': '"7"' }) });
    await apiFetch('/api/example', { headers: [['If-Match', '"7"']] });
  });

  await check('global interceptor preserves Request headers and explicit credentials', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    handler = async (input, init) => {
      assert.equal(headers(input, init).get('Authorization'), 'Bearer fixture-explicit');
      assert.equal(headers(input, init).get('If-Match'), '"3"');
      return json({});
    };
    await window.fetch(new Request('https://portal.test/api/example', { headers:
      { Authorization: 'Bearer fixture-explicit', 'If-Match': '"3"' } }));
  });

  await check('invalid global-fetch URLs reject through the fetch promise', async () => {
    await assert.rejects(window.fetch('http://['), TypeError);
  });

  await check('session credentials stay within same-origin API paths', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    handler = async (input, init) => {
      assert.equal(headers(input, init).has('Authorization'), false);
      return json({});
    };
    for (const url of ['https://other.test/api/example', '//other.test/api/example', '/api/../outside']) {
      await window.fetch(url);
      await apiFetch(url);
    }
  });

  await check('JSON objects serialize and native request bodies remain native', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const bodies = [new Blob(['fixture']), new URLSearchParams({ fixture: 'value' }),
      new FormData(), new Uint8Array([1, 2, 3])];
    for (const body of bodies) {
      handler = async (_input, init) => { assert.equal(init.body, body); return json({}); };
      await apiFetch('/api/example', { method: 'POST', body });
    }
    handler = async (input, init) => {
      assert.equal(init.body, '{"fixture":true}');
      assert.equal(headers(input, init).get('Content-Type'), 'application/json');
      return json({});
    };
    await apiFetch('/api/example', { method: 'POST', body: { fixture: true } });
  });

  await check('concurrent 401 responses share one refresh and preserve retry headers', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const gate = deferred();
    let refreshes = 0;
    handler = async (input, init) => {
      if (input === '/api/auth/refresh') { refreshes++; return gate.promise; }
      assert.equal(headers(input, init).get('If-Match'), '"7"');
      return headers(input, init).get('Authorization') === `Bearer ${login.token}` ? json({}) : json({}, 401);
    };
    const requests = [apiFetch('/api/example', { headers: new Headers({ 'If-Match': '"7"' }) }),
      apiFetch('/api/example', { headers: [['If-Match', '"7"']] })];
    const results = Promise.allSettled(requests);
    await setImmediate();
    gate.resolve(json(login));
    const settled = await results;
    for (const result of settled) if (result.status === 'rejected') throw result.reason;
    assert.deepEqual(settled.map(result => result.value.status), [200, 200]);
    assert.equal(refreshes, 1);
  });

  await check('a late old-token 401 reuses the already refreshed session', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const oldResponse = deferred();
    let refreshes = 0;
    handler = async (input, init) => {
      if (input === '/api/auth/refresh') { refreshes++; return json(login); }
      if (headers(input, init).get('Authorization') === `Bearer ${login.token}`) return json({});
      return input === '/api/late' ? oldResponse.promise : json({}, 401);
    };
    const late = apiFetch('/api/late');
    assert.equal((await apiFetch('/api/first')).status, 200);
    oldResponse.resolve(json({}, 401));
    assert.equal((await late).status, 200);
    assert.equal(refreshes, 1);
  });

  await check('malformed refresh data cannot become stored credentials', async () => {
    for (const payload of [null, {}, { ...login, token: '' }, { ...login, refreshToken: '' },
      { ...login, expiresAt: 'invalid' }]) {
      auth.setTokens('fixture-access', 'fixture-refresh');
      handler = async input => input === '/api/auth/refresh' ? json(payload) : json({}, 401);
      assert.equal((await apiFetch('/api/example')).status, 401);
      assert.equal(auth.getToken(), null);
      assert.equal(auth.getRefreshToken(), null);
    }
  });

  for (const replace of [false, true]) {
    await check(`late refresh cannot ${replace ? 'replace a new identity' : 'undo sign-out'}`, async () => {
      auth.setTokens('fixture-access', 'fixture-refresh');
      const gate = deferred();
      handler = async input => input === '/api/auth/refresh' ? gate.promise : json({}, 401);
      const request = apiFetch('/api/example');
      await setImmediate();
      if (replace) auth.setTokens('fixture-other-access', 'fixture-other-refresh');
      else auth.clear();
      gate.resolve(json(login));
      await assert.rejects(request, /session changed/i);
      assert.equal(auth.getToken(), replace ? 'fixture-other-access' : null);
      assert.equal(redirects.length, 0);
    });
  }

  for (const requestSignal of [false, true]) {
  await check(`an aborted ${requestSignal ? 'Request' : 'init'} refresh waiter leaves the shared refresh usable`, async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const gate = deferred();
    handler = async (input, init) => input === '/api/auth/refresh' ? gate.promise
      : headers(input, init).get('Authorization') === `Bearer ${login.token}` ? json({}) : json({}, 401);
    const controller = new AbortController();
    const cancelled = (requestSignal
      ? apiFetch(new Request('https://portal.test/api/cancelled', { signal: controller.signal }))
      : apiFetch('/api/cancelled', { signal: controller.signal })).then(() => 'resolved', error => error.name);
    const active = apiFetch('/api/active');
    await setImmediate();
    controller.abort();
    let observed;
    try { observed = await Promise.race([cancelled, setImmediate().then(() => 'pending')]); }
    finally { gate.resolve(json(login)); }
    await active;
    await cancelled;
    assert.equal(observed, 'AbortError');
    assert.equal(auth.getToken(), login.token);
  });
  }

  await check('new access credentials cannot inherit the previous refresh token', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    auth.setTokens('fixture-other-access');
    assert.equal(auth.getRefreshToken(), null);
    assert.throws(() => auth.setTokens(undefined, 'fixture-refresh'), /nonempty strings/);
    assert.equal(auth.getToken(), 'fixture-other-access');
  });

  await check('expired-session logout clears credentials before revoking with private credentials', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const gate = deferred();
    let refreshes = 0;
    handler = async (input, init) => {
      if (input === '/api/auth/refresh') { refreshes++; return json(login); }
      assert.equal(input, '/api/auth/logout');
      if (headers(input, init).get('Authorization') === `Bearer ${login.token}`) return new Response(null, { status: 204 });
      return gate.promise;
    };
    const request = authApi.logout();
    const tokenBeforeResponse = auth.getToken();
    gate.resolve(json({}, 401));
    await request;
    assert.equal(tokenBeforeResponse, null);
    assert.equal(refreshes, 1);
    assert.equal(auth.getToken(), null);
    assert.equal(auth.getRefreshToken(), null);
  });

  await check('logout shares a pending refresh without restoring the old identity', async () => {
    auth.setTokens('fixture-access', 'fixture-refresh');
    const gate = deferred();
    let refreshes = 0, revoked = false;
    handler = async (input, init) => {
      if (input === '/api/auth/refresh') { refreshes++; return gate.promise; }
      if (input === '/api/auth/logout' && headers(input, init).get('Authorization') === `Bearer ${login.token}`) {
        revoked = true;
        return new Response(null, { status: 204 });
      }
      return json({}, 401);
    };
    const pending = apiFetch('/api/example').then(() => 'resolved', error => error.message);
    await setImmediate();
    const logout = authApi.logout();
    await setImmediate();
    gate.resolve(json(login));
    await logout;
    assert.match(await pending, /session changed/i);
    assert.equal(refreshes, 1);
    assert.equal(revoked, true);
    assert.equal(auth.getToken(), null);
  });

  await check('error payloads preserve HTTP status and allow only the password-change redirect', async () => {
    for (const body of [null, [], { error: {} }, { redirect: 'https://other.test/collect' }]) {
      handler = async () => json(body, 403);
      await assert.rejects(catalogApi.search('fixture'), error => error.status === 403 && error instanceof Error);
      assert.equal(redirects.length, 0);
    }
    handler = async () => json({ error: 'Password change required', redirect: '/login.html?changePassword=true' }, 403);
    await assert.rejects(catalogApi.search('fixture'), error => error.status === 403);
    assert.equal(href, 'https://portal.test/login.html?changePassword=true');
  });

  await check('login rejects malformed successful responses', async () => {
    handler = async () => json({ token: 'fixture-access' });
    await assert.rejects(authApi.login('fixture-user', 'fixture-password'), /Invalid Portal sign-in response/);
    assert.equal(auth.getToken(), null);
  });
} finally {
  for (const [key, descriptor] of Object.entries(saved)) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
}
assert.deepEqual(failures, [], 'Portal HTTP client behavior checks failed.');
