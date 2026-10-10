/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-groups-api.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { catalogQuery } from './admin-catalog-ui.js';
import { readAdminGroupPage, readAdminMemberPage, readAdminGroupReceipt, readAdminGroupDeleteReceipt, readAdminMembersAdded, readAdminMembersRemoved, readAdminMemberRemoved } from './admin-groups-responses.js';
export class AdminGroupOutcomeUnconfirmed extends Error {
    groupId;
    constructor(groupId, cause) {
        super('Group change outcome is unconfirmed. Refresh groups and membership before trying another change.', { cause });
        this.groupId = groupId;
    }
}
async function read(url) {
    const response = await apiFetch(url);
    if (response.status !== 200)
        throw await responseError(response, 'The requested group catalog is unavailable.');
    return response.json();
}
async function mutate(groupId, url, options, status, receipt) {
    try {
        const response = await apiFetch(url, options);
        if (!response.ok)
            throw await responseError(response, 'The requested group change failed.');
        if (response.status !== status)
            throw new TypeError('The server did not confirm the expected group change.');
        return receipt(status === 204 ? null : await response.json());
    }
    catch (error) {
        if (error instanceof PortalApiError && error.status >= 400 && error.status < 500)
            throw error;
        throw new AdminGroupOutcomeUnconfirmed(groupId, error);
    }
}
const versionHeaders = (version) => ({ 'If-Match': `"${version}"` });
export const adminGroupsApi = {
    catalog: async (q, page) => readAdminGroupPage(await read(`/api/admin/groups/catalog?${catalogQuery({ q, page, pageSize: 25 })}`), page, 25),
    members: async (groupId, q, page) => readAdminMemberPage(await read(`/api/admin/groups/${groupId}/members/catalog?${catalogQuery({ q, page, pageSize: 25 })}`), groupId, page, 25),
    create: (body) => mutate(null, '/api/admin/groups', { method: 'POST', body }, 201, value => readAdminGroupReceipt(value, body, null)),
    update: (group, body) => mutate(group.id, `/api/admin/groups/${group.id}`, { method: 'PUT', body, headers: versionHeaders(group.version) }, 200, value => readAdminGroupReceipt(value, body, group)),
    delete: (group) => mutate(group.id, `/api/admin/groups/${group.id}`, { method: 'DELETE', headers: versionHeaders(group.version) }, 204, () => undefined),
    bulkDelete: (groups) => mutate(null, '/api/admin/groups/bulk-delete', { method: 'POST', body: { groups, cascade: false } }, 200, value => readAdminGroupDeleteReceipt(value, groups)),
    addMembers: (groupId, userIds, version) => mutate(groupId, `/api/admin/groups/${groupId}/members/bulk-add`, { method: 'POST', headers: versionHeaders(version), body: { userIds } }, 200, value => readAdminMembersAdded(value, groupId, userIds, version)),
    removeMembers: (groupId, userIds, version) => mutate(groupId, `/api/admin/groups/${groupId}/members/bulk-remove`, { method: 'POST', headers: versionHeaders(version), body: { userIds } }, 200, value => readAdminMembersRemoved(value, groupId, userIds, version)),
    removeMember: (groupId, userId, version) => mutate(groupId, `/api/admin/groups/${groupId}/members/${userId}`, { method: 'DELETE', headers: versionHeaders(version) }, 200, value => readAdminMemberRemoved(value, groupId, userId, version))
};
