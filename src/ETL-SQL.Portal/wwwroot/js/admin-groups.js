/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-groups.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { auth, PortalApiError } from './portal-http.js';
import { adminGroupsApi, AdminGroupOutcomeUnconfirmed } from './admin-groups-api.js';
import { adminUsersApi } from './admin-users-api.js';
import { bindSelection, headerSelectionCell, renderCatalogPager, selectedIds, selectionCell } from './admin-catalog-ui.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { failedState, loadingState } from './portal-states.js';
function element(doc, id, kind) {
    const value = doc.getElementById(id);
    if (!(value instanceof kind))
        throw new Error(`Missing admin group element: ${id}`);
    return value;
}
const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
/** Own group drafts, membership snapshots, request ordering, and mutation confirmation. */
export function createAdminGroups(doc = document) {
    const feedback = window.ETLSQLFeedback;
    if (!feedback)
        throw new Error('Portal feedback must be initialized before group administration.');
    const wrap = element(doc, 'groupTableWrap', HTMLDivElement), pager = element(doc, 'groupPager', HTMLDivElement);
    const filter = element(doc, 'groupFilter', HTMLInputElement), deleteSelected = element(doc, 'groupsDeleteBtn', HTMLButtonElement);
    const form = element(doc, 'newGroupForm', HTMLDivElement), errorBox = element(doc, 'ng-error', HTMLDivElement);
    const name = element(doc, 'ng-name', HTMLInputElement), description = element(doc, 'ng-desc', HTMLInputElement);
    const save = element(doc, 'ng-saveBtn', HTMLButtonElement), mutationStatus = element(doc, 'groupsMutationStatus', HTMLDivElement);
    const membersPanel = element(doc, 'membersPanel', HTMLDivElement), membersWrap = element(doc, 'memberTableWrap', HTMLDivElement);
    const membersPager = element(doc, 'memberPager', HTMLDivElement), memberFilter = element(doc, 'memberFilter', HTMLInputElement);
    const removeSelected = element(doc, 'membersRemoveBtn', HTMLButtonElement), add = element(doc, 'addMemberBtn', HTMLButtonElement);
    const pickerForm = element(doc, 'addMemberForm', HTMLDivElement), pickerSearch = element(doc, 'addMemberSearch', HTMLInputElement);
    const matches = element(doc, 'addMemberSelect', HTMLSelectElement), matchesPager = element(doc, 'addMemberPager', HTMLDivElement);
    const pickerSave = element(doc, 'am-saveBtn', HTMLButtonElement), pickerError = element(doc, 'am-error', HTMLDivElement);
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
    let groups = [], groupPage = 1, memberPage = 1, pickerPage = 1;
    let groupRead = 0, memberRead = 0, editorSequence = 0, pickerSequence = 0, pickerRead = 0;
    let openedGroup = null, membership = null, picker = null;
    let disposed = false, busy = false, matchesReady = false;
    let unconfirmed = null;
    const alive = () => !disposed && identity !== null && sessionKey() === identity;
    const listen = (target, event, handler) => target.addEventListener(event, handler, { signal: listeners.signal });
    function updateControls() {
        const locked = busy || !!unconfirmed || !alive();
        save.disabled = locked;
        deleteSelected.disabled = locked || selectedIds(wrap).length === 0;
        wrap.querySelectorAll('[data-action]').forEach(button => {
            button.disabled = !alive() || (locked && button.dataset.action === 'delete');
        });
        removeSelected.disabled = locked || membership?.version == null || selectedIds(membersWrap).length === 0;
        add.disabled = locked || membership?.version == null;
        membersWrap.querySelectorAll('[data-uid]').forEach(button => { button.disabled = locked || membership?.version == null; });
        pickerSave.disabled = locked || !matchesReady || !picker || membership?.version == null
            || membership.id !== picker.groupId || matches.selectedOptions.length === 0;
        matches.disabled = !matchesReady || busy;
        mutationStatus.hidden = !unconfirmed;
        mutationStatus.textContent = unconfirmed?.message || '';
    }
    function editorError(message) { errorBox.textContent = message; errorBox.classList.add('show'); }
    function closeEditor() { ++editorSequence; openedGroup = null; form.style.display = 'none'; }
    function closePicker() {
        ++pickerSequence;
        ++pickerRead;
        picker = null;
        matchesReady = false;
        matches.replaceChildren();
        matchesPager.replaceChildren();
        pickerForm.style.display = 'none';
        updateControls();
    }
    function openEditor(group) {
        if (!alive())
            return;
        ++editorSequence;
        openedGroup = group ? structuredClone(group) : null;
        errorBox.classList.remove('show');
        element(doc, 'ng-title', HTMLHeadingElement).textContent = group ? 'Edit Group' : 'Create Group';
        save.textContent = group ? 'Save Changes' : 'Create';
        name.value = group?.name || '';
        description.value = group?.description || '';
        element(doc, 'ng-id', HTMLInputElement).value = group ? String(group.id) : '';
        form.style.display = '';
        updateControls();
        name.focus();
    }
    async function reload() {
        if (!alive())
            return false;
        const sequence = ++groupRead, requestedPage = groupPage;
        wrap.dataset.groupsState = 'loading';
        try {
            const result = await adminGroupsApi.catalog(filter.value.trim(), requestedPage);
            if (!alive() || sequence !== groupRead)
                return false;
            groups = result.items;
            wrap.innerHTML = `<table class="data-table"><thead><tr>${headerSelectionCell('groups')}<th>Group Name</th><th>Description</th><th>Members</th><th>Actions</th></tr></thead><tbody>${groups.map(group => `<tr>
        ${selectionCell(group.id, `group ${escape(group.name)}`)}<td>${escape(group.name)}</td>
        <td class="text-sm text-muted">${escape(group.description || '')}</td><td>${group.memberCount}</td>
        <td><div class="table-actions"><button class="btn btn-outline btn-sm" data-action="edit-group" data-id="${group.id}">Edit</button>
          <button class="btn btn-outline btn-sm" data-action="members" data-id="${group.id}">Members</button>
          <button class="btn btn-danger btn-sm" data-action="delete" data-id="${group.id}">Delete</button></div></td>
        </tr>`).join('') || '<tr><td colspan="5" class="empty-state">No groups.</td></tr>'}</tbody></table>`;
            wrap.querySelectorAll('[data-action]').forEach(button => listen(button, 'click', () => { void groupAction(button); }));
            bindSelection(wrap, updateControls);
            renderCatalogPager(pager, result, page => { groupPage = page; void reload(); });
            wrap.dataset.groupsState = 'ready';
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || sequence !== groupRead)
                return false;
            groups = [];
            wrap.dataset.groupsState = 'failed';
            wrap.innerHTML = failedState({ title: 'Groups could not be loaded.', body: asError(error).message });
            pager.replaceChildren();
            updateControls();
            return false;
        }
    }
    async function reloadMembers() {
        if (!alive() || !membership)
            return false;
        const context = membership, sequence = ++memberRead, requestedPage = memberPage;
        context.version = null;
        membersWrap.dataset.membersState = 'loading';
        membersWrap.innerHTML = loadingState('Loading members…');
        membersPager.replaceChildren();
        updateControls();
        try {
            const result = await adminGroupsApi.members(context.id, memberFilter.value.trim(), requestedPage);
            if (!alive() || sequence !== memberRead || membership !== context)
                return false;
            context.version = result.version;
            membersWrap.innerHTML = `<table class="data-table"><thead><tr>${headerSelectionCell('members')}<th>Username</th><th>Email</th><th>Status</th><th>Actions</th></tr></thead><tbody>${result.items.map(member => `<tr>
        ${selectionCell(member.id, `member ${escape(member.username)}`)}<td>${escape(member.username)}</td><td>${escape(member.email || '')}</td>
        <td><span class="chip ${member.isActive ? 'chip-active' : 'chip-inactive'}">${member.isActive ? 'Active' : 'Inactive'}</span></td>
        <td><button class="btn btn-danger btn-sm" data-uid="${member.id}">Remove</button></td></tr>`).join('') || '<tr><td colspan="5" class="empty-state">No members.</td></tr>'}</tbody></table>`;
            membersWrap.querySelectorAll('[data-uid]').forEach(button => listen(button, 'click', () => {
                void removeMembers([Number(button.dataset.uid)], true);
            }));
            bindSelection(membersWrap, updateControls);
            renderCatalogPager(membersPager, result, page => { memberPage = page; void reloadMembers(); });
            membersWrap.dataset.membersState = 'ready';
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || sequence !== memberRead || membership !== context)
                return false;
            membersWrap.dataset.membersState = 'failed';
            membersWrap.innerHTML = failedState({ title: 'Group members could not be loaded.', body: asError(error).message });
            if (error instanceof PortalApiError && error.status === 404) {
                closePicker();
                membership = null;
                membersPanel.style.display = 'none';
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
        let membershipRead = true;
        if (outcome?.groupId != null && membership?.id !== outcome.groupId) {
            try {
                await adminGroupsApi.members(outcome.groupId, '', 1);
            }
            catch (error) {
                membershipRead = error instanceof PortalApiError && error.status === 404;
            }
        }
        if (membership)
            membershipRead = await reloadMembers() && membershipRead;
        if (alive() && !busy && outcome === unconfirmed && membershipRead) {
            unconfirmed = null;
            if (picker && picker.groupId === membership?.id && membership.version !== null)
                picker.version = membership.version;
        }
        updateControls();
    }
    async function perform(action, editor = null, openedPicker = null) {
        if (!alive() || busy || unconfirmed)
            return;
        busy = true;
        ++groupRead;
        ++memberRead;
        updateControls();
        try {
            await action();
        }
        catch (error) {
            if (!alive())
                return;
            if (error instanceof AdminGroupOutcomeUnconfirmed)
                unconfirmed = error;
            if (editor !== null && editor === editorSequence && form.style.display !== 'none')
                editorError(asError(error).message);
            else {
                if (openedPicker !== null && openedPicker === pickerSequence && picker) {
                    pickerError.textContent = asError(error).message;
                    pickerError.classList.add('show');
                }
                feedback.notify(asError(error).message, { title: 'Request failed', tone: 'error' });
            }
        }
        finally {
            busy = false;
            if (alive())
                updateControls();
        }
    }
    async function groupAction(button) {
        const group = groups.find(item => item.id === Number(button.dataset.id));
        if (!group || !alive())
            return;
        if (button.dataset.action === 'edit-group') {
            openEditor(group);
            return;
        }
        if (button.dataset.action === 'members') {
            closePicker();
            ++memberRead;
            memberPage = 1;
            memberFilter.value = '';
            membership = { id: group.id, name: group.name, version: null };
            element(doc, 'memberGroupName', HTMLSpanElement).textContent = group.name;
            membersPanel.style.display = '';
            await reloadMembers();
            return;
        }
        await perform(async () => {
            if (!await feedback.confirm('Delete this group?', { title: 'Delete group', confirmLabel: 'Delete group', danger: true, auditAction: 'admin.group.delete' }) || !alive())
                return;
            await adminGroupsApi.delete(group);
            if (!alive())
                return;
            if (membership?.id === group.id) {
                closePicker();
                ++memberRead;
                membership = null;
                membersPanel.style.display = 'none';
            }
            await reload();
        });
    }
    async function saveEditor() {
        if (form.style.display === 'none')
            return;
        const sequence = editorSequence, group = openedGroup;
        const body = { name: name.value.trim(), description: description.value.trim() };
        errorBox.classList.remove('show');
        if (!body.name) {
            editorError('Name is required.');
            return;
        }
        await perform(async () => {
            const receipt = group ? await adminGroupsApi.update(group, body) : await adminGroupsApi.create(body);
            if (!alive())
                return;
            if (sequence === editorSequence)
                closeEditor();
            if (group && membership?.id === group.id) {
                membership.name = receipt.name;
                element(doc, 'memberGroupName', HTMLSpanElement).textContent = receipt.name;
                await reloadMembers();
            }
            await reload();
        }, sequence);
    }
    async function bulkDelete() {
        const targets = selectedIds(wrap).map(id => groups.find(group => group.id === id));
        if (!targets.length || targets.some(group => !group))
            return;
        await perform(async () => {
            if (!await feedback.confirm('Delete the selected groups?', { title: 'Delete groups', impact: 'Groups with members or permission entries will be rejected.', confirmLabel: 'Delete groups', danger: true, auditAction: 'admin.group.bulk-delete' }) || !alive())
                return;
            const result = await adminGroupsApi.bulkDelete(targets.filter((group) => !!group).map(group => ({ id: group.id, version: group.version })));
            if (!alive())
                return;
            const skipped = result.results.filter(item => item.status !== 'Deleted').length;
            feedback.notify(`${result.deleted} groups deleted.${skipped ? ` ${skipped} groups were not deleted; review the refreshed list.` : ''}`, { title: 'Group deletion', tone: skipped ? 'warning' : 'success' });
            if (membership && result.results.some(item => item.id === membership.id && item.status === 'Deleted')) {
                closePicker();
                ++memberRead;
                membership = null;
                membersPanel.style.display = 'none';
            }
            await reload();
        });
    }
    async function loadMatches() {
        if (!alive() || !picker)
            return;
        const context = picker, sequence = ++pickerRead, requestedPage = pickerPage;
        matchesReady = false;
        pickerError.classList.remove('show');
        updateControls();
        try {
            const result = await adminUsersApi.catalog(pickerSearch.value.trim(), 'active', requestedPage);
            if (!alive() || sequence !== pickerRead || picker !== context)
                return;
            matches.replaceChildren(...result.items.map(user => new Option(`${user.username}${user.email ? ` — ${user.email}` : ''}`, String(user.id))));
            renderCatalogPager(matchesPager, result, page => { pickerPage = page; void loadMatches(); });
            matchesReady = true;
        }
        catch (error) {
            if (!alive() || sequence !== pickerRead || picker !== context)
                return;
            matches.replaceChildren();
            matchesPager.replaceChildren();
            pickerError.textContent = `Users could not be loaded. Reload matches to retry. ${asError(error).message}`;
            pickerError.classList.add('show');
        }
        updateControls();
    }
    async function openPicker() {
        if (!alive() || busy || unconfirmed || membership?.version == null)
            return;
        ++pickerSequence;
        picker = { groupId: membership.id, version: membership.version };
        pickerPage = 1;
        pickerSearch.value = '';
        pickerForm.style.display = '';
        pickerSearch.focus();
        await loadMatches();
    }
    async function addMembers() {
        if (!picker || !matchesReady || pickerForm.style.display === 'none')
            return;
        const context = picker, sequence = pickerSequence, ids = [...matches.selectedOptions].map(option => Number(option.value));
        if (!ids.length)
            return;
        await perform(async () => {
            const receipt = await adminGroupsApi.addMembers(context.groupId, ids, context.version);
            if (!alive())
                return;
            feedback.notify(`${receipt.added} users added.${receipt.skippedUserIds.length ? ` ${receipt.skippedUserIds.length} users were not added; review membership.` : ''}`, { title: 'Group membership', tone: receipt.skippedUserIds.length ? 'warning' : 'success' });
            if (sequence === pickerSequence)
                closePicker();
            if (membership?.id === context.groupId)
                await reloadMembers();
            await reload();
        }, null, sequence);
    }
    async function removeMembers(ids, single = false) {
        if (!ids.length || membership?.version == null)
            return;
        const context = { id: membership.id, version: membership.version, name: membership.name };
        await perform(async () => {
            if (!await feedback.confirm(`Remove ${single ? 'this user' : 'the selected users'} from ${context.name}?`, { title: 'Remove group members', confirmLabel: 'Remove users', danger: true, auditAction: 'admin.group.members.remove' }) || !alive())
                return;
            if (single)
                await adminGroupsApi.removeMember(context.id, ids[0], context.version);
            else {
                const receipt = await adminGroupsApi.removeMembers(context.id, ids, context.version);
                if (alive())
                    feedback.notify(`${receipt.removed} users removed.${receipt.skippedUserIds.length ? ` ${receipt.skippedUserIds.length} users were not removed; review membership.` : ''}`, { title: 'Group membership', tone: receipt.skippedUserIds.length ? 'warning' : 'success' });
            }
            if (!alive())
                return;
            if (membership?.id === context.id)
                await reloadMembers();
            await reload();
        });
    }
    listen(filter, 'input', () => { groupPage = 1; void reload(); });
    listen(element(doc, 'groupsRefreshBtn', HTMLButtonElement), 'click', () => { void refresh(); });
    listen(element(doc, 'membersRefreshBtn', HTMLButtonElement), 'click', () => { void refresh(); });
    listen(deleteSelected, 'click', () => { void bulkDelete(); });
    listen(element(doc, 'newGroupBtn', HTMLButtonElement), 'click', () => openEditor(null));
    listen(element(doc, 'ng-cancelBtn', HTMLButtonElement), 'click', closeEditor);
    listen(save, 'click', () => { void saveEditor(); });
    listen(memberFilter, 'input', () => { memberPage = 1; void reloadMembers(); });
    listen(removeSelected, 'click', () => { void removeMembers(selectedIds(membersWrap)); });
    listen(add, 'click', () => { void openPicker(); });
    listen(pickerSearch, 'input', () => { pickerPage = 1; void loadMatches(); });
    listen(matches, 'change', updateControls);
    listen(element(doc, 'am-reloadBtn', HTMLButtonElement), 'click', () => { void loadMatches(); });
    listen(element(doc, 'am-cancelBtn', HTMLButtonElement), 'click', closePicker);
    listen(pickerSave, 'click', () => { void addMembers(); });
    updateControls();
    return { reload, dispose() {
            if (disposed)
                return;
            disposed = true;
            ++groupRead;
            ++memberRead;
            closeEditor();
            closePicker();
            groups = [];
            membership = null;
            listeners.abort();
        } };
}
