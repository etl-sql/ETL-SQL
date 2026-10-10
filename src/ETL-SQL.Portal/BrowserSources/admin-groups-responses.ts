import { isObject } from './http-error.js';

const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const nullableText = (value: unknown) => value === null || typeof value === 'string';

function isGroup(value: unknown): value is GroupDto {
  return isObject(value) && positive(value.id) && typeof value.name === 'string' && !!value.name.trim()
    && nullableText(value.description) && count(value.memberCount) && nullableText(value.provider)
    && nullableText(value.adGroup) && positive(value.version);
}
function isMember(value: unknown): value is GroupMemberDto {
  return isObject(value) && positive(value.id) && typeof value.username === 'string' && !!value.username.trim()
    && nullableText(value.email) && typeof value.isActive === 'boolean';
}
function readPage<T extends { id: number }>(value: unknown, page: number, pageSize: number,
  valid: (item: unknown) => item is T): PagedResult<T> {
  if (!isObject(value) || !Array.isArray(value.items) || !value.items.every(valid)
    || !count(value.total) || value.page !== page || value.pageSize !== pageSize
    || value.items.length !== Math.min(pageSize, Math.max(0, value.total - (page - 1) * pageSize))
    || new Set(value.items.map(item => item.id)).size !== value.items.length) {
    throw new TypeError('The requested group catalog is unavailable or incomplete.');
  }
  return { items: value.items, total: value.total, page, pageSize };
}

export function readAdminGroupPage(value: unknown, page: number, pageSize: number): PagedResult<GroupDto> {
  return readPage(value, page, pageSize, isGroup);
}
export function readAdminMemberPage(value: unknown, groupId: number, page: number, pageSize: number): GroupMemberCatalogResponse {
  const result = readPage(value, page, pageSize, isMember);
  if (!isObject(value) || value.groupId !== groupId || !positive(value.version))
    throw new TypeError('The membership response does not identify the requested group and version.');
  return { ...result, groupId, version: value.version };
}
export function readAdminGroupReceipt(value: unknown, body: CreateGroupRequest | UpdateGroupRequest,
  previous: GroupDto | null): GroupDto {
  if (!isGroup(value) || (previous && (value.id !== previous.id || value.version <= previous.version))
    || (!previous && (value.memberCount !== 0 || value.version !== 1
      || value.description !== (body.description ?? null)
      || value.provider !== (body.provider ?? 'Local') || value.adGroup !== (body.adGroup ?? null)))
    || (body.name != null && value.name !== body.name)
    || (body.description != null && value.description !== body.description)
    || (body.provider != null && value.provider !== body.provider)
    || (body.adGroup != null && value.adGroup !== body.adGroup)
    || (previous && body.name == null && value.name !== previous.name)
    || (previous && body.description == null && value.description !== previous.description)
    || (previous && body.provider == null && value.provider !== previous.provider)
    || (previous && body.adGroup == null && value.adGroup !== previous.adGroup)) {
    throw new TypeError('The group change was not confirmed by its receipt.');
  }
  return value;
}
export function readAdminGroupDeleteReceipt(value: unknown, targets: VersionedResourceRequest[]): BulkDeleteGroupsResponse {
  if (!isObject(value) || !count(value.deleted) || !Array.isArray(value.results)
    || value.results.length !== targets.length || !value.results.every(item => isObject(item)
      && positive(item.id) && targets.some(target => target.id === item.id)
      && ['Deleted', 'Conflict', 'Blocked', 'NotFound'].includes(String(item.status))
      && (item.currentVersion === null || positive(item.currentVersion))
      && (item.error === null || typeof item.error === 'string')
      && (!['Conflict', 'Blocked'].includes(String(item.status)) || positive(item.currentVersion)))
    || new Set(value.results.map(item => item.id)).size !== value.results.length
    || value.deleted !== value.results.filter(item => item.status === 'Deleted').length)
    throw new TypeError('The group deletions were not confirmed by their receipts.');
  return value as unknown as BulkDeleteGroupsResponse;
}
function readBulkMembership(value: unknown, groupId: number, ids: number[], version: number, key: 'added' | 'removed') {
  if (!isObject(value) || value.groupId !== groupId || !positive(value.version) || value.version <= version
    || !count(value[key]) || !Array.isArray(value.changedUserIds) || !value.changedUserIds.every(positive)
    || !Array.isArray(value.skippedUserIds) || !value.skippedUserIds.every(positive))
    throw new TypeError('The membership changes were not confirmed by their receipt.');
  const outcomes = [...value.changedUserIds, ...value.skippedUserIds];
  if (outcomes.length !== ids.length || new Set(outcomes).size !== outcomes.length
    || !outcomes.every(id => ids.includes(id)) || value[key] !== value.changedUserIds.length)
    throw new TypeError('The membership receipt does not account for every requested user.');
  return value;
}
export function readAdminMembersAdded(value: unknown, groupId: number, ids: number[], version: number): BulkAddGroupMembersResponse {
  return readBulkMembership(value, groupId, ids, version, 'added') as unknown as BulkAddGroupMembersResponse;
}
export function readAdminMembersRemoved(value: unknown, groupId: number, ids: number[], version: number): BulkRemoveGroupMembersResponse {
  return readBulkMembership(value, groupId, ids, version, 'removed') as unknown as BulkRemoveGroupMembersResponse;
}
export function readAdminMemberRemoved(value: unknown, groupId: number, userId: number, version: number): GroupMembershipResponse {
  if (!isObject(value) || value.groupId !== groupId || value.userId !== userId || !positive(value.version) || value.version <= version)
    throw new TypeError('The member removal was not confirmed by its receipt.');
  return { groupId, userId, version: value.version };
}
