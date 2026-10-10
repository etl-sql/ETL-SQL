/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-folders.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { auth, PortalApiError } from './portal-http.js';
import { adminFoldersApi, AdminFolderOutcomeUnconfirmed } from './admin-folders-api.js';
import { adminGroupsApi } from './admin-groups-api.js';
import { renderCatalogPager } from './admin-catalog-ui.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { failedState, loadingState } from './portal-states.js';
function element(doc, id, kind) {
    const value = doc.getElementById(id);
    if (!(value instanceof kind))
        throw new Error(`Missing admin folder element: ${id}`);
    return value;
}
const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
/** Own the complete folder hierarchy, editor drafts, ACL scope, and grant/revoke confirmation. */
export function createAdminFolders(doc = document) {
    const feedback = window.ETLSQLFeedback;
    if (!feedback)
        throw new Error('Portal feedback must be initialized before folder administration.');
    const wrap = element(doc, 'folderTableWrap', HTMLDivElement), form = element(doc, 'newFolderForm', HTMLDivElement);
    const name = element(doc, 'nf-name', HTMLInputElement), parent = element(doc, 'nf-parent', HTMLSelectElement);
    const save = element(doc, 'nf-saveBtn', HTMLButtonElement), editorError = element(doc, 'nf-error', HTMLDivElement);
    const mutationStatus = element(doc, 'foldersMutationStatus', HTMLDivElement);
    const aclPanel = element(doc, 'aclPanel', HTMLDivElement), aclWrap = element(doc, 'aclTableWrap', HTMLDivElement);
    const grantForm = element(doc, 'addAclForm', HTMLDivElement), grantButton = element(doc, 'addAclBtn', HTMLButtonElement);
    const groupSearch = element(doc, 'aclGroupSearch', HTMLInputElement), groupSelect = element(doc, 'aclGroupSelect', HTMLSelectElement);
    const groupPager = element(doc, 'aclGroupPager', HTMLDivElement), permissionSelect = element(doc, 'aclPermSelect', HTMLSelectElement);
    const grantSave = element(doc, 'acl-saveBtn', HTMLButtonElement), grantError = element(doc, 'acl-error', HTMLDivElement);
    const sessionKey = () => {
        try {
            const identity = getSessionIdentity(auth.getToken());
            return JSON.stringify([identity.subject, identity.roles, identity.claims.tenant_id, identity.claims.service_account_id]);
        }
        catch {
            return null;
        }
    };
    const identity = sessionKey(), listeners = new AbortController();
    let folders = [], groups = [], groupPage = 1;
    let folderRead = 0, aclRead = 0, editorSequence = 0, grantSequence = 0, groupRead = 0;
    let openedFolder = null, scope = null, grant = null;
    let disposed = false, busy = false, foldersReady = false, groupsReady = false;
    let unconfirmed = null;
    const alive = () => !disposed && identity !== null && sessionKey() === identity;
    const listen = (target, event, handler) => target.addEventListener(event, handler, { signal: listeners.signal });
    const showError = (box, message) => { box.textContent = message; box.classList.add('show'); };
    function validParent() { return !parent.value || folders.some(folder => String(folder.id) === parent.value && !excludedParents().has(folder.id)); }
    function updateControls() {
        const locked = busy || !!unconfirmed || !alive();
        save.disabled = locked || !foldersReady || !validParent();
        parent.disabled = !foldersReady || busy;
        wrap.querySelectorAll('[data-action]').forEach(button => { button.disabled = !alive() || (locked && button.dataset.action === 'delete'); });
        grantButton.disabled = locked || !scope?.snapshot;
        aclWrap.querySelectorAll('[data-gid]').forEach(button => { button.disabled = locked || !scope?.snapshot; });
        grantSave.disabled = locked || !groupsReady || !grant || !scope?.snapshot || scope.id !== grant.folderId
            || !groups.some(group => String(group.id) === groupSelect.value)
            || !grant.permissions.some(option => String(option.permission) === permissionSelect.value);
        groupSelect.disabled = !groupsReady || busy;
        permissionSelect.disabled = !grant || busy;
        mutationStatus.hidden = !unconfirmed;
        mutationStatus.textContent = unconfirmed?.message || '';
    }
    function excludedParents() {
        const ids = new Set();
        if (!openedFolder)
            return ids;
        ids.add(openedFolder.id);
        let changed = true;
        while (changed) {
            changed = false;
            for (const folder of folders)
                if (folder.parentId != null && ids.has(folder.parentId) && !ids.has(folder.id)) {
                    ids.add(folder.id);
                    changed = true;
                }
        }
        return ids;
    }
    function renderParents(previous = parent.value) {
        const focused = doc.activeElement === parent, excluded = excludedParents();
        parent.replaceChildren(new Option('— Root —', ''), ...folders.filter(folder => !excluded.has(folder.id))
            .map(folder => new Option(folder.path, String(folder.id))));
        if (previous && ![...parent.options].some(option => option.value === previous)) {
            const unavailable = new Option(`Unavailable parent: ${previous}`, previous);
            unavailable.disabled = true;
            parent.append(unavailable);
        }
        parent.value = previous;
        if (focused)
            parent.focus();
    }
    function closeEditor() { ++editorSequence; openedFolder = null; form.style.display = 'none'; }
    function closeGrant() {
        ++grantSequence;
        ++groupRead;
        grant = null;
        groupsReady = false;
        groups = [];
        grantForm.style.display = 'none';
        groupSelect.replaceChildren();
        groupPager.replaceChildren();
        updateControls();
    }
    function openEditor(folder) {
        if (!alive())
            return;
        ++editorSequence;
        openedFolder = folder ? structuredClone(folder) : null;
        editorError.classList.remove('show');
        element(doc, 'nf-title', HTMLHeadingElement).textContent = folder ? 'Edit Folder' : 'Create Folder';
        save.textContent = folder ? 'Save Changes' : 'Create';
        name.value = folder?.name || '';
        element(doc, 'nf-id', HTMLInputElement).value = folder ? String(folder.id) : '';
        renderParents(folder?.parentId ? String(folder.parentId) : '');
        form.style.display = '';
        updateControls();
        name.focus();
    }
    async function reload() {
        if (!alive())
            return false;
        const sequence = ++folderRead;
        foldersReady = false;
        wrap.dataset.foldersState = 'loading';
        updateControls();
        try {
            const result = await adminFoldersApi.tree();
            if (!alive() || sequence !== folderRead)
                return false;
            folders = result.folders;
            foldersReady = true;
            wrap.innerHTML = `<table class="data-table"><thead><tr><th>Folder Path</th><th>Actions</th></tr></thead><tbody>${folders.map(folder => `<tr>
        <td>${escape(folder.path)}</td><td><div class="table-actions">
          <button class="btn btn-outline btn-sm" data-action="edit-folder" data-id="${folder.id}">Edit</button>
          <button class="btn btn-outline btn-sm" data-action="acl" data-id="${folder.id}">Permissions</button>
          <button class="btn btn-danger btn-sm" data-action="delete" data-id="${folder.id}">Delete</button>
        </div></td></tr>`).join('') || '<tr><td colspan="2" class="empty-state">No folders.</td></tr>'}</tbody></table>`;
            wrap.querySelectorAll('[data-action]').forEach(button => listen(button, 'click', () => { void folderAction(button); }));
            renderParents();
            wrap.dataset.foldersState = 'ready';
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || sequence !== folderRead)
                return false;
            folders = [];
            foldersReady = false;
            wrap.dataset.foldersState = 'failed';
            wrap.innerHTML = failedState({ title: 'Folders could not be loaded.', body: asError(error).message });
            updateControls();
            return false;
        }
    }
    async function reloadAcl() {
        if (!alive() || !scope)
            return false;
        const context = scope, sequence = ++aclRead;
        context.snapshot = null;
        aclWrap.dataset.aclState = 'loading';
        aclWrap.innerHTML = loadingState('Loading permissions…');
        updateControls();
        try {
            const result = await adminFoldersApi.acl(context.id);
            if (!alive() || sequence !== aclRead || scope !== context)
                return false;
            context.snapshot = result;
            context.name = result.folderName;
            element(doc, 'aclFolderName', HTMLSpanElement).textContent = result.folderName;
            aclWrap.innerHTML = `<table class="data-table"><thead><tr><th>Group</th><th>Permission</th><th>Actions</th></tr></thead><tbody>${result.items.map(item => `<tr>
        <td>${escape(item.groupName)}</td><td>${escape(result.permissions.find(option => option.permission === item.permission).name)}</td>
        <td><button class="btn btn-danger btn-sm" data-gid="${item.groupId}">Revoke</button></td>
        </tr>`).join('') || '<tr><td colspan="3" class="empty-state">No permissions set.</td></tr>'}</tbody></table>`;
            aclWrap.querySelectorAll('[data-gid]').forEach(button => listen(button, 'click', () => { void revoke(Number(button.dataset.gid)); }));
            aclWrap.dataset.aclState = 'ready';
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || sequence !== aclRead || scope !== context)
                return false;
            aclWrap.dataset.aclState = 'failed';
            aclWrap.innerHTML = failedState({ title: 'Folder permissions could not be loaded.', body: asError(error).message });
            if (error instanceof PortalApiError && error.status === 404) {
                closeGrant();
                scope = null;
                aclPanel.style.display = 'none';
                updateControls();
                return true;
            }
            updateControls();
            return false;
        }
    }
    async function refresh() {
        if (!alive() || busy)
            return;
        const outcome = unconfirmed;
        if (!await reload() || !alive() || busy)
            return;
        let confirmed = true;
        if (outcome?.folderId != null && scope?.id !== outcome.folderId) {
            try {
                await adminFoldersApi.acl(outcome.folderId);
            }
            catch (error) {
                confirmed = error instanceof PortalApiError && error.status === 404;
            }
        }
        if (scope)
            confirmed = await reloadAcl() && confirmed;
        if (alive() && !busy && outcome === unconfirmed && confirmed) {
            unconfirmed = null;
            if (outcome && grant && scope?.snapshot && grant.folderId === outcome.folderId && grant.folderId === scope.id) {
                grant.version = scope.snapshot.version;
                grantError.classList.remove('show');
            }
        }
        updateControls();
    }
    async function perform(action, editor = null, grantEditor = null) {
        if (!alive() || busy || unconfirmed)
            return;
        busy = true;
        ++folderRead;
        ++aclRead;
        updateControls();
        try {
            await action();
        }
        catch (error) {
            if (!alive())
                return;
            if (error instanceof AdminFolderOutcomeUnconfirmed)
                unconfirmed = error;
            if (editor !== null && editor === editorSequence && form.style.display !== 'none')
                showError(editorError, asError(error).message);
            else {
                if (grantEditor !== null && grantEditor === grantSequence && grant)
                    showError(grantError, asError(error).message);
                feedback.notify(asError(error).message, { title: 'Request failed', tone: 'error' });
            }
        }
        finally {
            busy = false;
            if (alive())
                updateControls();
        }
    }
    async function folderAction(button) {
        const folder = folders.find(item => item.id === Number(button.dataset.id));
        if (!alive() || !folder)
            return;
        if (button.dataset.action === 'edit-folder') {
            openEditor(folder);
            return;
        }
        if (button.dataset.action === 'acl') {
            closeGrant();
            ++aclRead;
            scope = { id: folder.id, name: folder.name, snapshot: null };
            element(doc, 'aclFolderName', HTMLSpanElement).textContent = folder.name;
            aclPanel.style.display = '';
            await reloadAcl();
            return;
        }
        await perform(async () => {
            if (!await feedback.confirm('Delete this folder?', { title: 'Delete folder and reports', impact: 'Descendant folders and reports inside them will also be removed.', confirmLabel: 'Delete folder', danger: true, auditAction: 'admin.folder.delete' }) || !alive())
                return;
            const deleted = new Set([folder.id]);
            let changed = true;
            while (changed) {
                changed = false;
                for (const child of folders)
                    if (child.parentId != null && deleted.has(child.parentId) && !deleted.has(child.id)) {
                        deleted.add(child.id);
                        changed = true;
                    }
            }
            await adminFoldersApi.delete(folder);
            if (!alive())
                return;
            if (scope && deleted.has(scope.id)) {
                closeGrant();
                ++aclRead;
                scope = null;
                aclPanel.style.display = 'none';
            }
            await reload();
        });
    }
    async function saveEditor() {
        if (!foldersReady || !validParent() || form.style.display === 'none')
            return;
        const sequence = editorSequence, folder = openedFolder;
        const body = { name: name.value.trim(), parentId: parent.value ? Number(parent.value) : null };
        editorError.classList.remove('show');
        if (!body.name) {
            showError(editorError, 'Name is required.');
            return;
        }
        await perform(async () => {
            if (folder)
                await adminFoldersApi.update(folder, body);
            else
                await adminFoldersApi.create(body);
            if (!alive())
                return;
            if (sequence === editorSequence)
                closeEditor();
            if (folder && scope?.id === folder.id)
                await reloadAcl();
            await reload();
        }, sequence);
    }
    async function loadGroups() {
        if (!alive() || !grant)
            return;
        const context = grant, sequence = ++groupRead, previous = groupSelect.value;
        groupsReady = false;
        grantError.classList.remove('show');
        updateControls();
        try {
            const result = await adminGroupsApi.catalog(groupSearch.value.trim(), groupPage);
            if (!alive() || sequence !== groupRead || grant !== context)
                return;
            groups = result.items;
            groupsReady = true;
            groupSelect.replaceChildren(new Option('Select group…', ''), ...groups.map(group => new Option(group.name, String(group.id))));
            groupSelect.value = groups.some(group => String(group.id) === previous) ? previous : '';
            renderCatalogPager(groupPager, result, page => { groupPage = page; void loadGroups(); });
        }
        catch (error) {
            if (!alive() || sequence !== groupRead || grant !== context)
                return;
            groups = [];
            groupSelect.replaceChildren();
            groupPager.replaceChildren();
            showError(grantError, `Groups could not be loaded. Reload groups to retry. ${asError(error).message}`);
        }
        updateControls();
    }
    async function openGrant() {
        if (!alive() || busy || unconfirmed || !scope?.snapshot)
            return;
        ++grantSequence;
        grant = { folderId: scope.id, version: scope.snapshot.version, permissions: structuredClone(scope.snapshot.permissions) };
        groupPage = 1;
        groupSearch.value = '';
        groupSelect.replaceChildren();
        permissionSelect.replaceChildren(...grant.permissions.map(option => new Option(option.name, String(option.permission))));
        grantForm.style.display = '';
        groupSearch.focus();
        await loadGroups();
    }
    async function saveGrant() {
        if (!grant || !groupsReady || grantForm.style.display === 'none')
            return;
        const context = grant, sequence = grantSequence;
        const group = groups.find(item => String(item.id) === groupSelect.value);
        const permission = context.permissions.find(option => String(option.permission) === permissionSelect.value);
        if (!group || !permission || !scope?.snapshot || scope.id !== context.folderId)
            return;
        grantError.classList.remove('show');
        await perform(async () => {
            await adminFoldersApi.grant(context.folderId, { groupId: group.id, permission: permission.permission }, context.version);
            if (!alive())
                return;
            if (sequence === grantSequence)
                closeGrant();
            if (scope?.id === context.folderId)
                await reloadAcl();
            await reload();
        }, null, sequence);
    }
    async function revoke(groupId) {
        if (!scope?.snapshot || !scope.snapshot.items.some(item => item.groupId === groupId))
            return;
        const context = { id: scope.id, name: scope.name, version: scope.snapshot.version };
        await perform(async () => {
            if (!await feedback.confirm(`Revoke this group's permission on ${context.name}?`, { title: 'Revoke folder permission', confirmLabel: 'Revoke permission', danger: true, auditAction: 'admin.folder.permission.revoke' }) || !alive())
                return;
            await adminFoldersApi.revoke(context.id, groupId, context.version);
            if (!alive())
                return;
            if (scope?.id === context.id)
                await reloadAcl();
            await reload();
        });
    }
    listen(element(doc, 'newFolderBtn', HTMLButtonElement), 'click', () => openEditor(null));
    listen(element(doc, 'nf-cancelBtn', HTMLButtonElement), 'click', closeEditor);
    listen(parent, 'change', updateControls);
    listen(save, 'click', () => { void saveEditor(); });
    listen(element(doc, 'foldersRefreshBtn', HTMLButtonElement), 'click', () => { void refresh(); });
    listen(element(doc, 'aclRefreshBtn', HTMLButtonElement), 'click', () => { void refresh(); });
    listen(grantButton, 'click', () => { void openGrant(); });
    listen(groupSearch, 'input', () => { groupPage = 1; void loadGroups(); });
    listen(groupSelect, 'change', updateControls);
    listen(permissionSelect, 'change', updateControls);
    listen(element(doc, 'acl-reloadBtn', HTMLButtonElement), 'click', () => { void loadGroups(); });
    listen(element(doc, 'acl-cancelBtn', HTMLButtonElement), 'click', closeGrant);
    listen(grantSave, 'click', () => { void saveGrant(); });
    updateControls();
    return { reload, dispose() {
            if (disposed)
                return;
            disposed = true;
            ++folderRead;
            ++aclRead;
            closeEditor();
            closeGrant();
            folders = [];
            scope = null;
            listeners.abort();
        } };
}
