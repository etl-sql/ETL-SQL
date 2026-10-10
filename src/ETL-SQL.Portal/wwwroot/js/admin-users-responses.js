/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-users-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isAdminUser } from './admin-user-directory.js';
import { isObject } from './http-error.js';
const positive = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const count = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
export function readAdminUser(value) {
    if (!isAdminUser(value))
        throw new TypeError('The user response is unavailable or incomplete.');
    return value;
}
export function readAdminUserPage(value, page, pageSize) {
    if (!isObject(value) || !Array.isArray(value.items) || !value.items.every(isAdminUser)
        || !count(value.total) || value.page !== page || value.pageSize !== pageSize
        || value.items.length !== Math.min(pageSize, Math.max(0, value.total - (page - 1) * pageSize))
        || new Set(value.items.map(item => item.id)).size !== value.items.length) {
        throw new TypeError('The user catalog is unavailable or incomplete.');
    }
    return { items: value.items, total: value.total, page, pageSize };
}
export function readAdminRoles(value) {
    if (!Array.isArray(value) || !value.every(item => isObject(item)
        && typeof item.name === 'string' && item.name.trim().length > 0)
        || new Set(value.map(item => item.name.toUpperCase())).size !== value.length) {
        throw new TypeError('The role catalog is unavailable or incomplete.');
    }
    return value;
}
export function readAdminUserReceipt(value, id, body, previousVersion) {
    const user = readAdminUser(value);
    if ((id !== null && (user.id !== id || (previousVersion !== undefined && user.version <= previousVersion)))
        || ('username' in body && user.username !== body.username)
        || (body.email != null && user.email !== body.email)
        || (body.role != null && (user.roles.length !== (body.role ? 1 : 0) || (body.role && user.roles[0] !== body.role)))
        || ('isActive' in body && body.isActive != null && user.isActive !== body.isActive)) {
        throw new TypeError('The user change was not confirmed by its receipt.');
    }
    return user;
}
export function readAdminUserBulkReceipt(value, requested) {
    if (!isObject(value) || !count(value.updated) || !Array.isArray(value.results)
        || value.results.length !== requested.length || !value.results.every(item => isObject(item)
        && positive(item.id) && ['Updated', 'Conflict', 'NotFound'].includes(String(item.status))
        && (item.currentVersion === null || positive(item.currentVersion))
        && (item.error === null || typeof item.error === 'string')
        && requested.some(request => request.id === item.id)
        && (item.status !== 'Updated' || requested.some(request => request.id === item.id
            && positive(item.currentVersion) && item.currentVersion > request.version)))
        || new Set(value.results.map(item => item.id)).size !== value.results.length
        || value.updated !== value.results.filter(item => item.status === 'Updated').length) {
        throw new TypeError('The user status changes were not confirmed by their receipts.');
    }
    return value;
}
