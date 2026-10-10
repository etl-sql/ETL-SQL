/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-subscriptions-api.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { catalogQuery } from './admin-catalog-ui.js';
import { readAdminSubscription, readAdminSubscriptionPage, readSubscriptionParameters, readSubscriptionHistory, readSubscriptionReceipt, readSubscriptionBulkReceipt } from './admin-subscriptions-responses.js';
export class AdminSubscriptionOutcomeUnconfirmed extends Error {
    subscriptionIds;
    constructor(subscriptionIds, cause) {
        super('Subscription change outcome is unconfirmed. Refresh subscriptions before trying another change.', { cause });
        this.subscriptionIds = [...subscriptionIds];
    }
}
async function read(url, signal) {
    const response = await apiFetch(url, { signal });
    if (response.status !== 200)
        throw await responseError(response, 'The requested subscription information is unavailable.');
    return response.json();
}
async function mutate(ids, url, options, status, receipt) {
    try {
        const response = await apiFetch(url, options);
        if (!response.ok)
            throw await responseError(response, 'The requested subscription change failed.');
        if (response.status !== status)
            throw new TypeError('The server did not confirm the expected subscription change.');
        return receipt(status === 204 ? null : await response.json());
    }
    catch (error) {
        if (error instanceof PortalApiError && error.status >= 400 && error.status < 500)
            throw error;
        throw new AdminSubscriptionOutcomeUnconfirmed(ids, error);
    }
}
export const adminSubscriptionsApi = {
    catalog: async (page, query, status, signal) => readAdminSubscriptionPage(await read(`/api/admin/subscriptions/catalog?${catalogQuery({ page, pageSize: 25, q: query, status })}`, signal), page, 25),
    get: async (id, signal) => {
        const response = await apiFetch(`/api/subscriptions/${id}`, { signal });
        if (response.status === 404)
            return null;
        if (response.status !== 200)
            throw await responseError(response, 'The subscription could not be refreshed.');
        return readAdminSubscription(await response.json(), id);
    },
    parameters: async (reportId, signal) => readSubscriptionParameters(await read(`/api/reports/${reportId}/parameters`, signal)),
    history: async (id, signal) => readSubscriptionHistory(await read(`/api/subscriptions/${id}/history?limit=50`, signal)),
    update: (target, body) => mutate([target.id], `/api/subscriptions/${target.id}`, { method: 'PUT', body, headers: { 'If-Match': `"${target.version}"` } }, 200, value => readSubscriptionReceipt(value, target, body)),
    delete: (target) => mutate([target.id], `/api/subscriptions/${target.id}`, { method: 'DELETE', headers: { 'If-Match': `"${target.version}"` } }, 204, () => undefined),
    bulkStatus: (targets, isActive) => {
        const body = { subscriptions: targets, isActive };
        return mutate(targets.map(target => target.id), '/api/admin/subscriptions/bulk-status', { method: 'POST', body }, 200, value => readSubscriptionBulkReceipt(value, targets));
    }
};
