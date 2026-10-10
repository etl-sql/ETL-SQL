import { createLineageRenderers, renderLineageCatalogShell, esc, escAttr } from './lineage-catalog-view.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { readLineageHistory, readStewardship, readProtectedRows, readSuggestions, readLineageImpact, readLineageAudit, readMetrics, readSavedLineageViews } from './lineage-catalog-responses.js';
import type { LineageCatalogOptions, LineageCatalogState, CatalogMode, LineageHistoryQuery } from './lineage-catalog-types.js';
import type { DagNodeLike, DagEdgeLike } from '../designer/dag.js';

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

const memoryStorage = (() => {
  const m = new Map<string, string>();
  return { getItem: (k: string) => (m.has(k) ? m.get(k) : null), setItem: (k: string, v: string) => m.set(k, String(v)), removeItem: (k: string) => m.delete(k) };
})();

// What an incoming deep link is allowed to select. Deliberately the impact form's own options
// rather than everything the server will accept: a kind this view cannot render leaves the control
// showing nothing, so the vocabulary the user can see is the vocabulary a link may set.
const IMPACT_KINDS = new Set(['table', 'column', 'job', 'script', 'dataset', 'report', 'owner', 'steward']);
const IMPACT_DIRECTIONS = new Set(['upstream', 'downstream', 'both']);
const MODES = new Set<CatalogMode>(['history', 'stewardship', 'protected', 'audit', 'impact']);
const surfaces = new WeakMap<HTMLElement, { dispose(): void }>();
function identityKey(): string {
  try { const identity = getSessionIdentity(sessionStorage.getItem('etlsql_token')); return JSON.stringify([identity.claims.iss, identity.claims.tenant_id, identity.subject]); }
  catch { return ''; }
}

/**
 * Build a lineage catalog explorer bound to a host element.
 *
 * @param {Object}   opts
 * @param {Element}  opts.host                  Container the view renders into.
 * @param {Object}   opts.catalogApi            { lineage(kind, args): Promise<row[]>, stewardship(args): Promise<object>, impact(args): Promise<object> }.
 * @param {Object}   [opts.adminApi]            { auditLog(page, pageSize, action): Promise<object>, operationalMetrics(): Promise<object> }.
 * @param {Function} opts.renderDag             (container, {nodes, edges}) → { dispose }.
 * @param {Function} opts.renderLineageRow      (row, { timeAgo, formatBuiltAt }) → html string.
 * @param {Function} opts.lineageRowsToCsv      (rows) → csv string.
 * @param {Function} [opts.openReport]          (id) → void, invoked from a row's "open report" button.
 * @param {Function} [opts.timeAgo]             (iso) → relative string.
 * @param {Function} [opts.formatBuiltAt]       (iso) → absolute string.
 * @param {Storage}  [opts.storage]             localStorage-like store for saved views.
 * @param {Function} [opts.prepare]             Side-effects to run before each full render (nav state).
 * @param {Function} [opts.onModeChange]        Updates the host route when the user changes mode.
 * @param {boolean}  [opts.allowAudit]          Whether the administrator-only audit mode is visible.
 * @param {Function} [opts.promptFn]            Async validated dialog used when naming a saved view.
 * @param {string}   [opts.viewKey]             Storage key for saved views.
 * @returns {{ render: Function, setMode: (mode: string) => void,
 *   showImpact: (target?: {kind?: string, name?: string, column?: string, direction?: string,
 *   depth?: number}) => boolean, dispose: Function, state: Object }}
 */
// No `= {}` default: `host`, `catalogApi`, `renderDag`, `renderLineageRow` and `lineageRowsToCsv`
// are all required, and the default claimed a call with no arguments would work.
export function createLineageCatalog(opts: LineageCatalogOptions) {
  const {
    host,
    catalogApi,
    adminApi = {},
    renderDag,
    renderLineageRow,
    lineageRowsToCsv,
    openReport = () => {},
    timeAgo = v => v ?? '',
    formatBuiltAt = v => v ?? '',
    storage = (typeof localStorage !== 'undefined' ? localStorage : memoryStorage),
    prepare = () => {},
    onModeChange = () => {},
    allowAudit = true,
    promptFn = ((message, value) => window.ETLSQLFeedback?.prompt(message, { title: 'Save lineage view', label: 'View name', value, required: true, confirmLabel: 'Save view', auditAction: 'lineage.view.save' }) ?? null),
    viewKey = 'etlsql_lineage_views',
  } = opts;

  if (!host) throw new TypeError('Lineage catalog needs a host.');
  const root = host;
  surfaces.get(root)?.dispose();
  let disposed = false, generation = 0, promptPending = false;
  let request: AbortController | null = null, shellElement: Element | null = null;
  const identity = identityKey();
  const state: LineageCatalogState = {
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
    auditLimit: 50,
    stewardAudit: null,
    protectedData: null,
  };
  let dagInstance: { dispose(): void } | null = null;
  const { renderLineageEmpty, renderStewardshipMetric, renderStewardshipAsset, renderStewardshipFacetOptions, renderProtectedAuditRow, renderSuggestionAuditRow, renderAuditEvent, renderAuditImpactList, isStaleAuditRow, renderImpactSection } = createLineageRenderers(state, timeAgo, formatBuiltAt);

  function scopedViewKey() { return `${viewKey}:${identity}`; }
  function disposeDag() { dagInstance?.dispose(); dagInstance = null; }
  function invalidate() { generation++; request?.abort(); request = null; disposeDag(); }
  function clearResults() { state.rows = []; state.stewardship = null; state.protectedData = null; state.stewardAudit = null; state.impact = null; }
  function dispose() { if (disposed) return; disposed = true; invalidate(); clearResults(); if (surfaces.get(root)?.dispose === dispose) surfaces.delete(root); }
  function active(): boolean {
    if (disposed) return false;
    if (identityKey() !== identity || shellElement && !root.contains(shellElement)) { dispose(); return false; }
    return true;
  }
  surfaces.set(root, { dispose });
  function startRead(mode: CatalogMode, message: string) {
    if (!active() || state.mode !== mode || mode === 'audit' && !allowAudit) return null;
    invalidate(); clearResults();
    const version = generation, controller = new AbortController(); request = controller;
    $('#lineageExportBtn').toggleAttribute('disabled', true);
    $('#lineageResults').innerHTML = `<div class="loading-state" role="status"><span class="spinner"></span><span>${esc(message)}</span></div>`;
    return { signal: controller.signal, valid: () => active() && generation === version && state.mode === mode };
  }
  function failed(label: string, reason: unknown) {
    $('#lineageResults').innerHTML = `<div class="empty-state empty-state-panel empty-state-error" role="status"><h2>${esc(label)} unavailable</h2><p>${esc(asError(reason).message)}</p></div>`;
  }
  function unavailableSource(label: string) { return `<div class="empty-state empty-state-error" role="status"><h3>${esc(label)} unavailable</h3><p>Refresh to inspect this source.</p></div>`; }
  function sourceResult<T>(result: PromiseSettledResult<unknown>, read: (value: unknown) => T, label: string, errors: string[]): T | null {
    try { if (result.status === 'rejected') throw result.reason; return read(result.value); }
    catch (reason) { errors.push(`${label} unavailable: ${asError(reason).message}`); return null; }
  }
  function changeMode(mode: string) {
    if (!active() || !MODES.has(mode as CatalogMode) || mode === 'audit' && !allowAudit) return;
    if (state.mode === mode && shellElement && root.contains(shellElement)) return;
    invalidate(); clearResults(); state.mode = mode as CatalogMode; onModeChange(state.mode); render();
  }
  function populateFacets(prefix: string, stewards: readonly StewardshipFacetDto[], domains: readonly StewardshipFacetDto[]) {
    for (const [suffix, values, selected, label] of [
      ['Steward', stewards, state.stewardshipSteward, 'All stewards'],
      ['Domain', domains, state.stewardshipDomain, 'All domains'],
    ] as const) {
      const select = input(`#${prefix}${suffix}`);
      select.innerHTML = `<option value="">${label}</option>${renderStewardshipFacetOptions(values, selected)}${selected && !values.some(item => item.value === selected) ? `<option value="${escAttr(selected)}" selected>${esc(selected)}</option>` : ''}`;
    }
  }
  function protectedFacets(rows: readonly ProtectedLineageHistoryEntry[]) {
    const facet = (key: 'steward' | 'domain') => {
      const counts = new Map<string, number>();
      for (const row of rows) { const value = row[key]; if (value) counts.set(value, (counts.get(value) ?? 0) + 1); }
      return [...counts].map(([value, count]) => ({ value, count })).sort((a, b) => a.value.localeCompare(b.value));
    };
    populateFacets('protected', facet('steward'), facet('domain'));
  }

  function $(selector: string): HTMLElement { const element = root.querySelector<HTMLElement>(selector); if (!element) throw new Error(`Lineage element missing: ${selector}`); return element; }
  function input(selector: string): HTMLInputElement | HTMLSelectElement { const element = $(selector); if (!(element instanceof HTMLInputElement || element instanceof HTMLSelectElement)) throw new TypeError(`Lineage input missing: ${selector}`); return element; }

  // ── Saved views (localStorage) ────────────────────────────────────────────
  function loadLineageViews() {
    try {
      return readSavedLineageViews(JSON.parse(storage.getItem(scopedViewKey()) || '[]'));
    } catch {
      return [];
    }
  }
  function persistLineageViews(views: LineageCatalogState['savedViews']) {
    storage.setItem(scopedViewKey(), JSON.stringify(views));
  }
  async function saveCurrentLineageView() {
    if (!active() || state.mode !== 'history' || promptPending) return;
    const version = generation;
    const snapshot = { name: '', kind: input('#lineageKind').value, query: input('#lineageQuery').value.trim(),
      column: input('#lineageColumn').value.trim(), tagValue: input('#lineageTagValue').value.trim(),
      from: input('#lineageFrom').value, to: input('#lineageTo').value };
    promptPending = true; $('#lineageSaveViewBtn').toggleAttribute('disabled', true);
    let name: string | null;
    try { name = await promptFn('Name this saved lineage view.', snapshot.query || snapshot.kind); }
    catch (reason) { if (active() && generation === version) failed('Saved lineage view', reason); return; }
    finally { promptPending = false; if (active() && generation === version) $('#lineageSaveViewBtn').toggleAttribute('disabled', false); }
    if (!active() || version !== generation || !name?.trim()) return;
    state.savedViews = loadLineageViews().filter(v => v.name !== name.trim());
    state.selectedView = name.trim();
    state.savedViews.push({ ...snapshot, name: name.trim() });
    try { persistLineageViews(state.savedViews); }
    catch (reason) { failed('Saved lineage view', reason); return; }
    state.kind = snapshot.kind; state.query = snapshot.query; state.column = snapshot.column;
    state.tagValue = snapshot.tagValue; state.from = snapshot.from; state.to = snapshot.to;
    render();
  }
  function applySelectedLineageView() {
    if (!active()) return;
    const selectedName = input('#lineageSavedView').value;
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
    if (!active()) return;
    const selectedName = input('#lineageSavedView').value;
    if (!selectedName) return;
    state.savedViews = loadLineageViews().filter(v => v.name !== selectedName);
    state.selectedView = '';
    try { persistLineageViews(state.savedViews); }
    catch (reason) { failed('Saved lineage view', reason); return; }
    render();
  }

  // ── Results ───────────────────────────────────────────────────────────────
  function lineageDateStart(value: string) { return value ? `${value}T00:00:00Z` : null; }
  function lineageDateEnd(value: string) { return value ? `${value}T23:59:59.9999999Z` : null; }

  async function loadLineageResults() {
    const read = startRead('history', 'Loading lineage…'); if (!read) return;
    if (!state.query) { $('#lineageResults').innerHTML = renderLineageEmpty('Enter a table, source, file, tag, or job name.'); return; }
    const args: LineageHistoryQuery = { limit: state.limit, from: lineageDateStart(state.from), to: lineageDateEnd(state.to) };
    if (state.kind === 'tag') { args.key = state.query; if (state.tagValue) args.value = state.tagValue; }
    else if (state.kind === 'source-file') args.path = state.query;
    else { args.name = state.query; if (state.kind === 'table' && state.column) args.column = state.column; }
    try {
      const rows = readLineageHistory(await catalogApi.lineage(state.kind, args, read.signal));
      if (!read.valid()) return;
      state.rows = rows; renderLineageResults(rows);
      $('#lineageExportBtn').toggleAttribute('disabled', !rows.length);
    } catch (reason) { if (read.valid()) failed('Lineage query', reason); }
  }

  function renderLineageResults(rows: CatalogLineageHistoryDto[]) {
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

    $results.querySelectorAll<HTMLElement>('[data-open-report]').forEach(btn => {
      btn.addEventListener('click', e => {
        e.stopPropagation();
        openReport(Number(btn.dataset.openReport));
      });
    });
  }

  function renderLineageDag(rows: CatalogLineageHistoryDto[]) {
    const $results = $('#lineageResults');
    $results.innerHTML = '<div id="lineageDagContainer" style="height:480px;"></div>';
    const container = $('#lineageDagContainer');

    const targets = new Set(rows.map(r => r.targetTable).filter(Boolean));
    const sources = new Set(rows.flatMap(r => r.sourceTables || []).filter(Boolean));
    const allTables = new Set([...targets, ...sources]);

    const nodeMap = new Map<string, DagNodeLike>();
    [...allTables].forEach((name, i) => {
      const isTarget = targets.has(name);
      const isSource = sources.has(name);
      const type = isTarget && isSource ? 'dataset' : isTarget ? 'io' : 'table';
      nodeMap.set(name, { id: `t${i}`, label: name, type });
    });

    const nodes = [...nodeMap.values()];
    const seenEdges = new Set<string>();
    const edges: DagEdgeLike[] = [];
    for (const row of rows) {
      if (!row.targetTable || !nodeMap.get(row.targetTable)) continue;
      for (const src of (row.sourceTables || [])) {
        if (!src || !nodeMap.get(src)) continue;
        const key = JSON.stringify([src, row.targetTable]);
        if (!seenEdges.has(key)) {
          seenEdges.add(key);
          edges.push({ source: nodeMap.get(src)!.id, target: nodeMap.get(row.targetTable)!.id, label: row.operation || undefined });
        }
      }
    }

    dagInstance = renderDag(container, { nodes, edges });
  }

  async function loadStewardshipResults() {
    const read = startRead('stewardship', 'Loading stewardship inventory…'); if (!read) return;
    const args = { view: state.stewardshipView, q: state.stewardshipQuery, steward: state.stewardshipSteward,
      domain: state.stewardshipDomain, staleAfterDays: state.stewardshipStaleDays, limit: state.limit };
    try {
      const data = readStewardship(await catalogApi.stewardship(args, read.signal));
      if (!read.valid()) return; state.stewardship = data;
      populateFacets('stewardship', data.stewards, data.domains); renderStewardshipResults();
    } catch (reason) { if (read.valid()) failed('Stewardship query', reason); }
  }

  function renderStewardshipResults() {
    const data = state.stewardship; if (!data) return;
    const summary = data.summary;
    const items = data.items;
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }
    $results.innerHTML = `
      <div class="stewardship-overview" aria-label="Stewardship summary">
        ${renderStewardshipMetric('Assets', summary.totalAssets)}
        ${renderStewardshipMetric('Sensitive', summary.sensitiveAssets)}
        ${renderStewardshipMetric('Missing metadata', summary?.missingMetadataAssets)}
        ${renderStewardshipMetric('Stale', summary.staleAssets)}
        ${renderStewardshipMetric('Queue', summary.stewardQueueAssets)}
      </div>
      <p class="lineage-detail">Asset totals cover the visible catalog inventory. Queue follows steward/domain filters; the asset list also uses search and view filters.</p>
      ${items.length
        ? `<div class="stewardship-result-list">${items.map(renderStewardshipAsset).join('')}</div>`
        : renderLineageEmpty('No stewardship assets match the current filters.')}`;
  }

  async function loadStewardAuditResults() {
    const read = startRead('audit', 'Loading steward audit…'); if (!read) return;
    const args = { limit: state.limit }, auditLimit = Math.min(200, state.auditLimit);
    const sources = await Promise.allSettled([
      catalogApi.protectedData?.(args, read.signal) ?? Promise.reject(new Error('Protected inventory source is unavailable.')),
      catalogApi.protectedDataSuggestions?.(args, read.signal) ?? Promise.reject(new Error('Classifier suggestions source is unavailable.')),
      catalogApi.stewardship({ view: 'all', q: state.stewardshipQuery, steward: state.stewardshipSteward,
        domain: state.stewardshipDomain, staleAfterDays: state.stewardshipStaleDays, limit: state.limit }, read.signal),
      adminApi.auditLog?.(1, auditLimit, 'STEWARD_LINEAGE_IMPACT', '', '', '', read.signal) ?? Promise.reject(new Error('Audit event source is unavailable.')),
      adminApi.operationalMetrics?.(read.signal) ?? Promise.reject(new Error('Outbox health source is unavailable.')),
    ]);
    if (!read.valid()) return;
    const errors: string[] = [];
    const protectedRows = sourceResult(sources[0], readProtectedRows, 'Protected inventory', errors);
    const suggestions = sourceResult(sources[1], readSuggestions, 'Classifier suggestions', errors);
    const stewardship = sourceResult(sources[2], readStewardship, 'Stewardship', errors);
    state.stewardship = stewardship;
    state.stewardAudit = { protectedRows: protectedRows && filterProtectedAuditRows(protectedRows),
      suggestions: suggestions && filterSuggestionAuditRows(suggestions), stewardship,
      auditEvents: sourceResult(sources[3], value => readLineageAudit(value, auditLimit), 'Audit events', errors),
      operational: sourceResult(sources[4], readMetrics, 'Outbox health', errors), errors };
    render();
  }

  function renderStewardAuditResults() {
    const data = state.stewardAudit; if (!data) return;
    const protectedRows = Array.isArray(data.protectedRows) ? data.protectedRows : [];
    const suggestions = Array.isArray(data.suggestions) ? data.suggestions : [];
    const stewardship = data.stewardship;
    const items = stewardship?.items ?? [];
    const summary = stewardship?.summary;
    const auditEvents = Array.isArray(data.auditEvents) ? data.auditEvents : [];
    const operational = data.operational;
    const staleProtected = protectedRows.filter(row => isStaleAuditRow(row, state.stewardshipStaleDays));
    const impact = buildAuditImpact(protectedRows);
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }

    $results.innerHTML = `
      ${data.errors?.length ? `<div class="steward-audit-errors">${data.errors.map(esc).join(' · ')}</div>` : ''}
      <div class="steward-audit-grid" aria-label="Steward audit summary">
        ${renderStewardshipMetric('Protected observations', data.protectedRows?.length)}
        ${renderStewardshipMetric('Suggestions', data.suggestions?.length)}
        ${renderStewardshipMetric('Missing metadata', summary?.missingMetadataAssets)}
        ${renderStewardshipMetric('Stale protected', data.protectedRows == null ? null : staleProtected.length)}
        ${renderStewardshipMetric('Impact events (loaded)', data.auditEvents?.length)}
        ${renderStewardshipMetric('Outbox pending', operational?.auditOutboxPending)}
        ${renderStewardshipMetric('Outbox failed', operational?.auditOutboxFailed)}
      </div>
      <div class="steward-audit-actions" aria-label="Audit workflow shortcuts">
        <button class="btn btn-outline" type="button" data-audit-shortcut="sensitive">Protected queue</button>
        <button class="btn btn-outline" type="button" data-audit-shortcut="missing">Metadata gaps</button>
        <button class="btn btn-outline" type="button" data-audit-shortcut="stale">Stale assets</button>
        <button class="btn btn-outline" type="button" data-audit-shortcut="impact">Impact search</button>
      </div>
      <div class="steward-audit-layout">
        <section class="steward-audit-section">
          <h3>Protected inventory</h3>
          ${data.protectedRows == null ? unavailableSource('Protected inventory') : protectedRows.length ? `<div class="stewardship-result-list">${protectedRows.slice(0, 8).map(renderProtectedAuditRow).join('')}</div>` : renderLineageEmpty('No protected lineage is currently recorded.')}
        </section>
        <section class="steward-audit-section">
          <h3>Classifier suggestions</h3>
          ${data.suggestions == null ? unavailableSource('Classifier suggestions') : suggestions.length ? `<div class="stewardship-result-list">${suggestions.slice(0, 8).map(renderSuggestionAuditRow).join('')}</div>` : renderLineageEmpty('No protected-data classifier suggestions were found.')}
        </section>
        <section class="steward-audit-section">
          <h3>Steward queue</h3>
          ${stewardship == null ? unavailableSource('Stewardship') : items.length ? `<div class="stewardship-result-list">${items.slice(0, 8).map(renderStewardshipAsset).join('')}</div>` : renderLineageEmpty('No stewardship queue items match the current filters.')}
        </section>
        <section class="steward-audit-section">
          <h3>References recorded in protected lineage</h3>
          ${data.protectedRows == null ? unavailableSource('Protected references') : renderAuditImpactList(impact)}
        </section>
        <section class="steward-audit-section">
          <h3>Recent steward-impact events</h3>
          ${data.auditEvents == null ? unavailableSource('Audit events') : auditEvents.length ? `<div class="impact-list">${auditEvents.map(renderAuditEvent).join('')}</div>` : renderLineageEmpty('No steward-impact audit events were found.')}
        </section>
      </div>`;

    $results.querySelectorAll<HTMLElement>('[data-audit-shortcut]').forEach(btn => {
      btn.addEventListener('click', () => {
        const target = btn.dataset.auditShortcut;
        if (target === 'impact') {
          const first = protectedRows[0];
          state.impactKind = 'table';
          state.impactName = first?.targetTable || '';
          state.impactColumn = '';
          changeMode('impact');
          return;
        }
        state.stewardshipView = target ?? 'all';
        changeMode('stewardship');
      });
    });
  }

  async function loadProtectedDataResults() {
    const read = startRead('protected', 'Loading protected data & tags…'); if (!read) return;
    const args = { limit: state.limit };
    const sources = await Promise.allSettled([
      catalogApi.protectedData?.(args, read.signal) ?? Promise.reject(new Error('Protected inventory source is unavailable.')),
      catalogApi.protectedDataSuggestions?.(args, read.signal) ?? Promise.reject(new Error('Classifier suggestions source is unavailable.')),
    ]);
    if (!read.valid()) return;
    const errors: string[] = [];
    const protectedRows = sourceResult(sources[0], readProtectedRows, 'Protected inventory', errors);
    const suggestions = sourceResult(sources[1], readSuggestions, 'Classifier suggestions', errors);
    if (protectedRows) protectedFacets(protectedRows);
    state.protectedData = { protectedRows: protectedRows && filterProtectedAuditRows(protectedRows),
      suggestions: suggestions && filterSuggestionAuditRows(suggestions), errors };
    renderProtectedDataResults();
  }

  function renderProtectedDataResults() {
    const data = state.protectedData; if (!data) return;
    const protectedRows = Array.isArray(data.protectedRows) ? data.protectedRows : [];
    const suggestions = Array.isArray(data.suggestions) ? data.suggestions : [];
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }

    $results.innerHTML = `
      ${data.errors.length ? `<div class="steward-audit-errors" role="status">${data.errors.map(esc).join(' · ')}</div>` : ''}
      <div class="steward-audit-grid" aria-label="Protected data summary">
        ${renderStewardshipMetric('Protected observations', data.protectedRows?.length)}
        ${renderStewardshipMetric('Tag suggestions', data.suggestions?.length)}
        ${renderStewardshipMetric('Restricted', data.protectedRows == null ? null : protectedRows.filter(r => r.classification?.toLowerCase() === 'restricted').length)}
        ${renderStewardshipMetric('Sensitive', data.protectedRows?.length)}
      </div>
      <p class="lineage-detail">Counts cover the returned protected observations and suggestions. Multiple runs can record the same asset.</p>
      <p class="lineage-detail">Steward/domain filters apply to inventory. Search also filters suggestions.</p>
      <div class="steward-audit-layout">
        <section class="steward-audit-section">
          <h3>Protected & Classified Inventory</h3>
          ${data.protectedRows == null ? unavailableSource('Protected inventory') : protectedRows.length ? `<div class="stewardship-result-list">${protectedRows.slice(0, 15).map(renderProtectedAuditRow).join('')}</div>` : renderLineageEmpty('No protected data or sensitive columns recorded.')}
        </section>
        <section class="steward-audit-section">
          <h3>Classifier Tag Suggestions</h3>
          ${data.suggestions == null ? unavailableSource('Classifier suggestions') : suggestions.length ? `<div class="stewardship-result-list">${suggestions.slice(0, 15).map(renderSuggestionAuditRow).join('')}</div>` : renderLineageEmpty('No classifier tag suggestions found.')}
        </section>
      </div>`;
  }

  function filterProtectedAuditRows(rows: ProtectedLineageHistoryEntry[]) {
    const query = (state.stewardshipQuery || '').toLowerCase();
    const steward = state.stewardshipSteward || '';
    const domain = state.stewardshipDomain || '';
    return rows.filter(row => {
      if (steward && !equalsIgnoreCase(row.steward, steward))
        return false;
      if (domain && !equalsIgnoreCase(row.domain, domain))
        return false;
      if (!query) return true;
      const haystack = [
        row.targetTable,
        row.targetColumn,
        row.owner,
        row.steward,
        row.domain,
        row.classification,
        row.quality,
        row.protectionReason,
        ...(Array.isArray(row.sourceTables) ? row.sourceTables : []),
        ...Object.entries(row.tags || {}).map(([k, v]) => `${k}:${v}`),
      ].join(' ').toLowerCase();
      return haystack.includes(query);
    });
  }

  function filterSuggestionAuditRows(rows: ProtectedDataSuggestionEntry[]) {
    const query = (state.stewardshipQuery || '').toLowerCase();
    return rows.filter(row => {
      if (!query) return true;
      const haystack = [
        row.targetTable,
        row.targetColumn,
        row.suggestedTag,
        row.suggestedValue,
        row.evidenceKind,
        row.evidence,
        row.reason,
        ...(Array.isArray(row.sourceTables) ? row.sourceTables : []),
        ...(Array.isArray(row.sourceColumns) ? row.sourceColumns : []),
        ...Object.entries(row.existingTags || {}).map(([k, v]) => `${k}:${v}`),
      ].join(' ').toLowerCase();
      return haystack.includes(query);
    });
  }

  function equalsIgnoreCase(left: string | null | undefined, right: string | null | undefined) {
    return String(left || '').toLowerCase() === String(right || '').toLowerCase();
  }

  function buildAuditImpact(rows: ProtectedLineageHistoryEntry[]) {
    const reports = new Set<string>();
    const datasets = new Set<string>();
    const subscriptions = new Set<string>();
    for (const row of rows) {
      if ((row.targetTable || '').toLowerCase().startsWith('report:')) reports.add(row.targetTable.slice(7));
      if ((row.targetTable || '').toLowerCase().startsWith('dataset:')) datasets.add(row.targetTable.slice(8));
      if (row.jobName?.toLowerCase().startsWith('subscription')) subscriptions.add(row.jobName);
    }
    return {
      reports: [...reports].sort((a, b) => a.localeCompare(b)),
      datasets: [...datasets].sort((a, b) => a.localeCompare(b)),
      subscriptions: [...subscriptions].sort((a, b) => a.localeCompare(b)),
    };
  }

  async function loadImpactResults() {
    const read = startRead('impact', 'Loading impact analysis…'); if (!read) return;
    if (!state.impactName) { $('#lineageResults').innerHTML = renderLineageEmpty('Enter a table, column, job, script, dataset, report, owner, or steward.'); return; }
    const args = { kind: state.impactKind, name: state.impactName, column: state.impactColumn,
      direction: state.impactDirection, depth: state.impactDepth, limit: state.limit };
    try {
      const data = readLineageImpact(await catalogApi.impact(args, read.signal), args);
      if (!read.valid()) return; state.impact = data; renderImpactResults();
    } catch (reason) { if (read.valid()) failed('Impact query', reason); }
  }

  function renderImpactResults() {
    const data = state.impact; if (!data) return;
    const summary = data.summary;
    const $results = $('#lineageResults');
    if (dagInstance) { dagInstance.dispose(); dagInstance = null; }
    const sections: [string, readonly LineageImpactItemDto[] | undefined][] = [
      ['Tables', data.tables],
      ['Columns', data.columns],
      ['Reports', data.reports],
      ['Datasets', data.datasets],
      ['Subscriptions', data.subscriptions],
      ['Jobs', data.jobs],
      ['Alerts', data.alerts],
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

  function exportLineageCsv() {
    if (!active() || state.mode !== 'history' || !state.rows.length) return;
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
    if (!active()) return;
    if (state.mode === 'audit' && !allowAudit) state.mode = 'history';
    invalidate();
    prepare(state.mode);
    state.savedViews = loadLineageViews();
    root.innerHTML = renderLineageCatalogShell(state, allowAudit, renderStewardshipFacetOptions);
    shellElement = root.firstElementChild;

    root.querySelectorAll<HTMLElement>('[data-lineage-mode]').forEach(btn => {
      const mode = btn.dataset.lineageMode ?? 'history';
      btn.id = `lineage-mode-${mode}`;
      btn.setAttribute('aria-controls', 'lineageResults');
      btn.tabIndex = state.mode === mode ? 0 : -1;
      btn.addEventListener('click', () => {
        changeMode(btn.dataset.lineageMode ?? 'history');
      });
      btn.addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const modes = [...root.querySelectorAll<HTMLElement>('[data-lineage-mode]')].map(tab => tab.dataset.lineageMode ?? 'history');
        const index = modes.indexOf(mode);
        const next = event.key === 'Home' ? modes[0] : event.key === 'End' ? modes[modes.length - 1]
          : modes[(index + (event.key === 'ArrowRight' ? 1 : modes.length - 1)) % modes.length];
        changeMode(next); root.querySelector<HTMLElement>(`#lineage-mode-${next}`)?.focus();
      });
    });
    $('#lineageResults').setAttribute('role', 'tabpanel');
    $('#lineageResults').setAttribute('aria-labelledby', `lineage-mode-${state.mode}`);
    root.querySelectorAll<HTMLInputElement | HTMLSelectElement>('form input, form select').forEach(element => {
      if (element.id === 'lineageSavedView' || element.id === 'lineageKind' || element.id === 'impactKind') return;
      element.addEventListener('input', () => {
        if (!active()) return;
        invalidate(); clearResults(); $('#lineageExportBtn').toggleAttribute('disabled', true);
        $('#lineageResults').innerHTML = '<div class="empty-state" role="status">Run the query to inspect these filters.</div>';
      });
    });

    const $form = $('#lineageSearchForm');
    $('#lineageKind')?.addEventListener('change', () => {
      state.kind = input('#lineageKind').value;
      state.column = '';
      state.tagValue = '';
      state.rows = [];
      render();
    });
    $form?.addEventListener('submit', async e => {
      e.preventDefault();
      state.kind = input('#lineageKind').value;
      state.query = input('#lineageQuery').value.trim();
      state.column = input('#lineageColumn').value.trim();
      state.tagValue = input('#lineageTagValue').value.trim();
      state.from = input('#lineageFrom').value;
      state.to = input('#lineageTo').value;
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
      state.stewardshipView = input('#stewardshipView').value;
      state.stewardshipQuery = input('#stewardshipQuery').value.trim();
      state.stewardshipSteward = input('#stewardshipSteward').value;
      state.stewardshipDomain = input('#stewardshipDomain').value;
      state.stewardshipStaleDays = Math.max(1, Math.min(3660, Number(input('#stewardshipStaleDays').value || 30)));
      await loadStewardshipResults();
    });

    $('#impactKind')?.addEventListener('change', () => {
      state.impactKind = input('#impactKind').value;
      state.impactColumn = '';
      state.impact = null;
      render();
    });
    $('#impactSearchForm')?.addEventListener('submit', async e => {
      e.preventDefault();
      state.impactKind = input('#impactKind').value;
      state.impactName = input('#impactName').value.trim();
      state.impactColumn = input('#impactColumn').value.trim();
      state.impactDirection = input('#impactDirection').value;
      state.impactDepth = Math.max(1, Math.min(8, Number(input('#impactDepth').value || 4)));
      await loadImpactResults();
    });

    $('#auditSearchForm')?.addEventListener('submit', async e => {
      e.preventDefault();
      state.stewardshipQuery = input('#auditQuery').value.trim();
      state.stewardshipSteward = input('#auditSteward').value;
      state.stewardshipDomain = input('#auditDomain').value;
      state.stewardshipStaleDays = Math.max(1, Math.min(3660, Number(input('#auditStaleDays').value || 30)));
      state.auditLimit = Math.max(1, Math.min(500, Number(input('#auditLimit').value || 50)));
      await loadStewardAuditResults();
    });

    $('#protectedSearchForm')?.addEventListener('submit', async e => {
      e.preventDefault();
      state.stewardshipQuery = input('#protectedQuery').value?.trim() || '';
      state.stewardshipSteward = input('#protectedSteward').value || '';
      state.stewardshipDomain = input('#protectedDomain').value || '';
      await loadProtectedDataResults();
    });

    if (state.mode === 'stewardship') {
      if (state.stewardship) renderStewardshipResults();
      else loadStewardshipResults();
    } else if (state.mode === 'protected') {
      if (state.protectedData) renderProtectedDataResults();
      else loadProtectedDataResults();
    } else if (state.mode === 'audit') {
      if (state.stewardAudit) renderStewardAuditResults();
      else loadStewardAuditResults();
    } else if (state.mode === 'impact') {
      if (state.impact) renderImpactResults();
      else if (state.impactName) loadImpactResults();
    } else if (state.query) {
      loadLineageResults();
    }
  }

  return {
    render,
    setMode(m: string) { changeMode(m); },
    /**
     * Opens impact mode already pointed at a target, so another surface can hand off to it — the
     * triage board asking "this job failed, what is downstream of it" is the motivating case.
     * Without this the impact target lived only in local state and there was nothing to link to.
     *
     * Unknown or missing fields fall back to the current state rather than throwing, because the
     * caller is a URL and a URL is user-editable.
     *
     * @param {Object} [target]
     * @param {string} [target.kind]
     * @param {string} [target.name]
     * @param {string} [target.column]
     * @param {string} [target.direction]
     * @param {number} [target.depth]
     */
    showImpact({ kind, name, column, direction, depth }: { kind?: string; name?: string; column?: string; direction?: string; depth?: number } = {}) {
      if (!active() || !name?.trim()) return false;
      invalidate(); clearResults();
      state.mode = 'impact';
      if (kind && IMPACT_KINDS.has(kind)) state.impactKind = kind;
      state.impactName = name.trim();
      state.impactColumn = column ? String(column) : '';
      if (direction && IMPACT_DIRECTIONS.has(direction)) state.impactDirection = direction;
      const parsedDepth = Number(depth);
      if (Number.isFinite(parsedDepth)) state.impactDepth = Math.min(8, Math.max(1, Math.trunc(parsedDepth)));
      onModeChange(state.mode);
      render();
      return true;
    },
    dispose,
    state,
  };
}
