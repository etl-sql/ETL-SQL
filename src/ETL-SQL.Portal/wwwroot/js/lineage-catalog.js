// Canonical "Lineage" sidebar view — the catalog-wide lineage explorer.
//
// Extracted from index.html so it can be (a) unit-tested and (b) previewed in the
// UI sandbox without the full portal. The portal shell injects its API + shared
// helpers; the module owns its own query state and rendering.
//
// Usage (portal):
//   const catalog = createLineageCatalog({
//     host: document.getElementById('mainContent'),
//     catalogApi, renderDag, renderLineageRow, lineageRowsToCsv,
//     openReport, timeAgo, formatBuiltAt,
//     prepare: () => { setSidebarViewActive('lineage'); clearFolderSelection(); },
//   });
//   catalog.render();

function esc(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

const memoryStorage = (() => {
  const m = new Map();
  return { getItem: k => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: k => m.delete(k) };
})();

/**
 * Build a lineage catalog explorer bound to a host element.
 *
 * @param {Object}   opts
 * @param {Element}  opts.host                  Container the view renders into.
 * @param {Object}   opts.catalogApi            { lineage(kind, args): Promise<row[]> }.
 * @param {Function} opts.renderDag             (container, {nodes, edges}) → { dispose }.
 * @param {Function} opts.renderLineageRow      (row, { timeAgo, formatBuiltAt }) → html string.
 * @param {Function} opts.lineageRowsToCsv      (rows) → csv string.
 * @param {Function} [opts.openReport]          (id) → void, invoked from a row's "open report" button.
 * @param {Function} [opts.timeAgo]             (iso) → relative string.
 * @param {Function} [opts.formatBuiltAt]       (iso) → absolute string.
 * @param {Storage}  [opts.storage]             localStorage-like store for saved views.
 * @param {Function} [opts.prepare]             Side-effects to run before each full render (nav state).
 * @param {Function} [opts.promptFn]            Prompt used when naming a saved view.
 * @param {string}   [opts.viewKey]             Storage key for saved views.
 * @returns {{ render: Function, dispose: Function, state: Object }}
 */
export function createLineageCatalog(opts = {}) {
  const {
    host,
    catalogApi,
    renderDag,
    renderLineageRow,
    lineageRowsToCsv,
    openReport = () => {},
    timeAgo = v => v,
    formatBuiltAt = v => v,
    storage = (typeof localStorage !== 'undefined' ? localStorage : memoryStorage),
    prepare = () => {},
    promptFn = (typeof window !== 'undefined' && window.prompt ? window.prompt.bind(window) : () => null),
    viewKey = 'etlsql_lineage_views',
  } = opts;

  const state = { kind: 'table', query: '', column: '', tagValue: '', from: '', to: '', limit: 100, rows: [], savedViews: [], selectedView: '', view: 'table' };
  let dagInstance = null;

  const $ = sel => host.querySelector(sel);

  // ── Saved views (localStorage) ────────────────────────────────────────────
  function loadLineageViews() {
    try {
      const parsed = JSON.parse(storage.getItem(viewKey) || '[]');
      return Array.isArray(parsed)
        ? parsed.filter(v => v && typeof v.name === 'string').sort((a, b) => a.name.localeCompare(b.name))
        : [];
    } catch {
      return [];
    }
  }
  function persistLineageViews(views) {
    storage.setItem(viewKey, JSON.stringify(views));
  }
  function currentLineageView(name) {
    return { name, kind: state.kind, query: state.query, column: state.column, tagValue: state.tagValue, from: state.from, to: state.to };
  }
  function saveCurrentLineageView() {
    const name = promptFn('Saved view name', state.query || state.kind);
    if (!name || !name.trim()) return;
    state.savedViews = loadLineageViews().filter(v => v.name !== name.trim());
    state.selectedView = name.trim();
    state.savedViews.push(currentLineageView(name.trim()));
    persistLineageViews(state.savedViews);
    render();
  }
  function applySelectedLineageView() {
    const selectedName = $('#lineageSavedView').value;
    $('#lineageDeleteViewBtn').toggleAttribute('disabled', !selectedName);
    if (!selectedName) return;
    const view = loadLineageViews().find(v => v.name === selectedName);
    if (!view) return;
    state.selectedView = selectedName;
    state.kind = view.kind || 'table';
    state.query = view.query || '';
    state.column = view.column || '';
    state.tagValue = view.tagValue || '';
    state.from = view.from || '';
    state.to = view.to || '';
    state.rows = [];
    render();
  }
  function deleteSelectedLineageView() {
    const selectedName = $('#lineageSavedView').value;
    if (!selectedName) return;
    state.savedViews = loadLineageViews().filter(v => v.name !== selectedName);
    state.selectedView = '';
    persistLineageViews(state.savedViews);
    render();
  }

  // ── Results ───────────────────────────────────────────────────────────────
  function renderLineageEmpty(message = 'Choose a catalog query and run Search.') {
    return `<div class="empty-state empty-state-panel">
      <div class="empty-state-icon empty-state-icon-lineage" aria-hidden="true"></div>
      <h2>No lineage results</h2>
      <p>${esc(message)}</p>
    </div>`;
  }

  function lineageDateStart(value) { return value ? `${value}T00:00:00Z` : null; }
  function lineageDateEnd(value) { return value ? `${value}T23:59:59Z` : null; }

  async function loadLineageResults() {
    const $results = $('#lineageResults');
    if (!state.query) {
      state.rows = [];
      $results.innerHTML = renderLineageEmpty('Enter a table, source, file, tag, or job name.');
      return;
    }
    $results.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading lineage…</span></div>`;
    try {
      const args = { limit: state.limit, from: lineageDateStart(state.from), to: lineageDateEnd(state.to) };
      if (state.kind === 'tag') {
        args.key = state.query;
        if (state.tagValue) args.value = state.tagValue;
      } else if (state.kind === 'source-file') {
        args.path = state.query;
      } else {
        args.name = state.query;
        if (state.kind === 'table' && state.column) args.column = state.column;
      }

      const rows = await catalogApi.lineage(state.kind, args);
      state.rows = rows || [];
      renderLineageResults(rows || []);
      $('#lineageExportBtn')?.toggleAttribute('disabled', !state.rows.length);
    } catch (err) {
      state.rows = [];
      $results.innerHTML = `<div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Lineage query failed</h2>
        <p>${esc(err.message)}</p>
      </div>`;
    }
  }

  function renderLineageResults(rows) {
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }
    if (!rows.length) {
      state.rows = [];
      $results.innerHTML = renderLineageEmpty('No matching lineage history was found.');
      return;
    }

    if (state.view === 'graph') {
      renderLineageDag(rows);
      return;
    }

    $results.innerHTML = `
      <div class="lineage-result-list">
        ${rows.map(row => renderLineageRow(row, { timeAgo, formatBuiltAt })).join('')}
      </div>`;

    $results.querySelectorAll('[data-open-report]').forEach(btn => {
      btn.addEventListener('click', e => {
        e.stopPropagation();
        openReport(+btn.dataset.openReport);
      });
    });
  }

  function renderLineageDag(rows) {
    const $results = $('#lineageResults');
    $results.innerHTML = '<div id="lineageDagContainer" style="height:480px;"></div>';
    const container = $('#lineageDagContainer');

    const targets = new Set(rows.map(r => r.targetTable).filter(Boolean));
    const sources = new Set(rows.flatMap(r => r.sourceTables || []).filter(Boolean));
    const allTables = new Set([...targets, ...sources]);

    const nodeMap = {};
    [...allTables].forEach((name, i) => {
      const isTarget = targets.has(name);
      const isSource = sources.has(name);
      const type = isTarget && isSource ? 'dataset' : isTarget ? 'io' : 'table';
      nodeMap[name] = { id: `t${i}`, label: name, type };
    });

    const nodes = Object.values(nodeMap);
    const seenEdges = new Set();
    const edges = [];
    for (const row of rows) {
      if (!row.targetTable || !nodeMap[row.targetTable]) continue;
      for (const src of (row.sourceTables || [])) {
        if (!src || !nodeMap[src]) continue;
        const key = `${src}->${row.targetTable}`;
        if (!seenEdges.has(key)) {
          seenEdges.add(key);
          edges.push({ source: nodeMap[src].id, target: nodeMap[row.targetTable].id, label: row.operation || null });
        }
      }
    }

    dagInstance = renderDag(container, { nodes, edges });
  }

  function exportLineageCsv() {
    if (!state.rows.length) return;
    const csv = lineageRowsToCsv(state.rows);
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `lineage-${state.kind}-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // ── Render shell + wiring ──────────────────────────────────────────────────
  function render() {
    prepare();
    state.savedViews = loadLineageViews();
    const valueLabel = state.kind === 'tag' ? 'Tag key' : state.kind === 'source-file' ? 'Source file' : 'Name';
    const savedOptions = state.savedViews
      .map(v => `<option value="${escAttr(v.name)}"${v.name === state.selectedView ? ' selected' : ''}>${esc(v.name)}</option>`)
      .join('');
    host.innerHTML = `
      <div class="library-toolbar lineage-toolbar">
        <div class="library-title">
          <span class="library-kicker">Catalog</span>
          <h2>Lineage</h2>
          <span class="folder-count">Cross-run history with report context</span>
        </div>
        <form id="lineageSearchForm" class="lineage-query" autocomplete="off">
          <select id="lineageKind" class="library-sort" aria-label="Lineage query type">
            <option value="table"${state.kind === 'table' ? ' selected' : ''}>Target table</option>
            <option value="source"${state.kind === 'source' ? ' selected' : ''}>Source table</option>
            <option value="source-file"${state.kind === 'source-file' ? ' selected' : ''}>Source file</option>
            <option value="tag"${state.kind === 'tag' ? ' selected' : ''}>Tag</option>
            <option value="job"${state.kind === 'job' ? ' selected' : ''}>Job</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="lineageQuery" type="search" placeholder="${escAttr(valueLabel)}" value="${escAttr(state.query)}">
          </label>
          <input id="lineageColumn" class="lineage-input lineage-column" type="search" placeholder="Column" value="${escAttr(state.column)}" ${state.kind === 'table' ? '' : 'hidden'}>
          <input id="lineageTagValue" class="lineage-input" type="search" placeholder="Tag value" value="${escAttr(state.tagValue)}" ${state.kind === 'tag' ? '' : 'hidden'}>
          <input id="lineageFrom" class="lineage-date" type="date" value="${escAttr(state.from)}" aria-label="From date">
          <input id="lineageTo" class="lineage-date" type="date" value="${escAttr(state.to)}" aria-label="To date">
          <button class="btn btn-primary" type="submit">Search</button>
          <button class="btn btn-outline" id="lineageExportBtn" type="button" ${state.rows.length ? '' : 'disabled'}>Export CSV</button>
          <button class="btn btn-outline" id="lineageViewToggle" type="button">${state.view === 'graph' ? 'Table' : 'Graph'}</button>
          <select id="lineageSavedView" class="library-sort lineage-saved" aria-label="Saved lineage view">
            <option value="">Saved views</option>
            ${savedOptions}
          </select>
          <button class="btn btn-outline" id="lineageSaveViewBtn" type="button">Save View</button>
          <button class="btn btn-outline" id="lineageDeleteViewBtn" type="button" ${state.selectedView ? '' : 'disabled'}>Delete</button>
        </form>
      </div>
      <div id="lineageResults">${renderLineageEmpty()}</div>`;

    const $form = $('#lineageSearchForm');
    $('#lineageKind').addEventListener('change', e => {
      state.kind = e.target.value;
      state.column = '';
      state.tagValue = '';
      state.rows = [];
      render();
    });
    $form.addEventListener('submit', async e => {
      e.preventDefault();
      state.kind = $('#lineageKind').value;
      state.query = $('#lineageQuery').value.trim();
      state.column = $('#lineageColumn').value.trim();
      state.tagValue = $('#lineageTagValue').value.trim();
      state.from = $('#lineageFrom').value;
      state.to = $('#lineageTo').value;
      await loadLineageResults();
    });
    $('#lineageExportBtn').addEventListener('click', exportLineageCsv);
    $('#lineageViewToggle').addEventListener('click', () => {
      state.view = state.view === 'graph' ? 'table' : 'graph';
      $('#lineageViewToggle').textContent = state.view === 'graph' ? 'Table' : 'Graph';
      if (state.rows.length) renderLineageResults(state.rows);
    });
    $('#lineageSavedView').addEventListener('change', applySelectedLineageView);
    $('#lineageSaveViewBtn').addEventListener('click', saveCurrentLineageView);
    $('#lineageDeleteViewBtn').addEventListener('click', deleteSelectedLineageView);

    if (state.query) loadLineageResults();
  }

  return {
    render,
    dispose() { if (dagInstance) { dagInstance.dispose(); dagInstance = null; } },
    state,
  };
}
