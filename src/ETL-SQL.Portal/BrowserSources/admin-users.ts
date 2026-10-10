import { auth } from './api.js';
import { adminUsersApi, AdminUserOutcomeUnconfirmed } from './admin-users-api.js';
import { bindSelection, headerSelectionCell, renderCatalogPager, selectedIds, selectionCell } from './admin-catalog-ui.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';

function element<T extends HTMLElement>(doc: Document, id: string, kind: new (...args: never[]) => T): T {
  const value = doc.getElementById(id);
  if (!(value instanceof kind)) throw new Error(`Missing admin user element: ${id}`);
  return value;
}
const escape = (value: string) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
const roleClass = (name: string) => /^[a-z][a-z0-9_-]*$/.test(name.toLowerCase()) ? `chip chip-${name.toLowerCase()}` : 'chip';

/** Own the user directory, editor drafts, request ordering, and mutation confirmation. */
export function createAdminUsers(doc: Document = document) {
  const feedback = window.ETLSQLFeedback;
  if (!feedback) throw new Error('Portal feedback must be initialized before user administration.');
  const wrap = element(doc, 'userTableWrap', HTMLDivElement), pager = element(doc, 'userPager', HTMLDivElement);
  const filter = element(doc, 'userFilter', HTMLInputElement), status = element(doc, 'userStatusFilter', HTMLSelectElement);
  const form = element(doc, 'newUserForm', HTMLDivElement), errorBox = element(doc, 'nu-error', HTMLDivElement);
  const username = element(doc, 'nu-username', HTMLInputElement), email = element(doc, 'nu-email', HTMLInputElement);
  const password = element(doc, 'nu-password', HTMLInputElement), role = element(doc, 'nu-role', HTMLSelectElement);
  const save = element(doc, 'nu-saveBtn', HTMLButtonElement), cancel = element(doc, 'nu-cancelBtn', HTMLButtonElement);
  const reloadRolesButton = element(doc, 'nu-reloadRolesBtn', HTMLButtonElement);
  const enable = element(doc, 'usersEnableBtn', HTMLButtonElement), disable = element(doc, 'usersDisableBtn', HTMLButtonElement);
  const refresh = element(doc, 'usersRefreshBtn', HTMLButtonElement), mutationStatus = element(doc, 'usersMutationStatus', HTMLDivElement);
  const sessionKey = () => {
    try {
      const identity = getSessionIdentity(auth.getToken());
      return JSON.stringify([identity.subject, identity.roles, identity.claims.tenant_id, identity.claims.service_account_id]);
    } catch { return null; }
  };
  const listeners = new AbortController(), identity = sessionKey();
  let users: UserDto[] = [], page = 1, readSequence = 0, editorSequence = 0, rolesSequence = 0;
  let preferredRole: string | null = null;
  let disposed = false, busy = false, unconfirmed = false, rolesReady = false;
  let openedUser: UserDto | null = null;
  const alive = () => !disposed && identity !== null && sessionKey() === identity;
  const listen = (target: EventTarget, name: string, handler: EventListener) =>
    target.addEventListener(name, handler, { signal: listeners.signal });

  function updateControls() {
    const locked = busy || unconfirmed || !alive();
    enable.disabled = disable.disabled = locked || selectedIds(wrap).length === 0;
    wrap.querySelectorAll<HTMLButtonElement>('[data-action]').forEach(button => { button.disabled = locked; });
    save.disabled = locked || !rolesReady;
    role.disabled = !rolesReady || busy;
    reloadRolesButton.disabled = busy || !alive();
    mutationStatus.hidden = !unconfirmed;
    mutationStatus.textContent = unconfirmed ? 'User change outcome is unconfirmed. Refresh users before trying another change.' : '';
  }
  function showEditorError(message: string) {
    errorBox.textContent = message;
    errorBox.classList.add('show');
  }
  function reportError(error: unknown, editor: number | null) {
    if (!alive()) return;
    if (error instanceof AdminUserOutcomeUnconfirmed) unconfirmed = true;
    if (editor !== null && editor === editorSequence && form.style.display !== 'none') showEditorError(asError(error).message);
    else feedback!.notify(asError(error).message, { title: 'Request failed', tone: 'error' });
  }

  async function reload(explicit = false) {
    if (!alive()) return;
    const sequence = ++readSequence, requestedPage = page;
    wrap.dataset.usersState = 'loading';
    try {
      const result = await adminUsersApi.catalog(filter.value.trim(), status.value, requestedPage);
      if (!alive() || sequence !== readSequence) return;
      users = result.items;
      wrap.innerHTML = `<table class="data-table"><thead><tr>${headerSelectionCell('users')}<th>Username</th><th>Email</th><th>Roles</th><th>Status</th><th>Actions</th></tr></thead><tbody>${users.map(user => `<tr>
        ${selectionCell(user.id, `user ${escape(user.username)}`)}
        <td>${escape(user.username)}</td><td>${escape(user.email || '')}</td>
        <td>${user.roles.map(name => `<span class="${roleClass(name)}">${escape(name)}</span>`).join(' ')}</td>
        <td><span class="chip ${user.isActive ? 'chip-active' : 'chip-inactive'}">${user.isActive ? 'Active' : 'Inactive'}</span></td>
        <td><div class="table-actions">
          <button class="btn btn-outline btn-sm" data-action="edit-user" data-id="${user.id}">Edit</button>
          <button class="btn btn-outline btn-sm" data-action="toggle" data-id="${user.id}">${user.isActive ? 'Disable' : 'Enable'}</button>
          <button class="btn btn-outline btn-sm" data-action="reset" data-id="${user.id}">Reset Pwd</button>
          <button class="btn btn-danger btn-sm" data-action="delete" data-id="${user.id}">Delete</button>
        </div></td></tr>`).join('') || '<tr><td colspan="6" class="empty-state">No users.</td></tr>'}</tbody></table>`;
      wrap.querySelectorAll<HTMLButtonElement>('[data-action]').forEach(button => {
        listen(button, 'click', () => { void handleAction(button); });
      });
      bindSelection(wrap, updateControls);
      renderCatalogPager(pager, result, next => { page = next; void reload(); });
      if (explicit && !busy) unconfirmed = false;
      wrap.dataset.usersState = 'ready';
      updateControls();
    } catch (error) {
      if (!alive() || sequence !== readSequence) return;
      users = [];
      wrap.dataset.usersState = 'failed';
      wrap.textContent = `Failed to load users. ${asError(error).message}`;
      pager.replaceChildren();
      updateControls();
    }
  }

  async function openEditor(user: UserDto | null) {
    if (!alive()) return;
    const sequence = ++editorSequence;
    openedUser = user ? structuredClone(user) : null;
    preferredRole = user ? user.roles[0] || '' : null;
    rolesReady = false;
    errorBox.classList.remove('show');
    element(doc, 'nu-title', HTMLHeadingElement).textContent = user ? 'Edit User' : 'Create User';
    save.textContent = user ? 'Save Changes' : 'Create';
    username.value = user?.username || ''; username.disabled = user !== null;
    email.value = user?.email || ''; password.value = '';
    element(doc, 'nu-id', HTMLInputElement).value = user ? String(user.id) : '';
    element(doc, 'nu-pass-hint', HTMLDivElement).style.display = user ? '' : 'none';
    form.style.display = '';
    updateControls();
    (user ? email : username).focus();
    await reloadRoles(sequence);
  }

  async function reloadRoles(editor: number) {
    const sequence = ++rolesSequence;
    rolesReady = false;
    errorBox.classList.remove('show');
    role.replaceChildren(new Option('Loading roles…', ''));
    updateControls();
    try {
      const roles = await adminUsersApi.roles();
      if (!alive() || editor !== editorSequence || sequence !== rolesSequence) return;
      const selected = preferredRole ?? roles.find(item => item.name === 'Viewer')?.name ?? '';
      const noRole = new Option(openedUser?.roles.length ? 'Keep current roles' : 'No role', '');
      noRole.disabled = !!openedUser?.roles.length;
      role.replaceChildren(noRole, ...roles.map(item => new Option(item.name, item.name)));
      if (selected && !roles.some(item => item.name === selected)) {
        const existing = new Option(`Current role: ${selected}`, selected);
        existing.disabled = true;
        role.append(existing);
      }
      role.value = selected;
      preferredRole = selected;
      rolesReady = true;
    } catch (error) {
      if (!alive() || editor !== editorSequence || sequence !== rolesSequence) return;
      showEditorError(`Roles could not be loaded. Reload roles to retry. ${asError(error).message}`);
    }
    updateControls();
  }

  function closeEditor() {
    ++editorSequence;
    ++rolesSequence;
    openedUser = null; rolesReady = false; password.value = '';
    form.style.display = 'none';
    updateControls();
  }

  async function perform(action: () => Promise<void>, editor: number | null = null) {
    if (!alive() || busy || unconfirmed) return;
    busy = true;
    ++readSequence;
    updateControls();
    try { await action(); }
    catch (error) { reportError(error, editor); }
    finally { busy = false; if (alive()) updateControls(); }
  }

  async function handleAction(button: HTMLButtonElement) {
    const user = users.find(item => item.id === Number(button.dataset.id));
    if (!user || !alive() || busy || unconfirmed) return;
    if (button.dataset.action === 'edit-user') { await openEditor(user); return; }
    await perform(async () => {
      if (button.dataset.action === 'delete') {
        if (!await feedback!.confirm('Delete this user?', { title: 'Delete user', impact: 'This cannot be undone.', confirmLabel: 'Delete user', danger: true, auditAction: 'admin.user.delete' }) || !alive()) return;
        await adminUsersApi.delete(user.id, user.version);
      } else if (button.dataset.action === 'toggle') {
        await adminUsersApi.update(user.id, { isActive: !user.isActive }, user.version);
      } else if (button.dataset.action === 'reset') {
        const value = await feedback!.prompt('Set a temporary password for this user.', { title: 'Reset password', label: 'Temporary password', secret: true, required: true, minLength: 8, autocomplete: 'new-password', confirmLabel: 'Reset password', auditAction: 'admin.user.password-reset' });
        if (!value || !alive()) return;
        await adminUsersApi.resetPassword(user.id, value, user.version);
        if (alive()) feedback!.notify('The temporary password was set.', { title: 'Password reset', tone: 'success', auditAction: 'admin.user.password-reset' });
      }
      if (alive()) await reload();
    });
  }

  async function saveEditor() {
    if (!alive() || busy || unconfirmed || !rolesReady || form.style.display === 'none') return;
    const sequence = editorSequence, user = openedUser, temporaryPassword = password.value;
    const desiredRole = role.value, desiredEmail = email.value.trim(), desiredName = username.value.trim();
    errorBox.classList.remove('show');
    if (!user && (!desiredName || !temporaryPassword)) { showEditorError('Username and password required.'); return; }
    await perform(async () => {
      if (user) {
        const body: UpdateUserRequest = { email: desiredEmail };
        if (desiredRole !== (user.roles[0] || '')) body.role = desiredRole;
        const receipt = await adminUsersApi.update(user.id, body, user.version);
        if (alive() && sequence === editorSequence) openedUser = receipt;
        if (temporaryPassword) {
          try { await adminUsersApi.resetPassword(user.id, temporaryPassword, receipt.version); }
          catch (error) {
            if (error instanceof AdminUserOutcomeUnconfirmed) throw error;
            throw new Error(`Profile was saved. Password was not changed: ${asError(error).message}`, { cause: error });
          }
        }
      } else {
        await adminUsersApi.create({ username: desiredName, email: desiredEmail, password: temporaryPassword, role: desiredRole });
      }
      if (!alive()) return;
      if (sequence === editorSequence) closeEditor();
      await reload();
    }, sequence);
  }

  async function bulkStatus(isActive: boolean) {
    const targets = selectedIds(wrap).map(id => users.find(user => user.id === id));
    if (!targets.length || targets.some(user => !user)) return;
    await perform(async () => {
      const result = await adminUsersApi.bulkStatus(targets.filter((user): user is UserDto => !!user)
        .map(user => ({ id: user.id, version: user.version })), isActive);
      if (!alive()) return;
      const failed = result.results.filter(item => item.status !== 'Updated');
      feedback!.notify(`${result.updated} users ${isActive ? 'enabled' : 'disabled'}.${failed.length ? ` ${failed.length} users were not changed; review the refreshed list.` : ''}`,
        { title: 'User status', tone: failed.length ? 'warning' : 'success' });
      await reload();
    });
  }

  listen(filter, 'input', () => { page = 1; void reload(); });
  listen(status, 'change', () => { page = 1; void reload(); });
  listen(refresh, 'click', () => { void reload(true); });
  listen(enable, 'click', () => { void bulkStatus(true); });
  listen(disable, 'click', () => { void bulkStatus(false); });
  listen(element(doc, 'newUserBtn', HTMLButtonElement), 'click', () => { void openEditor(null); });
  listen(cancel, 'click', closeEditor);
  listen(role, 'change', () => { preferredRole = role.value; });
  listen(reloadRolesButton, 'click', () => { if (form.style.display !== 'none' && alive()) void reloadRoles(editorSequence); });
  listen(save, 'click', () => { void saveEditor(); });
  updateControls();
  return { reload, dispose() {
    if (disposed) return;
    disposed = true; ++readSequence;
    closeEditor(); users = [];
    listeners.abort();
  } };
}
