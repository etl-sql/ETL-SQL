/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/auth-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
export function requiresPasswordChange(token) {
    return getSessionIdentity(token).claims.mustChangePassword === 'true';
}
export function readLoginResponse(value) {
    if (!isObject(value) || typeof value.token !== 'string' || !value.token.trim()
        || typeof value.refreshToken !== 'string' || !value.refreshToken.trim()
        || typeof value.expiresAt !== 'string' || !Number.isFinite(Date.parse(value.expiresAt))) {
        throw new TypeError('Invalid Portal sign-in response.');
    }
    return { token: value.token, refreshToken: value.refreshToken, expiresAt: value.expiresAt };
}
export function readAuthProvidersResponse(value) {
    if (!isObject(value) || typeof value.local !== 'boolean' || typeof value.oidcEnabled !== 'boolean'
        || (value.oidcLoginUrl != null && typeof value.oidcLoginUrl !== 'string')) {
        throw new TypeError('Invalid Portal identity-provider response.');
    }
    return { local: value.local, oidcEnabled: value.oidcEnabled, oidcLoginUrl: value.oidcLoginUrl ?? null };
}
