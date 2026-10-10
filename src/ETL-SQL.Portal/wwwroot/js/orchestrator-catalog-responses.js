/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/orchestrator-catalog-responses.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { isObject } from './http-error.js';
function isText(value) {
    return typeof value === 'string' && value.length > 0;
}
function isOptionalText(value) {
    return value == null || typeof value === 'string';
}
function isVersion(value) {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1;
}
function isDefinition(value) {
    return isObject(value) && isText(value.name) && typeof value.isEnabled === 'boolean'
        && isOptionalText(value.displayName) && isOptionalText(value.description)
        && isOptionalText(value.createdBy) && isVersion(value.version);
}
function uniqueNames(items) {
    return new Set(items.map(item => item.name.toLowerCase())).size === items.length;
}
export function readSchedules(value) {
    if (!Array.isArray(value))
        throw new TypeError('Invalid schedule catalog response.');
    const schedules = value.map((item) => {
        if (!isDefinition(item) || !isText(item.cron) || !isText(item.timeZone))
            throw new TypeError('Invalid schedule catalog response.');
        return {
            name: item.name, cron: item.cron, timeZone: item.timeZone, isEnabled: item.isEnabled,
            displayName: item.displayName,
            description: item.description,
            createdBy: item.createdBy, version: item.version
        };
    });
    if (!uniqueNames(schedules))
        throw new TypeError('Invalid schedule catalog response.');
    return schedules;
}
export function readNotifications(value) {
    if (!Array.isArray(value))
        throw new TypeError('Invalid notification catalog response.');
    const notifications = value.map((item) => {
        if (!isDefinition(item) || !isText(item.connectionName) || !isOptionalText(item.recipient))
            throw new TypeError('Invalid notification catalog response.');
        return {
            name: item.name, connectionName: item.connectionName, recipient: item.recipient,
            isEnabled: item.isEnabled,
            displayName: item.displayName,
            description: item.description,
            createdBy: item.createdBy, version: item.version
        };
    });
    if (!uniqueNames(notifications))
        throw new TypeError('Invalid notification catalog response.');
    return notifications;
}
/** The Portal relays some service errors as a JSON string that itself contains the service JSON. */
function errorText(body) {
    let value = body;
    if (typeof value === 'string') {
        try {
            value = JSON.parse(value);
        }
        catch {
            return null;
        }
    }
    if (!isObject(value))
        return null;
    const error = value.error ?? value.Error;
    return typeof error === 'string' && error.trim() ? error : null;
}
/** Reads a catalog response body, turning any non-success status into an error that names it. */
export async function readCatalogJson(response) {
    if (response.status === 200)
        return response.json();
    const body = await response.json().catch(() => null);
    throw new Error(errorText(body) ?? `The Orchestrator did not answer (HTTP ${response.status}).`);
}
/**
 * A test dispatch answers 200 when the message was delivered, 202 when the endpoint accepted the
 * request and sent nothing, and 502 when delivery was attempted and failed. Only the first is a
 * delivery; the other two carry the reason in the body.
 */
export async function readDispatchOutcome(response) {
    const body = await response.json().catch(() => null);
    if (response.status !== 200 && response.status !== 202 && response.status !== 502)
        throw new Error(errorText(body) ?? `Unexpected response (${response.status}). The dispatch was not confirmed.`);
    if (!isObject(body) || typeof body.delivered !== 'boolean' || typeof body.skipped !== 'boolean'
        || body.delivered !== (response.status === 200) || (body.delivered && body.skipped))
        throw new Error('The dispatch response was incomplete. Delivery was not confirmed.');
    if (body.message != null && typeof body.message !== 'string')
        throw new Error('The dispatch response was incomplete. Delivery was not confirmed.');
    return { delivered: body.delivered, skipped: body.skipped, detail: body.message?.trim() ?? '' };
}
