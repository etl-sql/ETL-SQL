/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/portal-http.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { readAuthProvidersResponse, readLoginResponse } from './auth-responses.js';
import { isObject } from './http-error.js';
const TOKEN_KEY = 'etlsql_token';
const REFRESH_KEY = 'etlsql_refresh';
const nativeFetch = window.fetch.bind(window);
let sessionGeneration = 0;
let refreshing = null;
function storeTokens(token, refreshToken) {
    if (typeof token !== 'string' || !token.trim()
        || (refreshToken != null && (typeof refreshToken !== 'string' || !refreshToken.trim())))
        throw new TypeError('Portal credentials must be nonempty strings.');
    sessionStorage.setItem(TOKEN_KEY, token);
    if (refreshToken)
        sessionStorage.setItem(REFRESH_KEY, refreshToken);
    else
        sessionStorage.removeItem(REFRESH_KEY);
}
export const auth = {
    getToken: () => sessionStorage.getItem(TOKEN_KEY),
    getRefreshToken: () => sessionStorage.getItem(REFRESH_KEY),
    setTokens(token, refreshToken) {
        storeTokens(token, refreshToken);
        sessionGeneration++;
    },
    clear() {
        sessionGeneration++;
        sessionStorage.removeItem(TOKEN_KEY);
        sessionStorage.removeItem(REFRESH_KEY);
    },
    isLoggedIn: () => !!sessionStorage.getItem(TOKEN_KEY),
    redirectToLogin() {
        auth.clear();
        window.location.href = '/login.html';
    }
};
function isPortalApi(input) {
    const address = input instanceof Request ? input.url : String(input);
    const url = new URL(address, window.location.href);
    return url.origin === window.location.origin && url.pathname.startsWith('/api/');
}
function requestBody(body, headers) {
    if (body == null || typeof body === 'string')
        return body;
    if (isNativeBody(body))
        return body;
    if (Array.isArray(body) || Object.getPrototypeOf(body) === Object.prototype || Object.getPrototypeOf(body) === null) {
        headers.set('Content-Type', 'application/json');
        return JSON.stringify(body);
    }
    throw new TypeError('Unsupported Portal request body.');
}
function isNativeBody(body) {
    return body instanceof Blob || body instanceof FormData || body instanceof URLSearchParams
        || body instanceof ArrayBuffer || (ArrayBuffer.isView(body) && body.buffer instanceof ArrayBuffer)
        || body instanceof ReadableStream;
}
function assertSession(generation) {
    if (generation !== sessionGeneration)
        throw new Error('Portal session changed while the request was running.');
}
async function requestRefresh(refreshToken) {
    const response = await nativeFetch('/api/auth/refresh', { method: 'POST', keepalive: true,
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken }) });
    return response.ok ? readLoginResponse(await response.json()) : null;
}
async function refreshSession(generation, refreshToken, pending) {
    try {
        const credentials = await pending;
        if (generation !== sessionGeneration || refreshToken !== auth.getRefreshToken())
            return { kind: 'session-changed' };
        if (!credentials) {
            auth.redirectToLogin();
            return { kind: 'failed', generation: sessionGeneration };
        }
        // Rotation belongs to the same identity. Public setTokens starts a new session generation.
        storeTokens(credentials.token, credentials.refreshToken);
        return { kind: 'refreshed', token: credentials.token };
    }
    catch {
        if (generation !== sessionGeneration || refreshToken !== auth.getRefreshToken())
            return { kind: 'session-changed' };
        auth.redirectToLogin();
        return { kind: 'failed', generation: sessionGeneration };
    }
}
function refreshToken(generation, token) {
    if (refreshing?.generation === generation && refreshing.refreshToken === token)
        return refreshing.promise;
    const credentials = requestRefresh(token);
    const current = { generation, refreshToken: token, credentials,
        promise: refreshSession(generation, token, credentials) };
    refreshing = current;
    current.promise = current.promise.finally(() => { if (refreshing === current)
        refreshing = null; });
    return current.promise;
}
function waitForRefresh(promise, signal) {
    if (!signal)
        return promise;
    signal.throwIfAborted();
    return new Promise((resolve, reject) => {
        const aborted = () => reject(signal.reason);
        signal.addEventListener('abort', aborted, { once: true });
        promise.then(resolve, reject).finally(() => signal.removeEventListener('abort', aborted));
    });
}
export async function apiFetch(url, options = {}) {
    const generation = sessionGeneration;
    const signal = options.signal === undefined && url instanceof Request ? url.signal : options.signal;
    signal?.throwIfAborted();
    const portal = isPortalApi(url);
    const token = portal ? auth.getToken() : null;
    const headers = new Headers(options.headers ?? (url instanceof Request ? url.headers : undefined));
    if (token)
        headers.set('Authorization', `Bearer ${token}`);
    const init = { ...options, body: requestBody(options.body, headers), headers };
    const retryInput = url instanceof Request ? url.clone() : url;
    let response = await nativeFetch(url, init);
    if (!portal)
        return response;
    assertSession(generation);
    if (response.status !== 401)
        return response;
    signal?.throwIfAborted();
    // Another request may have finished rotation before this old-token response arrived.
    let replacement = auth.getToken();
    if (!replacement || replacement === token) {
        const refresh = auth.getRefreshToken();
        if (!refresh) {
            auth.redirectToLogin();
            return response;
        }
        const result = await waitForRefresh(refreshToken(generation, refresh), signal);
        if (result.kind === 'session-changed')
            throw new Error('Portal session changed while the request was running.');
        if (result.kind === 'failed') {
            assertSession(result.generation);
            return response;
        }
        replacement = result.token;
    }
    assertSession(generation);
    signal?.throwIfAborted();
    if (init.body instanceof ReadableStream)
        throw new TypeError('Cannot retry a streamed Portal request.');
    headers.set('Authorization', `Bearer ${replacement}`);
    response = await nativeFetch(retryInput, init);
    assertSession(generation);
    if (response.status === 401)
        auth.redirectToLogin();
    return response;
}
export class PortalApiError extends Error {
    status;
    body;
    constructor(message, status, body) {
        super(message);
        this.status = status;
        this.body = body;
    }
}
export async function responseError(response, fallback) {
    const body = await response.json().catch(() => null);
    const message = isObject(body) && typeof body.error === 'string' && body.error.trim() ? body.error : fallback;
    return new PortalApiError(message, response.status, body);
}
export async function apiJson(url, options = {}) {
    const response = await apiFetch(url, options);
    if (!response.ok) {
        const error = await responseError(response, response.statusText || `Request failed (${response.status})`);
        // This is the exact hint emitted by MustChangePasswordMiddleware; the session is retained.
        if (response.status === 403 && isObject(error.body) && error.body.redirect === '/login.html?changePassword=true')
            window.location.href = '/login.html?changePassword=true';
        throw error;
    }
    if (response.status === 204)
        return {};
    const value = await response.json();
    return value;
}
export const authApi = {
    async providers() {
        const response = await nativeFetch('/api/auth/providers');
        if (!response.ok)
            return { local: true, oidcEnabled: false, oidcLoginUrl: null };
        return readAuthProvidersResponse(await response.json());
    },
    async login(username, password) {
        const response = await nativeFetch('/api/auth/login', { method: 'POST',
            headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password }) });
        if (!response.ok)
            throw await responseError(response, 'Login failed');
        return readLoginResponse(await response.json());
    },
    changePassword: (currentPassword, newPassword) => apiJson('/api/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } }),
    async logout() {
        const token = auth.getToken();
        const refreshToken = auth.getRefreshToken();
        const pending = refreshing?.generation === sessionGeneration && refreshing.refreshToken === refreshToken
            ? refreshing.credentials : null;
        auth.clear();
        const generation = sessionGeneration;
        try {
            if (refreshToken) {
                const revoke = (access) => nativeFetch('/api/auth/logout', { method: 'POST', keepalive: true,
                    headers: { 'Content-Type': 'application/json', ...(access ? { Authorization: `Bearer ${access}` } : {}) },
                    body: JSON.stringify({ refreshToken }) });
                const response = await revoke(token);
                if (response.status === 401) {
                    // Logout is authenticated. Reuse an in-flight rotation or obtain credentials only
                    // for server revocation; they must never restore the cleared browser session.
                    const credentials = await (pending ?? requestRefresh(refreshToken));
                    if (credentials)
                        await revoke(credentials.token);
                }
            }
        }
        catch {
            // Local credentials stay cleared even when server revocation is unavailable.
        }
        finally {
            if (generation === sessionGeneration)
                window.location.href = '/login.html';
        }
    }
};
// Shared report modules call fetch directly. Preserve a Request's own headers and credentials.
window.fetch = async (input, init) => {
    const token = isPortalApi(input) ? auth.getToken() : null;
    if (!token)
        return nativeFetch(input, init);
    const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
    if (!headers.has('Authorization'))
        headers.set('Authorization', `Bearer ${token}`);
    return nativeFetch(input, { ...init, headers });
};
