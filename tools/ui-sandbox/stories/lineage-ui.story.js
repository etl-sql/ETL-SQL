import { renderDependencies, renderLineageRow, lineageRowsToCsv } from '../../../src/ETL-SQL.Portal/wwwroot/js/lineage-ui.js';

const rows = [
  {
    runAt: '2026-05-30T14:15:00Z',
    jobName: 'nightly_sales_refresh',
    reportId: 42,
    reportName: 'Executive Sales',
    folderPath: '/Finance',
    targetTable: 'mart.SalesSummary',
    targetColumn: 'total_revenue',
    operation: 'SELECT',
    transformationKind: 'Aggregation',
    transformationExpression: 'SUM(Amount)',
    functionsApplied: ['SUM'],
    sourceTables: ['edw.Sales'],
    sourceColumns: ['Amount'],
    derivedFromDescriptions: 'Sales amount from catalog',
    tags: { pii: 'true', classification: 'confidential', owner: 'finance' },
    sourceFile: 'samples/integration/sales.rptsql',
    line: 18,
  },
];

const dependencies = {
  report: { name: 'Executive Sales', folderPath: '/Finance' },
  snapshot: { builtAt: '2026-05-30T14:15:00Z' },
  manifestDatasets: [{ tempTableName: '#sales', rowCount: 1280, refreshInterval: 'Manual', ttl: 'None' }],
  registeredDatasets: [{ name: '&sales_snap', folderPath: '/Shared', accessLevel: 'Read', rowCount: 1280, sources: [{ name: 'edw.Sales' }] }],
  refreshJobs: [{ orchestratorJobName: 'nightly_sales_refresh', refreshInterval: 'Daily', lastRefreshedAt: '2026-05-30T14:15:00Z' }],
  sources: [{ connection: 'warehouse', objectName: 'edw.Sales', kind: 'TABLE' }],
  lineageEntries: [{
    target: 'mart.SalesSummary',
    targetColumn: 'YAXIS',
    operation: 'SELECT',
    transformationKind: 'Aggregation',
    transformationExpression: 'SUM(Amount)',
    functionsApplied: ['SUM'],
    sources: ['edw.Sales'],
    sourceColumns: ['Amount'],
    derivedFromDescriptions: 'Sales amount from catalog',
    tags: { pii: 'true', classification: 'confidential', owner: 'finance' },
  }],
};

const downstream = [
  { reportId: 73, reportName: 'Revenue QA', folderPath: '/Audit', runCount: 3, lastSeen: '2026-05-31T09:00:00Z' },
];

function fmt(value) {
  return value ? new Date(value).toLocaleString() : 'Never';
}

export default {
  id: 'lineage-ui',
  title: 'Lineage UI',
  subtitle: 'catalog rows + dependencies',
  fixtures: [
    { id: 'row', label: 'Catalog result row' },
    { id: 'dependencies', label: 'Dependencies modal body' },
    { id: 'csv', label: 'CSV export' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    if (fixtureId === 'dependencies') {
      stage.innerHTML = `<div class="modal-body">${renderDependencies(dependencies, downstream, { formatBuiltAt: fmt })}</div>`;
      ctx.stat('dependency sections + lineage tags');
    } else if (fixtureId === 'csv') {
      const pre = document.createElement('pre');
      pre.className = 'sandbox-err';
      pre.textContent = lineageRowsToCsv(rows);
      stage.replaceChildren(pre);
      ctx.stat('lineageRowsToCsv()');
    } else {
      stage.innerHTML = `<div class="lineage-result-list">${rows.map(row => renderLineageRow(row, { timeAgo: fmt, formatBuiltAt: fmt })).join('')}</div>`;
      ctx.stat('renderLineageRow()');
    }
    return { dispose() {}, resize() {} };
  },
};
