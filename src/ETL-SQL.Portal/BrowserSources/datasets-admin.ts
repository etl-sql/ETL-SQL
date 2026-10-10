import { aclTableHtml } from './dataset-acl-ui.js';
import { PANEL_HTML, MODALS_HTML, esc, escAttr, formatOptionalDate } from './datasets-view.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { installDialogAccessibility } from './dialog-a11y.js';
import { readDatasetCatalog, readDataset, readDatasetRows, readDatasetStats, readDatasetValues,
  readDatasetAcl, readDatasetGroups, readDatasetLineage, readDatasetRefresh, readDatasetVersion } from './dataset-responses.js';
import type { DagHandle, DagGraphInput } from '../designer/dag.js';

export interface DatasetViewerQuery {
  page?: number; pageSize?: number; sort?: string | null; dir?: string | null;
  search?: string | null; filters?: readonly DatasetColumnFilterDto[] | null;
}
export interface DatasetAdminApi {
  list(signal?: AbortSignal): Promise<unknown>;
  update(id: number, body: UpdateDatasetRequest, version: number, signal?: AbortSignal): Promise<unknown>;
  delete(id: number, version: number, signal?: AbortSignal): Promise<unknown>;
  refresh(id: number, signal?: AbortSignal): Promise<unknown>;
  listAcl(id: number, signal?: AbortSignal): Promise<unknown>;
  grantAcl(id: number, groupId: number, permission: string, version: number, signal?: AbortSignal): Promise<unknown>;
  revokeAcl(id: number, groupId: number, version: number, signal?: AbortSignal): Promise<unknown>;
  revokeUserAcl(id: number, userId: number, version: number, signal?: AbortSignal): Promise<unknown>;
  data(id: number, query: DatasetViewerQuery, signal?: AbortSignal): Promise<unknown>;
  stats(id: number, filters: readonly DatasetColumnFilterDto[] | null, signal?: AbortSignal): Promise<unknown>;
  columnValues(id: number, column: string, query: { search: string; limit: number }, signal?: AbortSignal): Promise<unknown>;
  exportCsv(id: number, filename: string, query: DatasetViewerQuery, signal?: AbortSignal): Promise<unknown>;
  exportXlsx(id: number, filename: string, query: DatasetViewerQuery, signal?: AbortSignal): Promise<unknown>;
}
interface DatasetAdminOptions {
  host: HTMLElement | null; datasetsApi: DatasetAdminApi;
  adminApi: { listGroups(signal?: AbortSignal): Promise<unknown> };
  catalogApi: { lineage(kind: string, query: { name: string; limit: number }, signal?: AbortSignal): Promise<unknown> };
  renderDag: (container: HTMLElement, graph: DagGraphInput) => DagHandle;
  confirmFn?: (message: string) => boolean | Promise<boolean>;
  alertFn?: (message: string) => unknown; modalRoot?: HTMLElement;
}
interface ViewerState {
  id: number | null; name: string | null; columns: DatasetColumnDto[]; stats: DatasetColumnStatsDto[] | null;
  filters: DatasetColumnFilterDto[]; sort: string | null; dir: 'asc' | 'desc'; search: string; page: number; pageSize: number;
  pickerCol: string | null; pickerChecked: Set<string>;
}
const surfaces = new WeakMap<HTMLElement, { dispose(): void }>();
const uncertainByIdentity = new Map<string, Set<number>>();
const inFlightByIdentity = new Map<string, Set<number>>();
function identityKey(): string {
  try { const identity = getSessionIdentity(sessionStorage.getItem('etlsql_token')); return JSON.stringify([identity.subject, identity.claims.iss, identity.claims.tenant_id]); }
  catch { return ''; }
}
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
// No `= {}` default: `host`, `datasetsApi`, `adminApi`, `catalogApi` and `renderDag` are all
// required — the first thing this does is write into `host` — and the default said otherwise. Both
// call sites pass a full object.
export function createDatasetsAdmin(opts: DatasetAdminOptions) {
  const {
    host,
    datasetsApi,
    adminApi,
    catalogApi,
    renderDag,
    confirmFn = (message => window.ETLSQLFeedback?.confirm(message, { title: 'Delete dataset', impact: 'The registry entry and cached data will be removed.', confirmLabel: 'Delete dataset', danger: true, auditAction: 'admin.dataset.delete' }) ?? false),
    alertFn = (message => window.ETLSQLFeedback?.notify(message, { title: 'Dataset', tone: 'info' })),
    modalRoot = document.body,
  } = opts;

  if (!host) throw new TypeError('Dataset administration needs a host.');
  const root = host;
  surfaces.get(root)?.dispose();
  let disposed = false, catalogReady = false;
  const identity = identityKey(), lifetime = new AbortController();
  let loadVersion = 0, rowsVersion = 0, statsVersion = 0, pickerVersion = 0, lineageVersion = 0, aclVersion = 0, groupsVersion = 0;
  let listRequest: AbortController | null = null, rowsRequest: AbortController | null = null, statsRequest: AbortController | null = null;
  let pickerRequest: AbortController | null = null, lineageRequest: AbortController | null = null, aclRequest: AbortController | null = null;
  let editedDataset: DatasetDto | null = null;
  let pickerAnchor: HTMLElement | null = null;
  const writes = inFlightByIdentity.get(identity) ?? new Set<number>(); inFlightByIdentity.set(identity, writes);
  const uncertain = uncertainByIdentity.get(identity) ?? new Set<number>(); uncertainByIdentity.set(identity, uncertain);
  const alertErr = (err: unknown) => { if (active()) alertFn(asError(err).message || 'An error occurred.'); };

  host.innerHTML = PANEL_HTML;
  const modalFrag = document.createElement('div');
  modalFrag.innerHTML = MODALS_HTML;
  const modalEls = [...modalFrag.querySelectorAll<HTMLElement>(':scope > *')];
  modalEls.forEach(el => modalRoot.appendChild(el));
  const releaseDialogs = installDialogAccessibility(modalRoot);
  function get<T extends HTMLElement = HTMLElement>(id: string): T {
    const selector = `#${CSS.escape(id)}`;
    const element = root.querySelector<T>(selector) ?? modalEls.flatMap(el => el.id === id ? [el] : [...el.querySelectorAll<T>(selector)])[0];
    if (!element) throw new Error(`Dataset element is missing: ${id}`);
    return element as T;
  }
  function active(): boolean { if (disposed) return false; if (identityKey() !== identity) { dispose(); return false; } return true; }
  function writeFailed(id: number, reason: unknown): void {
    if (reason && typeof reason === 'object' && 'status' in reason && [400, 401, 403, 404, 409, 412, 422, 428].includes(Number(reason.status))) uncertain.delete(id);
    if (active()) get('datasetsStatus').textContent = uncertain.has(id)
      ? 'Dataset mutation outcome is unconfirmed. Inspect the dataset and audit history, then refresh before retrying.' : asError(reason).message;
  }
  function writeControls(): void {
    if (!active()) return;
    root.querySelectorAll<HTMLButtonElement>('[data-action="delete"], [data-action="refresh"]').forEach(button => {
      const id = Number(button.dataset.id); button.disabled = !catalogReady || writes.has(id) || uncertain.has(id);
    });
    get<HTMLButtonElement>('ds-saveBtn').disabled = !catalogReady || !editedDataset || !allDatasets.some(dataset => dataset.id === editedDataset?.id)
      || writes.has(editedDataset.id) || uncertain.has(editedDataset.id);
    get<HTMLButtonElement>('ds-acl-grantBtn').disabled = !catalogReady || selectedDatasetId === null
      || !allDatasets.some(dataset => dataset.id === selectedDatasetId) || !get<HTMLSelectElement>('ds-acl-group').options.length
      || writes.has(selectedDatasetId) || uncertain.has(selectedDatasetId);
    get('dsAclTableWrap').querySelectorAll<HTMLButtonElement>('[data-gid], [data-uid]').forEach(button => {
      button.disabled = !catalogReady || selectedDatasetId === null || writes.has(selectedDatasetId) || uncertain.has(selectedDatasetId);
    });
  }
  function listen(element: Element | Document, event: string, handler: EventListener): void {
    element.addEventListener(event, value => { if (active()) handler(value); }, { signal: lifetime.signal });
  }
  function closePicker(restoreFocus = false): void {
    pickerVersion++; pickerRequest?.abort(); dv.pickerCol = null;
    get('dv-pickerPopover').style.display = 'none'; get<HTMLInputElement>('dv-pickerSearch').oninput = null;
    if (restoreFocus && pickerAnchor?.isConnected) pickerAnchor.focus();
    pickerAnchor = null;
  }
  function closeViewer(): void {
    rowsVersion++; statsVersion++; rowsRequest?.abort(); statsRequest?.abort(); closePicker();
    dv.id = null; dv.columns = []; dv.stats = null;
    get('datasetViewerModal').style.display = 'none'; get('dv-tableWrap').replaceChildren(); clearCounts();
  }
  function closeLineage(): void { lineageVersion++; lineageRequest?.abort(); get('datasetLineageModal').style.display = 'none'; dsLineageDagInstance?.dispose(); dsLineageDagInstance = null; }
  function clearCounts(): void {
    get('dv-totalCount').textContent = '—'; get('dv-filteredCount').textContent = '—'; get('dv-pageInfo').textContent = 'Page — of —';
    get<HTMLButtonElement>('dv-prevBtn').disabled = true; get<HTMLButtonElement>('dv-nextBtn').disabled = true;
  }

  // ── State ──────────────────────────────────────────────────────────────────
  let allDatasets: DatasetDto[] = [];
  let selectedDatasetId: number | null = null;
  let dsLineageDagInstance: DagHandle | null = null;
  const dv: ViewerState = {
    id: null, name: null, columns: [], stats: null,
    filters: [], sort: null, dir: 'asc', search: '', page: 1, pageSize: 50,
    pickerCol: null, pickerChecked: new Set(),
  };

  // ── Datasets table ─────────────────────────────────────────────────────────
  async function loadDatasets(explicit = false) {
    if (!active()) return;
    const version = ++loadVersion; listRequest?.abort(); const request = listRequest = new AbortController();
    catalogReady = false; allDatasets = [];
    writeControls();
    const $wrap = get('datasetsTableWrap');
    $wrap.innerHTML = '<div class="loading-state">Loading datasets…</div>';
    try {
      const result = readDatasetCatalog(await datasetsApi.list(request.signal));
      if (!active() || version !== loadVersion) return;
      allDatasets = result; catalogReady = true;
      if (dv.id !== null && !allDatasets.some(dataset => dataset.id === dv.id)) closeViewer();
      if (explicit) { for (const id of uncertain) if (!writes.has(id)) uncertain.delete(id); get('datasetsStatus').textContent = ''; }
      if (!allDatasets.length) {
        $wrap.innerHTML = `
          <div class="empty-state empty-state-panel">
            <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
            <h2>No shared datasets registered</h2>
            <p>Datasets are created by <code>CREATE DATASET</code> statements in report scripts.</p>
          </div>`;
        writeControls();
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
      $wrap.querySelectorAll<HTMLElement>('[data-action]').forEach(btn => {
        listen(btn, 'click', () => { void handleDatasetAction(btn); });
      });
      writeControls();
    } catch (reason) { if (active() && version === loadVersion) $wrap.innerHTML = `<div class="empty-state empty-state-panel empty-state-error">Dataset catalog unavailable. ${esc(asError(reason).message)}</div>`; }
  }

  async function handleDatasetAction(btn: HTMLElement) {
    if (!active() || !catalogReady) return;
    const id = Number(btn.dataset.id);
    const action = btn.dataset.action;
    const dataset = allDatasets.find(item => item.id === id);
    if (!dataset || (['delete', 'refresh'].includes(action ?? '') && (writes.has(id) || uncertain.has(id)))) return;

    if (action === 'view') {
      dvOpen(id, btn.dataset.name || 'Dataset');
      return;
    } else if (action === 'lineage') {
      showDatasetLineage(btn.dataset.name || 'Dataset');
      return;
    } else if (action === 'delete') {
      writes.add(id);
      writeControls();
      try {
        if (!await confirmFn('Delete this dataset? This removes the registry entry and cached data.')) return;
        if (!active() || !catalogReady || !allDatasets.includes(dataset)) return;
        uncertain.add(id);
        const result = await datasetsApi.delete(id, dataset.version, lifetime.signal);
        if (result !== undefined) throw new TypeError('Dataset delete acknowledgement is incomplete.');
        if (!active()) return;
        uncertain.delete(id); await loadDatasets();
      }
      catch (err) { writeFailed(id, err); alertErr(err); }
      finally { writes.delete(id); writeControls(); }
    } else if (action === 'refresh') {
      try {
        writes.add(id);
        uncertain.add(id);
        writeControls();
        const result = readDatasetRefresh(await datasetsApi.refresh(id, lifetime.signal));
        if (!active()) return;
        uncertain.delete(id);
        alertFn(result.triggered
          ? `Refresh job queued (job ${result.jobId}).`
          : result.message || 'Dataset marked stale — will re-materialise on next script run.');
        loadDatasets();
      } catch (err) { writeFailed(id, err); alertErr(err); } finally { writes.delete(id); writeControls(); }
    } else if (action === 'edit') {
      const d = allDatasets.find(x => x.id === id);
      if (!d) return;
      editedDataset = d;
      writeControls();
      get<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('ds-accessLevel').value = d.accessLevel;
      get<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('ds-ttl').value = d.ttl || '';
      get<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('ds-id').value = String(d.id);
      get('ds-error').classList.remove('show');
      get('dsAclPanel').style.display = 'none';
      get('editDatasetForm').style.display = '';
    } else if (action === 'acl') {
      selectedDatasetId = id;
      get('dsAclDatasetName').textContent = dataset.name;
      get('editDatasetForm').style.display = 'none';
      get('dsAclPanel').style.display = '';
      loadDatasetAcl(id);
      const groupVersion = ++groupsVersion;
      get('ds-acl-group').replaceChildren(); get<HTMLButtonElement>('ds-acl-grantBtn').disabled = true;
      adminApi.listGroups(lifetime.signal).then(result => {
        const groups = readDatasetGroups(result);
        if (!active() || groupVersion !== groupsVersion || selectedDatasetId !== id) return;
        get('ds-acl-group').innerHTML =
          groups.map(g => `<option value="${g.id}">${esc(g.name)}</option>`).join('');
        writeControls();
      }).catch(reason => { if (active() && groupVersion === groupsVersion && selectedDatasetId === id) alertErr(reason); });
    }
  }

  // ── Dataset lineage DAG ─────────────────────────────────────────────────────
  async function showDatasetLineage(datasetName: string) {
    if (!active()) return;
    const version = ++lineageVersion; lineageRequest?.abort(); const request = lineageRequest = new AbortController();
    const $modal = get('datasetLineageModal');
    const $dag   = get('dsLineageDag');
    get('dsLineageTitle').textContent = `Lineage — ${datasetName}`;
    $modal.style.display = 'flex';
    $dag.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading lineage…</span></div>';
    if (dsLineageDagInstance) { dsLineageDagInstance.dispose(); dsLineageDagInstance = null; }

    try {
      const rows = readDatasetLineage(await catalogApi.lineage('table', { name: datasetName, limit: 100 }, request.signal));
      if (!active() || version !== lineageVersion) return;
      const tableSet = new Set<string>();
      const edgeMap = new Map<string, { source: string; target: string }>();
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
      if (active() && version === lineageVersion) $dag.innerHTML = `<div class="empty-state">Lineage unavailable: ${esc(asError(err).message)}</div>`;
    }
  }

  async function loadDatasetAcl(datasetId: number) {
    if (!active() || selectedDatasetId !== datasetId) return;
    const version = ++aclVersion; aclRequest?.abort(); const request = aclRequest = new AbortController();
    const $wrap = get('dsAclTableWrap');
    $wrap.innerHTML = '<div class="loading-state">Loading permissions…</div>';
    try {
      const acls = readDatasetAcl(await datasetsApi.listAcl(datasetId, request.signal));
      if (!active() || version !== aclVersion || selectedDatasetId !== datasetId) return;
      $wrap.innerHTML = aclTableHtml(acls, esc, escAttr);
      const revoke = async (call: (version: number) => Promise<unknown>) => {
        if (!active() || selectedDatasetId !== datasetId || !catalogReady || writes.has(datasetId) || uncertain.has(datasetId)) return;
        const dataset = allDatasets.find(x => x.id === datasetId);
        if (!dataset) return;
        writes.add(datasetId);
        uncertain.add(datasetId);
        writeControls();
        try {
          readDatasetVersion(await call(dataset.version), dataset.version);
          if (!active()) return;
          uncertain.delete(datasetId);
          await loadDatasets();
          if (selectedDatasetId === datasetId) await loadDatasetAcl(datasetId);
        } catch (err) { writeFailed(datasetId, err); alertErr(err); } finally { writes.delete(datasetId); writeControls(); }
      };
      $wrap.querySelectorAll('[data-gid]').forEach(btn => {
        btn.addEventListener('click', () =>
          revoke(version => datasetsApi.revokeAcl(datasetId, Number((btn as HTMLElement).dataset.gid), version, lifetime.signal)));
      });
      $wrap.querySelectorAll('[data-uid]').forEach(btn => {
        btn.addEventListener('click', () =>
          revoke(version => datasetsApi.revokeUserAcl(datasetId, Number((btn as HTMLElement).dataset.uid), version, lifetime.signal)));
      });
      writeControls();
    } catch (err) {
      if (active() && version === aclVersion && selectedDatasetId === datasetId) $wrap.innerHTML = `<div class="empty-state">Permissions unavailable: ${esc(asError(err).message)}</div>`;
    }
  }

  // ── Dataset Viewer ──────────────────────────────────────────────────────────
  function dvOpen(id: number, name: string) {
    if (!active()) return;
    closeViewer();
    Object.assign(dv, { id, name, columns: [], stats: null, filters: [], sort: null, dir: 'asc', search: '', page: 1, pageSize: 50, pickerCol: null, pickerChecked: new Set() });
    get('dv-title').textContent = name;
    get<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('dv-search').value = '';
    get<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('dv-pageSize').value = '50';
    get('dv-statsStatus').textContent = '';
    get('datasetViewerModal').style.display = 'flex';
    dvFetch();
    dvFetchStats();
  }

  async function dvFetch() {
    if (!active() || dv.id === null) return;
    const id = dv.id, page = dv.page, pageSize = dv.pageSize, version = ++rowsVersion;
    rowsRequest?.abort(); const request = rowsRequest = new AbortController();
    const query = { page, pageSize, sort: dv.sort, dir: dv.dir, search: dv.search, filters: dv.filters.map(filter => ({ ...filter })) };
    clearCounts(); dv.columns = [];
    const $wrap = get('dv-tableWrap');
    $wrap.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading…</span></div>';
    try {
      const res = readDatasetRows(await datasetsApi.data(id, query, request.signal), page, pageSize);
      if (!active() || version !== rowsVersion || dv.id !== id) return;
      dv.columns = res.columns || [];
      dvRender(res);
      get('dv-totalCount').textContent    = (res.totalCount    ?? 0).toLocaleString();
      get('dv-filteredCount').textContent = (res.filteredCount ?? 0).toLocaleString();
      const totalPages = Math.max(1, Math.ceil((res.filteredCount ?? 0) / dv.pageSize));
      get('dv-pageInfo').textContent = `Page ${dv.page} of ${totalPages}`;
      get<HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('dv-prevBtn').disabled = dv.page <= 1;
      get<HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('dv-nextBtn').disabled = dv.page >= totalPages;
      dvUpdateBadge();
    } catch (err) {
      if (active() && version === rowsVersion && dv.id === id) $wrap.innerHTML = `<div class="error-msg show">${esc(asError(err).message)}</div>`;
    }
  }

  async function dvFetchStats() {
    if (!active() || dv.id === null) return;
    const id = dv.id, version = ++statsVersion; statsRequest?.abort(); const request = statsRequest = new AbortController();
    const filters = dv.filters.map(filter => ({ ...filter }));
    dv.stats = null; get('dv-tableWrap').querySelector('tfoot')?.remove(); get('dv-statsStatus').textContent = '';
    try {
      const result = readDatasetStats(await datasetsApi.stats(id, filters.length ? filters : null, request.signal));
      if (!active() || version !== statsVersion || dv.id !== id) return;
      dv.stats = result;
      dvRefreshStatsFooter();
    } catch (reason) { if (active() && version === statsVersion && dv.id === id) statsUnavailable(reason); }
  }

  function statsUnavailable(reason: unknown): void {
    dv.stats = null; get('dv-tableWrap').querySelector('tfoot')?.remove();
    get('dv-statsStatus').textContent = `Statistics unavailable. ${asError(reason).message}`;
  }
  function validateStatsColumns(columns: readonly DatasetColumnDto[]): void {
    if (!dv.stats) return;
    try { readDatasetStats(dv.stats, columns); }
    catch (reason) { statsUnavailable(reason); }
  }

  function dvGuessType(typeStr: string | undefined) {
    const t = (typeStr || '').toLowerCase();
    if (/int|float|double|decimal|numeric|real|money|number/.test(t)) return 'number';
    if (/date|time/.test(t)) return 'date';
    return 'text';
  }

  function dvRender(res: DatasetRowsDto) {
    const cols = res.columns || [];
    const rows = res.rows    || [];
    validateStatsColumns(cols);

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
          const s = dv.stats?.find(x => x.name === col.name);
          if (!s) return '<td></td>';
          const type = dvGuessType(col.type);
          if (type === 'number') return `<td><span class="dv-stats-label">Min</span> ${esc(s.min ?? '—')} <span class="dv-stats-label">Max</span> ${esc(s.max ?? '—')} <span class="dv-stats-label">Avg</span> ${s.avg != null ? (+s.avg.toFixed(2)) : '—'} <span class="dv-stats-label">Nulls</span> ${s.nullCount}</td>`;
          return `<td><span class="dv-stats-label">Nulls</span> ${s.nullCount}</td>`;
        }).join('')}</tr></tfoot>`
      : '';

    const dataRows = rows.length
      ? rows.map(row => `<tr>${cols.map(col => {
          const v = row[col.name];
          return `<td>${v == null ? '<span class="dv-null">null</span>' : esc(String(v))}</td>`;
        }).join('')}</tr>`).join('')
      : `<tr><td colspan="${cols.length || 1}" style="text-align:center;color:var(--portal-muted);padding:20px">No rows match the current filters.</td></tr>`;

    const $wrap = get('dv-tableWrap');
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
      const col = (th as HTMLElement).dataset.col;
      if (!col) return;
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
        if (opEl)  (opEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value  = existing.op;
        if (valEl) (valEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value = existing.val  ?? '';
        if (val2El) (val2El as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value = existing.val2 ?? '';
      }

      // Show val2 for range ops
      function syncVal2() {
        if (!val2El) return;
        const op = opEl ? (opEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value : 'between';
        (val2El as HTMLElement).style.display = (op === 'between' || type === 'date') ? '' : 'none';
      }
      if (opEl) { opEl.addEventListener('change', syncVal2); syncVal2(); }

      // Apply filter on blur/enter
      function applyFilter() {
        const op  = opEl  ? (opEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value  : (type === 'date' ? 'between' : 'contains');
        const val = valEl ? (valEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value : '';
        const val2= val2El ? (val2El as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value : '';
        dv.filters = dv.filters.filter(f => f.col !== col.name);
        if (op === 'is_null' || op === 'not_null' || val.trim()) {
          dv.filters.push({ col: col.name, op, val: val || null, val2: val2 || null });
        }
        dv.page = 1; dvFetch(); dvFetchStats();
      }

      [valEl, val2El].filter((el): el is Element => el !== null).forEach(el => {
        el.addEventListener('keydown', e => { if ((e as KeyboardEvent).key === 'Enter') applyFilter(); });
        el.addEventListener('blur', e => {
          // Skip if focus is moving to the sibling input for the same column (val1↔val2 tab)
          if ((e as FocusEvent | MouseEvent).relatedTarget === valEl || (e as FocusEvent | MouseEvent).relatedTarget === val2El) return;
          applyFilter();
        });
      });
      if (opEl) opEl.addEventListener('change', () => {
        const op = (opEl as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;
        if (op === 'is_null' || op === 'not_null') applyFilter();
      });

      // Picker button
      const pickerBtn = $wrap.querySelector(`.dv-picker-btn[data-col="${CSS.escape(col.name)}"]`);
      if (pickerBtn) pickerBtn.addEventListener('click', e => { e.stopPropagation(); dvOpenPicker(col.name, pickerBtn); });
    });

    // Date filter val2 always shown for date type (handled by syncVal2 default)
    $wrap.querySelectorAll('.dv-filter-val2').forEach(el => {
      const col = (el as HTMLElement).dataset.col;
      const type = dvGuessType(cols.find(c => c.name === col)?.type);
      if (type === 'date') (el as HTMLElement).style.display = '';
    });
  }

  function dvRefreshStatsFooter() {
    if (!dv.stats || !dv.columns.length) return;
    validateStatsColumns(dv.columns);
    if (!dv.stats) return;
    const table = get('dv-tableWrap').querySelector('.dv-table');
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
      const s = dv.stats?.find(x => x.name === col.name);
      if (!s) return;
      const type = dvGuessType(col.type);
      if (type === 'number')
        tds[i].innerHTML = `<span class="dv-stats-label">Min</span> ${esc(s.min ?? '—')} <span class="dv-stats-label">Max</span> ${esc(s.max ?? '—')} <span class="dv-stats-label">Avg</span> ${s.avg != null ? (+s.avg.toFixed(2)) : '—'} <span class="dv-stats-label">Nulls</span> ${s.nullCount}`;
      else
        tds[i].innerHTML = `<span class="dv-stats-label">Nulls</span> ${Number.isFinite(Number(s.nullCount)) ? Math.trunc(Number(s.nullCount)) : 0}`;
    });
  }

  function dvFilterHtml(colName: string, type: string) {
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
    const $badge = get('dv-filter-badge');
    if (dv.filters.length) {
      $badge.textContent = `${dv.filters.length} filter${dv.filters.length > 1 ? 's' : ''}`;
      $badge.style.display = '';
    } else {
      $badge.style.display = 'none';
    }
  }

  // ── Value picker ────────────────────────────────────────────────────────────
  async function dvOpenPicker(colName: string, anchor: Element) {
    if (!active() || dv.id === null) return;
    closePicker();
    dv.pickerCol = colName;
    const popover = get('dv-pickerPopover');
    const $list   = get('dv-pickerList');
    const $search = get('dv-pickerSearch');

    const existing = dv.filters.find(f => f.col === colName && f.op === 'in');
    dv.pickerChecked = existing ? new Set(JSON.parse(existing.val || '[]')) : new Set();

    ($search as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value = '';
    $list.innerHTML = '<span style="color:var(--portal-muted);font-size:.84em">Loading…</span>';
    popover.style.display = '';

    const rect = anchor.getBoundingClientRect();
    popover.style.left = `${Math.min(rect.left, window.innerWidth - 220)}px`;
    popover.style.top  = `${rect.bottom + 4}px`;
    pickerAnchor = anchor instanceof HTMLElement ? anchor : null;
    $search.focus();

    await dvLoadPickerValues('');

    if (active() && dv.pickerCol === colName) $search.oninput = () => { if (active()) void dvLoadPickerValues(($search as HTMLInputElement).value); };
  }

  async function dvLoadPickerValues(search: string) {
    if (!active() || dv.id === null || !dv.pickerCol) return;
    const id = dv.id, column = dv.pickerCol, version = ++pickerVersion;
    pickerRequest?.abort(); const request = pickerRequest = new AbortController();
    const $list = get('dv-pickerList');
    try {
      const res = readDatasetValues(await datasetsApi.columnValues(id, column, { search, limit: 50 }, request.signal), 50);
      if (!active() || version !== pickerVersion || dv.id !== id || dv.pickerCol !== column) return;
      const values = res.values || [];
      $list.innerHTML = values.map(v => {
        const s = v == null ? '' : String(v);
        const checked = dv.pickerChecked.has(s) ? 'checked' : '';
        return `<label class="dv-picker-item"><input type="checkbox" value="${escAttr(s)}" ${checked}> ${esc(s || '(empty)')}</label>`;
      }).join('') || '<span style="color:var(--portal-muted);font-size:.84em">No values found.</span>';
    } catch { if (active() && version === pickerVersion && dv.id === id && dv.pickerCol === column) $list.innerHTML = '<span style="color:var(--portal-muted)">Values unavailable.</span>'; }
  }

  // ── Wiring ──────────────────────────────────────────────────────────────────
  function wire() {
    get('datasetViewerModal').addEventListener('keydown', event => {
      if (!active() || dv.pickerCol === null) return;
      if (event.key === 'Escape') {
        event.preventDefault(); event.stopImmediatePropagation(); closePicker(true);
      } else if (event.key === 'Tab') {
        const first = get('dv-pickerSearch'), last = get('dv-pickerApply');
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
    }, { capture: true, signal: lifetime.signal });
    listen(get('datasetsRefreshBtn'), 'click', () => { void loadDatasets(true); });
    listen(get('dsLineageCloseBtn'), 'click', closeLineage);
    listen(get('ds-cancelBtn'), 'click', () => { editedDataset = null; get('editDatasetForm').style.display = 'none'; });
    listen(get('ds-saveBtn'), 'click', async () => {
      const dataset = editedDataset;
      if (!dataset || !catalogReady || !allDatasets.some(item => item.id === dataset.id) || writes.has(dataset.id) || uncertain.has(dataset.id)) return;
      const accessLevel = get<HTMLSelectElement>('ds-accessLevel').value;
      const ttl = get<HTMLInputElement>('ds-ttl').value.trim() || null;
      const error = get('ds-error'); error.classList.remove('show');
      writes.add(dataset.id); get<HTMLButtonElement>('ds-saveBtn').disabled = true;
      uncertain.add(dataset.id);
      writeControls();
      try {
        const result = readDataset(await datasetsApi.update(dataset.id, { accessLevel, ttl: ttl ?? '' }, dataset.version, lifetime.signal), dataset.id);
        if (result.version <= dataset.version || result.accessLevel !== accessLevel || (result.ttl ?? null) !== ttl)
          throw new TypeError('Dataset save acknowledgement does not match the submitted changes.');
        if (!active()) return;
        uncertain.delete(dataset.id);
        if (editedDataset === dataset) { editedDataset = null; get('editDatasetForm').style.display = 'none'; }
        await loadDatasets();
      } catch (reason) {
        writeFailed(dataset.id, reason);
        if (active() && editedDataset === dataset) { error.textContent = asError(reason).message; error.classList.add('show'); }
      } finally { writes.delete(dataset.id); writeControls(); }
    });
    listen(get('dsAclCloseBtn'), 'click', () => {
      selectedDatasetId = null; aclVersion++; groupsVersion++; aclRequest?.abort();
      get('dsAclPanel').style.display = 'none'; get('dsAclTableWrap').replaceChildren();
    });
    listen(get('ds-acl-grantBtn'), 'click', async () => {
      const id = selectedDatasetId, groupId = Number(get<HTMLSelectElement>('ds-acl-group').value);
      const dataset = allDatasets.find(item => item.id === id);
      if (!id || !groupId || !dataset || !catalogReady || writes.has(id) || uncertain.has(id)) return;
      const permission = get<HTMLSelectElement>('ds-acl-perm').value;
      writes.add(id); get<HTMLButtonElement>('ds-acl-grantBtn').disabled = true;
      uncertain.add(id);
      writeControls();
      try {
        readDatasetVersion(await datasetsApi.grantAcl(id, groupId, permission, dataset.version, lifetime.signal), dataset.version);
        if (!active()) return;
        uncertain.delete(id);
        await loadDatasets(); if (selectedDatasetId === id) await loadDatasetAcl(id);
      } catch (reason) { writeFailed(id, reason); alertErr(reason); }
      finally { writes.delete(id); writeControls(); }
    });
    listen(get('dv-pickerList'), 'change', event => {
      if (!(event.target instanceof HTMLInputElement) || event.target.type !== 'checkbox') return;
      if (event.target.checked) dv.pickerChecked.add(event.target.value); else dv.pickerChecked.delete(event.target.value);
    });
    listen(get('dv-pickerApply'), 'click', () => {
      const column = dv.pickerCol;
      if (!column || dv.id === null) return;
      dv.filters = dv.filters.filter(filter => filter.col !== column);
      if (dv.pickerChecked.size) dv.filters.push({ col: column, op: 'in', val: JSON.stringify([...dv.pickerChecked]), val2: null });
      closePicker(); dv.page = 1; void dvFetch(); void dvFetchStats();
    });
    listen(document, 'click', event => {
      if (!(event.target instanceof Element)) return;
      const popover = get('dv-pickerPopover');
      if (!popover.contains(event.target) && !event.target.closest('.dv-picker-btn')) closePicker();
    });
    listen(document, 'keydown', event => {
      if (!(event instanceof KeyboardEvent) || event.key !== 'Escape') return;
      const pickerOpen = get('dv-pickerPopover').style.display !== 'none';
      if (pickerOpen) { closePicker(); event.preventDefault(); return; }
      if (get('datasetViewerModal').style.display !== 'none') closeViewer();
      else if (get('datasetLineageModal').style.display !== 'none') closeLineage();
    });
    listen(get('dv-search'), 'keydown', event => {
      if (!(event instanceof KeyboardEvent) || event.key !== 'Enter' || !(event.target instanceof HTMLInputElement)) return;
      closePicker(); dv.search = event.target.value.trim(); dv.page = 1; void dvFetch();
    });
    listen(get('dv-resetBtn'), 'click', () => {
      closePicker(); dv.filters = []; dv.search = ''; dv.sort = null; dv.dir = 'asc'; dv.page = 1;
      get<HTMLInputElement>('dv-search').value = ''; void dvFetch(); void dvFetchStats();
    });
    const exportData = async (format: 'csv' | 'xlsx') => {
      if (dv.id === null) return;
      const id = dv.id, filename = (dv.name || 'dataset') + '.' + format;
      const query = { sort: dv.sort, dir: dv.dir, search: dv.search, filters: dv.filters.map(filter => ({ ...filter })) };
      try { await (format === 'csv' ? datasetsApi.exportCsv(id, filename, query, lifetime.signal) : datasetsApi.exportXlsx(id, filename, query, lifetime.signal)); }
      catch (reason) { alertErr(reason); }
    };
    listen(get('dv-exportBtn'), 'click', () => { void exportData('csv'); });
    listen(get('dv-exportXlsxBtn'), 'click', () => { void exportData('xlsx'); });
    listen(get('dv-closeBtn'), 'click', closeViewer);
    listen(get('dv-prevBtn'), 'click', () => { if (dv.page > 1) { dv.page--; void dvFetch(); } });
    listen(get('dv-nextBtn'), 'click', () => { dv.page++; void dvFetch(); });
    listen(get('dv-pageSize'), 'change', event => {
      if (!(event.target instanceof HTMLSelectElement)) return;
      const size = Number(event.target.value); if (![25, 50, 100, 200].includes(size)) return;
      dv.pageSize = size; dv.page = 1; void dvFetch();
    });
    for (const modal of modalEls.filter(element => element.classList.contains('modal-overlay'))) {
      listen(modal, 'click', event => { if (event.target !== modal) return; if (modal.id === 'datasetViewerModal') closeViewer(); else closeLineage(); });
    }
  }
  function dispose(): void {
    if (disposed) return;
    disposed = true; lifetime.abort(); listRequest?.abort(); rowsRequest?.abort(); statsRequest?.abort();
    pickerRequest?.abort(); lineageRequest?.abort(); aclRequest?.abort(); releaseDialogs?.();
    dsLineageDagInstance?.dispose(); dsLineageDagInstance = null; modalEls.forEach(element => element.remove());
    if (surfaces.get(root) === surface) { root.replaceChildren(); surfaces.delete(root); }
  }
  const surface = { load: loadDatasets, dispose };
  surfaces.set(root, surface); wire(); return surface;
}
