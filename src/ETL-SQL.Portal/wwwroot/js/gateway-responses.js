/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/gateway-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
const text = (value) => typeof value === 'string';
const name = (value) => text(value) && value.trim().length > 0;
const date = (value) => name(value) && Number.isFinite(Date.parse(value));
const count = (value) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const optionalText = (value) => value == null || text(value);
const optionalDate = (value) => value == null || date(value);
function isResource(value) {
    return isObject(value) && ['resourceId', 'connectorType', 'allowedOperations'].every(field => name(value[field]))
        && value.state === 'Approved' && typeof value.isOnline === 'boolean' && optionalDate(value.lastSeenUtc);
}
function isNode(value) {
    return isObject(value) && ['tenantId', 'gatewayId', 'nodeId', 'workloadPublicKeyThumbprint'].every(field => name(value[field]))
        && date(value.connectedUtc) && typeof value.isActive === 'boolean';
}
function isEnrollment(value) {
    if (!isObject(value) || !name(value.enrollmentId) || !name(value.gatewayId)
        || !['Pending', 'Consumed', 'Revoked'].includes(String(value.state))
        || !date(value.createdUtc) || !date(value.expiresUtc) || !optionalDate(value.consumedUtc)
        || !optionalText(value.workloadPublicKeyThumbprint) || typeof value.isOnline !== 'boolean'
        || !count(value.activeNodes) || !count(value.totalNodes) || !Array.isArray(value.nodes) || !value.nodes.every(isNode)
        || value.totalNodes !== value.nodes.length || value.activeNodes !== value.nodes.filter(node => node.isActive).length
        || value.isOnline !== (value.activeNodes > 0)
        || value.publishedResources != null && (!Array.isArray(value.publishedResources) || !value.publishedResources.every(isResource)))
        return false;
    return value.nodes.every(node => node.gatewayId === value.gatewayId)
        && new Set(value.nodes.map(node => node.tenantId)).size <= 1
        && new Set(value.nodes.map(node => node.nodeId.toUpperCase())).size === value.nodes.length;
}
export function readGatewayFleet(value) {
    if (!Array.isArray(value) || !value.every(isEnrollment)
        || new Set(value.map(enrollment => enrollment.enrollmentId)).size !== value.length
        || new Set(value.flatMap(enrollment => enrollment.nodes.map(node => node.tenantId))).size > 1) {
        throw new TypeError('Gateway fleet metadata is unavailable or incomplete.');
    }
    return value;
}
/** Match the durable store's current enrollment choice; history rows are not extra clusters. */
export function currentGatewayFleet(enrollments) {
    const groups = new Map();
    for (const entry of enrollments) {
        const existing = groups.get(entry.gatewayId);
        if (!existing || (entry.state === 'Consumed' && existing.state !== 'Consumed')
            || (entry.state === 'Consumed') === (existing.state === 'Consumed') && Date.parse(entry.createdUtc) > Date.parse(existing.createdUtc)) {
            groups.set(entry.gatewayId, entry);
        }
    }
    return [...groups.values()];
}
function isIssued(value) {
    return isObject(value) && ['enrollmentId', 'gatewayId', 'tenantId'].every(field => name(value[field]))
        && typeof value.oneTimeToken === 'string' && /^[a-zA-Z0-9_-]{32,}$/.test(value.oneTimeToken) && date(value.expiresUtc);
}
export function readIssuedGateway(value, gatewayId, now = Date.now()) {
    if (!isIssued(value) || value.gatewayId !== gatewayId || Date.parse(value.expiresUtc) <= now) {
        throw new TypeError('Enrollment outcome is unconfirmed. Refresh and inspect the gateway before issuing another token.');
    }
    return value;
}
export function readGatewayRevocation(value, gatewayId) {
    if (!isObject(value) || value.success !== true || value.gatewayId !== gatewayId) {
        throw new TypeError('Gateway revocation is unconfirmed. Refresh before retrying.');
    }
    return { success: true, gatewayId };
}
export function readGatewayResources(value) {
    if (!Array.isArray(value) || !value.every(isResource)
        || new Set(value.map(resource => resource.resourceId)).size !== value.length)
        throw new TypeError('Gateway resources are unavailable or incomplete.');
    return value;
}
function isDiscoveredGateway(value) {
    return isObject(value) && name(value.id) && name(value.name) && typeof value.isOnline === 'boolean'
        && count(value.activeNodes) && count(value.totalNodes) && value.activeNodes <= value.totalNodes
        && value.isOnline === (value.activeNodes > 0) && optionalText(value.region) && optionalText(value.status)
        && (value.resources == null || Array.isArray(value.resources) && value.resources.every(isResource));
}
export function readDiscoveredGateways(value) {
    if (!Array.isArray(value) || !value.every(isDiscoveredGateway))
        throw new TypeError('Gateway discovery is unavailable or incomplete.');
    // Discovery currently includes every non-revoked enrollment; newest entries arrive first.
    const current = new Map();
    for (const gateway of value)
        if (!current.has(gateway.id))
            current.set(gateway.id, gateway);
    return [...current.values()];
}
function isOption(value) {
    return isObject(value) && name(value.name) && typeof value.type === 'number' && [0, 1, 2, 3, 4, 5].includes(value.type)
        && typeof value.isMandatory === 'boolean' && text(value.category)
        && ['defaultValue', 'description', 'mutuallyExclusiveGroup'].every(field => optionalText(value[field]))
        && (value.allowedValues == null || Array.isArray(value.allowedValues) && value.allowedValues.every(text));
}
function isSchema(value) {
    return isObject(value) && name(value.connectorType) && Array.isArray(value.aliases) && value.aliases.every(text)
        && text(value.description) && typeof value.isFileBased === 'boolean' && typeof value.isDataWarehouse === 'boolean'
        && count(value.commandTimeoutSeconds) && Array.isArray(value.options) && value.options.every(isOption);
}
export function readConnectorSchemas(value) {
    if (!Array.isArray(value) || !value.every(isSchema) || new Set(value.map(schema => schema.connectorType)).size !== value.length) {
        throw new TypeError('Connector metadata is unavailable or incomplete.');
    }
    return value;
}
function isDiagnosticStep(value) {
    return isObject(value) && text(value.layer) && ['Ok', 'Failed', 'Skipped', 'Denied'].includes(String(value.status))
        && text(value.detail) && optionalText(value.remedy);
}
function isDiagnostic(value) {
    return isObject(value) && text(value.connection) && name(value.connectorType) && typeof value.succeeded === 'boolean'
        && Array.isArray(value.steps) && value.steps.every(isDiagnosticStep)
        && value.succeeded === value.steps.every(step => step.status === 'Ok' || step.status === 'Skipped');
}
export function readConnectionDiagnostic(value) {
    if (!isDiagnostic(value))
        throw new TypeError('Connection diagnostic outcome is unavailable or incomplete.');
    return value;
}
