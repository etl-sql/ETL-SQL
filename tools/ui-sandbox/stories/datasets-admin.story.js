// Story for Admin → Shared Datasets. Imports the canonical createDatasetsAdmin
// module and drives it with a mock backend (an in-memory table) so the datasets
// table AND the Dataset Viewer — sort, search, per-column filters, the value
// picker, stats footer, paging, CSV — all actually work without the portal.

import { importFresh, DESIGNER_JS } from '../util.js';

const DATASETS_ADMIN_JS = '/src/ETL-SQL.Portal/wwwroot/js/datasets-admin.js';

// ── Mock "cache" table behind the dataset viewer ─────────────────────────────
const COLUMNS = [
  { name: 'region',     type: 'NVARCHAR' },
  { name: 'product',    type: 'NVARCHAR' },
  { name: 'units',      type: 'INT' },
  { name: 'revenue',    type: 'DECIMAL(18,2)' },
  { name: 'margin_pct', type: 'FLOAT' },
  { name: 'order_date', type: 'DATE' },
  { name: 'channel',    type: 'NVARCHAR' },
];

const TABLE = (() => {
  const regions = ['North', 'South', 'East', 'West'];
  const products = ['Widget', 'Gadget', 'Gizmo', 'Doohickey'];
  const channels = ['Online', 'Retail', 'Partner', null];
  const rows = [];
  let seed = 7;
  const rand = () => (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
  for (let i = 0; i < 48; i++) {
    const units = Math.floor(rand() * 500) + 1;
    const revenue = +(units * (8 + rand() * 40)).toFixed(2);
    rows.push({
      region: regions[i % regions.length],
      product: products[Math.floor(rand() * products.length)],
      units,
      revenue,
      margin_pct: i % 11 === 0 ? null : +(rand() * 0.6).toFixed(3),  // a few nulls
      order_date: `2026-0${1 + (i % 5)}-${String(1 + (i % 27)).padStart(2, '0')}`,
      channel: channels[Math.floor(rand() * channels.length)],
    });
  }
  return rows;
})();

const colType = name => (COLUMNS.find(c => c.name === name) || {}).type || '';
function guessType(typeStr) {
  const t = (typeStr || '').toLowerCase();
  if (/int|float|double|decimal|numeric|real|money|number/.test(t)) return 'number';
  if (/date|time/.test(t)) return 'date';
  return 'text';
}

function passesFilter(row, f) {
  const type = guessType(colType(f.col));
  const v = row[f.col];
  if (f.op === 'is_null') return v == null;
  if (f.op === 'not_null') return v != null;
  if (f.op === 'in') { const set = new Set(JSON.parse(f.val || '[]')); return set.has(v == null ? '' : String(v)); }
  if (v == null) return false;
  if (type === 'number') {
    const n = Number(v), a = Number(f.val), b = Number(f.val2);
    switch (f.op) {
      case 'eq': return n === a; case 'gt': return n > a; case 'lt': return n < a;
      case 'gte': return n >= a; case 'lte': return n <= a;
      case 'between': return n >= a && (Number.isNaN(b) ? true : n <= b);
      default: return true;
    }
  }
  if (type === 'date') {
    const d = new Date(v).getTime();
    const a = f.val ? new Date(f.val).getTime() : null;
    const b = f.val2 ? new Date(f.val2).getTime() : null;
    if (a != null && d < a) return false;
    if (b != null && d > b) return false;
    return true;
  }
  const s = String(v).toLowerCase(), q = String(f.val ?? '').toLowerCase();
  switch (f.op) {
    case 'contains': return s.includes(q); case 'eq': return s === q;
    case 'neq': return s !== q; case 'starts_with': return s.startsWith(q);
    default: return true;
  }
}

function selectRows({ search, filters }) {
  let rows = TABLE.slice();
  if (search) {
    const q = search.toLowerCase();
    rows = rows.filter(r => COLUMNS.some(c => String(r[c.name] ?? '').toLowerCase().includes(q)));
  }
  for (const f of (filters || [])) rows = rows.filter(r => passesFilter(r, f));
  return rows;
}

// ── Mock portal APIs ─────────────────────────────────────────────────────────
const DATASETS = [
  { id: 1, name: 'sales_summary', folderPath: '/Finance', accessLevel: 'Public', isEncrypted: false,
    rowCount: TABLE.length, isStale: false, ttl: '1h', lastRefresh: '2026-05-31T02:10:00Z',
    refreshInterval: '1h', owningReportName: 'Executive Sales', owningReportId: 42 },
  { id: 2, name: 'customer_360', folderPath: '/Marketing', accessLevel: 'Private', isEncrypted: true,
    rowCount: 12840, isStale: true, ttl: '6h', lastRefresh: '2026-05-30T18:00:00Z',
    refreshInterval: '6h', owningReportName: 'Campaign Insights', owningReportId: 58 },
  { id: 3, name: 'staging_orders', folderPath: 'Root', accessLevel: 'Private', isEncrypted: false,
    rowCount: 0, isStale: true, ttl: null, lastRefresh: null,
    refreshInterval: null, owningReportName: null, owningReportId: null },
].map(item => ({ ...item, version: 1, createdAt: '2026-05-01T00:00:00Z', updatedAt: '2026-05-31T00:00:00Z' }));

const datasetsApi = {
  async list() { return DATASETS.map(d => ({ ...d })); },
  async get(id) { return DATASETS.find(d => d.id === id); },
  async update(id, body) { const d = DATASETS.find(x => x.id === id); if (d) Object.assign(d, body); return d; },
  async delete() { return { ok: true }; },
  async refresh() { return { triggered: true, jobId: 'dataset-refresh-4502' }; },
  async listAcl() { return [{ groupId: 5, groupName: 'Finance-Analysts', permission: 'Viewer', principalKind: 'Group', userId: null, userName: null }, { groupId: 9, groupName: 'Data-Platform', permission: 'Owner', principalKind: 'Group', userId: null, userName: null }]; },
  async grantAcl() { return { ok: true }; },
  async revokeAcl() { return { ok: true }; },

  async data(id, { page = 1, pageSize = 50, sort = null, dir = 'asc', search = null, filters = null } = {}) {
    
    let rows = selectRows({ search, filters });
    const filteredCount = rows.length;
    if (sort) {
      const type = guessType(colType(sort));
      rows = rows.slice().sort((a, b) => {
        const x = a[sort], y = b[sort];
        if (x == null && y == null) return 0;
        if (x == null) return 1;
        if (y == null) return -1;
        if (type === 'number') return Number(x) - Number(y);
        if (type === 'date') return new Date(x) - new Date(y);
        return String(x).localeCompare(String(y));
      });
      if (dir === 'desc') rows.reverse();
    }
    const start = (page - 1) * pageSize;
    return { columns: COLUMNS, rows: rows.slice(start, start + pageSize), totalCount: TABLE.length, filteredCount, page, pageSize };
  },

  async stats(id, filters = null) {
    
    const rows = selectRows({ search: null, filters });
    return COLUMNS.map(c => {
      const vals = rows.map(r => r[c.name]);
      const nullCount = vals.filter(v => v == null).length;
      if (guessType(c.type) === 'number') {
        const nums = vals.filter(v => v != null).map(Number);
        const min = nums.length ? Math.min(...nums) : null;
        const max = nums.length ? Math.max(...nums) : null;
        const avg = nums.length ? nums.reduce((a, b) => a + b, 0) / nums.length : null;
        return { name: c.name, min, max, avg, nullCount };
      }
      return { name: c.name, nullCount, min: null, max: null, avg: null };
    });
  },

  async columnValues(id, colName, { search = null, limit = 50 } = {}) {
    
    let vals = [...new Set(TABLE.map(r => r[colName]).map(v => (v == null ? '' : String(v))))];
    if (search) vals = vals.filter(v => v.toLowerCase().includes(search.toLowerCase()));
    return { values: vals.slice(0, limit), totalDistinct: vals.length };
  },

  async exportCsv(id, filename) { console.log('[sandbox] exportCsv', filename); },
  async exportXlsx(id, filename) { console.log('[sandbox] exportXlsx', filename); },
};

const adminApi = {
  async listGroups() { return [{ id: 5, name: 'Finance-Analysts' }, { id: 9, name: 'Data-Platform' }, { id: 12, name: 'Marketing' }].map(item => ({ ...item, description: null, memberCount: 0, provider: null, adGroup: null, version: 1 })); },
};

const catalogApi = {
  async lineage(kind, { name } = {}) {
    
    return [
      { targetTable: name, sourceTables: ['edw.Sales', 'edw.Customers'], operation: 'SELECT' },
      { targetTable: 'edw.Sales', sourceTables: ['stg.sales_csv'], operation: 'BULK INSERT' },
    ].map((item, index) => ({ ...item, id: index + 1, runAt: '2026-05-31T00:00:00Z', jobName: null, scriptPath: null,
      targetColumn: null, tags: {}, sourceFile: null, line: 0, sourceColumns: [], transformationKind: null,
      transformationExpression: null, functionsApplied: null, derivedFromDescriptions: null, reportId: null, reportName: null, folderPath: null }));
  },
};

function controlledApi(source) {
  let mode = 'healthy', paused = null;
  const records = structuredClone(DATASETS);
  const calls = [], pending = [], waiters = [];
  return {
    calls, setMode(value) { mode = value; }, pause(kind) { paused = kind; },
    whenPaused(kind) { return pending.some(item => item.kind === kind) ? Promise.resolve() : new Promise(resolve => waiters.push({ kind, resolve })); },
    release() { for (const item of pending.splice(0)) item.resolve(); },
    ...Object.fromEntries(Object.entries(source).map(([kind, implementation]) => [kind, async (...args) => {
      calls.push({ kind, args: structuredClone(args.filter(value => !(value instanceof AbortSignal))) });
      if (mode === 'data-unavailable' && kind === 'data') throw new Error('Dataset cache temporarily unavailable.');
      let value;
      if (kind === 'list') value = records;
      else if (kind === 'update') {
        const dataset = records.find(item => item.id === args[0]);
        Object.assign(dataset, args[1], { ttl: args[1].ttl || null, version: dataset.version + 1 }); value = dataset;
      } else if (kind === 'delete') { const index = records.findIndex(item => item.id === args[0]); if (index >= 0) records.splice(index, 1); }
      else if (['grantAcl', 'revokeAcl', 'revokeUserAcl'].includes(kind)) {
        const dataset = records.find(item => item.id === args[0]); value = { version: ++dataset.version };
      } else value = await implementation(...args);
      if (mode === 'bad-data' && kind === 'data') value = { columns: COLUMNS, rows: [] };
      if (mode === 'bad-stats' && kind === 'stats') value = value.slice(1);
      if (mode === 'stats-unavailable' && kind === 'stats') throw new Error('Dataset statistics temporarily unavailable.');
      if (mode === 'stats-literal' && kind === 'stats') value = value.map(item => item.name === 'units'
        ? { ...item, min: '<img src=x onerror="window.__datasetInjected=true">', max: null, avg: null } : item);
      if (mode === 'invalid-update' && kind === 'update') value = { ...value, id: 999 };
      if (mode === 'bad-acl' && kind === 'listAcl') value = [{ principalKind: 'User' }];
      if (mode === 'bad-lineage' && kind === 'lineage') value = null;
      if (mode === 'target-distinct' && kind === 'data' && args[0] === 2)
        value = { ...value, rows: value.rows.map(row => ({ ...row, region: 'Customer snapshot' })) };
      const snapshot = structuredClone(value);
      if (paused !== kind) return snapshot;
      paused = null;
      return new Promise(resolve => {
        pending.push({ kind, resolve: () => resolve(snapshot) });
        for (const waiter of waiters.filter(item => item.kind === kind)) waiter.resolve();
      });
    }]))
  };
}

export function createDatasetMock() { return controlledApi(datasetsApi); }

function rendered(host, selector) {
  if (host.querySelector(selector)) return Promise.resolve();
  return new Promise(resolve => {
    const observer = new MutationObserver(() => { if (host.querySelector(selector)) { observer.disconnect(); resolve(); } });
    observer.observe(host, { childList: true, subtree: true });
  });
}

export default {
  id: 'datasets-admin',
  title: 'Datasets admin',
  subtitle: 'Admin → Shared Datasets',
  fixtures: [
    { id: 'panel',  label: 'Datasets table' },
    { id: 'viewer', label: 'Dataset Viewer (auto-open)' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const panel = document.createElement('div');
    panel.className = 'admin-panel';
    panel.style.display = 'block';
    stage.replaceChildren(panel);

    const mod = await importFresh(DATASETS_ADMIN_JS);
    const designer = await importFresh(DESIGNER_JS);

    const mock = createDatasetMock(), adminMock = controlledApi(adminApi), catalogMock = controlledApi(catalogApi);
    const ds = mod.createDatasetsAdmin({
      host: panel,
      datasetsApi: mock,
      adminApi: adminMock,
      catalogApi: catalogMock,
      renderDag: designer.renderDag,
      modalRoot: document.body,
    });

    window.__datasets = { api: mock, admin: adminMock, catalog: catalogMock, surface: ds };

    await ds.load();

    if (fixtureId === 'viewer') {
      // Open the viewer for the first dataset so the data grid is front-and-centre.
      panel.querySelector('[data-action="view"]')?.click();
      await rendered(document.body, '#dv-tableWrap tbody tr');
      ctx.stat('dataset viewer · sort, search, per-column filters, value picker, stats, paging, CSV');
    } else {
      ctx.stat('shared datasets table · View Data / Lineage / Refresh / Edit / Permissions per row');
    }

    return { dispose() { ds.dispose(); delete window.__datasets; }, resize() {} };
  },
};
