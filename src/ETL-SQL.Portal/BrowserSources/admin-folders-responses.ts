import { isObject } from './http-error.js';

const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
type Permission = FolderPermissionOptionDto['permission'];
// Exhaustive against the reflected enum. Labels and authority order come from the server.
const permissionValues = { 0: true, 1: true, 2: true, 3: true } satisfies Record<Permission, boolean>;
const isPermission = (value: unknown): value is Permission => typeof value === 'number' && Object.hasOwn(permissionValues, value);
function isFolder(value: unknown): value is FolderDto {
  return isObject(value) && positive(value.id) && (value.parentId === null || positive(value.parentId))
    && value.parentId !== value.id
    && typeof value.name === 'string' && !!value.name.trim() && typeof value.path === 'string' && value.path.startsWith('/')
    && positive(value.version) && Array.isArray(value.children);
}

/** Validate the whole hierarchy before exposing any rows or parent choices. */
export function readAdminFolderTree(value: unknown): { roots: FolderDto[]; folders: FolderDto[] } {
  if (!Array.isArray(value)) throw new TypeError('The folder hierarchy is unavailable or incomplete.');
  const pending: { value: unknown; parent: number | null }[] = value.map(item => ({ value: item, parent: null }));
  const ids = new Set<number>(), folders: FolderDto[] = [];
  while (pending.length) {
    const entry = pending.pop()!;
    if (!isFolder(entry.value) || ids.has(entry.value.id) || (entry.parent !== null && entry.value.parentId !== entry.parent))
      throw new TypeError('The folder hierarchy is unavailable or inconsistent.');
    const folder = entry.value;
    ids.add(folder.id); folders.push(folder);
    for (const child of folder.children) pending.push({ value: child, parent: folder.id });
  }
  if (value.some(root => root.parentId !== null && ids.has(root.parentId)))
    throw new TypeError('The folder hierarchy contains misplaced roots.');
  folders.sort((left, right) => left.path.localeCompare(right.path));
  return { roots: value as FolderDto[], folders };
}
export function readAdminFolder(value: unknown): FolderDto {
  const folder = readAdminFolderTree([value]).folders[0];
  if (!folder) throw new TypeError('The folder response is unavailable.');
  return folder;
}
export function readAdminFolderReceipt(value: unknown, body: CreateFolderRequest | UpdateFolderRequest, previous: FolderDto | null): FolderDto {
  const folder = readAdminFolder(value);
  if (folder.children.length || (previous && (folder.id !== previous.id || folder.version <= previous.version))
    || (!previous && folder.version !== 1) || (body.name != null && folder.name !== body.name)
    || folder.parentId !== (body.parentId ?? null)
    || (previous && body.name == null && folder.name !== previous.name)
    || !folder.path.endsWith('/' + folder.name))
    throw new TypeError('The folder change was not confirmed by its receipt.');
  return folder;
}
function isOption(value: unknown): value is FolderPermissionOptionDto {
  return isObject(value) && isPermission(value.permission) && typeof value.name === 'string' && !!value.name.trim()
    && count(value.rank) && value.rank < Object.keys(permissionValues).length;
}
function isAcl(value: unknown): value is FolderAclDto {
  return isObject(value) && positive(value.groupId) && typeof value.groupName === 'string' && !!value.groupName.trim()
    && isPermission(value.permission);
}
export function readAdminFolderAcl(value: unknown, folderId: number): FolderAclCatalogResponse {
  if (!isObject(value) || value.folderId !== folderId || typeof value.folderName !== 'string' || !value.folderName.trim()
    || !positive(value.version) || !Array.isArray(value.items) || !value.items.every(isAcl)
    || new Set(value.items.map(item => item.groupId)).size !== value.items.length
    || !Array.isArray(value.permissions) || !value.permissions.every(isOption)
    || value.permissions.length !== Object.keys(permissionValues).length
    || new Set(value.permissions.map(item => item.permission)).size !== value.permissions.length
    || new Set(value.permissions.map(item => item.name)).size !== value.permissions.length
    || new Set(value.permissions.map(item => item.rank)).size !== value.permissions.length)
    throw new TypeError('The folder permissions are unavailable or incomplete.');
  return { folderId, folderName: value.folderName, version: value.version, items: value.items,
    permissions: value.permissions.slice().sort((left, right) => left.rank - right.rank) };
}
export function readAdminFolderGrant(value: unknown, folderId: number, groupId: number, permission: Permission, version: number): FolderAclGrantResponse {
  if (!isObject(value) || value.folderId !== folderId || value.groupId !== groupId || value.permission !== permission
    || !positive(value.version) || value.version <= version)
    throw new TypeError('The folder permission grant was not confirmed by its receipt.');
  return { folderId, groupId, permission, version: value.version };
}
export function readAdminFolderRevoke(value: unknown, folderId: number, groupId: number, version: number): FolderAclRevokeResponse {
  if (!isObject(value) || value.folderId !== folderId || value.groupId !== groupId || !positive(value.version) || value.version <= version)
    throw new TypeError('The folder permission removal was not confirmed by its receipt.');
  return { folderId, groupId, version: value.version };
}
