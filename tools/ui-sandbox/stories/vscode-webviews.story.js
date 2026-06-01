// VS Code webview stories — render the extension's three webviews (results panel,
// report preview, report designer) outside VS Code by shimming acquireVsCodeApi()
// and feeding each one realistic data over the same protocol the extension host uses.

function makeFrame(srcdoc) {
  const iframe = document.createElement('iframe');
  iframe.className = 'vscode-webview-frame';
  iframe.srcdoc = srcdoc;
  return iframe;
}

// Shim VS Code's acquireVsCodeApi(). postMessage() is also forwarded to the parent
// frame so the story can detect the webview's `{type:'ready'}` handshake and start
// streaming exactly when the React app's message listener is live.
function vscodeShim() {
  return `
    <script>
      window.acquireVsCodeApi = function() {
        return {
          postMessage: function(message) {
            try { window.parent.postMessage({ __fromWebview: message }, '*'); } catch (e) {}
            console.log('vscode.postMessage', message);
          },
          getState: function() { return {}; },
          setState: function() {}
        };
      };
    </script>`;
}

// ── Results panel ───────────────────────────────────────────────────────────
// A scripted run that mirrors src/etl-sql-vscode/ui/src/mock_protocol.ts: a
// multi-level pipeline (with a PARALLEL block), two result sets so the result
// navigation/compare UI appears, and a performance summary that feeds the footer.
const RESULTS_TRACE = [
  { type: 'message', text: 'Executing script: monthly_customer_etl.etlsql', level: 'sys' },

  { type: 'progress', data: [
    { id: '1', name: 'Create Connection [edw]', status: 'Running', rowsProcessed: 0, durationMs: 0, isParallelBlock: false, children: [] },
  ]},
  { type: 'progress', data: [
    { id: '1', name: 'Create Connection [edw]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] },
  ]},

  { type: 'message', text: 'Executing: SELECT * FROM edw.Customers TRANSFORM(...)', level: 'sys' },
  { type: 'progress', data: [
    { id: '1', name: 'Create Connection [edw]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] },
    { id: '2', name: 'Scan Customers', status: 'Running', rowsProcessed: 1200, durationMs: 22, isParallelBlock: false, children: [
      { id: '3', name: 'PARALLEL (4)', status: 'Running', rowsProcessed: 0, durationMs: 5, isParallelBlock: true, children: [
        { id: '4', name: 'Normalize Email', status: 'Completed', rowsProcessed: 300, durationMs: 3, isParallelBlock: false, children: [] },
        { id: '5', name: 'Validate Phone',  status: 'Running',   rowsProcessed: 150, durationMs: 5, isParallelBlock: false, children: [] },
        { id: '6', name: 'Lookup Region',   status: 'Waiting',   rowsProcessed: 0,   durationMs: 0, isParallelBlock: false, children: [] },
        { id: '7', name: 'Score Risk',      status: 'Waiting',   rowsProcessed: 0,   durationMs: 0, isParallelBlock: false, children: [] },
      ]},
    ]},
  ]},

  { type: 'message', text: 'Fetched 5,000 rows from edw.Customers', level: 'info' },
  { type: 'progress', data: [
    { id: '1', name: 'Create Connection [edw]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] },
    { id: '2', name: 'Scan Customers', status: 'Completed', rowsProcessed: 5000, durationMs: 88, isParallelBlock: false, children: [
      { id: '3', name: 'PARALLEL (4)', status: 'Completed', rowsProcessed: 5000, durationMs: 41, isParallelBlock: true, children: [
        { id: '4', name: 'Normalize Email', status: 'Completed', rowsProcessed: 1250, durationMs: 18, isParallelBlock: false, children: [] },
        { id: '5', name: 'Validate Phone',  status: 'Completed', rowsProcessed: 1250, durationMs: 22, isParallelBlock: false, children: [] },
        { id: '6', name: 'Lookup Region',   status: 'Completed', rowsProcessed: 1250, durationMs: 19, isParallelBlock: false, children: [] },
        { id: '7', name: 'Score Risk',      status: 'Completed', rowsProcessed: 1250, durationMs: 41, isParallelBlock: false, children: [] },
      ]},
    ]},
  ]},

  { type: 'results', columns: ['id', 'name', 'region', 'segment', 'lifetime_value'], rows: [
    { id: 1001, name: 'Acme Industrial',     region: 'North', segment: 'Enterprise', lifetime_value: 248500.75 },
    { id: 1002, name: 'Bluewave Logistics',  region: 'West',  segment: 'Mid-Market', lifetime_value: 184220.25 },
    { id: 1003, name: 'Cedar Foods',         region: 'South', segment: 'Enterprise', lifetime_value: 312045.00 },
    { id: 1004, name: 'Dynamo Retail',       region: 'East',  segment: 'SMB',        lifetime_value:  42890.40 },
    { id: 1005, name: 'Everest Health',      region: 'North', segment: 'Enterprise', lifetime_value: 276330.10 },
    { id: 1006, name: 'Fjord Maritime',      region: 'West',  segment: 'Mid-Market', lifetime_value:  98155.60 },
    { id: 1007, name: 'Granite Systems',     region: 'South', segment: 'SMB',        lifetime_value:  33470.00 },
    { id: 1008, name: 'Harbor Analytics',    region: 'East',  segment: 'Mid-Market', lifetime_value: 151980.85 },
  ]},

  { type: 'message', text: 'Executing: SELECT region, COUNT(*) AS orders, SUM(amount) AS revenue FROM edw.Orders GROUP BY region', level: 'sys' },
  { type: 'progress', data: [
    { id: '1', name: 'Create Connection [edw]', status: 'Completed', rowsProcessed: 0,     durationMs: 14,  isParallelBlock: false, children: [] },
    { id: '2', name: 'Scan Customers',          status: 'Completed', rowsProcessed: 5000,  durationMs: 88,  isParallelBlock: false, children: [] },
    { id: '8', name: 'Aggregate Orders',        status: 'Completed', rowsProcessed: 12450, durationMs: 112, isParallelBlock: false, children: [] },
  ]},

  { type: 'results', columns: ['region', 'orders', 'revenue'], rows: [
    { region: 'North', orders: 3204, revenue: 1284500.50 },
    { region: 'South', orders: 2890, revenue: 1102300.00 },
    { region: 'East',  orders: 3512, revenue:  944120.75 },
    { region: 'West',  orders: 2844, revenue:  812905.20 },
  ]},

  { type: 'performance', metrics: {
    executionMs: 220,
    rowsProcessed: 17450,
    memoryMb: 18.2,
    statements: [
      { type: 'CONN',   totalMs: 14 },
      { type: 'SELECT', totalMs: 88 },
      { type: 'SELECT', totalMs: 112 },
    ],
  }},
];

function renderResults(stage, ctx) {
  let timer = null;
  let started = false;
  let onParentMessage = null;
  const iframe = makeFrame('');  // srcdoc set below once html is fetched

  fetch('/src/etl-sql-vscode/ui/dist/index.html').then(r => r.text()).then(html => {
    iframe.srcdoc = html.replace('<head>', `<head>${vscodeShim()}<script>window.VIEW_TYPE='results';</script>`);
  });
  stage.replaceChildren(iframe);

  function startReplay() {
    if (started || !iframe.contentWindow) return;
    started = true;
    const post = m => iframe.contentWindow && iframe.contentWindow.postMessage(m, '*');
    post({ type: 'clear', resetHistory: true });
    post({ type: 'status', status: 'running' });
    let i = 0;
    timer = setInterval(() => {
      if (i < RESULTS_TRACE.length) { post(RESULTS_TRACE[i++]); }
      else { post({ type: 'done', exitCode: 0 }); clearInterval(timer); timer = null; }
    }, 300);
  }

  // Start streaming as soon as the webview signals it is ready (its useVsCodeApi
  // hook posts {type:'ready'} on mount); fall back to a timer if the handshake
  // is missed so the panel never sits empty.
  onParentMessage = (e) => {
    if (e.source === iframe.contentWindow && e.data && e.data.__fromWebview && e.data.__fromWebview.type === 'ready') {
      startReplay();
    }
  };
  window.addEventListener('message', onParentMessage);
  iframe.addEventListener('load', () => setTimeout(startReplay, 600), { once: true });

  ctx.stat('results panel · live mock run — pipeline + 2 result sets + performance');
  return {
    dispose() {
      if (timer) clearInterval(timer);
      if (onParentMessage) window.removeEventListener('message', onParentMessage);
      iframe.remove();
    },
    resize() {},
  };
}

// ── Report preview ──────────────────────────────────────────────────────────
// Renders the report-runtime exactly as reportPreviewPanel.ts does (manifest on
// window.__MANIFEST__). Snapshots are real sample reports that ship with data, so
// the visuals populate. The runtime is paged, so multi-page reports stay snappy.
const PREVIEW_SNAPSHOTS = {
  preview:         '/samples/golden_workflow/golden_workflow.snapshot.json',           // realistic multi-page dashboard
  'preview-sink':  '/samples/10_Kitchen_Sinks/report_kitchen_sink.snapshot.json',      // every visual type, 9 pages
};

async function renderPreview(stage, ctx, fixtureId) {
  const url = PREVIEW_SNAPSHOTS[fixtureId] || PREVIEW_SNAPSHOTS.preview;
  const manifest = await fetch(url).then(r => r.json());
  const manifestJson = JSON.stringify(manifest).replace(/</g, '\\u003c');
  const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="/src/etl-sql-vscode/media/report-runtime.css">
  ${vscodeShim()}
  <title>VS Code Report Preview</title>
</head>
<body class="vscode-theme">
  <div id="root"></div>
  <script>window.__MANIFEST__ = ${manifestJson};</script>
  <script src="/src/etl-sql-vscode/media/echarts.min.js"></script>
  <script src="/src/etl-sql-vscode/media/report-runtime.js"></script>
</body>
</html>`;
  const iframe = makeFrame(html);
  stage.replaceChildren(iframe);
  const pages = (manifest.pages || []).length;
  const visuals = (manifest.visuals || []).length;
  ctx.stat(`report preview · ${manifest.title || 'report'} — ${visuals} visuals, ${pages} page${pages === 1 ? '' : 's'}`);
  return { dispose() { iframe.remove(); }, resize() {} };
}

// ── Report designer ─────────────────────────────────────────────────────────
// A fuller report than a single chart: two pages and a spread of visual types so
// the canvas, page tabs, dataset list, and properties drawer all have something
// to show.
function sampleState() {
  return {
    pages: [
      {
        id: 'p1', name: 'Overview', mode: 'Dashboard',
        visuals: [
          { id: 'salesBar', name: 'Vendor Sales', type: 'BAR',   title: 'Vendor Sales by day', gridCol: 1, gridColSpan: 8, gridRow: 1, gridRowSpan: 6, dataset: 'sales',  mappings: { X: 'Date', Y: 'total', SERIES: 'Vendor' } },
          { id: 'kpiRev',   name: 'Revenue KPI',  type: 'CARD',  title: 'Total Revenue',       gridCol: 9, gridColSpan: 4, gridRow: 1, gridRowSpan: 3, dataset: 'sales',  mappings: { VALUE: 'total' } },
          { id: 'kpiOrders',name: 'Orders KPI',   type: 'CARD',  title: 'Orders',              gridCol: 9, gridColSpan: 4, gridRow: 4, gridRowSpan: 3, dataset: 'orders', mappings: { VALUE: 'orders' } },
          { id: 'trend',    name: 'Revenue Trend',type: 'LINE',  title: 'Revenue Trend',       gridCol: 1, gridColSpan: 8, gridRow: 7, gridRowSpan: 5, dataset: 'orders', mappings: { X: 'month', Y: 'revenue' } },
          { id: 'detail',   name: 'Order Detail', type: 'TABLE', title: 'Order Detail',        gridCol: 9, gridColSpan: 4, gridRow: 7, gridRowSpan: 5, dataset: 'orders', mappings: {} },
        ],
      },
      {
        id: 'p2', name: 'Regional', mode: 'Dashboard',
        visuals: [
          { id: 'regionSlicer', name: 'Region Slicer',   type: 'SLICER', title: 'Region', gridCol: 1, gridColSpan: 3, gridRow: 1, gridRowSpan: 2, dataset: 'orders', mappings: { VALUE: 'region' } },
          { id: 'regionPie',    name: 'Revenue by Region',type: 'PIE',    title: 'Revenue by Region', gridCol: 4, gridColSpan: 5, gridRow: 1, gridRowSpan: 6, dataset: 'orders', mappings: { CATEGORY: 'region', VALUE: 'revenue' } },
          { id: 'regionTable',  name: 'Region Breakdown', type: 'TABLE',  title: 'Region Breakdown',  gridCol: 9, gridColSpan: 4, gridRow: 1, gridRowSpan: 6, dataset: 'orders', mappings: {} },
        ],
      },
    ],
    datasets: [
      { name: 'sales',  query: 'SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales GROUP BY Date, Vendor' },
      { name: 'orders', query: 'SELECT region, month, COUNT(*) AS orders, SUM(amount) AS revenue FROM edw.Orders GROUP BY region, month' },
    ],
  };
}

function renderDesigner(stage, ctx) {
  const state = sampleState();
  const initJson = JSON.stringify({ reportName: 'Quarterly Sales Review', scriptText: '' }).replace(/</g, '\\u003c');
  const stateJson = JSON.stringify(state).replace(/</g, '\\u003c');
  const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="/src/etl-sql-vscode/media/designer/designer.css">
  ${vscodeShim()}
  <style>html,body,#designerRoot{height:100%;margin:0;overflow:hidden}</style>
  <title>VS Code Report Designer</title>
</head>
<body>
  <div id="designerRoot"></div>
  <script>
    window.__INIT__ = ${initJson};
    window.__SANDBOX_STATE__ = ${stateJson};
    window.__vscodeFetch = async function(url) {
      if (url.endsWith('/api/designer/parse')) {
        return new Response(JSON.stringify({ designState: window.__SANDBOX_STATE__ }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.endsWith('/api/designer/generate')) {
        return new Response(JSON.stringify({ script: "SET REPORT TITLE = 'Quarterly Sales Review';\\nCREATE PAGE Overview AS (...);" }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response('{}', { status: 404 });
    };
    window.__vscodeSave = async function(script) { console.log('vscode.save', script.length); };
  </script>
  <script src="/src/etl-sql-vscode/media/echarts.min.js"></script>
  <script type="module">
    import { createDesigner } from '/src/etl-sql-vscode/media/designer/designer.js';
    createDesigner(document.getElementById('designerRoot'), {
      designState: window.__SANDBOX_STATE__,
      reportName: window.__INIT__.reportName,
      apiBase: '',
      host: 'vscode',
      authFetch: window.__vscodeFetch,
      onSaveScript: window.__vscodeSave,
      onCancel: () => console.log('vscode.cancel')
    });
  </script>
</body>
</html>`;
  const iframe = makeFrame(html);
  stage.replaceChildren(iframe);
  ctx.stat('report designer · 2 pages, 8 visuals, 2 datasets + vscode API shim');
  return { dispose() { iframe.remove(); }, resize() {} };
}

export default {
  id: 'vscode-webviews',
  title: 'VS Code webviews',
  subtitle: 'shimmed acquireVsCodeApi()',
  fixtures: [
    { id: 'results',      label: 'Results panel (live run)' },
    { id: 'preview',      label: 'Report preview' },
    { id: 'preview-sink', label: 'Report preview · all visuals' },
    { id: 'designer',     label: 'Report designer' },
  ],
  async mount(stage, fixtureId, ctx) {
    if (fixtureId === 'preview' || fixtureId === 'preview-sink') return renderPreview(stage, ctx, fixtureId);
    if (fixtureId === 'designer') return renderDesigner(stage, ctx);
    return renderResults(stage, ctx);
  },
};
