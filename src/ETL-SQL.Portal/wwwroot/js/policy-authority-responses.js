/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/policy-authority-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const text = (value) => typeof value === 'string';
const name = (value) => text(value) && value.trim().length > 0;
const date = (value) => name(value) && Number.isFinite(Date.parse(value));
const optionalText = (value) => value == null || text(value);
const optionalDate = (value) => value == null || date(value);
const sameScope = (value, scope) => value.tenant === scope.tenant && value.environment === scope.environment;
export function readPolicyAuthorityStatus(value) {
    if (!isObject(value) || typeof value.configured !== 'boolean'
        || !optionalText(value.error) || !optionalText(value.signingPublicKeyPem)
        || value.configured && !name(value.signingPublicKeyPem) || !value.configured && !name(value.error)) {
        throw new TypeError('Policy signer status is unavailable or incomplete.');
    }
    return { configured: value.configured, signingPublicKeyPem: value.signingPublicKeyPem,
        error: value.error };
}
function isValidation(value) {
    return isObject(value) && typeof value.isValid === 'boolean' && Array.isArray(value.errors)
        && value.errors.every(name) && value.isValid === (value.errors.length === 0);
}
export function readPolicyValidation(value) {
    if (!isValidation(value))
        throw new TypeError('Policy validation is unavailable or incomplete.');
    return value;
}
function isVersion(value) {
    if (!isObject(value) || !['tenant', 'environment', 'policyVersion', 'author'].every(field => name(value[field]))
        || !text(value.policyHash) || !/^[a-f0-9]{64}$/i.test(value.policyHash)
        || !['Staged', 'Active', 'Superseded', 'RolledBack', 'Canary'].includes(String(value.rolloutState))
        || !['issuedAtUtc', 'expiresAtUtc', 'publishedAtUtc'].every(field => date(value[field]))
        || !['reviewer', 'supersededVersion', 'canaryGroup'].every(field => optionalText(value[field])))
        return false;
    const group = value.canaryGroup != null, percentage = value.canaryPercentage != null;
    return (!group || name(value.canaryGroup))
        && (!percentage || typeof value.canaryPercentage === 'number' && Number.isInteger(value.canaryPercentage)
            && value.canaryPercentage >= 1 && value.canaryPercentage <= 100)
        && !(group && percentage) && (value.rolloutState !== 'Canary' || group !== percentage);
}
export function readPolicyVersions(value, scope) {
    if (!Array.isArray(value) || !value.every(isVersion) || !value.every(version => sameScope(version, scope))
        || new Set(value.map(version => version.policyVersion)).size !== value.length) {
        throw new TypeError('Policy versions are unavailable or incomplete for this scope.');
    }
    return value;
}
export function readPolicyVersionReceipt(value, scope, state, version) {
    if (!isVersion(value) || !sameScope(value, scope) || value.rolloutState !== state || version !== undefined && value.policyVersion !== version) {
        throw new TypeError('Policy outcome is unconfirmed. Refresh and inspect this scope before retrying.');
    }
    return value;
}
const guid = (value) => text(value) && /^[a-f0-9]{32}$/i.test(value);
function isMachine(value) {
    return isObject(value) && guid(value.machineId) && guid(value.enrollmentId) && name(value.tenant) && name(value.environment)
        && typeof value.requiresClientCertificate === 'boolean' && typeof value.revoked === 'boolean' && date(value.registeredAtUtc)
        && optionalDate(value.revokedAtUtc) && optionalDate(value.lastSeenAtUtc)
        && optionalText(value.revokedReason) && optionalText(value.canaryGroup);
}
export function readPolicyMachines(value, scope) {
    if (!Array.isArray(value) || !value.every(isMachine) || !value.every(machine => sameScope(machine, scope))
        || new Set(value.map(machine => machine.machineId)).size !== value.length) {
        throw new TypeError('Policy machines are unavailable or incomplete for this scope.');
    }
    return value;
}
export function readPolicyMachineReceipt(value, scope, machineId, revoked) {
    if (!isMachine(value) || !sameScope(value, scope) || value.machineId !== machineId || value.revoked !== revoked) {
        throw new TypeError('Machine outcome is unconfirmed. Refresh and inspect this scope before retrying.');
    }
    return value;
}
