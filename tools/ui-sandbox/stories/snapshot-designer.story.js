// Story: Snapshot-backed layout designer (createDesigner with .etlsnap package support).
// Allows the Report Designer to load and deserialize compiled .etlsnap snapshot packages
// so visuals render with historical data without touching live production databases.
import { importFresh, DESIGNER_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

function getSalesSnapshotPackage() {
  return {
    format: 'etl-sql.snapshot',
    version: 2,
    createdAt: '2026-07-20T12:00:00Z',
    reportName: 'Quarterly Sales & Revenue Snapshot',
    securityContext: {
      rlsApplied: true,
      userRole: 'Regional_Manager',
      rowsSampled: 1250,
      totalRows: 45000,
    },
    designState: {
      pages: [{
        id: 'p1', name: 'Executive Overview', mode: 'Dashboard',
        visuals: [
          { id: 'salesBar', type: 'BAR', title: 'Quarterly Revenue by Region', gridCol: 1, gridColSpan: 7, gridRow: 1, gridRowSpan: 6, dataset: 'sales', mappings: { X: 'Region', Y: 'Revenue', SERIES: 'Quarter' } },
          { id: 'kpiRev', type: 'CARD', title: 'Total Q3 Revenue', gridCol: 8, gridColSpan: 5, gridRow: 1, gridRowSpan: 3, dataset: 'kpi', mappings: { VALUE: 'TotalRevenue' } },
          { id: 'marginDonut', type: 'DONUT', title: 'Margin Distribution', gridCol: 8, gridColSpan: 5, gridRow: 4, gridRowSpan: 3, dataset: 'sales', mappings: { CATEGORY: 'Region', VALUE: 'Revenue' } },
          { id: 'detailTable', type: 'TABLE', title: 'Regional Performance Detail', gridCol: 1, gridColSpan: 12, gridRow: 7, gridRowSpan: 5, dataset: 'sales', mappings: {} },
        ],
      }],
      datasets: [{ name: 'sales', query: 'SELECT Region, Quarter, Revenue FROM edw.Sales' }],
    },
    sampleRows: {
      sales: [
        ['North America', 'Q1', 1450000],
        ['North America', 'Q2', 1620000],
        ['Europe', 'Q1', 980000],
        ['Europe', 'Q2', 1120000],
        ['Asia Pacific', 'Q1', 2100000],
        ['Asia Pacific', 'Q2', 2350000],
      ],
      kpi: [
        ['TotalRevenue', '$6,170,000'],
      ],
    },
  };
}

function getRlsSnapshotPackage() {
  const base = getSalesSnapshotPackage();
  base.reportName = 'RLS-Restricted Regional Snapshot';
  base.securityContext = {
    rlsApplied: true,
    userRole: 'EU_Sales_Rep',
    rowsSampled: 420,
    totalRows: 42000,
    redactedColumns: ['CustomerSSN', 'CostMargin'],
  };
  base.sampleRows.sales = base.sampleRows.sales.filter(r => r[0] === 'Europe');
  return base;
}

function getLargeSnapshotPackage() {
  const base = getSalesSnapshotPackage();
  base.reportName = 'Large Dataset Snapshot (50k rows sampled)';
  base.securityContext = {
    rlsApplied: false,
    rowsSampled: 2000,
    totalRows: 50000,
    isCapped: true,
    capLimit: 2000,
  };
  return base;
}

export default {
  id: 'snapshot-designer',
  title: 'Snapshot layout designer',
  subtitle: 'Snapshot-backed .etlsnap layout designing',
  fixtures: [
    { id: 'etlsnap-sales', label: 'Sales snapshot (.etlsnap)' },
    { id: 'etlsnap-rls',   label: 'RLS-Filtered snapshot' },
    { id: 'etlsnap-large', label: 'Large dataset (sampled)' },
  ],
  async mount(stage, fixtureId, ctx) {
    let pkg;
    if (fixtureId === 'etlsnap-rls') pkg = getRlsSnapshotPackage();
    else if (fixtureId === 'etlsnap-large') pkg = getLargeSnapshotPackage();
    else pkg = getSalesSnapshotPackage();

    stage.innerHTML = '';
    const wrapper = document.createElement('div');
    wrapper.style.display = 'flex';
    wrapper.style.flexDirection = 'column';
    wrapper.style.height = '100%';

    // Snapshot Info Banner
    const banner = document.createElement('div');
    banner.style.padding = '8px 16px';
    banner.style.background = '#1e293b';
    banner.style.color = '#f8fafc';
    banner.style.fontSize = '12px';
    banner.style.display = 'flex';
    banner.style.alignItems = 'center';
    banner.style.gap = '12px';
    banner.style.borderBottom = '1px solid #334155';

    const rlsChip = pkg.securityContext.rlsApplied
      ? `<span style="background:#065f46;color:#34d399;padding:2px 8px;border-radius:12px;font-weight:600">🔒 RLS Enforced (${pkg.securityContext.userRole})</span>`
      : `<span style="background:#1e3a8a;color:#93c5fd;padding:2px 8px;border-radius:12px;font-weight:600">🌐 Unfiltered</span>`;

    const capChip = pkg.securityContext.isCapped
      ? `<span style="background:#854d0e;color:#fde047;padding:2px 8px;border-radius:12px;font-weight:600">⚡ Capped at ${pkg.securityContext.capLimit} rows</span>`
      : '';

    banner.innerHTML = `
      <span style="font-weight:700;color:#38bdf8">📦 .etlsnap Mode:</span>
      <span><strong>${pkg.reportName}</strong></span>
      ${rlsChip}
      ${capChip}
      <span style="color:#94a3b8">Sampled: ${pkg.securityContext.rowsSampled} of ${pkg.securityContext.totalRows.toLocaleString()} rows</span>
      <span style="flex:1"></span>
      <button id="snap-reload-btn" class="btn btn-xs btn-primary">⚡ Reload Snapshot</button>
    `;
    wrapper.appendChild(banner);

    const designerHost = document.createElement('div');
    designerHost.style.flex = '1';
    designerHost.style.position = 'relative';
    wrapper.appendChild(designerHost);
    stage.appendChild(wrapper);

    const mod = await importFresh(DESIGNER_JS);
    const opts = {
      designState: pkg.designState,
      reportName: pkg.reportName,
      snapshotMode: true,
      snapshotPackage: pkg,
      authFetch: makeMockApi(pkg.designState),
      previewUrl: '/tools/ui-sandbox/designer-preview.html',
      onSaveScript: async (script) => ctx.stat(`Snapshot layout saved · ${script.length} chars`),
    };

    const inst = mod.createDesigner(designerHost, opts);

    banner.querySelector('#snap-reload-btn')?.addEventListener('click', () => {
      ctx.stat(`Snapshot ${pkg.reportName} reloaded cleanly.`);
    });

    ctx.stat(`Loaded .etlsnap package '${pkg.reportName}' · Live-like canvas backed by ${pkg.securityContext.rowsSampled} snapshot rows.`);
    return inst;
  },
};
