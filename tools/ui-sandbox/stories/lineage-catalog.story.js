// Story for the portal's sidebar "Lineage" view (the catalog-wide lineage explorer).
// Imports the canonical createLineageCatalog module and drives it with a mock
// catalogApi so you can see how the query form, table/graph toggle, tags, and CSV
// export behave — without the portal, a catalog DB, or any run history.

import { importFresh, DESIGNER_JS } from '../util.js';

const LINEAGE_CATALOG_JS = '/src/ETL-SQL.Portal/wwwroot/js/lineage-catalog.js';
const LINEAGE_UI_JS = '/src/ETL-SQL.Portal/wwwroot/js/lineage-ui.js';

const sleep = ms => new Promise(r => setTimeout(r, ms));

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
];

// Mock of catalogApi.lineage(kind, args) — same contract as js/api.js.
async function lineage(kind, args = {}) {
  await sleep(250);
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
  return rows;
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
    { id: 'blank', label: 'Initial state (before search)' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const [catMod, lineageUi, designer] = await Promise.all([
      importFresh(LINEAGE_CATALOG_JS),
      importFresh(LINEAGE_UI_JS),
      importFresh(DESIGNER_JS),
    ]);

    const catalog = catMod.createLineageCatalog({
      host: stage,
      catalogApi: { lineage },
      renderDag: designer.renderDag,
      renderLineageRow: lineageUi.renderLineageRow,
      lineageRowsToCsv: lineageUi.lineageRowsToCsv,
      openReport: id => ctx.stat(`openReport(${id}) — would open that report in the portal`),
      timeAgo,
      formatBuiltAt,
      viewKey: 'etlsql_sandbox_lineage_views',  // keep sandbox saved-views separate
      prepare: () => {},
    });

    // Pre-seed a query so the explorer shows populated results instead of the
    // blank "pick a query and Search" state. (Edit the form and Search to explore.)
    if (fixtureId === 'tag') {
      catalog.state.kind = 'tag';
      catalog.state.query = 'pii';
    } else if (fixtureId !== 'blank') {
      catalog.state.kind = 'table';
      catalog.state.query = 'mart.SalesSummary';
    }
    if (fixtureId === 'graph') catalog.state.view = 'graph';

    catalog.render();
    ctx.stat(fixtureId === 'blank'
      ? 'the view as it first appears — choose a query type, type a name, and Search'
      : 'pre-seeded query · change the kind/name and Search, or toggle Table/Graph');

    return { dispose() { catalog.dispose(); }, resize() {} };
  },
};
