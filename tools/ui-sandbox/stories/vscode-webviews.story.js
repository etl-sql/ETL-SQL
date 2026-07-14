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

    function sanitizeName(name, id) {
      const input = name || id || 'visual1';
      let safe = input.trim().replace(/[^a-zA-Z0-9_]/g, '_');
      if (!/^[a-zA-Z]/.test(safe)) safe = 'v_' + safe;
      return safe;
    }

    function buildStructure(visuals) {
      if (!visuals || visuals.length === 0) return '.';
      const maxRow = Math.max(...visuals.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1));
      const maxCol = Math.max(...visuals.map(v => (v.gridCol || 1) + (v.gridColSpan || 12) - 1));
      const usedCols = Math.min(12, maxCol);
      
      const grid = Array.from({ length: maxRow }, () => Array(usedCols).fill('.'));
      
      for (const v of visuals) {
        const slot = sanitizeName(v.name, v.id);
        const startRow = (v.gridRow || 1) - 1;
        const endRow = startRow + (v.gridRowSpan || 4);
        const startCol = (v.gridCol || 1) - 1;
        const endCol = startCol + (v.gridColSpan || 12);
        
        for (let r = startRow; r < endRow && r < maxRow; r++) {
          for (let c = startCol; c < endCol && c < usedCols; c++) {
            grid[r][c] = slot;
          }
        }
      }
      
      return grid.map(row => row.join(' ')).join(' / ');
    }

    function generateMockScript(state) {
      const out = ['-- generated by the sandbox mock (not the real DesignerController)'];
      for (const ds of (state?.datasets ?? [])) {
        const name = ds.name.startsWith('&') ? ds.name : '&' + ds.name;
        out.push('CREATE DATASET ' + name + ' AS (\\n  ' + ds.query + '\\n);');
      }
      for (const p of (state?.pages ?? [])) {
        out.push('');
        for (const v of (p.visuals ?? [])) {
          const vName = sanitizeName(v.name, v.id);
          if (v.type === 'CONTAINER') {
            const containerType = v.options?.CONTAINER_TYPE || 'BOX';
            out.push('CREATE CONTAINER ' + vName + ' AS ' + containerType.toUpperCase() + ' (\\n    TITLE = \\'' + (v.title || '') + '\\',\\n);');
          } else if (v.type === 'BUTTON') {
            const buttonType = v.options?.BUTTON_TYPE || 'REFRESH';
            out.push('CREATE BUTTON ' + vName + ' AS (\\n    TITLE = \\'' + (v.title || '') + '\\',\\n    OPTIONS (BUTTON_TYPE = \\'' + buttonType + '\\'),\\n);');
          } else {
            const maps = Object.entries(v.mappings ?? {})
              .filter(([_, c]) => c)
              .map(([k, c]) => k + ' = ' + c)
              .join(', ');
            const dsName = v.dataset ? (v.dataset.startsWith('&') ? v.dataset : '&' + v.dataset) : '&sales';
            out.push('CREATE VISUAL ' + vName + ' AS ' + v.type + ' (\\n    SOURCE = ' + dsName + (maps ? ',\\n    MAPPINGS (' + maps + ')' : '') + ',\\n    TITLE = \\'' + (v.title || v.name) + '\\'\\n);');
          }
        }
        const structure = buildStructure(p.visuals);
        const mapEntries = (p.visuals ?? []).map(v => {
          const slot = sanitizeName(v.name, v.id);
          return "            '" + slot + "' = " + sanitizeName(v.name, v.id);
        }).join(',\\n');

        out.push('CREATE PAGE [' + sanitizeName(p.name, p.id) + '] AS DASHBOARD (\\n    LAYOUT (\\n        STRUCTURE = \\'' + structure + '\\',\\n        MAP (\\n' + mapEntries + '\\n        )\\n    )\\n);');
      }
      return out.join('\\n');
    }

    window.__vscodeFetch = async function(url, init) {
      if (url.endsWith('/api/designer/parse')) {
        return new Response(JSON.stringify({ designState: window.__SANDBOX_STATE__ }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.endsWith('/api/designer/generate')) {
        let body = {};
        try { body = init?.body ? JSON.parse(init.body) : {}; } catch {}
        if (body.designState) {
          window.__SANDBOX_STATE__ = body.designState;
        }
        const script = generateMockScript(body.designState ?? window.__SANDBOX_STATE__);
        return new Response(JSON.stringify({ script: script }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response('{}', { status: 404 });
    };
    window.__vscodeSave = async function(script) { console.log('vscode.save', script.length); };
  </script>
  <script s      authFetch: window.__vscodeFetch,
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

function renderVisualFlow(stage, ctx) {
  const graph = {
    nodes: [
      { id: 'conn:src_sales', label: 'src_sales (CSV File)', type: 'table', meta: { columns: ['OrderId', 'CustomerId', 'Amount', 'ProductId'], columnLineage: {} } },
      { id: 'conn:src_crm', label: 'src_crm.dbo.Customers (MSSQL)', type: 'table', meta: { columns: ['CustomerId', 'CustomerName', 'Region'], columnLineage: {} } },
      { id: 'table:#temp_sales', label: '#temp_sales (Temp Table)', type: 'table', meta: { columns: ['OrderId', 'CustomerId', 'Amount', 'ProductId'], columnLineage: {
        OrderId: { sources: [{ table: 'src_sales', column: 'OrderId' }], transform: 'PASS' },
        CustomerId: { sources: [{ table: 'src_sales', column: 'CustomerId' }], transform: 'PASS' },
        Amount: { sources: [{ table: 'src_sales', column: 'Amount' }], transform: 'PASS' },
      } } },
      { id: 'table:#temp_customers', label: '#temp_customers (Temp Table)', type: 'table', meta: { columns: ['CustomerId', 'CustomerName', 'Region'], columnLineage: {
        CustomerId: { sources: [{ table: 'src_crm', column: 'CustomerId' }], transform: 'PASS' },
        CustomerName: { sources: [{ table: 'src_crm', column: 'CustomerName' }], transform: 'PASS' },
        Region: { sources: [{ table: 'src_crm', column: 'Region' }], transform: 'PASS' },
      } } },
      { id: 'table:#temp_enriched', label: '#temp_enriched (Temp Table)', type: 'table', meta: { columns: ['OrderId', 'CustomerId', 'CustomerName', 'Region', 'Amount'], columnLineage: {
        OrderId: { sources: [{ table: '#temp_sales', column: 'OrderId' }], transform: 'PASS' },
        CustomerId: { sources: [{ table: '#temp_sales', column: 'CustomerId' }], transform: 'PASS' },
        CustomerName: { sources: [{ table: '#temp_customers', column: 'CustomerName' }], transform: 'PASS' },
        Region: { sources: [{ table: '#temp_customers', column: 'Region' }], transform: 'PASS' },
        Amount: { sources: [{ table: '#temp_sales', column: 'Amount' }], transform: 'PASS' },
      } } },
      { id: 'table:dest_db', label: 'dest_db.reporting.MonthlySalesSummary (Postgres)', type: 'table', meta: { columns: ['OrderId', 'CustomerId', 'CustomerName', 'Region', 'Amount', 'EnrichedAt'], columnLineage: {
        OrderId: { sources: [{ table: '#temp_enriched', column: 'OrderId' }], transform: 'PASS' },
        CustomerId: { sources: [{ table: '#temp_enriched', column: 'CustomerId' }], transform: 'PASS' },
        CustomerName: { sources: [{ table: '#temp_enriched', column: 'CustomerName' }], transform: 'PASS' },
        Region: { sources: [{ table: '#temp_enriched', column: 'Region' }], transform: 'PASS' },
        Amount: { sources: [{ table: '#temp_enriched', column: 'Amount' }], transform: 'PASS' },
        EnrichedAt: { sources: [], transform: 'GETDATE()' },
      } } }
    ],
    edges: [
      { source: 'conn:src_sales', target: 'table:#temp_sales', label: 'SELECT INTO (SALES)' },
      { source: 'conn:src_crm', target: 'table:#temp_customers', label: 'SELECT INTO (CRM)' },
      { source: 'table:#temp_sales', target: 'table:#temp_enriched', label: 'JOIN (SALES)' },
      { source: 'table:#temp_customers', target: 'table:#temp_enriched', label: 'JOIN (CRM)' },
      { source: 'table:#temp_enriched', target: 'table:dest_db', label: 'MERGE (DEST)' }
    ]
  };

  const initData = JSON.stringify(graph).replace(/</g, '\\u003c');
  const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="/src/etl-sql-vscode/media/designer/designer.css">
  \${vscodeShim()}
  <style>
    html, body {
      height: 100%;
      margin: 0;
      overflow: hidden;
      background-color: #1e1e1e;
      color: #d4d4d4;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    }
    #dagRoot {
      height: calc(100% - 75px);
      width: 100%;
      position: relative;
    }
    .vscode-header {
      height: 35px;
      line-height: 35px;
      padding: 0 16px;
      background-color: #252526;
      border-bottom: 1px solid #3c3c3c;
      font-size: 12px;
      font-weight: 600;
      color: #969696;
      display: flex;
      justify-content: space-between;
      align-items: center;
      user-select: none;
    }
    .vscode-header-tab {
      background-color: #1e1e1e;
      color: #e1e1e1;
      padding: 0 12px;
      border-right: 1px solid #252526;
      height: 35px;
      display: flex;
      align-items: center;
    }
    .vscode-header-right {
      color: #858585;
      font-size: 11px;
    }
    
    /* Toolbar styles */
    .vscode-toolbar {
      height: 40px;
      padding: 0 16px;
      background-color: #2d2d2d;
      border-bottom: 1px solid #3c3c3c;
      display: flex;
      align-items: center;
      gap: 8px;
      user-select: none;
    }
    .vscode-btn {
      background-color: #0e639c;
      color: #ffffff;
      border: none;
      padding: 4px 12px;
      font-size: 11px;
      cursor: pointer;
      font-weight: 600;
      border-radius: 2px;
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .vscode-btn:hover {
      background-color: #1177bb;
    }
    .vscode-btn:disabled {
      background-color: #3c3c3c;
      color: #777777;
      cursor: not-allowed;
    }
    .vscode-btn.secondary {
      background-color: #3c3c3c;
      color: #cccccc;
    }
    .vscode-btn.secondary:hover {
      background-color: #4c4c4c;
    }
    .vscode-status {
      font-size: 11px;
      color: #aaaaaa;
      margin-left: auto;
      font-family: monospace;
    }
    
    /* SSIS Execution visual overrides */
    .etlsql-dag-card {
      transition: border-color 0.3s ease, box-shadow 0.3s ease, opacity 0.3s ease;
      background-color: #181818 !important;
    }
    
    /* Waiting status - dashed border, translucent */
    .etlsql-dag-card.status-waiting {
      border: 1px dashed #4e4e4e !important;
      opacity: 0.5;
    }
    
    /* Running status - yellow border, pulsing glow */
    .etlsql-dag-card.status-running {
      border: 1.5px solid #eab308 !important;
      box-shadow: 0 0 12px rgba(234, 179, 8, 0.4);
      opacity: 1;
      animation: ssis-pulse 1.5s infinite alternate;
    }
    
    /* Completed status - green border, slight green gradient */
    .etlsql-dag-card.status-completed {
      border: 1.5px solid #22c55e !important;
      box-shadow: 0 0 8px rgba(34, 197, 94, 0.2);
      opacity: 1;
      background: linear-gradient(180deg, #181818 0%, #112815 100%) !important;
    }
    
    @keyframes ssis-pulse {
      from { box-shadow: 0 0 4px rgba(234, 179, 8, 0.2); }
      to { box-shadow: 0 0 16px rgba(234, 179, 8, 0.6); }
    }
    
    /* Icon styling */
    .status-icon {
      font-size: 11px;
      font-weight: bold;
      margin-left: 6px;
      display: inline-block;
    }
    .status-icon.running {
      animation: ssis-spin 1.2s linear infinite;
      color: #eab308;
    }
    .status-icon.completed {
      color: #22c55e;
    }
    
    @keyframes ssis-spin {
      from { transform: rotate(0deg); }
      to { transform: rotate(360deg); }
    }
    
    /* Edge badges transitions */
    .etlsql-dag-edge-badge {
      font-family: monospace;
      font-size: 10px !important;
      font-weight: bold;
      transition: color 0.3s ease, border-color 0.3s ease, background-color 0.3s ease;
    }
  </style>
  <title>VS Code Visual Flow</title>
</head>
<body>
  <div class="vscode-header">
    <div class="vscode-header-tab">
      <span>monthly_sales_summary.etlsql (Visual Flow)</span>
    </div>
    <div class="vscode-header-right">
      <span>Auto-refresh: On Save</span>
    </div>
  </div>
  
  <div class="vscode-toolbar">
    <button id="runBtn" class="vscode-btn">▶ Run Pipeline</button>
    <button id="resetBtn" class="vscode-btn secondary">↻ Reset</button>
    <span id="runStatus" class="vscode-status">Status: Ready</span>
  </div>
  
  <div id="dagRoot"></div>
  
  <script type="module">
    import { renderDag } from '/src/etl-sql-vscode/media/designer/designer.js';
    
    const data = \${initData};
    const container = document.getElementById('dagRoot');
    
    // Mount the DAG
    const instance = renderDag(container, data, {
      theme: 'vscode',
      onNodeClick: (nodeId, nodeMeta) => {
        // Send a postMessage to VS Code to mock cursor jumping to code location
        window.acquireVsCodeApi().postMessage({
          type: 'jump_to_line',
          nodeId: nodeId,
          label: nodeMeta?.label || nodeId
        });
      }
    });

    // ── Pipeline Animation Engine ─────────────────────────────────────────────
    let animInterval = null;
    
    function resetPipeline() {
      clearInterval(animInterval);
      document.getElementById('runStatus').textContent = 'Status: Ready';
      document.getElementById('runStatus').style.color = '#aaaaaa';
      document.getElementById('runBtn').disabled = false;
      
      // Reset all cards to waiting status
      const cards = document.querySelectorAll('.etlsql-dag-card');
      cards.forEach(card => {
        card.className = 'etlsql-dag-card status-waiting';
        const icon = card.querySelector('.status-icon');
        if (icon) icon.remove();
      });
      
      // Reset all edge badges to their original label texts
      const badges = document.querySelectorAll('.etlsql-dag-edge-badge');
      badges.forEach(b => {
        if (!b.dataset.original) b.dataset.original = b.textContent;
        b.textContent = b.dataset.original;
        b.style.color = '#93c5fd';
        b.style.borderColor = '#3b82f6';
        b.style.backgroundColor = '#1e293b';
      });
    }

    function runPipeline() {
      resetPipeline();
      document.getElementById('runBtn').disabled = true;
      document.getElementById('runStatus').textContent = 'Status: Running...';
      document.getElementById('runStatus').style.color = '#eab308';
      
      let t = 0;
      const TICK_MS = 50;
      
      animInterval = setInterval(() => {
        t += TICK_MS;
        const sec = t / 1000;
        
        // ── Phase 1: Ingestion / Extraction (0s to 2s) ──
        if (sec > 0 && sec <= 2) {
          setCardStatus('conn:src_sales', 'running', '↻');
          setCardStatus('conn:src_crm', 'running', '↻');
          
          const pct = sec / 2;
          const salesRows = Math.floor(12300 * pct);
          const crmRows = Math.floor(4500 * pct);
          
          updateEdgeBadge('(SALES)', \`\${salesRows.toLocaleString()} rows\`, '#eab308', 'rgba(234,179,8,0.15)');
          updateEdgeBadge('(CRM)', \`\${crmRows.toLocaleString()} rows\`, '#eab308', 'rgba(234,179,8,0.15)');
        }
        
        // ── Phase 2: Ingestion Complete, Join Staging (2s to 4s) ──
        if (sec > 2 && sec <= 4) {
          setCardStatus('conn:src_sales', 'completed', '✔️');
          setCardStatus('conn:src_crm', 'completed', '✔️');
          setCardStatus('table:#temp_sales', 'completed', '✔️');
          setCardStatus('table:#temp_customers', 'completed', '✔️');
          
          updateEdgeBadge('(SALES)', '12,300 rows', '#22c55e', 'rgba(34,197,94,0.15)');
          updateEdgeBadge('(CRM)', '4,500 rows', '#22c55e', 'rgba(34,197,94,0.15)');
          
          // Join node is running
          setCardStatus('table:#temp_enriched', 'running', '↻');
          const pct = (sec - 2) / 2;
          const joinRows = Math.floor(12300 * pct);
          
          updateEdgeBadge('JOIN (SALES)', \`\${joinRows.toLocaleString()} rows\`, '#eab308', 'rgba(234,179,8,0.15)');
          updateEdgeBadge('JOIN (CRM)', \`\${joinRows.toLocaleString()} rows\`, '#eab308', 'rgba(234,179,8,0.15)');
        }
        
        // ── Phase 3: Enrichment Complete, Load Merging (4s to 6s) ──
        if (sec > 4 && sec <= 6) {
          setCardStatus('table:#temp_enriched', 'completed', '✔️');
          updateEdgeBadge('JOIN (SALES)', '12,300 rows', '#22c55e', 'rgba(34,197,94,0.15)');
          updateEdgeBadge('JOIN (CRM)', '12,300 rows', '#22c55e', 'rgba(34,197,94,0.15)');
          
          // Target merge node is running
          setCardStatus('table:dest_db', 'running', '↻');
          const pct = (sec - 4) / 2;
          const loadRows = Math.floor(12300 * pct);
          
          updateEdgeBadge('(DEST)', \`\${loadRows.toLocaleString()} rows\`, '#eab308', 'rgba(234,179,8,0.15)');
        }
        
        // ── Phase 4: Execution Complete (6s+) ──
        if (sec > 6) {
          clearInterval(animInterval);
          setCardStatus('table:dest_db', 'completed', '✔️');
          updateEdgeBadge('(DEST)', '12,300 rows', '#22c55e', 'rgba(34,197,94,0.15)');
          
          document.getElementById('runStatus').textContent = 'Status: Success (6.2s) - 12,300 rows loaded';
          document.getElementById('runStatus').style.color = '#22c55e';
          document.getElementById('runBtn').disabled = false;
        }
      }, TICK_MS);
    }
    
    function setCardStatus(id, status, iconText) {
      const card = document.getElementById(\`node__\${id}\`);
      if (!card) return;
      
      card.className = \`etlsql-dag-card status-\${status}\`;
      
      const header = card.querySelector('.etlsql-dag-card-header');
      if (header) {
        let icon = header.querySelector('.status-icon');
        if (!icon) {
          icon = document.createElement('span');
          header.appendChild(icon);
        }
        icon.className = \`status-icon \${status === 'running' ? 'running' : ''}\`;
        icon.textContent = iconText;
        if (status === 'completed') {
          icon.style.color = '#22c55e';
        } else if (status === 'running') {
          icon.style.color = '#eab308';
        }
      }
    }
    
    function updateEdgeBadge(keyword, text, color, bgColor) {
      const badges = document.querySelectorAll('.etlsql-dag-edge-badge');
      for (const b of badges) {
        const currentText = b.textContent;
        const originalText = b.dataset.original || currentText;
        
        if (currentText.includes(keyword) || originalText.includes(keyword)) {
          b.textContent = text;
          if (color) {
            b.style.color = color;
            b.style.borderColor = color;
          }
          if (bgColor) {
            b.style.backgroundColor = bgColor;
          }
          break;
        }
      }
    }

    // Attach listeners
    document.getElementById('runBtn').addEventListener('click', runPipeline);
    document.getElementById('resetBtn').addEventListener('click', resetPipeline);
    
    // Initialize
    setTimeout(resetPipeline, 100);
  </script>
</body>
</html>`;

  const iframe = makeFrame(html);
  
  const onMessage = (e) => {
    if (e.data && e.data.__fromWebview) {
      const msg = e.data.__fromWebview;
      if (msg.type === 'jump_to_line') {
        ctx.stat(`vscode.postMessage -> jump_to_line: ${msg.label} (Mock cursor jumps to SQL code)`);
      }
    }
  };
  window.addEventListener('message', onMessage);

  stage.replaceChildren(iframe);
  ctx.stat('Visual Flow (DAG) · 6 nodes, 5 edges · Hover nodes for columns · Click header to inspect details · Click card body to jump cursor');
  
  return {
    dispose() {
      window.removeEventListener('message', onMessage);
      iframe.remove();
    },
    resize() {}
  };
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
    { id: 'visual-flow',  label: 'Visual Flow (DAG)' },
  ],
  async mount(stage, fixtureId, ctx) {
    if (fixtureId === 'preview' || fixtureId === 'preview-sink') return renderPreview(stage, ctx, fixtureId);
    if (fixtureId === 'designer') return renderDesigner(stage, ctx);
    if (fixtureId === 'visual-flow') return renderVisualFlow(stage, ctx);
    return renderResults(stage, ctx);
  },
};
