import { importFresh } from '../util.js';

const CATALOG_UI_JS = '/src/ETL-SQL.Portal/wwwroot/js/admin-catalog-ui.js';

const users = [
  { id: 1, userName: 'finance_read', email: 'finance_read@example.com', role: 'Viewer', active: true },
  { id: 2, userName: 'finance_publish', email: 'finance_publish@example.com', role: 'Publisher', active: true },
  { id: 3, userName: 'ops_read', email: 'ops_read@example.com', role: 'Viewer', active: false },
];

export default {
  id: 'admin-catalog',
  title: 'Admin catalog controls',
  subtitle: 'Paged user administration',
  fixtures: [{ id: 'users', label: 'Users catalog' }],
  async mount(stage, _fixtureId, ctx) {
    stage.classList.add('portal-page');
    const mod = await importFresh(CATALOG_UI_JS);
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = `
      <div class="card-header">
        <div><span class="section-kicker">Identity</span><h3>Users</h3></div>
        <div class="admin-action-group">
          <input type="search" value="finance" class="admin-filter-input" aria-label="Search users">
          <select class="admin-filter-select" aria-label="Filter users by status"><option>All statuses</option><option selected>Active</option></select>
          <button class="btn btn-outline btn-sm" data-enable disabled>Enable</button>
          <button class="btn btn-outline btn-sm" data-disable disabled>Disable</button>
          <button class="btn btn-outline btn-sm">New User</button>
        </div>
      </div>
      <div data-table></div>
      <div class="admin-pager" data-pager></div>`;
    const rows = users.map(user => `<tr>
      ${mod.selectionCell(user.id, `user ${user.userName}`)}
      <td>${user.userName}</td><td>${user.email}</td><td><span class="chip chip-viewer">${user.role}</span></td>
      <td><span class="chip ${user.active ? 'chip-active' : 'chip-inactive'}">${user.active ? 'Active' : 'Inactive'}</span></td>
      <td><div class="table-actions"><button class="btn btn-outline btn-sm">Edit</button><button class="btn btn-outline btn-sm">${user.active ? 'Disable' : 'Enable'}</button></div></td>
    </tr>`).join('');
    card.querySelector('[data-table]').innerHTML = `<table class="data-table">
      <thead><tr>${mod.headerSelectionCell('users')}<th>Username</th><th>Email</th><th>Roles</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>${rows}</tbody></table>`;
    mod.bindSelection(card, ids => {
      card.querySelector('[data-enable]').disabled = ids.length === 0;
      card.querySelector('[data-disable]').disabled = ids.length === 0;
      ctx.stat(`${ids.length} selected`);
    });
    mod.renderCatalogPager(card.querySelector('[data-pager]'), { total: 87, page: 2, pageSize: 25 }, page => ctx.stat(`Page ${page}`));
    stage.replaceChildren(card);
    return { resize() {} };
  },
};
