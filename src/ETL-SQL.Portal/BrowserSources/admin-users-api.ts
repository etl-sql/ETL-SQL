import { apiFetch } from './portal-http.js';
import { PortalApiError, responseError } from './portal-http.js';
import { catalogQuery } from './admin-catalog-ui.js';
import { readAdminRoles, readAdminUser, readAdminUserPage, readAdminUserReceipt, readAdminUserBulkReceipt } from './admin-users-responses.js';
import type { ApiRequestOptions } from './portal-http.js';

export class AdminUserOutcomeUnconfirmed extends Error {}

async function read(url: string): Promise<unknown> {
  const response = await apiFetch(url);
  if (response.status !== 200) throw await responseError(response, 'The requested catalog is unavailable.');
  return response.json();
}

async function mutate<T>(url: string, options: ApiRequestOptions, status: number, receipt: (value: unknown) => T): Promise<T> {
  try {
    const response = await apiFetch(url, options);
    if (!response.ok) throw await responseError(response, 'The requested user change failed.');
    if (response.status !== status) throw new TypeError('The server did not confirm the expected user change.');
    return receipt(status === 204 ? null : await response.json());
  } catch (error) {
    if (error instanceof PortalApiError && error.status >= 400 && error.status < 500) throw error;
    throw new AdminUserOutcomeUnconfirmed('User change outcome is unconfirmed. Refresh users before trying another change.', { cause: error });
  }
}

const versionHeaders = (version: number) => ({ 'If-Match': `"${version}"` });
export const adminUsersApi = {
  roles: async () => readAdminRoles(await read('/api/admin/roles')),
  catalog: async (q: string, status: string, page: number) => readAdminUserPage(await read(`/api/admin/users/catalog?${catalogQuery({ q, status, page, pageSize: 25 })}`), page, 25),
  user: async (id: number) => readAdminUser(await read(`/api/admin/users/${id}`)),
  create: (body: CreateUserRequest) => mutate('/api/admin/users', { method: 'POST', body }, 201,
    value => readAdminUserReceipt(value, null, body)),
  update: (id: number, body: UpdateUserRequest, version: number) => mutate(`/api/admin/users/${id}`,
    { method: 'PUT', headers: versionHeaders(version), body }, 200, value => readAdminUserReceipt(value, id, body, version)),
  resetPassword: (id: number, newPassword: string, version: number) => mutate(`/api/admin/users/${id}/reset-password`,
    { method: 'POST', headers: versionHeaders(version), body: { newPassword } }, 204, () => undefined),
  delete: (id: number, version: number) => mutate(`/api/admin/users/${id}`,
    { method: 'DELETE', headers: versionHeaders(version) }, 204, () => undefined),
  bulkStatus: (users: VersionedResourceRequest[], isActive: boolean) => mutate('/api/admin/users/bulk-status',
    { method: 'POST', body: { users, isActive } }, 200, value => readAdminUserBulkReceipt(value, users))
};
