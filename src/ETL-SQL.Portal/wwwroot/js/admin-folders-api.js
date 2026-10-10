/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-folders-api.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { readAdminFolder, readAdminFolderTree, readAdminFolderReceipt, readAdminFolderAcl, readAdminFolderGrant, readAdminFolderRevoke } from './admin-folders-responses.js';
export class AdminFolderOutcomeUnconfirmed extends Error {
    folderId;
    constructor(folderId, cause) {
        super('Folder change outcome is unconfirmed. Refresh folders and permissions before trying another change.', { cause });
        this.folderId = folderId;
    }
}
async function read(url) {
    const response = await apiFetch(url);
    if (response.status !== 200)
        throw await responseError(response, 'The requested folder catalog is unavailable.');
    return response.json();
}
async function mutate(folderId, url, options, status, receipt) {
    try {
        const response = await apiFetch(url, options);
        if (!response.ok)
            throw await responseError(response, 'The requested folder change failed.');
        if (response.status !== status)
            throw new TypeError('The server did not confirm the expected folder change.');
        return receipt(status === 204 ? null : await response.json());
    }
    catch (error) {
        if (error instanceof PortalApiError && error.status >= 400 && error.status < 500)
            throw error;
        throw new AdminFolderOutcomeUnconfirmed(folderId, error);
    }
}
const versionHeaders = (version) => ({ 'If-Match': `"${version}"` });
export const adminFoldersApi = {
    tree: async () => readAdminFolderTree(await read('/api/folders')),
    folder: async (id) => {
        const folder = readAdminFolder(await read(`/api/folders/${id}`));
        if (folder.id !== id)
            throw new TypeError('The folder response does not match the requested folder.');
        return folder;
    },
    acl: async (id) => readAdminFolderAcl(await read(`/api/folders/${id}/acl/catalog`), id),
    create: (body) => mutate(null, '/api/folders', { method: 'POST', body }, 201, value => readAdminFolderReceipt(value, body, null)),
    update: (folder, body) => mutate(folder.id, `/api/folders/${folder.id}`, { method: 'PUT', body, headers: versionHeaders(folder.version) }, 200, value => readAdminFolderReceipt(value, body, folder)),
    delete: (folder) => mutate(folder.id, `/api/folders/${folder.id}?cascade=true`, { method: 'DELETE', headers: versionHeaders(folder.version) }, 204, () => undefined),
    grant: (folderId, body, version) => mutate(folderId, `/api/folders/${folderId}/acl`, { method: 'POST', headers: versionHeaders(version), body }, 200, value => readAdminFolderGrant(value, folderId, body.groupId, body.permission, version)),
    revoke: (folderId, groupId, version) => mutate(folderId, `/api/folders/${folderId}/acl/${groupId}`, { method: 'DELETE', headers: versionHeaders(version) }, 200, value => readAdminFolderRevoke(value, folderId, groupId, version))
};
