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
 * @param {Object}   opts.catalogApi            { lineage(kind, args): Promise<row[]>, stewardship(args): Promise<object>, impact(args): Promise<object> }.
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

  const state = {
    mode: 'history',
    kind: 'table',
    query: '',
    column: '',
    tagValue: '',
    from: '',
    to: '',
    limit: 100,
    rows: [],
    savedViews: [],
    selectedView: '',
    view: 'table',
    stewardshipView: 'missing',
    stewardshipQuery: '',
    stewardshipSteward: '',
    stewardshipDomain: '',
    stewardshipStaleDays: 30,
    stewardship: null,
    impactKind: 'table',
    impactName: '',
    impactColumn: '',
    impactDirection: 'downstream',
    impactDepth: 4,
    impact: null,
  };
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

  async function loadStewardshipResults() {
    const $results = $('#lineageResults');
    $results.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading stewardship inventory…</span></div>`;
    try {
      state.stewardship = await catalogApi.stewardship({
        view: state.stewardshipView,
        q: state.stewardshipQuery,
        steward: state.stewardshipSteward,
        domain: state.stewardshipDomain,
        staleAfterDays: state.stewardshipStaleDays,
        limit: state.limit,
      });
      renderStewardshipResults();
    } catch (err) {
      state.stewardship = null;
      $results.innerHTML = `<div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Stewardship query failed</h2>
        <p>${esc(err.message)}</p>
      </div>`;
    }
  }

  function renderStewardshipResults() {
    const data = state.stewardship || {};
    const summary = data.summary || {};
    const items = Array.isArray(data.items) ? data.items : [];
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }
    $results.innerHTML = `
      <div class="stewardship-overview" aria-label="Stewardship summary">
        ${renderStewardshipMetric('Assets', summary.totalAssets)}
        ${renderStewardshipMetric('Sensitive', summary.sensitiveAssets)}
        ${renderStewardshipMetric('Missing metadata', summary.missingMetadataAssets)}
        ${renderStewardshipMetric('Stale', summary.staleAssets)}
        ${renderStewardshipMetric('Queue', summary.stewardQueueAssets)}
      </div>
      ${items.length
        ? `<div class="stewardship-result-list">${items.map(renderStewardshipAsset).join('')}</div>`
        : renderLineageEmpty('No stewardship assets match the current filters.')}`;
  }

  function renderStewardshipMetric(label, value) {
    return `<div class="stewardship-metric">
      <span>${esc(label)}</span>
      <strong>${Number(value || 0).toLocaleString()}</strong>
    </div>`;
  }

  function renderStewardshipAsset(item) {
    const tags = item.tags || {};
    const missing = Array.isArray(item.missingTags) ? item.missingTags : [];
    const sourceList = Array.isArray(item.sourceTables) && item.sourceTables.length
      ? item.sourceTables.join(', ')
      : 'No source recorded';
    const badges = [
      item.isRestricted ? '<span class="stewardship-badge stewardship-badge-danger">restricted</span>' : '',
      item.isSensitive ? '<span class="stewardship-badge stewardship-badge-warn">sensitive</span>' : '',
      item.isStale ? '<span class="stewardship-badge">stale</span>' : '',
      missing.length ? `<span class="stewardship-badge">${missing.length} missing</span>` : '',
    ].filter(Boolean).join('');
    const tagChips = Object.entries(tags).slice(0, 8)
      .map(([key, value]) => `<span class="stewardship-chip"><b>@${esc(key)}</b>${esc(value)}</span>`)
      .join('');
    const missingText = missing.length
      ? `<div class="stewardship-gap">Missing: ${missing.map(t => `@${esc(t)}`).join(', ')}</div>`
      : '';
    return `<article class="stewardship-row">
      <div class="stewardship-row-main">
        <div class="stewardship-row-title">
          <code>${esc(item.targetTable)}${item.targetColumn ? `.${esc(item.targetColumn)}` : ''}</code>
          ${badges}
        </div>
        <div class="lineage-detail">Sources: ${esc(sourceList)}</div>
        <div class="lineage-detail">Steward: ${esc(item.steward || 'Unassigned')} &middot; Owner: ${esc(item.owner || 'Unassigned')} &middot; Domain: ${esc(item.domain || 'Unassigned')}</div>
        ${missingText}
        ${tagChips ? `<div class="stewardship-tags">${tagChips}</div>` : ''}
      </div>
      <div class="stewardship-row-meta">
        <strong>${esc(timeAgo(item.runAt))}</strong>
        <span>${esc(formatBuiltAt(item.runAt))}</span>
        <span>${esc(item.staleReason || 'Fresh')}</span>
      </div>
    </article>`;
  }

  function renderStewardshipFacetOptions(values, selected) {
    return (Array.isArray(values) ? values : [])
      .map(v => `<option value="${escAttr(v.value)}"${v.value === selected ? ' selected' : ''}>${esc(v.value)} (${Number(v.count || 0).toLocaleString()})</option>`)
      .join('');
  }

  async function loadImpactResults() {
    const $results = $('#lineageResults');
    if (!state.impactName) {
      state.impact = null;
      $results.innerHTML = renderLineageEmpty('Enter a table, column, job, script, dataset, report, owner, or steward.');
      return;
    }
    $results.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading impact analysis…</span></div>`;
    try {
      state.impact = await catalogApi.impact({
        kind: state.impactKind,
        name: state.impactName,
        column: state.impactColumn,
        direction: state.impactDirection,
        depth: state.impactDepth,
        limit: state.limit,
      });
      renderImpactResults();
    } catch (err) {
      state.impact = null;
      $results.innerHTML = `<div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Impact query failed</h2>
        <p>${esc(err.message)}</p>
      </div>`;
    }
  }

  function renderImpactResults() {
    const data = state.impact || {};
    const summary = data.summary || {};
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }
    const sections = [
      ['Tables', data.tables],
      ['Columns', data.columns],
      ['Reports', data.reports],
      ['Datasets', data.datasets],
      ['Subscriptions', data.subscriptions],
      ['Jobs', data.jobs],
      ['Owners and stewards', data.stewards],
    ];
    const total = sections.reduce((n, [, items]) => n + (Array.isArray(items) ? items.length : 0), 0);
    $results.innerHTML = `
      <div class="stewardship-overview impact-overview" aria-label="Impact summary">
        ${renderStewardshipMetric('Tables', summary.tables)}
        ${renderStewardshipMetric('Columns', summary.columns)}
        ${renderStewardshipMetric('Reports', summary.reports)}
        ${renderStewardshipMetric('Datasets', summary.datasets)}
        ${renderStewardshipMetric('Jobs', summary.jobs)}
      </div>
      ${total
        ? `<div class="impact-sections">${sections.map(([title, items]) => renderImpactSection(title, items)).join('')}</div>`
        : renderLineageEmpty('No upstream or downstream impact was found for this target.')}`;
  }

  function renderImpactSection(title, items) {
    items = Array.isArray(items) ? items : [];
    if (!items.length) return '';
    return `<section class="impact-section">
      <h3>${esc(title)}</h3>
      <div class="impact-list">${items.map(renderImpactItem).join('')}</div>
    </section>`;
  }

  function renderImpactItem(item) {
    const detail = item.detail ? `<span>${esc(item.detail)}</span>` : '';
    const seen = item.lastSeen ? `<span>${esc(timeAgo(item.lastSeen))}</span>` : '';
    const count = item.count ? `<span>${Number(item.count).toLocaleString()} observations</span>` : '';
    return `<article class="impact-row">
      <div>
        <strong>${esc(item.name)}</strong>
        <small>${esc(item.type)}</small>
      </div>
      <div>${detail}${seen}${count}</div>
    </article>`;
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
    const stewardship = state.stewardship || {};
    const savedOptions = state.savedViews
      .map(v => `<option value="${escAttr(v.name)}"${v.name === state.selectedView ? ' selected' : ''}>${esc(v.name)}</option>`)
      .join('');
    host.innerHTML = `
      <div class="library-toolbar lineage-toolbar">
        <div class="library-title">
          <span class="library-kicker">Catalog</span>
          <h2>${state.mode === 'stewardship' ? 'Stewardship' : 'Lineage'}</h2>
          <span class="folder-count">${state.mode === 'stewardship' ? 'Metadata gaps, sensitive assets, stale lineage, and steward queues' : 'Cross-run history with report context'}</span>
        </div>
        <div class="lineage-mode-toggle" role="tablist" aria-label="Catalog mode">
          <button type="button" class="lineage-mode-btn ${state.mode === 'history' ? 'active' : ''}" data-lineage-mode="history" aria-selected="${state.mode === 'history'}">History</button>
          <button type="button" class="lineage-mode-btn ${state.mode === 'stewardship' ? 'active' : ''}" data-lineage-mode="stewardship" aria-selected="${state.mode === 'stewardship'}">Stewardship</button>
          <button type="button" class="lineage-mode-btn ${state.mode === 'impact' ? 'active' : ''}" data-lineage-mode="impact" aria-selected="${state.mode === 'impact'}">Impact</button>
        </div>
        <form id="lineageSearchForm" class="lineage-query" autocomplete="off" ${state.mode === 'history' ? '' : 'hidden'}>
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
        <form id="stewardshipSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'stewardship' ? '' : 'hidden'}>
          <select id="stewardshipView" class="library-sort" aria-label="Stewardship view">
            <option value="all"${state.stewardshipView === 'all' ? ' selected' : ''}>All assets</option>
            <option value="sensitive"${state.stewardshipView === 'sensitive' ? ' selected' : ''}>Sensitive</option>
            <option value="missing"${state.stewardshipView === 'missing' ? ' selected' : ''}>Missing metadata</option>
            <option value="stale"${state.stewardshipView === 'stale' ? ' selected' : ''}>Stale lineage</option>
            <option value="queue"${state.stewardshipView === 'queue' ? ' selected' : ''}>Steward queue</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="stewardshipQuery" type="search" placeholder="Search assets or tags" value="${escAttr(state.stewardshipQuery)}">
          </label>
          <select id="stewardshipSteward" class="library-sort lineage-saved" aria-label="Filter by steward">
            <option value="">All stewards</option>
            ${renderStewardshipFacetOptions(stewardship.stewards, state.stewardshipSteward)}
          </select>
          <select id="stewardshipDomain" class="library-sort lineage-saved" aria-label="Filter by domain">
            <option value="">All domains</option>
            ${renderStewardshipFacetOptions(stewardship.domains, state.stewardshipDomain)}
          </select>
          <input id="stewardshipStaleDays" class="lineage-input stewardship-days" type="number" min="1" max="3660" value="${escAttr(state.stewardshipStaleDays)}" aria-label="Stale after days">
          <button class="btn btn-primary" type="submit">Search</button>
        </form>
        <form id="impactSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'impact' ? '' : 'hidden'}>
          <select id="impactKind" class="library-sort" aria-label="Impact target type">
            <option value="table"${state.impactKind === 'table' ? ' selected' : ''}>Table</option>
            <option value="column"${state.impactKind === 'column' ? ' selected' : ''}>Column</option>
            <option value="job"${state.impactKind === 'job' ? ' selected' : ''}>Job</option>
            <option value="script"${state.impactKind === 'script' ? ' selected' : ''}>Script</option>
            <option value="dataset"${state.impactKind === 'dataset' ? ' selected' : ''}>Dataset</option>
            <option value="report"${state.impactKind === 'report' ? ' selected' : ''}>Report</option>
            <option value="owner"${state.impactKind === 'owner' ? ' selected' : ''}>Owner</option>
            <option value="steward"${state.impactKind === 'steward' ? ' selected' : ''}>Steward</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="impactName" type="search" placeholder="Impact target" value="${escAttr(state.impactName)}">
          </label>
          <input id="impactColumn" class="lineage-input lineage-column" type="search" placeholder="Column" value="${escAttr(state.impactColumn)}" ${state.impactKind === 'table' || state.impactKind === 'column' ? '' : 'hidden'}>
          <select id="impactDirection" class="library-sort" aria-label="Impact direction">
            <option value="downstream"${state.impactDirection === 'downstream' ? ' selected' : ''}>Downstream</option>
            <option value="upstream"${state.impactDirection === 'upstream' ? ' selected' : ''}>Upstream</option>
            <option value="both"${state.impactDirection === 'both' ? ' selected' : ''}>Both</option>
          </select>
          <input id="impactDepth" class="lineage-input stewardship-days" type="number" min="1" max="8" value="${escAttr(state.impactDepth)}" aria-label="Traversal depth">
          <button class="btn btn-primary" type="submit">Analyze</button>
        </form>
      </div>
      <div id="lineageResults">${state.mode === 'stewardship' && state.stewardship ? '' : renderLineageEmpty()}</div>`;

    host.querySelectorAll('[data-lineage-mode]').forEach(btn => {
      btn.addEventListener('click', () => {
        state.mode = btn.dataset.lineageMode;
        render();
      });
    });

    const $form = $('#lineageSearchForm');
    $('#lineageKind')?.addEventListener('change', e => {
      state.kind = e.target.value;
      state.column = '';
      state.tagValue = '';
      state.rows = [];
      render();
    });
    $form?.addEventListener('submit', async e => {
      e.preventDefault();
      state.kind = $('#lineageKind').value;
      state.query = $('#lineageQuery').value.trim();
      state.column = $('#lineageColumn').value.trim();
      state.tagValue = $('#lineageTagValue').value.trim();
      state.from = $('#lineageFrom').value;
      state.to = $('#lineageTo').value;
      await loadLineageResults();
    });
    $('#lineageExportBtn')?.addEventListener('click', exportLineageCsv);
    $('#lineageViewToggle')?.addEventListener('click', () => {
      state.view = state.view === 'graph' ? 'table' : 'graph';
      $('#lineageViewToggle').textContent = state.view === 'graph' ? 'Table' : 'Graph';
      if (state.rows.length) renderLineageResults(state.rows);
    });
    $('#lineageSavedView')?.addEventListener('change', applySelectedLineageView);
    $('#lineageSaveViewBtn')?.addEventListener('click', saveCurrentLineageView);
    $('#lineageDeleteViewBtn')?.addEventListener('click', deleteSelectedLineageView);

    $('#stewardshipSearchForm')?.addEventListener('submit', async e => {
      e.preventDefault();
      state.stewardshipView = $('#stewardshipView').value;
      state.stewardshipQuery = $('#stewardshipQuery').value.trim();
      state.stewardshipSteward = $('#stewardshipSteward').value;
      state.stewardshipDomain = $('#stewardshipDomain').value;
      state.stewardshipStaleDays = Math.max(1, Math.min(3660, Number($('#stewardshipStaleDays').value || 30)));
      await loadStewardshipResults();
    });

    $('#impactKind')?.addEventListener('change', e => {
      state.impactKind = e.target.value;
      state.impactColumn = '';
      state.impact = null;
      render();
    });
    $('#impactSearchForm')?.addEventListener('submit', async e => {
      e.preventDefault();
      state.impactKind = $('#impactKind').value;
      state.impactName = $('#impactName').value.trim();
      state.impactColumn = $('#impactColumn').value.trim();
      state.impactDirection = $('#impactDirection').value;
      state.impactDepth = Math.max(1, Math.min(8, Number($('#impactDepth').value || 4)));
      await loadImpactResults();
    });

    if (state.mode === 'stewardship') {
      if (state.stewardship) renderStewardshipResults();
      else loadStewardshipResults();
    } else if (state.mode === 'impact') {
      if (state.impact) renderImpactResults();
      else if (state.impactName) loadImpactResults();
    } else if (state.query) {
      loadLineageResults();
    }
  }

  return {
    render,
    dispose() { if (dagInstance) { dagInstance.dispose(); dagInstance = null; } },
    state,
  };
}
