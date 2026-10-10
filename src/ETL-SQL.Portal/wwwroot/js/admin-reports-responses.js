/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-reports-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const positive = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const text = (value) => typeof value === 'string';
const nonempty = (value) => text(value) && !!value.trim();
const date = (value) => text(value) && Number.isFinite(Date.parse(value));
const nullableText = (value) => value == null || text(value);
function isReport(value) {
    return isObject(value) && positive(value.id) && positive(value.folderId) && positive(value.version)
        && nonempty(value.name) && nullableText(value.description) && nonempty(value.scriptPath)
        && nonempty(value.folderName) && nonempty(value.folderPath) && value.folderPath.startsWith('/')
        && date(value.createdAt) && date(value.updatedAt);
}
export function readAdminReportCatalog(value) {
    if (!Array.isArray(value) || !value.every(isReport) || new Set(value.map(item => item.id)).size !== value.length)
        throw new TypeError('The report catalog is unavailable or incomplete.');
    return value;
}
// The server resolves a relative script key to its configured physical root.
// Compare normalized components, retaining POSIX casing and Windows casing semantics.
export function matchesReportScript(actual, requested) {
    const normalize = (path) => {
        const parts = [];
        for (const part of path.replaceAll('\\', '/').split('/')) {
            if (!part || part === '.')
                continue;
            if (part === '..' && parts.length && parts.at(-1) !== '..')
                parts.pop();
            else
                parts.push(part);
        }
        return parts.join('/');
    };
    let result = normalize(actual), input = normalize(requested);
    if (/^[a-z]:[\\/]|^\\\\/i.test(actual)) {
        result = result.toLowerCase();
        input = input.toLowerCase();
    }
    const rooted = /^[a-z]:[\\/]|^[\\/]/i.test(requested);
    return result === input || (!rooted && !!input && result.endsWith('/' + input));
}
export function readAdminReportIdentity(value, id) {
    if (!isObject(value) || !positive(value.id) || (id !== null && value.id !== id) || !positive(value.folderId)
        || !positive(value.version) || !nonempty(value.name) || !nonempty(value.scriptPath) || !nullableText(value.description))
        throw new TypeError('The report response is unavailable or does not match its target.');
    return { id: value.id, folderId: value.folderId, name: value.name, scriptPath: value.scriptPath,
        description: text(value.description) ? value.description : null, version: value.version };
}
export function readAdminReportReceipt(value, body, previous) {
    const receipt = readAdminReportIdentity(value, previous?.id ?? null);
    if (receipt.name !== body.name || receipt.folderId !== body.folderId || !matchesReportScript(receipt.scriptPath, body.scriptPath)
        || (previous ? receipt.version <= previous.version : receipt.version !== 1)
        || (body.description?.trim() && receipt.description !== body.description))
        throw new TypeError('The report change was not confirmed by its receipt.');
    return receipt;
}
function isParameter(value) {
    return isObject(value) && nonempty(value.name) && nonempty(value.type) && typeof value.required === 'boolean'
        && nullableText(value.default) && nullableText(value.description);
}
export function readAdminReportValidation(value, scriptPath) {
    if (!isObject(value) || typeof value.isValid !== 'boolean' || !nonempty(value.scriptPath)
        || !matchesReportScript(value.scriptPath, scriptPath) || !nullableText(value.hash)
        || !(value.lastModified == null || date(value.lastModified)) || !isObject(value.metadata)
        || !Object.values(value.metadata).every(text) || !Array.isArray(value.parameters) || !value.parameters.every(isParameter)
        || new Set(value.parameters.map(parameter => parameter.name)).size !== value.parameters.length
        || !Array.isArray(value.errors) || !value.errors.every(nonempty)
        || (value.isValid ? !text(value.hash) || !/^sha256:[0-9a-f]{64}$/i.test(value.hash) || value.errors.length !== 0 : value.errors.length === 0))
        throw new TypeError('The script validation is unavailable, incomplete, or belongs to another path.');
    return { isValid: value.isValid, scriptPath: value.scriptPath, hash: text(value.hash) ? value.hash : null,
        lastModified: date(value.lastModified) ? value.lastModified : null, metadata: value.metadata,
        parameters: value.parameters, errors: value.errors };
}
export function readAdminReportScripts(value) {
    if (!Array.isArray(value) || !value.every((path) => nonempty(path) && /\.rptsql$/i.test(path))
        || new Set(value).size !== value.length)
        throw new TypeError('The available-script catalog is unavailable or incomplete.');
    return value;
}
