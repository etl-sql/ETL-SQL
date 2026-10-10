import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { catalogQuery } from './admin-catalog-ui.js';
import { readAdminGroupPage, readAdminMemberPage, readAdminGroupReceipt, readAdminGroupDeleteReceipt,
  readAdminMembersAdded, readAdminMembersRemoved, readAdminMemberRemoved } from './admin-groups-responses.js';
import type { ApiRequestOptions } from './portal-http.js';

export class AdminGroupOutcomeUnconfirmed extends Error {
  readonly groupId: number | null;
  constructor(groupId: number | null, cause: unknown) {
    super('Group change outcome is unconfirmed. Refresh groups and membership before trying another change.', { cause });
    this.groupId = groupId;
  }
}
async function read(url: string): Promise<unknown> {
  const response = await apiFetch(url);
  if (response.status !== 200) throw await responseError(response, 'The requested group catalog is unavailable.');
  return response.json();
}
async function mutate<T>(groupId: number | null, url: string, options: ApiRequestOptions, status: number,
  receipt: (value: unknown) => T): Promise<T> {
  try {
    const response = await apiFetch(url, options);
    if (!response.ok) throw await responseError(response, 'The requested group change failed.');
    if (response.status !== status) throw new TypeError('The server did not confirm the expected group change.');
    return receipt(status === 204 ? null : await response.json());
  } catch (error) {
    if (error instanceof PortalApiError && error.status >= 400 && error.status < 500) throw error;
    throw new AdminGroupOutcomeUnconfirmed(groupId, error);
  }
}
const versionHeaders = (version: number) => ({ 'If-Match': `"${version}"` });
export const adminGroupsApi = {
  catalog: async (q: string, page: number) => readAdminGroupPage(await read(`/api/admin/groups/catalog?${catalogQuery({ q, page, pageSize: 25 })}`), page, 25),
  members: async (groupId: number, q: string, page: number) => readAdminMemberPage(
    await read(`/api/admin/groups/${groupId}/members/catalog?${catalogQuery({ q, page, pageSize: 25 })}`), groupId, page, 25),
  create: (body: CreateGroupRequest) => mutate(null, '/api/admin/groups', { method: 'POST', body }, 201,
    value => readAdminGroupReceipt(value, body, null)),
  update: (group: GroupDto, body: UpdateGroupRequest) => mutate(group.id, `/api/admin/groups/${group.id}`,
    { method: 'PUT', body, headers: versionHeaders(group.version) }, 200, value => readAdminGroupReceipt(value, body, group)),
  delete: (group: GroupDto) => mutate(group.id, `/api/admin/groups/${group.id}`,
    { method: 'DELETE', headers: versionHeaders(group.version) }, 204, () => undefined),
  bulkDelete: (groups: VersionedResourceRequest[]) => mutate(null, '/api/admin/groups/bulk-delete',
    { method: 'POST', body: { groups, cascade: false } satisfies Pick<BulkDeleteGroupsRequest, 'groups' | 'cascade'> }, 200,
    value => readAdminGroupDeleteReceipt(value, groups)),
  addMembers: (groupId: number, userIds: number[], version: number) => mutate(groupId, `/api/admin/groups/${groupId}/members/bulk-add`,
    { method: 'POST', headers: versionHeaders(version), body: { userIds } satisfies BulkGroupMembershipRequest }, 200,
    value => readAdminMembersAdded(value, groupId, userIds, version)),
  removeMembers: (groupId: number, userIds: number[], version: number) => mutate(groupId, `/api/admin/groups/${groupId}/members/bulk-remove`,
    { method: 'POST', headers: versionHeaders(version), body: { userIds } satisfies BulkGroupMembershipRequest }, 200,
    value => readAdminMembersRemoved(value, groupId, userIds, version)),
  removeMember: (groupId: number, userId: number, version: number) => mutate(groupId, `/api/admin/groups/${groupId}/members/${userId}`,
    { method: 'DELETE', headers: versionHeaders(version) }, 200, value => readAdminMemberRemoved(value, groupId, userId, version))
};
