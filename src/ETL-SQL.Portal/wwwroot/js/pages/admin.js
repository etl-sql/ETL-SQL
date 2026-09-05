/**
 * Page module for admin.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi, foldersApi, adminApi, subscriptionsApi, reportsApi, datasetsApi, catalogApi, secretsApi, connectionsApi, gatewaysApi, policyAuthorityApi, studioApi } from '../api.js';
import { populateFolderSelects, createFolderInline } from '../publish-folders.js';
import { renderSubscriptionHistory } from '../subscription-history-ui.js';
import { bindSelection, catalogQuery, headerSelectionCell, renderCatalogPager, selectedIds, selectionCell } from '../admin-catalog-ui.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { renderDag } from '../../designer/designer.js';
import { createDatasetsAdmin } from '../datasets-admin.js';
import { createSecretsAdmin } from '../secrets-admin.js';
import { createConnectionsAdmin } from '../connections-admin.js';
import { createGatewaysAdmin } from '../gateways-admin.js';
import { createPolicyAuthorityAdmin } from '../policy-authority-admin.js';
import { createOperationsAdmin } from '../operations-admin.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { failedState, loadingState, installPortalStateStyles } from '../portal-states.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';

renderPortalHeader();
installDialogAccessibility();
if (!auth.isLoggedIn()) { window.location.href = '/login.html'; }
applyPortalBranding();
initTheme();
installPortalStateStyles();

// ── Bootstrap ──────────────────────────────────────────────────────────────────
try {
  const identity = getSessionIdentity(auth.getToken());
  renderSessionIdentity(identity, document.getElementById('topbarUser'));
  const isAdmin = hasRole(identity, 'Admin');
  if (!isAdmin) window.location.href = '/index.html';
} catch { window.location.href = '/login.html'; }

applyNavigationSafely();

let studioSession = null;
const studioSessionReady = studioApi.session().then(session => {
  studioSession = session;
  // Not the nav entry — that is the shared navigation's answer. This is whether *this page's*
  // publish action opens Studio, which is a different question with a different rule.
  if (session.mode === 'CatalogOnly') document.getElementById('openPublishBtn').textContent = 'Open Studio';
  return session;
}).catch(() => {
  document.getElementById('openPublishBtn').style.display = 'none';
  return null;
});

document.getElementById('logoutBtn').addEventListener('click', () => authApi.logout());

// ── Tab switching ──────────────────────────────────────────────────────────────
const panels = { users: null, groups: null, folders: null, reports: null, audit: null, subscriptions: null, datasets: null, secrets: null, connections: null, gateways: null, policy: null, operations: null, settings: null };
document.querySelectorAll('.admin-tab').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.admin-tab').forEach(b => {
      b.classList.remove('active');
      b.setAttribute('aria-selected', 'false');
    });
    document.querySelectorAll('.admin-panel').forEach(p => p.classList.remove('active'));
    btn.classList.add('active');
    btn.setAttribute('aria-selected', 'true');
    const id = `panel-${/** @type {HTMLElement} */ (btn).dataset.tab}`;
    document.getElementById(id).classList.add('active');
    if (!panels[/** @type {HTMLElement} */ (btn).dataset.tab]) {
      panels[/** @type {HTMLElement} */ (btn).dataset.tab] = true;
      tabLoaders[/** @type {HTMLElement} */ (btn).dataset.tab]?.();
    }
  });
});

const tabLoaders = { users: loadUsers, groups: loadGroups, folders: loadFolders, reports: loadReports, audit: loadAudit, subscriptions: loadSubscriptions, datasets: mountDatasets, secrets: mountSecrets, connections: mountConnections, gateways: mountGateways, policy: mountPolicyAuthority, operations: mountOperations, settings: loadSettings };
// Load the initial tab (users)
panels.users = true;
loadUsers();

// ── Users ──────────────────────────────────────────────────────────────────────
let allUsers = [];
let userPage = 1;

async function loadUsers() {
  const $wrap = document.getElementById('userTableWrap');
  try {
    const result = await adminApi.userCatalog(catalogQuery({
      q: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('userFilter')).value.trim(),
      status: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('userStatusFilter')).value,
      page: userPage,
      pageSize: 25,
    }));
    allUsers = result.items || [];
    const rows = allUsers.map(u => `
      <tr>
        ${selectionCell(u.id, `user ${escAttr(u.username)}`)}
        <td>${esc(u.username)}</td>
        <td>${esc(u.email || '')}</td>
        <td>${(u.roles || []).map(r => `<span class="chip chip-${r.toLowerCase()}">${esc(r)}</span>`).join(' ')}</td>
        <td><span class="chip ${u.isActive ? 'chip-active' : 'chip-inactive'}">${u.isActive ? 'Active' : 'Inactive'}</span></td>
        <td>
          <div class="table-actions">
            <button class="btn btn-outline btn-sm" data-action="edit-user" data-id="${u.id}">Edit</button>
            <button class="btn btn-outline btn-sm" data-action="toggle" data-id="${u.id}" data-active="${u.isActive}">
              ${u.isActive ? 'Disable' : 'Enable'}
            </button>
            <button class="btn btn-outline btn-sm" data-action="reset" data-id="${u.id}">Reset Pwd</button>
            <button class="btn btn-danger btn-sm" data-action="delete" data-id="${u.id}">Delete</button>
          </div>
        </td>
      </tr>`).join('');

    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr>${headerSelectionCell('users')}<th>Username</th><th>Email</th><th>Roles</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="6" class="empty-state">No users.</td></tr>'}</tbody>
      </table>`;

    $wrap.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', () => handleUserAction(btn));
    });
    bindSelection($wrap, ids => {
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('usersEnableBtn')).disabled = ids.length === 0;
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('usersDisableBtn')).disabled = ids.length === 0;
    });
    renderCatalogPager(document.getElementById('userPager'), result, page => { userPage = page; loadUsers(); });
  } catch { $wrap.innerHTML = `<div class="empty-state">Failed to load users.</div>`; }
}

document.getElementById('userFilter').addEventListener('input', () => { userPage = 1; loadUsers(); });
document.getElementById('userStatusFilter').addEventListener('change', () => { userPage = 1; loadUsers(); });
document.getElementById('usersEnableBtn').addEventListener('click', () => bulkUserStatus(true));
document.getElementById('usersDisableBtn').addEventListener('click', () => bulkUserStatus(false));

async function bulkUserStatus(isActive) {
  const ids = selectedIds(document.getElementById('userTableWrap'));
  if (!ids.length) return;
  const users = ids.map(id => {
    const user = allUsers.find(x => x.id === id);
    return { id, version: user?.version };
  });
  await adminApi.bulkUserStatus(users, isActive).catch(alertErr);
  loadUsers();
}

async function handleUserAction(btn) {
  const id = +btn.dataset.id;
  const action = btn.dataset.action;
  if (action === 'delete') {
    if (!await ETLSQLFeedback.confirm('Delete this user?', { title: 'Delete user', impact: 'This cannot be undone.', confirmLabel: 'Delete user', danger: true, auditAction: 'admin.user.delete' })) return;
    const user = allUsers.find(x => x.id === id);
    await adminApi.deleteUser(id, user?.version).catch(alertErr);
    loadUsers();
  } else if (action === 'toggle') {
    const active = btn.dataset.active === 'true';
    const user = allUsers.find(x => x.id === id);
    await adminApi.updateUser(id, { isActive: !active }, user?.version).catch(alertErr);
    loadUsers();
  } else if (action === 'reset') {
    const pwd = await ETLSQLFeedback.prompt('Set a temporary password for this user.', { title: 'Reset password', label: 'Temporary password', secret: true, required: true, minLength: 8, autocomplete: 'new-password', confirmLabel: 'Reset password', auditAction: 'admin.user.password-reset' });
    if (!pwd) return;
    const user = allUsers.find(x => x.id === id);
    await adminApi.resetPassword(id, pwd, user?.version).catch(alertErr);
    ETLSQLFeedback.notify('The temporary password was set.', { title: 'Password reset', tone: 'success', auditAction: 'admin.user.password-reset' });
  } else if (action === 'edit-user') {
    const u = allUsers.find(x => x.id == id);
    if (!u) return;
    document.getElementById('nu-title').textContent = 'Edit User';
    document.getElementById('nu-saveBtn').textContent = 'Save Changes';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-username')).value = u.username;
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-username')).disabled = true; // Identity doesn't usually like username changes
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-email')).value = u.email || '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-password')).value = '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-role')).value = (u.roles && u.roles[0]) || 'Viewer';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-id')).value = u.id;
    document.getElementById('nu-pass-hint').style.display = '';
    document.getElementById('newUserForm').style.display = '';
  }
}

// New user form
document.getElementById('newUserBtn').addEventListener('click', () => {
  document.getElementById('nu-title').textContent = 'Create User';
  document.getElementById('nu-saveBtn').textContent = 'Create';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-username')).value = '';
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-username')).disabled = false;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-email')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-password')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-role')).value = 'Viewer';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-id')).value = '';
  document.getElementById('nu-pass-hint').style.display = 'none';
  document.getElementById('newUserForm').style.display = '';
});
document.getElementById('nu-cancelBtn').addEventListener('click', () => {
  document.getElementById('newUserForm').style.display = 'none';
});
document.getElementById('nu-saveBtn').addEventListener('click', async () => {
  const $err = document.getElementById('nu-error');
  $err.classList.remove('show');
  const id   = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-id')).value;
  const body = {
    email:    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-email')).value.trim(),
    password: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-password')).value,
    role:     /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-role')).value
  };
  
  if (!id) {
    body.username = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nu-username')).value.trim();
    if (!body.username || !body.password) { $err.textContent = 'Username and password required.'; $err.classList.add('show'); return; }
  }

  try {
    if (id) {
      const user = allUsers.find(x => x.id === +id);
      await adminApi.updateUser(+id, body, user?.version);
      if (body.password) {
        const refreshed = await adminApi.listUsers();
        const updatedUser = refreshed.find(x => x.id === +id);
        await adminApi.resetPassword(+id, body.password, updatedUser?.version);
      }
    } else {
      await adminApi.createUser(body);
    }
    document.getElementById('newUserForm').style.display = 'none';
    await loadUsers();
  } catch (err) { $err.textContent = err.message; $err.classList.add('show'); }
});

// ── Groups ─────────────────────────────────────────────────────────────────────
let selectedGroupId = null;
let allGroups = [];
let groupPage = 1;
let memberPage = 1;

async function loadGroups() {
  const $wrap = document.getElementById('groupTableWrap');
  try {
    const result = await adminApi.groupCatalog(catalogQuery({
      q: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('groupFilter')).value.trim(),
      page: groupPage,
      pageSize: 25,
    }));
    allGroups = result.items || [];
    const rows = allGroups.map(g => `
      <tr>
        ${selectionCell(g.id, `group ${escAttr(g.name)}`)}
        <td>${esc(g.name)}</td>
        <td class="text-sm text-muted">${esc(g.description || '')}</td>
        <td>${g.memberCount}</td>
        <td>
          <div class="table-actions">
            <button class="btn btn-outline btn-sm" data-action="edit-group" data-id="${g.id}">Edit</button>
            <button class="btn btn-outline btn-sm" data-action="members" data-id="${g.id}" data-name="${esc(g.name)}">Members</button>
            <button class="btn btn-danger btn-sm" data-action="delete" data-id="${g.id}">Delete</button>
          </div>
        </td>
      </tr>`).join('');

    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr>${headerSelectionCell('groups')}<th>Group Name</th><th>Description</th><th>Members</th><th>Actions</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="5" class="empty-state">No groups.</td></tr>'}</tbody>
      </table>`;

    $wrap.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', () => handleGroupAction(btn));
    });
    bindSelection($wrap, ids => {
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('groupsDeleteBtn')).disabled = ids.length === 0;
    });
    renderCatalogPager(document.getElementById('groupPager'), result, page => { groupPage = page; loadGroups(); });
  } catch { $wrap.innerHTML = `<div class="empty-state">Failed to load groups.</div>`; }
}

document.getElementById('groupFilter').addEventListener('input', () => { groupPage = 1; loadGroups(); });
document.getElementById('groupsDeleteBtn').addEventListener('click', async () => {
  const ids = selectedIds(document.getElementById('groupTableWrap'));
  if (!ids.length || !await ETLSQLFeedback.confirm('Delete the selected groups?', { title: 'Delete groups', impact: 'Groups with members or permission entries will be rejected.', confirmLabel: 'Delete groups', danger: true, auditAction: 'admin.group.bulk-delete' })) return;
  const groups = ids.map(id => {
    const group = allGroups.find(x => x.id === id);
    return { id, version: group?.version };
  });
  await adminApi.bulkDeleteGroups(groups).catch(alertErr);
  loadGroups();
});

async function handleGroupAction(btn) {
  const id = +btn.dataset.id;
  if (btn.dataset.action === 'delete') {
    if (!await ETLSQLFeedback.confirm('Delete this group?', { title: 'Delete group', confirmLabel: 'Delete group', danger: true, auditAction: 'admin.group.delete' })) return;
    const group = allGroups.find(x => x.id === id);
    await adminApi.deleteGroup(id, group?.version).catch(alertErr);
    loadGroups();
  } else if (btn.dataset.action === 'edit-group') {
    const g = allGroups.find(x => x.id == id);
    if (!g) return;
    document.getElementById('ng-title').textContent = 'Edit Group';
    document.getElementById('ng-saveBtn').textContent = 'Save Changes';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-name')).value = g.name;
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-desc')).value = g.description || '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-id')).value = g.id;
    document.getElementById('newGroupForm').style.display = '';
  } else if (btn.dataset.action === 'members') {
    selectedGroupId = id;
    memberPage = 1;
    document.getElementById('memberGroupName').textContent = btn.dataset.name;
    document.getElementById('membersPanel').style.display = '';
    loadMembers(id);
  }
}

async function loadMembers(groupId) {
  const $wrap = document.getElementById('memberTableWrap');
  $wrap.innerHTML = loadingState('Loading members…');
  try {
    const result = await adminApi.memberCatalog(groupId, catalogQuery({
      q: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('memberFilter')).value.trim(),
      page: memberPage,
      pageSize: 25,
    }));
    const members = result.items || [];
    const rows = members.map(m => `
      <tr>
        ${selectionCell(m.id, `member ${escAttr(m.username)}`)}
        <td>${esc(m.username)}</td>
        <td>${esc(m.email || '')}</td>
        <td><span class="chip ${m.isActive ? 'chip-active' : 'chip-inactive'}">${m.isActive ? 'Active' : 'Inactive'}</span></td>
        <td>
          <button class="btn btn-danger btn-sm" data-uid="${m.id}">Remove</button>
        </td>
      </tr>`).join('');
    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr>${headerSelectionCell('members')}<th>Username</th><th>Email</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="5" class="empty-state">No members.</td></tr>'}</tbody>
      </table>`;
    $wrap.querySelectorAll('[data-uid]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const group = allGroups.find(x => x.id === groupId);
        await adminApi.removeMember(groupId, +/** @type {HTMLElement} */ (btn).dataset.uid, group?.version).catch(alertErr);
        await loadGroups();
        loadMembers(groupId);
      });
    });
    bindSelection($wrap, ids => {
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('membersRemoveBtn')).disabled = ids.length === 0;
    });
    renderCatalogPager(document.getElementById('memberPager'), result, page => { memberPage = page; loadMembers(groupId); });
  } catch (err) {
    // "No members" and "we could not read the membership" lead to opposite actions — one invites
    // deleting the group or granting its access elsewhere.
    $wrap.innerHTML = failedState({
      title: 'Group members could not be loaded.',
      body: err?.message || 'The membership service could not be reached.'
    });
  }
}

document.getElementById('memberFilter').addEventListener('input', () => {
  memberPage = 1;
  if (selectedGroupId) loadMembers(selectedGroupId);
});
document.getElementById('membersRemoveBtn').addEventListener('click', async () => {
  const ids = selectedIds(document.getElementById('memberTableWrap'));
  if (!selectedGroupId || !ids.length || !await ETLSQLFeedback.confirm('Remove the selected users from this group?', { title: 'Remove group members', confirmLabel: 'Remove users', danger: true, auditAction: 'admin.group.members.remove' })) return;
  const group = allGroups.find(x => x.id === selectedGroupId);
  await adminApi.bulkRemoveMembers(selectedGroupId, ids, group?.version).catch(alertErr);
  loadMembers(selectedGroupId);
  await loadGroups();
});

document.getElementById('newGroupBtn').addEventListener('click', () => {
  document.getElementById('newGroupForm').style.display = '';
});
document.getElementById('ng-cancelBtn').addEventListener('click', () => {
  document.getElementById('newGroupForm').style.display = 'none';
});
document.getElementById('ng-saveBtn').addEventListener('click', async () => {
  const $err = document.getElementById('ng-error');
  $err.classList.remove('show');
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-name')).value.trim();
  const desc = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-desc')).value.trim();
  const id   = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-id')).value;
  if (!name) { $err.textContent = 'Name is required.'; $err.classList.add('show'); return; }
  try {
    if (id) {
      const group = allGroups.find(x => x.id === +id);
      await adminApi.updateGroup(+id, { name, description: desc }, group?.version);
    } else {
      await adminApi.createGroup({ name, description: desc });
    }
    document.getElementById('newGroupForm').style.display = 'none';
    loadGroups();
  } catch (err) { $err.textContent = err.message; $err.classList.add('show'); }
});

document.getElementById('newGroupBtn').addEventListener('click', () => {
  document.getElementById('ng-title').textContent = 'Create Group';
  document.getElementById('ng-saveBtn').textContent = 'Create';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-desc')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('ng-id')).value = '';
  document.getElementById('newGroupForm').style.display = '';
});

document.getElementById('addMemberBtn').addEventListener('click', async () => {
  const $form = document.getElementById('addMemberForm');
  $form.style.display = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('addMemberSearch')).value = '';
  refreshAddMemberOptions();
});
document.getElementById('addMemberSearch').addEventListener('input', refreshAddMemberOptions);

async function refreshAddMemberOptions() {
  const result = await adminApi.userCatalog(catalogQuery({
    q: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('addMemberSearch')).value.trim(),
    status: 'active',
    pageSize: 25,
  })).catch(() => ({ items: [] }));
  document.getElementById('addMemberSelect').innerHTML = (result.items || [])
    .map(u => `<option value="${u.id}">${esc(u.username)}${u.email ? ` — ${esc(u.email)}` : ''}</option>`)
    .join('');
}
document.getElementById('am-cancelBtn').addEventListener('click', () => {
  document.getElementById('addMemberForm').style.display = 'none';
});
document.getElementById('am-saveBtn').addEventListener('click', async () => {
  const userIds = [.../** @type {HTMLSelectElement} */ (document.getElementById('addMemberSelect')).selectedOptions].map(option => +option.value);
  if (!selectedGroupId || !userIds.length) return;
  const group = allGroups.find(x => x.id === selectedGroupId);
  await adminApi.bulkAddMembers(selectedGroupId, userIds, group?.version).catch(alertErr);
  document.getElementById('addMemberForm').style.display = 'none';
  loadMembers(selectedGroupId);
  await loadGroups();
});

// ── Folders & ACL ──────────────────────────────────────────────────────────────
let selectedFolderId = null;
let allFolders = [];

async function loadFolders() {
  const $wrap = document.getElementById('folderTableWrap');
  try {
    allFolders = await foldersApi.list();
    const rows = allFolders.map(f => `
      <tr>
        <td>${esc(f.path || f.name)}</td>
        <td>
          <div class="table-actions">
            <button class="btn btn-outline btn-sm" data-action="edit-folder" data-id="${f.id}">Edit</button>
            <button class="btn btn-outline btn-sm" data-action="acl" data-id="${f.id}" data-name="${esc(f.name)}">Permissions</button>
            <button class="btn btn-danger btn-sm" data-action="delete" data-id="${f.id}">Delete</button>
          </div>
        </td>
      </tr>`).join('');

    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr><th>Folder Path</th><th>Actions</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="2" class="empty-state">No folders.</td></tr>'}</tbody>
      </table>`;

    // Populate parent dropdown for new folder form
    const $parent = document.getElementById('nf-parent');
    $parent.innerHTML = '<option value="">— Root —</option>' +
      allFolders.map(f => `<option value="${f.id}">${esc(f.path || f.name)}</option>`).join('');

    $wrap.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', () => handleFolderAction(btn));
    });
  } catch { $wrap.innerHTML = `<div class="empty-state">Failed to load folders.</div>`; }
}

async function handleFolderAction(btn) {
  const id = +btn.dataset.id;
  if (btn.dataset.action === 'delete') {
    if (!await ETLSQLFeedback.confirm('Delete this folder?', { title: 'Delete folder and reports', impact: 'Reports inside the folder will also be removed.', confirmLabel: 'Delete folder', danger: true, auditAction: 'admin.folder.delete' })) return;
    const folder = allFolders.find(x => x.id === id);
    await foldersApi.delete(id, true, folder?.version).catch(alertErr);
    loadFolders();
  } else if (btn.dataset.action === 'edit-folder') {
    const f = allFolders.find(x => x.id == id);
    if (!f) return;
    document.getElementById('nf-title').textContent = 'Edit Folder';
    document.getElementById('nf-saveBtn').textContent = 'Save Changes';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-name')).value = f.name;
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-parent')).value = f.parentId || '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-id')).value = f.id;
    document.getElementById('newFolderForm').style.display = '';
  } else if (btn.dataset.action === 'acl') {
    selectedFolderId = id;
    document.getElementById('aclFolderName').textContent = btn.dataset.name;
    document.getElementById('aclPanel').style.display = '';
    loadAcl(id);
  }
}

async function loadAcl(folderId) {
  const $wrap = document.getElementById('aclTableWrap');
  // Cleared before the request, not after it. The heading is already showing the folder that was
  // just clicked, so leaving the previous folder's rows in place would present one folder's
  // access-control list as another's — and the Revoke buttons would still carry the old group ids.
  $wrap.innerHTML = loadingState('Loading permissions…');
  try {
    const acls = await foldersApi.listAcl(folderId);
    const rows = acls.map(a => `
      <tr>
        <td>${esc(a.groupName)}</td>
        <td>${esc(a.permission)}</td>
        <td>
          <button class="btn btn-danger btn-sm" data-gid="${a.groupId}">Revoke</button>
        </td>
      </tr>`).join('');
    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr><th>Group</th><th>Permission</th><th></th></tr></thead>
        <tbody>${rows || '<tr><td colspan="3" class="empty-state">No permissions set.</td></tr>'}</tbody>
      </table>`;
    $wrap.querySelectorAll('[data-gid]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const folder = allFolders.find(x => x.id === folderId);
        await foldersApi.revokeAcl(folderId, +/** @type {HTMLElement} */ (btn).dataset.gid, folder?.version).catch(alertErr);
        await loadFolders();
        loadAcl(folderId);
      });
    });
  } catch (err) {
    // "No permissions set" and "we could not read the permissions" lead an administrator to
    // opposite conclusions about whether a folder is protected, so this never falls back to the
    // empty rendering.
    $wrap.innerHTML = failedState({
      title: 'Folder permissions could not be loaded.',
      body: err?.message || 'The permissions service could not be reached.'
    });
  }
}

document.getElementById('newFolderBtn').addEventListener('click', () => {
  document.getElementById('newFolderForm').style.display = '';
});
document.getElementById('nf-cancelBtn').addEventListener('click', () => {
  document.getElementById('newFolderForm').style.display = 'none';
});
document.getElementById('nf-saveBtn').addEventListener('click', async () => {
  const $err = document.getElementById('nf-error');
  $err.classList.remove('show');
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-name')).value.trim();
  const parentId = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-parent')).value || null;
  const id = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-id')).value;
  if (!name) { $err.textContent = 'Name is required.'; $err.classList.add('show'); return; }
  try {
    if (id) {
      const folder = allFolders.find(x => x.id === +id);
      await foldersApi.update(+id, { name, parentId: parentId ? +parentId : null }, folder?.version);
    } else {
      await foldersApi.create(name, parentId ? +parentId : null);
    }
    document.getElementById('newFolderForm').style.display = 'none';
    loadFolders();
  } catch (err) { $err.textContent = err.message; $err.classList.add('show'); }
});

document.getElementById('newFolderBtn').addEventListener('click', () => {
  document.getElementById('nf-title').textContent = 'Create Folder';
  document.getElementById('nf-saveBtn').textContent = 'Create';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-parent')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('nf-id')).value = '';
  document.getElementById('newFolderForm').style.display = '';
});

// ── Reports ────────────────────────────────────────────────────────────────────
let allReports = [];
async function loadReports() {
  const $wrap = document.getElementById('reportsTableWrap');
  try {
    await studioSessionReady;
    allReports = await adminApi.listAllReports();
    const exposesExternalSource = studioSession?.mode === 'SourceControlled';
    const canOpenStudio = Boolean(studioSession?.capabilities?.includes('StudioAccess'));
    const rows = allReports.map(r => `
      <tr>
        <td>${esc(r.name)}</td>
        <td class="text-sm text-muted">${esc(r.folderPath)}</td>
        ${exposesExternalSource ? `<td class="text-sm text-muted"><code>${esc(r.scriptPath)}</code></td>` : ''}
        <td class="text-sm">${new Date(r.createdAt).toLocaleDateString()}</td>
        <td>
          <div class="table-actions">
            ${canOpenStudio ? `<button class="btn btn-outline btn-sm" data-action="edit-report" data-id="${r.id}">${exposesExternalSource ? 'Edit' : 'Open in Studio'}</button>` : ''}
            <button class="btn btn-danger btn-sm" data-action="delete-report" data-id="${r.id}">Delete</button>
          </div>
        </td>
      </tr>`).join('');

    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr><th>Report Name</th><th>Folder</th>${exposesExternalSource ? '<th>Script Path</th>' : ''}<th>Created</th><th>Actions</th></tr></thead>
        <tbody>${rows || `<tr><td colspan="${exposesExternalSource ? 5 : 4}" class="empty-state">No reports published.</td></tr>`}</tbody>
      </table>`;

    $wrap.querySelectorAll('[data-action="delete-report"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        if (!await ETLSQLFeedback.confirm('Delete this report?', { title: 'Delete report', impact: 'Subscription schedules for this report will be cancelled.', confirmLabel: 'Delete report', danger: true, auditAction: 'admin.report.delete' })) return;
        const id = +/** @type {HTMLElement} */ (btn).dataset.id;
        const report = allReports.find(x => x.id === id);
        await reportsApi.delete(id, report?.version).catch(alertErr);
        loadReports();
      });
    });

    $wrap.querySelectorAll('[data-action="edit-report"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const id = +/** @type {HTMLElement} */ (btn).dataset.id;
        const r = allReports.find(x => x.id === id);
        if (!r) return;
        if (studioSession?.mode === 'CatalogOnly') {
          window.location.href = `/designer.html?id=${id}`;
          return;
        }
        
        const $form = document.getElementById('publishReportForm');
        $form.style.display = '';
        document.getElementById('pr-title').textContent = 'Edit Report';
        document.getElementById('pr-saveBtn').textContent = 'Save Changes';
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-name')).value = r.name;
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-path')).value = r.scriptPath;
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-desc')).value = r.description || '';
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-id')).value = r.id;
        document.getElementById('pr-error').classList.remove('show');
        resetPublishValidation();

        hidePublishNewFolder();
        await populateFolderSelects({
          foldersApi, esc,
          select: /** @type {HTMLSelectElement} */ (document.getElementById('pr-folder')),
          parentSelect: /** @type {HTMLSelectElement} */ (document.getElementById('pr-newFolderParent')),
          selectedId: r.folderId,
        });
      });
    });
  } catch { $wrap.innerHTML = `<div class="empty-state">Failed to load reports.</div>`; }
}

document.getElementById('reportsRefreshBtn').addEventListener('click', () => loadReports());

document.getElementById('openPublishBtn').addEventListener('click', async () => {
  await studioSessionReady;
  if (studioSession?.mode === 'CatalogOnly') {
    window.location.href = '/studio.html';
    return;
  }
  if (!studioSession) return;
  const $form = document.getElementById('publishReportForm');
  $form.style.display = '';
  document.getElementById('pr-title').textContent = 'Publish New Report';
  document.getElementById('pr-saveBtn').textContent = 'Publish';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-path')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-desc')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-id')).value = '';
  document.getElementById('pr-error').classList.remove('show');
  resetPublishValidation();

  // Always fetch fresh so a folder created moments ago shows up without a page reload.
  hidePublishNewFolder();
  await populateFolderSelects({
    foldersApi, esc,
    select: /** @type {HTMLSelectElement} */ (document.getElementById('pr-folder')),
    parentSelect: /** @type {HTMLSelectElement} */ (document.getElementById('pr-newFolderParent')),
  });
});

// ── Inline folder creation from the publish form ─────────────────────────────────
function hidePublishNewFolder() {
  document.getElementById('pr-newFolderRow').style.display = 'none';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-newFolderName')).value = '';
  const $err = document.getElementById('pr-newFolderErr');
  $err.style.display = 'none';
  $err.textContent = '';
}

document.getElementById('pr-newFolderToggle').addEventListener('click', () => {
  const $row = document.getElementById('pr-newFolderRow');
  if ($row.style.display !== 'none') { hidePublishNewFolder(); return; }
  $row.style.display = '';
  // Default the new folder's parent to whatever is selected in the destination dropdown.
  const current = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (/** @type {HTMLSelectElement} */ (document.getElementById('pr-folder'))).value;
  if (current) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (/** @type {HTMLSelectElement} */ (document.getElementById('pr-newFolderParent'))).value = current;
  document.getElementById('pr-newFolderName').focus();
});

document.getElementById('pr-newFolderCancelBtn').addEventListener('click', hidePublishNewFolder);

document.getElementById('pr-newFolderCreate').addEventListener('click', async () => {
  const $err = document.getElementById('pr-newFolderErr');
  $err.style.display = 'none';
  try {
    const created = await createFolderInline({
      foldersApi,
      name: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-newFolderName')).value,
      parentId: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (/** @type {HTMLSelectElement} */ (document.getElementById('pr-newFolderParent'))).value,
    });
    await populateFolderSelects({
      foldersApi, esc,
      select: /** @type {HTMLSelectElement} */ (document.getElementById('pr-folder')),
      parentSelect: /** @type {HTMLSelectElement} */ (document.getElementById('pr-newFolderParent')),
      selectedId: created?.id,
    });
    hidePublishNewFolder();
  } catch (err) {
    $err.textContent = err?.body?.error || err?.message || 'Could not create folder.';
    $err.style.display = '';
  }
});

// Publish Report
document.getElementById('pr-cancelBtn').addEventListener('click', () => {
  document.getElementById('publishReportForm').style.display = 'none';
});

document.getElementById('pr-validateBtn').addEventListener('click', async () => {
  const path = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-path')).value.trim();
  const $err = document.getElementById('pr-error');
  $err.classList.remove('show');
  if (!path) {
    $err.textContent = 'Script path is required.';
    $err.classList.add('show');
    return;
  }
  try {
    const result = await reportsApi.validateScript(path);
    renderPublishValidation(result);
  } catch (err) {
    renderPublishValidation(err.body || { isValid: false, errors: [err.message || 'Validation failed.'] });
  }
});

document.getElementById('pr-saveBtn').addEventListener('click', async () => {
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-name')).value.trim();
  const path = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-path')).value.trim();
  const desc = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-desc')).value.trim();
  const folderId = +/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (/** @type {HTMLSelectElement} */ (document.getElementById('pr-folder'))).value;
  const id = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-id')).value;
  const $err = document.getElementById('pr-error');
  $err.classList.remove('show');

  if (!name || !path || !folderId) {
    $err.textContent = 'Name, Path, and Folder are required.';
    $err.classList.add('show');
    return;
  }

  try {
    const validation = await reportsApi.validateScript(path);
    renderPublishValidation(validation);
    if (id) {
      const report = allReports.find(x => x.id === +id);
      await reportsApi.update(+id, { name, scriptPath: path, description: desc, folderId }, report?.version);
    } else {
      await reportsApi.create({ name, scriptPath: path, description: desc, folderId });
    }
    document.getElementById('publishReportForm').style.display = 'none';
    loadReports();
    ETLSQLFeedback.notify(id ? 'The report was updated.' : 'The report was published.', { title: id ? 'Report updated' : 'Report published', tone: 'success', auditAction: id ? 'admin.report.update' : 'admin.report.publish' });
  } catch (err) {
    if (err.body && err.body.isValid === false) renderPublishValidation(err.body);
    $err.textContent = err.message || 'Failed to save report.';
    $err.classList.add('show');
  }
});

function resetPublishValidation() {
  const panel = document.getElementById('pr-validation');
  panel.style.display = 'none';
  panel.innerHTML = '';
}

function renderPublishValidation(result) {
  const panel = document.getElementById('pr-validation');
  const errors = result.errors || [];
  const parameters = result.parameters || [];
  panel.style.display = '';
  panel.className = `validation-panel ${result.isValid ? 'validation-ok' : 'validation-bad'}`;
  panel.innerHTML = result.isValid
    ? `<div class="validation-title">Script validated</div>
       <div class="validation-line">Hash: <code>${esc(result.hash || '')}</code></div>
       <div class="validation-line">Parameters: ${esc(parameters.length ? parameters.map(p => p.name).join(', ') : 'None')}</div>`
    : `<div class="validation-title">Script validation failed</div>
       <ul>${errors.map(e => `<li>${esc(e)}</li>`).join('')}</ul>`;
}

// ── Script Browser ─────────────────────────────────────────────────────────────
let allAvailableScripts = [];

document.getElementById('pr-browseBtn').addEventListener('click', async () => {
  const $modal = document.getElementById('scriptBrowserModal');
  const $list  = document.getElementById('sb-list');
  $modal.style.display = 'flex';
  $list.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading scripts…</span></div>';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sb-filter')).value = '';

  try {
    allAvailableScripts = await reportsApi.listAvailableScripts();
    renderScriptBrowserList(allAvailableScripts);
  } catch (err) {
    document.getElementById('sb-error').textContent = err.message;
    document.getElementById('sb-error').classList.add('show');
  }
});

function renderScriptBrowserList(scripts) {
  const $list = document.getElementById('sb-list');
  if (!scripts.length) {
    $list.innerHTML = '<div class="empty-state">No .rptsql files found in ScriptRoot.</div>';
    return;
  }
  $list.innerHTML = scripts.map(s => `
    <div class="sb-item" data-path="${escAttr(s)}">
      <span class="script-glyph" aria-hidden="true"></span> ${esc(s)}
    </div>`).join('');

  $list.querySelectorAll('.sb-item').forEach(el => {
    el.addEventListener('click', () => {
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('pr-path')).value = /** @type {HTMLElement} */ (el).dataset.path;
      document.getElementById('scriptBrowserModal').style.display = 'none';
    });
  });
}

document.getElementById('sb-filter').addEventListener('input', e => {
  const q = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value.toLowerCase();
  const filtered = allAvailableScripts.filter(s => s.toLowerCase().includes(q));
  renderScriptBrowserList(filtered);
});

document.getElementById('sb-cancelBtn').addEventListener('click', () => {
  document.getElementById('scriptBrowserModal').style.display = 'none';
});

// ── Audit log ──────────────────────────────────────────────────────────────────
let auditPage = 1;
const auditPageSize = 50;

async function loadAudit() {
  const $wrap = document.getElementById('auditTableWrap');
  const action = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('auditFilter')).value.trim();
  try {
    const data = await adminApi.auditLog(auditPage, auditPageSize, action);
    const rows = (data.items || data).map(e => `
      <tr>
        <td class="text-sm">${new Date(e.timestamp).toLocaleString()}</td>
        <td>${esc(e.username || '')}</td>
        <td><code>${esc(e.action)}</code></td>
        <td class="text-sm text-muted">${esc(e.resourceType || '')} ${esc(e.resourceId || '')}</td>
        <td class="text-sm text-muted">${esc(e.detail || '')}</td>
      </tr>`).join('');

    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr><th>Time</th><th>User</th><th>Action</th><th>Resource</th><th>Detail</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="5" class="empty-state">No entries.</td></tr>'}</tbody>
      </table>`;

    const total = data.totalCount || (data.items || data).length;
    renderAuditPager(total);
  } catch { $wrap.innerHTML = `<div class="empty-state">Failed to load audit log.</div>`; }
}

function renderAuditPager(total) {
  const pages = Math.ceil(total / auditPageSize);
  const $p = document.getElementById('auditPager');
  $p.innerHTML = '';
  if (pages <= 1) return;
  for (let i = 1; i <= Math.min(pages, 10); i++) {
    const btn = document.createElement('button');
    btn.className = `btn btn-outline btn-sm ${i === auditPage ? 'active' : ''}`;
    btn.textContent = String(i);
    btn.addEventListener('click', () => { auditPage = i; loadAudit(); });
    $p.appendChild(btn);
  }
}

document.getElementById('auditRefreshBtn').addEventListener('click', () => { auditPage = 1; loadAudit(); });
document.getElementById('auditFilter').addEventListener('keydown', e => {
  if (e.key === 'Enter') { auditPage = 1; loadAudit(); }
});
document.getElementById('auditExportBtn').addEventListener('click', () => {
  const action = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('auditFilter')).value.trim();
  const qs = action ? `?action=${encodeURIComponent(action)}` : '';
  const token = auth.getToken();
  // Use a hidden <a> with the auth token embedded via fetch + blob URL
  fetch(`/api/admin/audit/export/csv${qs}`, {
    headers: { Authorization: `Bearer ${token}` }
  }).then(r => r.blob()).then(blob => {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `audit_log_${new Date().toISOString().slice(0,10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }).catch(() => ETLSQLFeedback.notify('The audit export could not be created.', { title: 'Export failed', tone: 'error' }));
});

// ── Subscriptions ──────────────────────────────────────────────────────────────
let subscriptionPage = 1;
let allSubscriptions = [];

async function loadSubscriptions() {
  const $wrap = document.getElementById('subsTableWrap');
  try {
    const result = await adminApi.subscriptionCatalog(catalogQuery({
      q: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('subsFilter')).value.trim(),
      status: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('subsStatusFilter')).value,
      page: subscriptionPage,
      pageSize: 25,
    }));
    const subs = result.items || [];
    allSubscriptions = subs;
    const rows = subs.map(s => {
      const schedule = s.deliverOnRefresh ? 'On Refresh' : (s.schedule || '—');
      return `
        <tr>
          ${selectionCell(s.id, `subscription ${escAttr(s.name || s.reportName)}`)}
          <td class="text-sm">${esc(s.recipients || '')}</td>
          <td>${esc(s.reportName || '')}</td>
          <td>${esc(s.name || '—')}</td>
          <td class="text-sm">${esc(schedule)}</td>
          <td>${esc(s.format || '')}</td>
          <td class="text-sm text-muted">${esc(s.parameterSummary || '—')}</td>
          <td>
            <span class="chip ${s.isActive ? 'chip-active' : 'chip-inactive'}">${s.isActive ? 'Active' : 'Paused'}</span>
            ${s.failCount ? `<div class="sub-warning">${s.failCount} failed send${s.failCount === 1 ? '' : 's'}</div>` : `<div class="text-sm text-muted">Last: ${esc(formatOptionalDate(s.lastSentAt))}</div>`}
          </td>
          <td>
            <div class="table-actions">
              <button class="btn btn-outline btn-sm" data-action="history" data-id="${s.id}"
                data-label="${escAttr(s.name || s.reportName)}">History</button>
              <button class="btn btn-outline btn-sm" data-action="edit-params"
                data-id="${s.id}" data-rid="${s.reportId}"
                data-label="${escAttr(s.name || s.reportName)}"
                data-params='${escAttr(JSON.stringify(s.parameters || {}))}'>Edit Params</button>
              <button class="btn btn-outline btn-sm" data-action="toggle" data-id="${s.id}"
                data-active="${s.isActive}">${s.isActive ? 'Pause' : 'Resume'}</button>
              <button class="btn btn-outline btn-sm btn-danger-soft" data-action="delete" data-id="${s.id}">Delete</button>
            </div>
          </td>
        </tr>`;
    }).join('');
    $wrap.innerHTML = `
      <table class="data-table">
        <thead><tr>${headerSelectionCell('subscriptions')}<th>Recipients</th><th>Report</th><th>Name</th><th>Schedule</th><th>Format</th><th>Parameters</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="9" class="empty-state">No subscriptions.</td></tr>'}</tbody>
      </table>`;
    $wrap.querySelectorAll('[data-action]').forEach(btn => {
      btn.addEventListener('click', () => {
        handleAdminSubscriptionAction(btn);
      });
    });
    bindSelection($wrap, ids => {
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('subsResumeBtn')).disabled = ids.length === 0;
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('subsPauseBtn')).disabled = ids.length === 0;
    });
    renderCatalogPager(document.getElementById('subsPager'), result, page => { subscriptionPage = page; loadSubscriptions(); });
  } catch { $wrap.innerHTML = '<div class="empty-state">Failed to load subscriptions.</div>'; }
}

document.getElementById('subsRefreshBtn').addEventListener('click', () => { subscriptionPage = 1; loadSubscriptions(); });
document.getElementById('subsFilter').addEventListener('input', () => { subscriptionPage = 1; loadSubscriptions(); });
document.getElementById('subsStatusFilter').addEventListener('change', () => { subscriptionPage = 1; loadSubscriptions(); });
document.getElementById('subsResumeBtn').addEventListener('click', () => bulkSubscriptionStatus(true));
document.getElementById('subsPauseBtn').addEventListener('click', () => bulkSubscriptionStatus(false));

async function bulkSubscriptionStatus(isActive) {
  const ids = selectedIds(document.getElementById('subsTableWrap'));
  if (!ids.length) return;
  const subscriptions = ids.map(id => {
    const subscription = allSubscriptions.find(x => x.id === id);
    return { id, version: subscription?.version };
  });
  await adminApi.bulkSubscriptionStatus(subscriptions, isActive).catch(alertErr);
  loadSubscriptions();
}

async function handleAdminSubscriptionAction(btn) {
  const id = +btn.dataset.id;
  if (btn.dataset.action === 'history') {
    await showAdminSubscriptionHistory(id, btn.dataset.label);
  } else if (btn.dataset.action === 'toggle') {
    const subscription = allSubscriptions.find(x => x.id === id);
    await subscriptionsApi.update(id, { isActive: btn.dataset.active !== 'true' }, subscription?.version).catch(alertErr);
    loadSubscriptions();
  } else if (btn.dataset.action === 'delete') {
    if (!await ETLSQLFeedback.confirm('Delete this subscription?', { title: 'Delete subscription', impact: 'This stops future deliveries and removes its generated job.', confirmLabel: 'Delete subscription', danger: true, auditAction: 'admin.subscription.delete' })) return;
    const subscription = allSubscriptions.find(x => x.id === id);
    await subscriptionsApi.delete(id, subscription?.version).catch(alertErr);
    loadSubscriptions();
  } else if (btn.dataset.action === 'edit-params') {
    const current = JSON.parse(btn.dataset.params || '{}');
    const subscription = allSubscriptions.find(x => x.id === id);
    openEditParamsModal(id, +btn.dataset.rid, btn.dataset.label, current, subscription?.version, () => loadSubscriptions());
  }
}

async function showAdminSubscriptionHistory(id, name) {
  const $modal = document.getElementById('subscriptionHistoryModal');
  const $body = document.getElementById('subscriptionHistoryBody');
  document.getElementById('subscriptionHistoryTitle').textContent = `${name || 'Subscription'} Delivery History`;
  $modal.style.display = 'flex';
  $body.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading delivery history…</span></div>';
  try {
    const history = await subscriptionsApi.history(id);
    $body.innerHTML = renderSubscriptionHistory(history, {
      esc,
      formatDate: value => value ? new Date(value).toLocaleString() : '—',
    });
  } catch (err) {
    $body.innerHTML = `<div class="empty-state">Failed to load delivery history: ${esc(err.message)}</div>`;
  }
}

document.getElementById('subscriptionHistoryCloseBtn').addEventListener('click', () => {
  document.getElementById('subscriptionHistoryModal').style.display = 'none';
});

let _onParamsSaved = null;
let _editParamsVersion = null;

function openEditParamsModal(subId, reportId, label, currentValues, version, onSaved) {
  _onParamsSaved = onSaved;
  _editParamsVersion = version;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('editParamsSubId')).value = subId;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('editParamsReportId')).value = reportId;
  document.getElementById('editParamsTitle').textContent = `Edit Parameters — ${label}`;
  document.getElementById('editParamsError').classList.remove('show');
  const $fields = document.getElementById('editParamsFields');
  $fields.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading parameters…</span></div>';
  document.getElementById('editParamsModal').style.display = 'flex';

  reportsApi.getParameters(reportId).then(params => {
    if (!params.length) {
      $fields.innerHTML = '<div class="param-empty">This report has no input parameters.</div>';
      return;
    }
    renderParamFields('editParamsFields', params, currentValues);
  }).catch(() => {
    $fields.innerHTML = '<div class="param-empty text-danger">Failed to load parameters.</div>';
  });
}

function renderParamFields(containerId, params, currentValues) {
  const $c = document.getElementById(containerId);
  $c.innerHTML = params.map(p => {
    const val = currentValues?.[p.name] ?? p.default ?? '';
    const isReldate = (p.type || '').toUpperCase() === 'RELDATE';
    const quickpicks = isReldate ? `
      <div class="reldate-quickpicks">
        ${['D-0','D-1','D-7','D-30','M-1','M-3','Y-1'].map(v =>
          `<button type="button" data-qp="${v}" data-target="param-${p.name}">${v === 'D-0' ? 'Today' : v}</button>`
        ).join('')}
      </div>` : '';
    return `
      <div class="param-row">
        <div class="param-heading">
          <label class="param-label" for="param-${escAttr(p.name)}">${esc(p.name)}</label>
          <span class="param-type">${esc(p.type || '')}</span>
          ${p.required ? '<span class="required-marker">Required</span>' : '<span class="optional-marker">Optional</span>'}
        </div>
        ${p.description ? `<div class="param-hint">${esc(p.description)}</div>` : ''}
        <input class="param-input" id="param-${p.name}" type="text" value="${escAttr(String(val))}"
          placeholder="${isReldate ? 'e.g. D-1, M-1, Y-1' : ''}">
        ${quickpicks}
      </div>`;
  }).join('');

  $c.querySelectorAll('[data-qp]').forEach(btn => {
    btn.addEventListener('click', () => {
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById(/** @type {HTMLElement} */ (btn).dataset.target)).value = /** @type {HTMLElement} */ (btn).dataset.qp;
    });
  });
}

function collectParamValues(containerId, params) {
  const result = {};
  params.forEach(p => {
    const $input = document.getElementById(`param-${p.name}`);
    if ($input && /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ ($input).value.trim()) result[p.name] = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ ($input).value.trim();
  });
  return Object.keys(result).length ? result : null;
}

document.getElementById('editParamsCancelBtn').addEventListener('click', () => {
  document.getElementById('editParamsModal').style.display = 'none';
});

document.getElementById('editParamsSaveBtn').addEventListener('click', async () => {
  const subId    = +/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('editParamsSubId')).value;
  const reportId = +/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('editParamsReportId')).value;
  const $err = document.getElementById('editParamsError');
  $err.classList.remove('show');

  let params;
  try {
    const allParams = await reportsApi.getParameters(reportId);
    params = collectParamValues('editParamsFields', allParams);
  } catch { params = null; }

  try {
    await subscriptionsApi.update(subId, { parameters: params }, _editParamsVersion);
    document.getElementById('editParamsModal').style.display = 'none';
    _onParamsSaved?.();
  } catch (err) {
    $err.textContent = err.message || 'Save failed.';
    $err.classList.add('show');
  }
});

// ── Shared Datasets ────────────────────────────────────────────────────────────
// Shared Datasets admin lives in js/datasets-admin.js so it can be previewed in the
// UI sandbox without Docker. This wrapper binds the canonical module to the admin page.
let datasetsAdmin = null;
function mountDatasets() {
  if (!datasetsAdmin) {
    datasetsAdmin = createDatasetsAdmin({
      host: document.getElementById('panel-datasets'),
      datasetsApi, adminApi, catalogApi, renderDag,
    });
  }
  datasetsAdmin.load();
}

// ── Secrets & Shared Connections ───────────────────────────────────────────────
// Both live in extracted modules (js/secrets-admin.js, js/connections-admin.js) so they can be
// previewed in the UI sandbox without the portal; these wrappers bind them to the admin page.
let secretsAdmin = null;
function mountSecrets() {
  if (!secretsAdmin) {
    secretsAdmin = createSecretsAdmin({ host: document.getElementById('panel-secrets'), secretsApi });
  }
  secretsAdmin.load();
}

let connectionsAdmin = null;
function mountConnections() {
  if (!connectionsAdmin) {
    connectionsAdmin = createConnectionsAdmin({ host: document.getElementById('panel-connections'), connectionsApi });
  }
  connectionsAdmin.load();
}

let gatewaysAdmin = null;
function mountGateways() {
  if (!gatewaysAdmin) {
    gatewaysAdmin = createGatewaysAdmin({ host: document.getElementById('panel-gateways'), gatewaysApi });
  }
  gatewaysAdmin.load();
}

let policyAuthorityAdmin = null;
function mountPolicyAuthority() {
  if (!policyAuthorityAdmin) {
    policyAuthorityAdmin = createPolicyAuthorityAdmin({
      host: document.getElementById('panel-policy'),
      policyAuthorityApi,
    });
  }
  policyAuthorityAdmin.load();
}

let operationsAdmin = null;
function mountOperations() {
  if (!operationsAdmin) {
    operationsAdmin = createOperationsAdmin({
      host: document.getElementById('panel-operations'),
      adminApi
    });
  }
  return operationsAdmin.load();
}

// ── Settings ───────────────────────────────────────────────────────────────────

async function loadSettings() {
  try {
    const s = await adminApi.getOrchestratorSettings();
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('orch-url')).value = s.apiUrl || '';
    const $status = document.getElementById('orch-key-status');
    const $clearRow = document.getElementById('orch-clear-row');
    if (s.hasApiKey) {
      $status.textContent = '— key is set';
      $clearRow.style.display = '';
    } else {
      $status.textContent = '— not set';
      $clearRow.style.display = 'none';
    }
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('orch-key')).value = '';
    /** @type {HTMLInputElement} */ (document.getElementById('orch-clear-check')).checked = false;
    document.getElementById('orch-error').classList.remove('show');
    document.getElementById('orch-test-result').textContent = '';
  } catch { }

  try {
    const branding = await adminApi.getBrandingSettings();
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-name')).value = branding.displayName || '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-logo')).value = branding.logoUrl || '';
    /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-footer')).value = branding.footerText || '';
    document.getElementById('brand-error').classList.remove('show');
  } catch { }
}

document.getElementById('orchSaveBtn').addEventListener('click', async () => {
  const url = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('orch-url')).value.trim() || null;
  const keyInput = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('orch-key')).value;
  const clearKey = /** @type {HTMLInputElement} */ (document.getElementById('orch-clear-check')).checked;
  // null = keep current; '' = explicitly clear; non-empty = set new value
  const apiKey = clearKey ? '' : (keyInput || null);
  const $err = document.getElementById('orch-error');
  $err.classList.remove('show');
  try {
    await adminApi.updateOrchestratorSettings({ apiUrl: url, apiKey });
    await loadSettings();
    document.getElementById('orch-test-result').textContent = 'Saved.';
  } catch (err) {
    $err.textContent = err.message || 'Failed to save settings.';
    $err.classList.add('show');
  }
});

document.getElementById('orchTestBtn').addEventListener('click', async () => {
  const $result = document.getElementById('orch-test-result');
  $result.textContent = 'Testing…';
  try {
    const resp = await fetch('/api/orchestrator/status', {
      headers: { Authorization: `Bearer ${auth.getToken()}` }
    });
    const data = await resp.json();
    $result.innerHTML = data.online
      ? '<span class="chip chip-active">Online</span>'
      : '<span class="chip chip-inactive">Offline — service unreachable</span>';
  } catch {
    $result.innerHTML = '<span class="chip chip-inactive">Connection failed</span>';
  }
});

document.getElementById('brandSaveBtn').addEventListener('click', async () => {
  const displayName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-name')).value.trim() || null;
  const logoUrl = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-logo')).value.trim() || null;
  const footerText = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('brand-footer')).value.trim() || null;
  const $err = document.getElementById('brand-error');
  $err.classList.remove('show');
  try {
    await adminApi.updateBrandingSettings({ displayName, footerText, logoUrl });
    await loadSettings();
    await applyPortalBranding();
  } catch (err) {
    $err.textContent = err.message || 'Failed to save branding.';
    $err.classList.add('show');
  }
});

// ── Utilities ──────────────────────────────────────────────────────────────────
function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function escAttr(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}
function formatOptionalDate(value) {
  return value ? new Date(value).toLocaleString() : 'Not refreshed';
}
function alertErr(err) { ETLSQLFeedback.notify(err.message || 'An error occurred.', { title: 'Request failed', tone: 'error' }); }

document.querySelectorAll('.modal-overlay').forEach(modal => {
  modal.addEventListener('click', e => {
    if (e.target === modal) /** @type {HTMLElement} */ (modal).style.display = 'none';
  });
});

document.addEventListener('keydown', e => {
  if (e.key !== 'Escape') return;
  const openModal = [...document.querySelectorAll('.modal-overlay')]
    .reverse()
    .find(m => /** @type {HTMLElement} */ (m).style.display !== 'none');
  if (openModal) /** @type {HTMLElement} */ (openModal).style.display = 'none';
});
