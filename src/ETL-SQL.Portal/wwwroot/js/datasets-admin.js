import { aclTableHtml } from './dataset-acl-ui.js?v=0.17.0';

// Canonical "Shared Datasets" admin surface (Admin → Shared Datasets).
//
// Extracted from admin.html so it can be previewed in the UI sandbox without the
// portal/Docker. The module owns its own markup: the datasets panel renders into
// `host`, and the lineage/viewer modals + value-picker popover are appended to
// `modalRoot` (default document.body). Portal dependencies are injected.
//
// Usage (portal):
//   const ds = createDatasetsAdmin({
//     host: document.getElementById('panel-datasets'),
//     datasetsApi, adminApi, catalogApi, renderDag,
//   });
//   ds.load();

function esc(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
function formatOptionalDate(value) {
  return value ? new Date(value).toLocaleString() : 'Not refreshed';
}

const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Reusable data assets</span>
        <h3>Shared Datasets</h3>
      </div>
      <div class="admin-action-group">
        <button class="btn btn-outline btn-sm" id="datasetsRefreshBtn">Refresh</button>
      </div>
    </div>
    <div id="datasetsTableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading datasets…</span></div></div>
  </div>

  <div class="card" id="editDatasetForm" style="display:none">
    <div class="card-header"><h3>Edit Dataset</h3></div>
    <div class="form-row">
      <div class="form-group">
        <label for="ds-accessLevel">Access Level</label>
        <select id="ds-accessLevel">
          <option value="Public">Public</option>
          <option value="Private">Private</option>
        </select>
      </div>
      <div class="form-group">
        <label for="ds-ttl">TTL (e.g. 1h, 30m, 7d — blank = no expiry)</label>
        <input id="ds-ttl" type="text" placeholder="e.g. 1h">
      </div>
    </div>
    <div id="ds-error" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="ds-saveBtn">Save</button>
      <button class="btn btn-outline btn-sm" id="ds-cancelBtn">Cancel</button>
    </div>
    <input type="hidden" id="ds-id">
  </div>

  <div class="card" id="dsAclPanel" style="display:none">
    <div class="card-header">
      <h3>Permissions — <span id="dsAclDatasetName"></span></h3>
      <button class="btn btn-outline btn-sm" id="dsAclCloseBtn">Close</button>
    </div>
    <div id="dsAclTableWrap"></div>
    <div class="stacked-form">
      <h4 class="section-kicker">Grant Permission</h4>
      <div class="form-row">
        <div class="form-group">
          <label for="ds-acl-group">Group</label>
          <select id="ds-acl-group"></select>
        </div>
        <div class="form-group">
          <label for="ds-acl-perm">Permission</label>
          <select id="ds-acl-perm">
            <option value="Viewer">Viewer</option>
            <option value="Editor">Editor</option>
            <option value="Owner">Owner</option>
          </select>
        </div>
      </div>
      <button class="btn btn-primary btn-sm" id="ds-acl-grantBtn">Grant</button>
    </div>
  </div>`;

const MODALS_HTML = `
  <div id="datasetLineageModal" class="modal-overlay" style="display:none" role="dialog" aria-modal="true" aria-labelledby="dsLineageTitle">
    <div class="modal-card modal-lg">
      <div class="modal-header">
        <div>
          <span class="library-kicker">Lineage</span>
          <h3 class="modal-title" id="dsLineageTitle">Dataset Lineage</h3>
          <p class="modal-subtitle">Source tables that feed this dataset, aggregated across recent runs.</p>
        </div>
      </div>
      <div id="dsLineageDag" style="height:420px;min-height:420px;"></div>
      <div class="modal-actions">
        <button class="btn btn-outline" id="dsLineageCloseBtn">Close</button>
      </div>
    </div>
  </div>

  <div id="datasetViewerModal" class="modal-overlay" style="display:none" role="dialog" aria-modal="true" aria-labelledby="dv-title">
    <div class="modal-card dv-modal">
      <div class="modal-header">
        <div>
          <span class="library-kicker">Dataset</span>
          <h3 id="dv-title" class="modal-title">Loading…</h3>
        </div>
        <button class="btn btn-ghost" id="dv-closeBtn" aria-label="Close">✕</button>
      </div>
      <div class="dv-toolbar">
        <span class="dv-counts"><strong id="dv-totalCount">—</strong> rows total · <strong id="dv-filteredCount">—</strong> matching</span>
        <div class="dv-toolbar-right">
          <input type="text" id="dv-search" placeholder="Search all columns…" class="admin-filter-input" style="width:200px">
          <span id="dv-filter-badge" class="chip chip-filter" style="display:none"></span>
          <button class="btn btn-outline btn-sm" id="dv-resetBtn">Reset</button>
          <button class="btn btn-outline btn-sm" id="dv-exportBtn">Export CSV</button>
          <button class="btn btn-outline btn-sm" id="dv-exportXlsxBtn">Export XLSX</button>
        </div>
      </div>
      <div class="dv-table-wrap" id="dv-tableWrap">
        <div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading…</span></div>
      </div>
      <div class="dv-pagination">
        <button class="btn btn-outline btn-sm" id="dv-prevBtn">← Prev</button>
        <span class="dv-page-info" id="dv-pageInfo">Page 1 of —</span>
        <button class="btn btn-outline btn-sm" id="dv-nextBtn">Next →</button>
        <select id="dv-pageSize" class="param-input" style="width:auto">
          <option value="25">25 / page</option>
          <option value="50" selected>50 / page</option>
          <option value="100">100 / page</option>
          <option value="200">200 / page</option>
        </select>
      </div>
    </div>
  </div>

  <div id="dv-pickerPopover" class="dv-picker-popover" style="display:none">
    <input type="text" id="dv-pickerSearch" class="dv-picker-search" placeholder="Search values…">
    <div id="dv-pickerList"></div>
    <button class="btn btn-primary btn-sm dv-picker-apply" id="dv-pickerApply">Apply</button>
  </div>`;

/**
 * Build the Shared Datasets admin surface bound to a host element.
 *
 * @param {Object}   opts
 * @param {Element}  opts.host         Element the datasets panel renders into.
 * @param {Object}   opts.datasetsApi  The portal datasetsApi (list/get/update/delete/refresh/acl/data/stats/columnValues/exportCsv).
 * @param {Object}   opts.adminApi     Needs listGroups() for the ACL grant dropdown.
 * @param {Object}   opts.catalogApi   Needs lineage(kind, args) for the lineage DAG.
 * @param {Function} opts.renderDag    (container, {nodes, edges}) → { dispose }.
 * @param {Function} [opts.confirmFn]  Async confirmation dialog.
 * @param {Function} [opts.alertFn]    Accessible notification function.
 * @param {Element}  [opts.modalRoot]  Where modals are appended (default document.body).
 * @returns {{ load: Function, dispose: Function }}
 */
export function createDatasetsAdmin(opts = {}) {
  const {
    host,
    datasetsApi,
    adminApi,
    catalogApi,
    renderDag,
    confirmFn = (message => window.ETLSQLFeedback.confirm(message, { title: 'Delete dataset', impact: 'The registry entry and cached data will be removed.', confirmLabel: 'Delete dataset', danger: true, auditAction: 'admin.dataset.delete' })),
    alertFn = (message => window.ETLSQLFeedback.notify(message, { title: 'Dataset', tone: 'info' })),
    modalRoot = document.body,
  } = opts;

  const alertErr = err => alertFn(err.message || 'An error occurred.');

  host.innerHTML = PANEL_HTML;
  const modalFrag = document.createElement('div');
  modalFrag.innerHTML = MODALS_HTML;
  const modalEls = [...modalFrag.children];
  modalEls.forEach(el => modalRoot.appendChild(el));

  // ── State ──────────────────────────────────────────────────────────────────
  let allDatasets = [];
  let selectedDatasetId = null;
  let dsLineageDagInstance = null;
  const dv = {
    id: null, name: null, columns: [], stats: null,
    filters: [], sort: null, dir: 'asc', search: '', page: 1, pageSize: 50,
    pickerCol: null, pickerChecked: new Set(),
  };

  // ── Datasets table ─────────────────────────────────────────────────────────
  async function loadDatasets() {
    const $wrap = document.getElementById('datasetsTableWrap');
    try {
      allDatasets = await datasetsApi.list();
      if (!allDatasets.length) {
        $wrap.innerHTML = `
          <div class="empty-state empty-state-panel">
            <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
            <h2>No shared datasets registered</h2>
            <p>Datasets are created by <code>CREATE DATASET</code> statements in report scripts.</p>
          </div>`;
        return;
      }
      const rows = allDatasets.map(d => `
        <tr>
          <td>
            <div class="asset-name">${esc(d.name)}</div>
            <div class="asset-path">${esc(d.folderPath || 'Root')}</div>
          </td>
          <td>
            <span class="chip chip-${(d.accessLevel || '').toLowerCase()}">${esc(d.accessLevel)}</span>
            ${d.isEncrypted ? '<div class="asset-meta">Encrypted cache</div>' : '<div class="asset-meta">Plain cache</div>'}
          </td>
          <td>
            <div class="asset-count">${d.rowCount != null ? d.rowCount.toLocaleString() : '—'}</div>
            <div class="asset-meta">rows</div>
          </td>
          <td>
            <span class="chip ${d.isStale ? 'chip-inactive' : 'chip-active'}">${d.isStale ? 'Stale' : 'Fresh'}</span>
            <div class="asset-meta">TTL ${esc(d.ttl || 'none')}</div>
          </td>
          <td>
            <div class="asset-refresh">
              <span>${esc(formatOptionalDate(d.lastRefresh))}</span>
              <span class="asset-meta">${esc(d.refreshInterval ? `Every ${d.refreshInterval}` : 'Manual refresh')}</span>
            </div>
          </td>
          <td>
            <div class="asset-lineage">${esc(d.owningReportName || 'Unowned dataset')}</div>
            <div class="asset-meta">${d.owningReportId ? `Report #${d.owningReportId}` : 'No owning report'}</div>
          </td>
          <td>
            <div class="table-actions">
              <button class="btn btn-outline btn-sm" data-action="view" data-id="${d.id}" data-name="${escAttr(d.name)}">View Data</button>
              <button class="btn btn-outline btn-sm" data-action="lineage" data-id="${d.id}" data-name="${escAttr(d.name)}">Lineage</button>
              <button class="btn btn-outline btn-sm" data-action="refresh" data-id="${d.id}">Refresh</button>
              <button class="btn btn-outline btn-sm" data-action="edit" data-id="${d.id}">Edit</button>
              <button class="btn btn-outline btn-sm" data-action="acl" data-id="${d.id}" data-name="${escAttr(d.name)}">Permissions</button>
              <button class="btn btn-outline btn-sm btn-danger-soft" data-action="delete" data-id="${d.id}">Delete</button>
            </div>
          </td>
        </tr>`).join('');
      $wrap.innerHTML = `
        <div class="dataset-table-wrap">
          <table class="data-table">
            <thead><tr><th>Dataset</th><th>Access</th><th>Rows</th><th>Status</th><th>Refresh</th><th>Lineage</th><th>Actions</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
        </div>`;
      $wrap.querySelectorAll('[data-action]').forEach(btn => {
        btn.addEventListener('click', () => handleDatasetAction(btn));
      });
    } catch { $wrap.innerHTML = '<div class="empty-state empty-state-panel empty-state-error">Failed to load datasets.</div>'; }
  }

  async function handleDatasetAction(btn) {
    const id = +btn.dataset.id;
    const action = btn.dataset.action;

    if (action === 'view') {
      dvOpen(id, btn.dataset.name || 'Dataset');
      return;
    } else if (action === 'lineage') {
      showDatasetLineage(btn.dataset.name || 'Dataset');
      return;
    } else if (action === 'delete') {
      if (!await confirmFn('Delete this dataset? This removes the registry entry and cached data.')) return;
      const dataset = allDatasets.find(x => x.id === id);
      await datasetsApi.delete(id, dataset?.version).catch(alertErr);
      loadDatasets();
    } else if (action === 'refresh') {
      try {
        const result = await datasetsApi.refresh(id);
        alertFn(result.triggered
          ? `Refresh job queued (job ${result.jobId}).`
          : result.message || 'Dataset marked stale — will re-materialise on next script run.');
        loadDatasets();
      } catch (err) { alertErr(err); }
    } else if (action === 'edit') {
      const d = allDatasets.find(x => x.id === id);
      if (!d) return;
      document.getElementById('ds-accessLevel').value = d.accessLevel;
      document.getElementById('ds-ttl').value = d.ttl || '';
      document.getElementById('ds-id').value = d.id;
      document.getElementById('ds-error').classList.remove('show');
      document.getElementById('dsAclPanel').style.display = 'none';
      document.getElementById('editDatasetForm').style.display = '';
    } else if (action === 'acl') {
      selectedDatasetId = id;
      document.getElementById('dsAclDatasetName').textContent = btn.dataset.name;
      document.getElementById('editDatasetForm').style.display = 'none';
      document.getElementById('dsAclPanel').style.display = '';
      loadDatasetAcl(id);
      adminApi.listGroups().then(groups => {
        document.getElementById('ds-acl-group').innerHTML =
          groups.map(g => `<option value="${g.id}">${esc(g.name)}</option>`).join('');
      }).catch(() => {});
    }
  }

  // ── Dataset lineage DAG ─────────────────────────────────────────────────────
  async function showDatasetLineage(datasetName) {
    const $modal = document.getElementById('datasetLineageModal');
    const $dag   = document.getElementById('dsLineageDag');
    document.getElementById('dsLineageTitle').textContent = `Lineage — ${datasetName}`;
    $modal.style.display = 'flex';
    $dag.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading lineage…</span></div>';
    if (dsLineageDagInstance) { dsLineageDagInstance.dispose(); dsLineageDagInstance = null; }

    try {
      const rows = await catalogApi.lineage('table', { name: datasetName, limit: 100 }) ?? [];
      const tableSet = new Set();
      const edgeMap  = new Map();
      for (const row of rows) {
        const target = row.targetTable;
        if (!target) continue;
        tableSet.add(target);
        for (const src of (row.sourceTables || [])) {
          tableSet.add(src);
          edgeMap.set(`${src}→${target}`, { source: src, target });
        }
      }
      $dag.innerHTML = '';
      const nodes = [...tableSet].map(t => ({
        id: t, label: t,
        type: t.toLowerCase() === datasetName.toLowerCase() ? 'dataset' : 'table'
      }));
      const edges = [...edgeMap.values()];
      dsLineageDagInstance = renderDag($dag, { nodes, edges });
    } catch (err) {
      $dag.innerHTML = `<div class="empty-state">Failed to load lineage: ${esc(err.message)}</div>`;
    }
  }

  async function loadDatasetAcl(datasetId) {
    const $wrap = document.getElementById('dsAclTableWrap');
    try {
      const acls = await datasetsApi.listAcl(datasetId);
      $wrap.innerHTML = aclTableHtml(acls, esc, escAttr);
      const revoke = async (call) => {
        const dataset = allDatasets.find(x => x.id === datasetId);
        await call(dataset?.version).catch(alertErr);
        await loadDatasets();
        loadDatasetAcl(datasetId);
      };
      $wrap.querySelectorAll('[data-gid]').forEach(btn => {
        btn.addEventListener('click', () =>
          revoke(version => datasetsApi.revokeAcl(datasetId, +btn.dataset.gid, version)));
      });
      $wrap.querySelectorAll('[data-uid]').forEach(btn => {
        btn.addEventListener('click', () =>
          revoke(version => datasetsApi.revokeUserAcl(datasetId, +btn.dataset.uid, version)));
      });
    } catch {}
  }

  // ── Dataset Viewer ──────────────────────────────────────────────────────────
  function dvOpen(id, name) {
    Object.assign(dv, { id, name, columns: [], stats: null, filters: [], sort: null, dir: 'asc', search: '', page: 1, pageSize: 50, pickerCol: null, pickerChecked: new Set() });
    document.getElementById('dv-title').textContent = name;
    document.getElementById('dv-search').value = '';
    document.getElementById('dv-pageSize').value = '50';
    document.getElementById('datasetViewerModal').style.display = 'flex';
    dvFetch();
    dvFetchStats();
  }

  async function dvFetch() {
    const $wrap = document.getElementById('dv-tableWrap');
    $wrap.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading…</span></div>';
    try {
      const res = await datasetsApi.data(dv.id, { page: dv.page, pageSize: dv.pageSize, sort: dv.sort, dir: dv.dir, search: dv.search, filters: dv.filters });
      dv.columns = res.columns || [];
      dvRender(res);
      document.getElementById('dv-totalCount').textContent    = (res.totalCount    ?? 0).toLocaleString();
      document.getElementById('dv-filteredCount').textContent = (res.filteredCount ?? 0).toLocaleString();
      const totalPages = Math.max(1, Math.ceil((res.filteredCount ?? 0) / dv.pageSize));
      document.getElementById('dv-pageInfo').textContent = `Page ${dv.page} of ${totalPages}`;
      document.getElementById('dv-prevBtn').disabled = dv.page <= 1;
      document.getElementById('dv-nextBtn').disabled = dv.page >= totalPages;
      dvUpdateBadge();
    } catch (err) {
      $wrap.innerHTML = `<div class="error-msg show">${esc(err.message)}</div>`;
    }
  }

  async function dvFetchStats() {
    try {
      dv.stats = await datasetsApi.stats(dv.id, dv.filters.length ? dv.filters : null);
      dvRefreshStatsFooter();
    } catch { dv.stats = null; }
  }

  function dvGuessType(typeStr) {
    const t = (typeStr || '').toLowerCase();
    if (/int|float|double|decimal|numeric|real|money|number/.test(t)) return 'number';
    if (/date|time/.test(t)) return 'date';
    return 'text';
  }

  function dvRender(res) {
    const cols = res.columns || [];
    const rows = res.rows    || [];

    const headerCells = cols.map(col => {
      const arrow = dv.sort === col.name ? (dv.dir === 'asc' ? ' ↑' : ' ↓') : '';
      return `<th class="dv-col-header" data-col="${escAttr(col.name)}">${esc(col.name)}${arrow}<span class="col-type-tag">${esc(col.type)}</span></th>`;
    }).join('');

    const filterCells = cols.map(col => {
      const type = dvGuessType(col.type);
      return `<th class="dv-filter-cell">${dvFilterHtml(col.name, type)}</th>`;
    }).join('');

    const statsRow = (dv.stats && dv.stats.length)
      ? `<tfoot><tr class="dv-stats-row">${cols.map(col => {
          const s = dv.stats.find(x => x.name === col.name);
          if (!s) return '<td></td>';
          const type = dvGuessType(col.type);
          if (type === 'number') return `<td><span class="dv-stats-label">Min</span> ${s.min ?? '—'} <span class="dv-stats-label">Max</span> ${s.max ?? '—'} <span class="dv-stats-label">Avg</span> ${s.avg != null ? (+s.avg.toFixed(2)) : '—'} <span class="dv-stats-label">Nulls</span> ${s.nullCount}</td>`;
          return `<td><span class="dv-stats-label">Nulls</span> ${s.nullCount}</td>`;
        }).join('')}</tr></tfoot>`
      : '';

    const dataRows = rows.length
      ? rows.map(row => `<tr>${cols.map(col => {
          const v = row[col.name];
          return `<td>${v == null ? '<span class="dv-null">null</span>' : esc(String(v))}</td>`;
        }).join('')}</tr>`).join('')
      : `<tr><td colspan="${cols.length || 1}" style="text-align:center;color:var(--portal-muted);padding:20px">No rows match the current filters.</td></tr>`;

    const $wrap = document.getElementById('dv-tableWrap');
    $wrap.innerHTML = `
      <table class="data-table dv-table">
        <thead>
          <tr>${headerCells}</tr>
          <tr class="dv-filter-row">${filterCells}</tr>
        </thead>
        <tbody>${dataRows}</tbody>
        ${statsRow}
      </table>`;

    // Sort
    $wrap.querySelectorAll('.dv-col-header').forEach(th => th.addEventListener('click', () => {
      const col = th.dataset.col;
      dv.dir = (dv.sort === col && dv.dir === 'asc') ? 'desc' : 'asc';
      dv.sort = col; dv.page = 1; dvFetch();
    }));

    // Restore filter values from dv.filters and wire inputs
    cols.forEach(col => {
      const type = dvGuessType(col.type);
      const existing = dv.filters.find(f => f.col === col.name);
      const opEl  = $wrap.querySelector(`.dv-filter-op[data-col="${CSS.escape(col.name)}"]`);
      const valEl = $wrap.querySelector(`.dv-filter-val[data-col="${CSS.escape(col.name)}"]`);
      const val2El= $wrap.querySelector(`.dv-filter-val2[data-col="${CSS.escape(col.name)}"]`);

      if (existing) {
        if (opEl)  opEl.value  = existing.op;
        if (valEl) valEl.value = existing.val  ?? '';
        if (val2El) val2El.value = existing.val2 ?? '';
      }

      // Show val2 for range ops
      function syncVal2() {
        if (!val2El) return;
        const op = opEl ? opEl.value : 'between';
        val2El.style.display = (op === 'between' || type === 'date') ? '' : 'none';
      }
      if (opEl) { opEl.addEventListener('change', syncVal2); syncVal2(); }

      // Apply filter on blur/enter
      function applyFilter() {
        const op  = opEl  ? opEl.value  : (type === 'date' ? 'between' : 'contains');
        const val = valEl ? valEl.value : '';
        const val2= val2El ? val2El.value : '';
        dv.filters = dv.filters.filter(f => f.col !== col.name);
        if (op === 'is_null' || op === 'not_null' || val.trim()) {
          dv.filters.push({ col: col.name, op, val: val || null, val2: val2 || null });
        }
        dv.page = 1; dvFetch(); dvFetchStats();
      }

      [valEl, val2El].filter(Boolean).forEach(el => {
        el.addEventListener('keydown', e => { if (e.key === 'Enter') applyFilter(); });
        el.addEventListener('blur', e => {
          // Skip if focus is moving to the sibling input for the same column (val1↔val2 tab)
          if (e.relatedTarget === valEl || e.relatedTarget === val2El) return;
          applyFilter();
        });
      });
      if (opEl) opEl.addEventListener('change', () => {
        const op = opEl.value;
        if (op === 'is_null' || op === 'not_null') applyFilter();
      });

      // Picker button
      const pickerBtn = $wrap.querySelector(`.dv-picker-btn[data-col="${CSS.escape(col.name)}"]`);
      if (pickerBtn) pickerBtn.addEventListener('click', e => { e.stopPropagation(); dvOpenPicker(col.name, pickerBtn); });
    });

    // Date filter val2 always shown for date type (handled by syncVal2 default)
    $wrap.querySelectorAll('.dv-filter-val2').forEach(el => {
      const col = el.dataset.col;
      const type = dvGuessType((cols.find(c => c.name === col) || {}).type || '');
      if (type === 'date') el.style.display = '';
    });
  }

  function dvRefreshStatsFooter() {
    if (!dv.stats || !dv.columns.length) return;
    const table = document.querySelector('#dv-tableWrap .dv-table');
    if (!table) return;

    let row = table.querySelector('.dv-stats-row');
    if (!row) {
      // Stats resolved after data — inject the tfoot now
      const tfoot = document.createElement('tfoot');
      const tr    = document.createElement('tr');
      tr.className = 'dv-stats-row';
      dv.columns.forEach(() => tr.appendChild(document.createElement('td')));
      tfoot.appendChild(tr);
      table.appendChild(tfoot);
      row = tr;
    }

    const tds = row.querySelectorAll('td');
    dv.columns.forEach((col, i) => {
      if (!tds[i]) return;
      const s = dv.stats.find(x => x.name === col.name);
      if (!s) return;
      const type = dvGuessType(col.type);
      if (type === 'number')
        tds[i].innerHTML = `<span class="dv-stats-label">Min</span> ${s.min ?? '—'} <span class="dv-stats-label">Max</span> ${s.max ?? '—'} <span class="dv-stats-label">Avg</span> ${s.avg != null ? (+s.avg.toFixed(2)) : '—'} <span class="dv-stats-label">Nulls</span> ${s.nullCount}`;
      else
        tds[i].innerHTML = `<span class="dv-stats-label">Nulls</span> ${s.nullCount}`;
    });
  }

  function dvFilterHtml(colName, type) {
    const safe = escAttr(colName);
    const opSel = type === 'text'
      ? `<select class="dv-filter-op param-input" data-col="${safe}" title="op">
          <option value="contains">contains</option><option value="eq">equals</option>
          <option value="starts_with">starts with</option><option value="neq">≠</option>
          <option value="is_null">null</option><option value="not_null">not null</option>
         </select>`
      : type === 'number'
      ? `<select class="dv-filter-op param-input" data-col="${safe}" title="op">
          <option value="between">between</option><option value="eq">=</option>
          <option value="gt">&gt;</option><option value="lt">&lt;</option>
          <option value="gte">≥</option><option value="lte">≤</option>
          <option value="is_null">null</option><option value="not_null">not null</option>
         </select>`
      : '';

    const val1 = type === 'date'
      ? `<input type="date" class="dv-filter-val param-input" data-col="${safe}">`
      : `<input type="${type === 'number' ? 'number' : 'text'}" class="dv-filter-val param-input" data-col="${safe}" placeholder="filter…">`;

    const val2 = `<input type="${type === 'date' ? 'date' : 'number'}" class="dv-filter-val2 param-input" data-col="${safe}" placeholder="${type === 'date' ? '' : 'max'}" style="display:none">`;

    const picker = (type !== 'date') ? `<button class="dv-picker-btn" data-col="${safe}" title="Pick values">≡</button>` : '';

    return `<div class="dv-${type === 'date' ? 'range' : 'text'}-filter">${opSel}${val1}${val2}${picker}</div>`;
  }

  function dvUpdateBadge() {
    const $badge = document.getElementById('dv-filter-badge');
    if (dv.filters.length) {
      $badge.textContent = `${dv.filters.length} filter${dv.filters.length > 1 ? 's' : ''}`;
      $badge.style.display = '';
    } else {
      $badge.style.display = 'none';
    }
  }

  // ── Value picker ────────────────────────────────────────────────────────────
  async function dvOpenPicker(colName, anchor) {
    dv.pickerCol = colName;
    const popover = document.getElementById('dv-pickerPopover');
    const $list   = document.getElementById('dv-pickerList');
    const $search = document.getElementById('dv-pickerSearch');

    const existing = dv.filters.find(f => f.col === colName && f.op === 'in');
    dv.pickerChecked = existing ? new Set(JSON.parse(existing.val || '[]')) : new Set();

    $search.value = '';
    $list.innerHTML = '<span style="color:var(--portal-muted);font-size:.84em">Loading…</span>';
    popover.style.display = '';

    const rect = anchor.getBoundingClientRect();
    popover.style.left = `${Math.min(rect.left, window.innerWidth - 220)}px`;
    popover.style.top  = `${rect.bottom + 4}px`;

    await dvLoadPickerValues('');

    $search.oninput = () => dvLoadPickerValues($search.value);
  }

  async function dvLoadPickerValues(search) {
    const $list = document.getElementById('dv-pickerList');
    try {
      const res = await datasetsApi.columnValues(dv.id, dv.pickerCol, { search, limit: 50 });
      const values = res.values || [];
      $list.innerHTML = values.map(v => {
        const s = v == null ? '' : String(v);
        const checked = dv.pickerChecked.has(s) ? 'checked' : '';
        return `<label class="dv-picker-item"><input type="checkbox" value="${escAttr(s)}" ${checked}> ${esc(s || '(empty)')}</label>`;
      }).join('') || '<span style="color:var(--portal-muted);font-size:.84em">No values found.</span>';
    } catch { $list.innerHTML = '<span style="color:var(--portal-muted)">Error loading values.</span>'; }
  }

  // ── Wiring ──────────────────────────────────────────────────────────────────
  const onDocClickPicker = e => {
    const pop = document.getElementById('dv-pickerPopover');
    if (pop && !pop.contains(e.target) && !e.target.classList.contains('dv-picker-btn')) {
      pop.style.display = 'none';
    }
  };
  const onDocKeydownEsc = e => {
    if (e.key !== 'Escape') return;
    const open = modalEls.filter(m => m.classList.contains('modal-overlay')).reverse().find(m => m.style.display !== 'none');
    if (open) open.style.display = 'none';
    const pop = document.getElementById('dv-pickerPopover');
    if (pop) pop.style.display = 'none';
  };

  function wire() {
    document.getElementById('datasetsRefreshBtn').addEventListener('click', () => loadDatasets());

    document.getElementById('dsLineageCloseBtn').addEventListener('click', () => {
      document.getElementById('datasetLineageModal').style.display = 'none';
      if (dsLineageDagInstance) { dsLineageDagInstance.dispose(); dsLineageDagInstance = null; }
    });

    document.getElementById('ds-cancelBtn').addEventListener('click', () => {
      document.getElementById('editDatasetForm').style.display = 'none';
    });
    document.getElementById('ds-saveBtn').addEventListener('click', async () => {
      const id = +document.getElementById('ds-id').value;
      const accessLevel = document.getElementById('ds-accessLevel').value;
      const ttl = document.getElementById('ds-ttl').value.trim() || null;
      const $err = document.getElementById('ds-error');
      $err.classList.remove('show');
      try {
        const dataset = allDatasets.find(x => x.id === id);
        await datasetsApi.update(id, { accessLevel, ttl }, dataset?.version);
        document.getElementById('editDatasetForm').style.display = 'none';
        loadDatasets();
      } catch (err) { $err.textContent = err.message; $err.classList.add('show'); }
    });

    document.getElementById('dsAclCloseBtn').addEventListener('click', () => {
      document.getElementById('dsAclPanel').style.display = 'none';
    });
    document.getElementById('ds-acl-grantBtn').addEventListener('click', async () => {
      const groupId = +document.getElementById('ds-acl-group').value;
      const permission = document.getElementById('ds-acl-perm').value;
      if (!groupId) return;
      const dataset = allDatasets.find(x => x.id === selectedDatasetId);
      await datasetsApi.grantAcl(selectedDatasetId, groupId, permission, dataset?.version).catch(alertErr);
      await loadDatasets();
      loadDatasetAcl(selectedDatasetId);
    });

    document.getElementById('dv-pickerList').addEventListener('change', e => {
      if (e.target.type !== 'checkbox') return;
      if (e.target.checked) dv.pickerChecked.add(e.target.value);
      else dv.pickerChecked.delete(e.target.value);
    });
    document.getElementById('dv-pickerApply').addEventListener('click', () => {
      const col = dv.pickerCol;
      dv.filters = dv.filters.filter(f => !(f.col === col && f.op === 'in'));
      if (dv.pickerChecked.size) {
        dv.filters.push({ col, op: 'in', val: JSON.stringify([...dv.pickerChecked]), val2: null });
      }
      document.getElementById('dv-pickerPopover').style.display = 'none';
      dv.page = 1; dvFetch(); dvFetchStats();
    });
    document.addEventListener('click', onDocClickPicker);

    document.getElementById('dv-search').addEventListener('keydown', e => {
      if (e.key !== 'Enter') return;
      dv.search = e.target.value.trim(); dv.page = 1; dvFetch();
    });
    document.getElementById('dv-resetBtn').addEventListener('click', () => {
      dv.filters = []; dv.search = ''; dv.sort = null; dv.dir = 'asc'; dv.page = 1;
      document.getElementById('dv-search').value = '';
      dvFetch(); dvFetchStats();
    });
    document.getElementById('dv-exportBtn').addEventListener('click', async () => {
      try { await datasetsApi.exportCsv(dv.id, dv.name + '.csv', { sort: dv.sort, dir: dv.dir, search: dv.search, filters: dv.filters }); }
      catch (err) { alertErr(err); }
    });
    document.getElementById('dv-exportXlsxBtn').addEventListener('click', async () => {
      try { await datasetsApi.exportXlsx(dv.id, dv.name + '.xlsx', { sort: dv.sort, dir: dv.dir, search: dv.search, filters: dv.filters }); }
      catch (err) { alertErr(err); }
    });
    document.getElementById('dv-closeBtn').addEventListener('click', () => {
      document.getElementById('datasetViewerModal').style.display = 'none';
      document.getElementById('dv-pickerPopover').style.display   = 'none';
    });
    document.getElementById('dv-prevBtn').addEventListener('click', () => { if (dv.page > 1) { dv.page--; dvFetch(); } });
    document.getElementById('dv-nextBtn').addEventListener('click', () => { dv.page++; dvFetch(); });
    document.getElementById('dv-pageSize').addEventListener('change', e => {
      dv.pageSize = +e.target.value; dv.page = 1; dvFetch();
    });

    // Close modals on overlay backdrop click + Escape (the module owns its modals).
    modalEls.filter(m => m.classList.contains('modal-overlay')).forEach(modal => {
      modal.addEventListener('click', e => { if (e.target === modal) modal.style.display = 'none'; });
    });
    document.addEventListener('keydown', onDocKeydownEsc);
  }

  wire();

  return {
    load: loadDatasets,
    dispose() {
      if (dsLineageDagInstance) { dsLineageDagInstance.dispose(); dsLineageDagInstance = null; }
      document.removeEventListener('click', onDocClickPicker);
      document.removeEventListener('keydown', onDocKeydownEsc);
      modalEls.forEach(el => el.remove());
    },
  };
}
