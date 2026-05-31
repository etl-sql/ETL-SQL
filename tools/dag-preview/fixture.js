/*
 * DAG preview fixtures — NOT shipped, dev-only test data.
 *
 * Produces { nodes, edges } in the exact shape the portal's
 * GET /api/reports/{id}/structure endpoint returns, so renderDag() in the
 * canonical designer.js can be exercised at realistic scale without Docker,
 * the portal, or a catalog database.
 *
 * Node shape:  { id, label, type, meta }
 *   type: 'page' | 'visual' | 'dataset' | 'table' | 'column'
 *   dataset/table meta: { columns:[...], colEdges:[{tgtCol,srcTable,srcCol}] }
 *   visual meta:        { page, visualType }
 * Edge shape:  { source, target, label }
 */

// ── Real names lifted from samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql ──
const PAGES = [
  'Overview', 'Trends', 'Composition', 'Controls', 'Detail',
  'FeatureLab', 'BusinessSignals', 'MapCharts', 'FlowPlanning', 'Drilldown',
];

const VISUALS = [
  ['KpiRevenue', 'CARD'], ['KpiQty', 'CARD'], ['KpiDiscount', 'CARD'],
  ['KpiReturns', 'CARD'], ['KpiAvgTxn', 'CARD'], ['RegionSlicer', 'SLICER'],
  ['DrillRegionDetail', 'BAR'], ['BarByRegion', 'BAR'], ['LineTrend', 'LINE'],
  ['DonutCat', 'DONUT'], ['TxnTable', 'TABLE'], ['ComboRevenueReturns', 'COMBO'],
  ['WaterfallDelta', 'WATERFALL'], ['ScatterQtyRev', 'SCATTER'], ['HBarRep', 'HBAR'],
  ['BoxRevDist', 'BOXPLOT'], ['TreemapCat', 'TREEMAP'], ['HeatmapRegCat', 'HEATMAP'],
  ['FunnelPipeline', 'FUNNEL'], ['GaugeTarget', 'GAUGE'], ['GaugeProgress', 'GAUGE'],
  ['PieUnits', 'PIE'], ['CategorySlicer', 'SLICER'], ['RegionMulti', 'MULTISELECT'],
  ['DateStart', 'DATEPICKER'], ['DateEnd', 'DATEPICKER'], ['StartPicker', 'RELDATEPICKER'],
  ['EndPicker', 'RELDATEPICKER'], ['QtySlider', 'SLIDER'], ['TxnSearch', 'SEARCH'],
  ['IncludeReturnsToggle', 'CHECKBOX'], ['AnalystNoteBox', 'TEXTBOX'],
  ['RevenueFloorBox', 'NUMBERBOX'], ['BrandImage', 'IMAGE'], ['ControlsExplainer', 'TEXT'],
  ['ButtonsText', 'TEXT'], ['FeatureLabText', 'TEXT'], ['SankeyFlow', 'SANKEY'],
  ['SunburstRevenue', 'SUNBURST'], ['NetworkSales', 'NETWORK'], ['TrellisRevRegion', 'TRELLIS'],
  ['MatrixRevenue', 'MATRIX'], ['GanttMilestones', 'GANTT'], ['ScatterBrush', 'SCATTER'],
  ['BubbleSalesPerson', 'BUBBLE'], ['RadarRegions', 'RADAR'], ['CandlestickDemo', 'CANDLESTICK'],
  ['StateRevenueMap', 'MAP'], ['WorldRevenueMap', 'MAP'], ['MonthlyTable', 'TABLE'],
  ['AuditLogTable', 'TABLE'], ['RichCellTable', 'TABLE'], ['DeferredTxnPreview', 'TABLE'],
  ['BarMonthlyByRegion', 'BAR'], ['DrillInExplainer', 'TEXT'], ['DrillInByCategory', 'BAR'],
];

// ── Synthetic data layer (datasets + temp/source tables) ────────────────────────
// label, type, [source table labels], edgeLabel
const TABLES = [
  // raw sources (no upstream)
  ['RawSales', 'table', []], ['RawReturns', 'table', []], ['RawDiscounts', 'table', []],
  ['RawTargets', 'table', []], ['RawPipeline', 'table', []], ['RawInventory', 'table', []],
  ['RawShipments', 'table', []], ['RawMilestones', 'table', []],
  // dimensions (hub nodes — fan out widely, good for focus mode)
  ['DimRegion', 'table', []], ['DimCategory', 'table', []], ['DimDate', 'table', []],
  ['DimRep', 'table', []], ['DimProduct', 'table', []], ['DimChannel', 'table', []],
  // staging
  ['StgSales', 'table', ['RawSales', 'DimDate'], 'SELECT'],
  ['StgReturns', 'table', ['RawReturns', 'DimDate'], 'SELECT'],
  ['StgDiscounts', 'table', ['RawDiscounts'], 'SELECT'],
  ['StgInventory', 'table', ['RawInventory', 'DimProduct'], 'SELECT'],
  ['StgPipeline', 'table', ['RawPipeline'], 'SELECT'],
  ['StgShipments', 'table', ['RawShipments', 'DimRegion'], 'SELECT'],
  // enriched
  ['EnrichedSales', 'table', ['StgSales', 'DimRegion', 'DimCategory', 'DimRep', 'DimChannel'], 'SELECT'],
  ['EnrichedReturns', 'table', ['StgReturns', 'DimRegion', 'DimCategory'], 'SELECT'],
  ['EnrichedInventory', 'table', ['StgInventory', 'DimRegion'], 'SELECT'],
  // facts
  ['FactTransactions', 'table', ['EnrichedSales', 'StgDiscounts'], 'SELECT'],
  ['FactReturns', 'table', ['EnrichedReturns'], 'SELECT'],
  ['FactInventory', 'table', ['EnrichedInventory', 'StgShipments'], 'SELECT'],
  // rollups (GROUP BY)
  ['RegionRollup', 'table', ['FactTransactions', 'DimRegion'], 'GROUP BY'],
  ['CategoryRollup', 'table', ['FactTransactions', 'DimCategory'], 'GROUP BY'],
  ['MonthlyRollup', 'table', ['FactTransactions', 'DimDate'], 'GROUP BY'],
  ['RepRollup', 'table', ['FactTransactions', 'DimRep'], 'GROUP BY'],
  ['ChannelRollup', 'table', ['FactTransactions', 'DimChannel'], 'GROUP BY'],
  // side tables
  ['TargetPlan', 'table', ['RawTargets'], 'SELECT'],
  ['PipelineStages', 'table', ['StgPipeline'], 'SELECT'],
  ['MilestonePlan', 'table', ['RawMilestones'], 'SELECT'],
  ['GeoMap', 'table', ['DimRegion'], 'SELECT'],
  ['AuditLog', 'table', ['FactTransactions'], 'SELECT'],
  // datasets (the published query roots)
  ['SalesDS', 'dataset', ['RegionRollup', 'CategoryRollup', 'MonthlyRollup', 'RepRollup', 'ChannelRollup'], 'GROUP BY'],
  ['InventoryDS', 'dataset', ['FactInventory', 'MilestonePlan'], 'GROUP BY'],
];

// Sources a visual can bind to, cycled to spread the report half across the data half.
const VISUAL_SOURCES = [
  'SalesDS', 'SalesDS', 'SalesDS', 'RegionRollup', 'CategoryRollup', 'MonthlyRollup',
  'RepRollup', 'ChannelRollup', 'InventoryDS', 'FactTransactions', 'GeoMap', 'TargetPlan',
  'PipelineStages', 'MilestonePlan', 'AuditLog', 'DimRegion', 'DimCategory', 'DimDate',
];

const COLUMN_POOL = ['Region', 'Category', 'Month', 'Rep', 'Channel', 'Revenue', 'Qty', 'Returns', 'Discount', 'Margin'];

function colsFor(label, n) {
  // deterministic pseudo-random column pick based on label
  let h = 0;
  for (const c of label) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  const out = [];
  for (let i = 0; i < n; i++) out.push(COLUMN_POOL[(h + i * 3) % COLUMN_POOL.length]);
  return [...new Set(out)];
}

// Structured field mappings per visual, mirroring the server's visual.Mappings
// (role + column, where column may carry an aggregation expression).
function mappingsFor(label, type) {
  const c = colsFor(label, 4);
  if (['SLICER', 'MULTISELECT', 'SEARCH', 'CHECKBOX', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER'].includes(type))
    return [{ role: 'FILTER', column: c[0] }];
  if (['CARD', 'GAUGE', 'NUMBERBOX'].includes(type))
    return [{ role: 'VALUES', column: `SUM(${c[0]})` }];
  if (['TABLE', 'MATRIX'].includes(type))
    return c.map(col => ({ role: 'COLUMN', column: col }));
  if (['TEXT', 'TEXTBOX', 'IMAGE'].includes(type))
    return [];
  return [
    { role: 'XAXIS',  column: c[0] },
    { role: 'YAXIS',  column: `SUM(${c[1]})` },
    { role: 'VALUES', column: `COUNT(${c[2]})` },
  ];
}

// Compact edge label derived from the X/Y mappings (matches the server).
function axisLabel(mappings) {
  const x = mappings.find(m => m.role === 'XAXIS')?.column;
  const y = mappings.find(m => m.role === 'YAXIS')?.column;
  const parts = [];
  if (x) parts.push(`X: ${x}`);
  if (y) parts.push(`Y: ${y}`);
  return parts.length ? parts.join(' · ') : null;
}

/** Full Kitchen-Sink-scale graph (~106 nodes, ~150 edges before column expansion). */
export function buildKitchenSinkGraph() {
  const nodes = [];
  const edges = [];
  const id = (label, type) => (type === 'dataset' ? `ds:${label}` : type === 'page' ? `page:${label}` : type === 'visual' ? `vis:${label}` : `table:${label}`);

  // 1. data layer nodes + producer edges + column lineage
  for (const [label, type, srcs, lbl] of TABLES) {
    const columns = colsFor(label, 5);
    const colEdges = (srcs ?? []).length
      ? columns.slice(0, 3).map((c, i) => ({ tgtCol: c, srcTable: srcs[i % srcs.length], srcCol: c }))
      : [];
    nodes.push({ id: id(label, type), label, type, meta: { columns, colEdges } });
    for (const s of (srcs ?? [])) edges.push({ source: id(s, 'table'), target: id(label, type), label: lbl });
  }
  // fix dataset source ids (sources are tables, target may be a dataset — source id is table:*)
  // (already correct: id(s,'table') => table:*, which matches the table nodes above)

  // 2. pages + visuals, distributed ~evenly across pages
  const perPage = Math.ceil(VISUALS.length / PAGES.length);
  PAGES.forEach((p) => nodes.push({ id: id(p, 'page'), label: p, type: 'page', meta: null }));

  VISUALS.forEach(([name, type], i) => {
    const page = PAGES[Math.floor(i / perPage)] ?? PAGES[PAGES.length - 1];
    const src = VISUAL_SOURCES[i % VISUAL_SOURCES.length];
    const srcType = src.endsWith('DS') ? 'dataset' : 'table';
    const mappings = mappingsFor(name, type);
    nodes.push({ id: id(name, 'visual'), label: `${type} · ${name}`, type: 'visual', meta: { page, visualType: type, mappings } });
    edges.push({ source: id(page, 'page'), target: id(name, 'visual'), label: null });
    edges.push({ source: id(src, srcType), target: id(name, 'visual'), label: axisLabel(mappings) });
  });

  return { nodes, edges };
}

/** A small single-page report, for before/after comparison. */
export function buildSmallGraph() {
  const nodes = [
    { id: 'table:RawSales', label: 'RawSales', type: 'table', meta: { columns: ['Region', 'Revenue', 'Qty'], colEdges: [] } },
    { id: 'ds:SalesDS', label: 'SalesDS', type: 'dataset', meta: { columns: ['Region', 'Revenue'], colEdges: [{ tgtCol: 'Revenue', srcTable: 'RawSales', srcCol: 'Revenue' }] } },
    { id: 'page:Main', label: 'Main', type: 'page', meta: null },
    { id: 'vis:Bar', label: 'BAR · RevByRegion', type: 'visual', meta: { page: 'Main', visualType: 'BAR', mappings: [{ role: 'XAXIS', column: 'Region' }, { role: 'YAXIS', column: 'SUM(Revenue)' }] } },
    { id: 'vis:Card', label: 'CARD · TotalRev', type: 'visual', meta: { page: 'Main', visualType: 'CARD', mappings: [{ role: 'VALUES', column: 'SUM(Revenue)' }] } },
  ];
  const edges = [
    { source: 'table:RawSales', target: 'ds:SalesDS', label: 'GROUP BY' },
    { source: 'page:Main', target: 'vis:Bar', label: null },
    { source: 'page:Main', target: 'vis:Card', label: null },
    { source: 'ds:SalesDS', target: 'vis:Bar', label: 'x: Region · y: Revenue' },
    { source: 'ds:SalesDS', target: 'vis:Card', label: 'value: Revenue' },
  ];
  return { nodes, edges };
}
