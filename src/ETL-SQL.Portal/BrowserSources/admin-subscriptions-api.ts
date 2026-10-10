import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import type { ApiRequestOptions } from './portal-http.js';
import { catalogQuery } from './admin-catalog-ui.js';
import { readAdminSubscription, readAdminSubscriptionPage, readSubscriptionParameters, readSubscriptionHistory,
  readSubscriptionReceipt, readSubscriptionBulkReceipt } from './admin-subscriptions-responses.js';
import type { SubscriptionTarget, SubscriptionChange } from './admin-subscriptions-responses.js';

export class AdminSubscriptionOutcomeUnconfirmed extends Error {
  readonly subscriptionIds: readonly number[];
  constructor(subscriptionIds: readonly number[], cause: unknown) {
    super('Subscription change outcome is unconfirmed. Refresh subscriptions before trying another change.', { cause });
    this.subscriptionIds = [...subscriptionIds];
  }
}
async function read(url: string, signal?: AbortSignal) {
  const response = await apiFetch(url, { signal });
  if (response.status !== 200) throw await responseError(response, 'The requested subscription information is unavailable.');
  return response.json() as Promise<unknown>;
}
async function mutate<T>(ids: readonly number[], url: string, options: ApiRequestOptions, status: number,
  receipt: (value: unknown) => T): Promise<T> {
  try {
    const response = await apiFetch(url, options);
    if (!response.ok) throw await responseError(response, 'The requested subscription change failed.');
    if (response.status !== status) throw new TypeError('The server did not confirm the expected subscription change.');
    return receipt(status === 204 ? null : await response.json());
  } catch (error) {
    if (error instanceof PortalApiError && error.status >= 400 && error.status < 500) throw error;
    throw new AdminSubscriptionOutcomeUnconfirmed(ids, error);
  }
}
export const adminSubscriptionsApi = {
  catalog: async (page: number, query: string, status: string, signal?: AbortSignal) =>
    readAdminSubscriptionPage(await read(`/api/admin/subscriptions/catalog?${catalogQuery({ page, pageSize: 25, q: query, status })}`, signal), page, 25),
  get: async (id: number, signal?: AbortSignal) => {
    const response = await apiFetch(`/api/subscriptions/${id}`, { signal });
    if (response.status === 404) return null;
    if (response.status !== 200) throw await responseError(response, 'The subscription could not be refreshed.');
    return readAdminSubscription(await response.json(), id);
  },
  parameters: async (reportId: number, signal?: AbortSignal) =>
    readSubscriptionParameters(await read(`/api/reports/${reportId}/parameters`, signal)),
  history: async (id: number, signal?: AbortSignal) =>
    readSubscriptionHistory(await read(`/api/subscriptions/${id}/history?limit=50`, signal)),
  update: (target: SubscriptionTarget, body: SubscriptionChange) => mutate([target.id], `/api/subscriptions/${target.id}`,
    { method: 'PUT', body, headers: { 'If-Match': `"${target.version}"` } }, 200, value => readSubscriptionReceipt(value, target, body)),
  delete: (target: SubscriptionTarget) => mutate([target.id], `/api/subscriptions/${target.id}`,
    { method: 'DELETE', headers: { 'If-Match': `"${target.version}"` } }, 204, () => undefined),
  bulkStatus: (targets: VersionedResourceRequest[], isActive: boolean) => {
    const body: Pick<BulkSubscriptionStatusRequest, 'subscriptions' | 'isActive'> = { subscriptions: targets, isActive };
    return mutate(targets.map(target => target.id), '/api/admin/subscriptions/bulk-status', { method: 'POST', body }, 200,
      value => readSubscriptionBulkReceipt(value, targets));
  }
};
