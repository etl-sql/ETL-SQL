// Story for the portal's sidebar "Lineage" view (the catalog-wide lineage explorer).
// Imports the canonical createLineageCatalog module and drives it with a mock
// catalogApi so you can see how the query form, table/graph toggle, tags, and CSV
// export behave — without the portal, a catalog DB, or any run history.

import { importFresh, DESIGNER_JS } from '../util.js';

const LINEAGE_CATALOG_JS = '/src/ETL-SQL.Portal/wwwroot/js/lineage-catalog.js';
const LINEAGE_UI_JS = '/src/ETL-SQL.Portal/wwwroot/js/lineage-ui.js';


// A small but connected slice of cross-run lineage history:
//   csv -> edw.Sales, edw.Customers, edw.Orders -> mart.SalesSummary -> mart.ExecutiveKpis
const DB = [
  { runAt: '2026-05-31T02:10:00Z', jobName: 'nightly_sales_refresh', reportId: 42, reportName: 'Executive Sales', folderPath: '/Finance',
    targetTable: 'mart.SalesSummary', targetColumn: 'total_revenue', operation: 'SELECT',
    transformationKind: 'Aggregation', transformationExpression: 'SUM(Amount)', functionsApplied: ['SUM'],
    sourceTables: ['edw.Sales'], sourceColumns: ['Amount'], derivedFromDescriptions: 'Sales amount (from catalog)',
    tags: { classification: 'internal', owner: 'finance' }, sourceFile: 'samples/integration/sales.rptsql', line: 18 },

  { runAt: '2026-05-31T02:10:01Z', jobName: 'nightly_sales_refresh', reportId: 42, reportName: 'Executive Sales', folderPath: '/Finance',
    targetTable: 'mart.SalesSummary', targetColumn: 'region', operation: 'SELECT',
    transformationKind: 'Direct', transformationExpression: 'Region', functionsApplied: [],
    sourceTables: ['edw.Customers'], sourceColumns: ['Region'], derivedFromDescriptions: '',
    tags: { owner: 'finance' }, sourceFile: 'samples/integration/sales.rptsql', line: 12 },

  { runAt: '2026-05-31T02:10:02Z', jobName: 'nightly_sales_refresh', reportId: 42, reportName: 'Executive Sales', folderPath: '/Finance',
    targetTable: 'mart.SalesSummary', targetColumn: 'customer_email', operation: 'SELECT',
    transformationKind: 'Direct', transformationExpression: 'Email', functionsApplied: [],
    sourceTables: ['edw.Customers'], sourceColumns: ['Email'], derivedFromDescriptions: 'Customer email address',
    tags: { pii: 'true', classification: 'confidential', owner: 'finance' }, sourceFile: 'samples/integration/sales.rptsql', line: 14 },

  { runAt: '2026-05-31T02:10:03Z', jobName: 'nightly_sales_refresh', reportId: 42, reportName: 'Executive Sales', folderPath: '/Finance',
    targetTable: 'mart.SalesSummary', targetColumn: 'order_count', operation: 'SELECT',
    transformationKind: 'Aggregation', transformationExpression: 'COUNT(*)', functionsApplied: ['COUNT'],
    sourceTables: ['edw.Orders'], sourceColumns: ['OrderId'], derivedFromDescriptions: '',
    tags: {}, sourceFile: 'samples/integration/sales.rptsql', line: 20 },

  { runAt: '2026-05-31T02:12:00Z', jobName: 'kpi_rollup', reportId: 73, reportName: 'Executive KPIs', folderPath: '/Finance',
    targetTable: 'mart.ExecutiveKpis', targetColumn: 'revenue_yoy', operation: 'SELECT',
    transformationKind: 'Window', transformationExpression: 'LAG(total_revenue) OVER (ORDER BY period)', functionsApplied: ['LAG'],
    sourceTables: ['mart.SalesSummary'], sourceColumns: ['total_revenue'], derivedFromDescriptions: '',
    tags: { classification: 'internal', owner: 'exec' }, sourceFile: 'samples/integration/kpis.rptsql', line: 9 },

  { runAt: '2026-05-30T23:50:00Z', jobName: 'load_sales_csv', reportId: null, reportName: null, folderPath: null,
    targetTable: 'edw.Sales', targetColumn: 'Amount', operation: 'BULK INSERT',
    transformationKind: 'Direct', transformationExpression: 'amount', functionsApplied: [],
    sourceTables: [], sourceColumns: ['amount'], derivedFromDescriptions: 'Raw amount from the daily CSV drop',
    tags: { pii: 'false' }, sourceFile: 'C:/data/staging/sales_2026_05.csv', line: 1 },
].map((row, index) => ({ id: index + 1, scriptPath: row.sourceFile, ...row }));

// Mock of catalogApi.lineage(kind, args) — same contract as js/api.js.
async function lineage(kind, args = {}) {
  const name = (args.name || '').toLowerCase();
  let rows;
  if (kind === 'table') {
    rows = DB.filter(r => (r.targetTable || '').toLowerCase().includes(name));
    if (args.column) rows = rows.filter(r => (r.targetColumn || '').toLowerCase().includes(args.column.toLowerCase()));
  } else if (kind === 'source') {
    rows = DB.filter(r => (r.sourceTables || []).some(s => s.toLowerCase().includes(name)));
  } else if (kind === 'source-file') {
    const path = (args.path || '').toLowerCase();
    rows = DB.filter(r => (r.sourceFile || '').toLowerCase().includes(path));
  } else if (kind === 'tag') {
    rows = DB.filter(r => r.tags && Object.prototype.hasOwnProperty.call(r.tags, args.key) && (!args.value || r.tags[args.key] === args.value));
  } else if (kind === 'job') {
    rows = DB.filter(r => (r.jobName || '').toLowerCase().includes(name));
  } else {
    rows = [];
  }
  if (args.from) rows = rows.filter(row => row.runAt >= args.from);
  if (args.to) rows = rows.filter(row => row.runAt <= args.to);
  return rows.slice(0, args.limit || 100);
}

async function impact(args = {}) {
  const target = args.name || 'edw.Sales';
  return {
    request: {
      kind: args.kind || 'table',
      name: target,
      column: args.column || null,
      direction: args.direction || 'downstream',
      depth: args.depth || 4,
      limit: args.limit || 100,
    },
    summary: {
      tables: 3,
      columns: 2,
      reports: 2,
      datasets: 1,
      subscriptions: 1,
      jobs: 2,
      stewards: 2,
      alerts: 1,
    },
    tables: [
      { type: 'Table', name: target, detail: null, lastSeen: '2026-05-31T02:10:00Z', count: 1 },
      { type: 'Table', name: 'mart.SalesSummary', detail: null, lastSeen: '2026-05-31T02:10:03Z', count: 4 },
      { type: 'Table', name: 'mart.ExecutiveKpis', detail: null, lastSeen: '2026-05-31T02:12:00Z', count: 1 },
    ],
    columns: [
      { type: 'Column', name: 'mart.SalesSummary.total_revenue', detail: null, lastSeen: null, count: null },
      { type: 'Column', name: 'mart.ExecutiveKpis.revenue_yoy', detail: null, lastSeen: null, count: null },
    ],
    reports: [
      { type: 'Report', name: 'Executive Sales', detail: '/Finance', lastSeen: '2026-05-31T02:10:03Z', count: null },
      { type: 'Report', name: 'Executive KPIs', detail: '/Finance', lastSeen: '2026-05-31T02:12:00Z', count: null },
    ],
    datasets: [
      { type: 'Dataset', name: '&sales_snapshot', detail: '/Finance', lastSeen: '2026-05-31T02:15:00Z', count: null },
    ],
    subscriptions: [
      { type: 'Subscription', name: 'Subscription #19', detail: 'Executive KPIs', lastSeen: '2026-05-31T08:00:00Z', count: null },
    ],
    jobs: [
      { type: 'Job', name: 'nightly_sales_refresh', detail: 'samples/integration/sales.rptsql', lastSeen: '2026-05-31T02:10:03Z', count: null },
      { type: 'Job', name: 'kpi_rollup', detail: 'samples/integration/kpis.rptsql', lastSeen: '2026-05-31T02:12:00Z', count: null },
    ],
    stewards: [
      { type: 'Owner', name: 'finance', detail: null, lastSeen: null, count: null },
      { type: 'Steward', name: 'Maria Chen', detail: null, lastSeen: null, count: null },
    ],
    alerts: [{ type: 'Alert', name: 'Protected data alert', detail: 'Executive Sales', lastSeen: null, count: 0 }],
  };
}

async function stewardship(args = {}) {
  const staleAfterMs = (args.staleAfterDays || 30) * 86400000;
  const now = Date.now();
  const items = DB.map(row => {
    const tags = row.tags || {};
    const missingTags = ['owner', 'steward', 'contact', 'classification', 'quality']
      .filter(tag => !tags[tag]);
    const classification = tags.classification || null;
    const isRestricted = classification === 'restricted';
    const isSensitive = isRestricted || classification === 'confidential' || tags.pii === 'true' || tags.phi === 'true' || tags.pci === 'true' || tags.sensitive === 'true';
    const isStale = now - new Date(row.runAt).getTime() > staleAfterMs;
    return {
      targetTable: row.targetTable,
      targetColumn: row.targetColumn,
      runAt: row.runAt,
      jobName: row.jobName,
      scriptPath: row.sourceFile,
      sourceTables: row.sourceTables,
      tags,
      missingTags,
      isSensitive,
      isRestricted,
      isStale,
      staleReason: isStale ? 'Outside selected stale window' : 'Fresh',
      owner: tags.owner || null,
      steward: tags.steward || 'Maria Chen',
      contact: tags.contact || null,
      domain: tags.domain || 'finance',
      classification,
      quality: tags.quality || null,
      freshness: tags.freshness || null,
    };
  });
  let filtered = items;
  if (args.view === 'sensitive') filtered = filtered.filter(i => i.isSensitive || i.isRestricted);
  if (args.view === 'missing') filtered = filtered.filter(i => i.missingTags.length);
  if (args.view === 'stale') filtered = filtered.filter(i => i.isStale);
  if (args.view === 'queue') filtered = filtered.filter(i => i.steward && (i.missingTags.length || i.isStale || i.isSensitive));
  if (args.q) filtered = filtered.filter(i => JSON.stringify(i).toLowerCase().includes(args.q.toLowerCase()));
  if (args.steward) filtered = filtered.filter(i => i.steward === args.steward);
  if (args.domain) filtered = filtered.filter(i => i.domain === args.domain);
  const facet = values => [...values.reduce((m, v) => v ? m.set(v, (m.get(v) || 0) + 1) : m, new Map())]
    .map(([value, count]) => ({ value, count }));
  return {
    summary: {
      totalAssets: items.length,
      sensitiveAssets: items.filter(i => i.isSensitive || i.isRestricted).length,
      missingMetadataAssets: items.filter(i => i.missingTags.length).length,
      staleAssets: items.filter(i => i.isStale).length,
      stewardQueueAssets: items.filter(i => i.steward && (i.missingTags.length || i.isStale || i.isSensitive)).length,
    },
    stewards: facet(items.map(i => i.steward)),
    domains: facet(items.map(i => i.domain)),
    classifications: facet(items.map(i => i.classification)),
    qualities: facet(items.map(i => i.quality)),
    items: filtered.slice(0, args.limit || 100),
  };
}

async function protectedData() {
  return DB.filter(row => row.tags?.pii === 'true' || row.tags?.classification === 'confidential')
    .map(row => ({
      id: row.id, runAt: row.runAt, jobName: row.jobName, scriptPath: row.scriptPath,
      targetTable: row.targetTable, targetColumn: row.targetColumn, sourceTables: row.sourceTables,
      operation: row.operation, tags: row.tags, sourceFile: row.sourceFile, line: row.line,
      protectionTags: row.tags?.pii === 'true' ? ['@pii=true', '@classification=confidential'] : ['@classification=confidential'],
      protectionReason: row.tags?.pii === 'true' ? '@pii=true, @classification=confidential' : '@classification=confidential',
      owner: row.tags?.owner || null,
      steward: row.tags?.steward || 'Maria Chen',
      contact: row.tags?.contact || null,
      domain: row.tags?.domain || 'finance',
      classification: row.tags?.classification || null,
      quality: row.tags?.quality || null,
    }));
}

async function auditLog(page = 1, pageSize = 50) {
  return {
    items: [
      { id: 10, userId: 1, username: 'publisher', action: 'STEWARD_LINEAGE_IMPACT', resourceType: 'Steward', resourceId: 'Maria Chen', timestamp: '2026-05-31T02:16:00Z', detail: 'Executive Sales touched mart.SalesSummary.customer_email', ipAddress: null, userAgent: null },
      { id: 11, userId: 2, username: 'scheduler', action: 'STEWARD_LINEAGE_IMPACT', resourceType: 'Steward', resourceId: 'FinanceOps', timestamp: '2026-05-31T08:00:00Z', detail: 'Daily refresh affected 2 protected targets', ipAddress: null, userAgent: null },
    ],
    total: 2,
    page,
    pageSize,
  };
}

async function operationalMetrics() {
  return {
    queuedExecutions: 0, activeExecutions: 1, executionCap: 8, windowHours: 24,
    recentExecutionFailures: 0, recentExecutions: 1, staleDatasets: 0, datasetStorageBytes: 0,
    securityEventPending: 0, securityEventFailed: 0, nodeId: 'sandbox', schemaUpToDate: true,
    auditOutboxPending: 3,
    auditOutboxFailed: 0,
    auditOutboxOldestPendingAgeSeconds: 42,
  };
}

async function protectedDataSuggestions() {
  const row = DB[2];
  return [{ id: row.id, runAt: row.runAt, jobName: row.jobName, scriptPath: row.scriptPath,
    targetTable: row.targetTable, targetColumn: row.targetColumn, sourceTables: row.sourceTables,
    sourceColumns: row.sourceColumns, suggestedTag: 'pii', suggestedValue: 'true', confidence: 0.95,
    evidenceKind: 'TargetColumn', evidence: 'customer_email', reason: 'email identifier',
    existingTags: row.tags, sourceFile: row.sourceFile, line: row.line }];
}

export function createLineageMock() {
  let mode = 'healthy', pauseKind = null, held = null;
  let started;
  const calls = [];
  const api = { calls, graphs: [], setMode(value) { mode = value; },
    pause(kind) { pauseKind = kind; started = Promise.withResolvers(); },
    whenPaused() { return started.promise; },
    async release() { if (!held) throw new Error('No held lineage response.'); const pending = held; held = null; pending.resolve(); await pending.finished.promise; }
  };
  for (const [kind, action] of Object.entries({ lineage, impact, stewardship, protectedData, protectedDataSuggestions, auditLog, operationalMetrics })) {
    api[kind] = async (...args) => {
      calls.push({ kind, args });
      let value = await action(...args);
      if (mode === 'unavailable' && ['protectedData', 'protectedDataSuggestions', 'operationalMetrics'].includes(kind)) throw new Error(`${kind} temporarily unavailable.`);
      if (mode === 'malformed') value = kind === 'lineage' ? null : {};
      if (mode === 'wrong-impact' && kind === 'impact') value.request.name = 'other-target';
      if (mode === 'graph-names' && kind === 'lineage') value = value.map((row, index) => ({ ...row,
        targetTable: ['__proto__', 'constructor', 'b->constructor', 'a'][index],
        sourceTables: [['constructor'], ['a->b'], ['a'], []][index] }));
      if (pauseKind === kind) {
        pauseKind = null;
        const pending = Promise.withResolvers(), finished = Promise.withResolvers();
        held = { resolve: pending.resolve, finished }; started.resolve();
        await pending.promise; finished.resolve();
      }
      return structuredClone(value);
    };
  }
  return api;
}

function formatBuiltAt(value) {
  return value ? new Date(value).toLocaleString() : 'Never run';
}
function timeAgo(value) {
  if (!value) return 'Never run';
  const ms = Date.now() - new Date(value).getTime();
  if (!Number.isFinite(ms) || ms < 0) return formatBuiltAt(value);
  const minutes = Math.floor(ms / 60000);
  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hr ago`;
  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? '' : 's'} ago`;
}

export default {
  id: 'lineage-catalog',
  title: 'Lineage explorer',
  subtitle: 'sidebar "Lineage" view',
  fixtures: [
    { id: 'table', label: 'Target-table query (results)' },
    { id: 'graph', label: 'Results as graph' },
    { id: 'tag',   label: 'Tag query (pii)' },
    { id: 'impact', label: 'Impact analysis' },
    { id: 'audit', label: 'Steward audit workflow' },
    { id: 'protected-unavailable', label: 'Unavailable protected sources' },
    { id: 'reader', label: 'Reader modes without audit' },
    { id: 'blank', label: 'Initial state (before search)' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const [catMod, lineageUi, designer] = await Promise.all([
      importFresh(LINEAGE_CATALOG_JS),
      importFresh(LINEAGE_UI_JS),
      importFresh(DESIGNER_JS),
    ]);

    const api = createLineageMock();
    const catalog = catMod.createLineageCatalog({
      host: stage,
      catalogApi: api,
      adminApi: api,
      renderDag: (container, graph) => { api.graphs.push(structuredClone(graph)); return designer.renderDag(container, graph); },
      renderLineageRow: lineageUi.renderLineageRow,
      lineageRowsToCsv: lineageUi.lineageRowsToCsv,
      openReport: id => ctx.stat(`openReport(${id}) — would open that report in the portal`),
      timeAgo,
      formatBuiltAt,
      viewKey: 'etlsql_sandbox_lineage_views',  // keep sandbox saved-views separate
      allowAudit: fixtureId !== 'reader',
      prepare: () => {},
    });

    // Pre-seed a query so the explorer shows populated results instead of the
    // blank "pick a query and Search" state. (Edit the form and Search to explore.)
    if (fixtureId === 'protected-unavailable') {
      api.setMode('unavailable'); catalog.state.mode = 'protected';
    } else if (fixtureId === 'impact') {
      catalog.state.mode = 'impact';
      catalog.state.impactKind = 'table';
      catalog.state.impactName = 'edw.Sales';
      catalog.state.impactDirection = 'downstream';
    } else if (fixtureId === 'audit') {
      catalog.state.mode = 'audit';
      catalog.state.stewardshipQuery = '';
    } else if (fixtureId === 'tag') {
      catalog.state.kind = 'tag';
      catalog.state.query = 'pii';
    } else if (fixtureId !== 'blank') {
      catalog.state.kind = 'table';
      catalog.state.query = 'mart.SalesSummary';
    }
    if (fixtureId === 'graph') catalog.state.view = 'graph';

    window.__lineage = { catalog, api, host: stage };
    catalog.render();
    ctx.stat(fixtureId === 'blank'
      ? 'the view as it first appears — choose a query type, type a name, and Search'
      : fixtureId === 'impact'
        ? 'pre-seeded impact analysis · change the target/direction and Analyze'
      : fixtureId === 'audit'
        ? 'combined steward audit workflow · protected data, queues, impact, and audit delivery'
      : 'pre-seeded query · change the kind/name and Search, or toggle Table/Graph');

    return { dispose() { catalog.dispose(); }, resize() {} };
  },
};
