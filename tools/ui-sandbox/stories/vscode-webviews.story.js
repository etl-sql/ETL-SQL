function makeFrame(srcdoc) {
  const iframe = document.createElement('iframe');
  iframe.className = 'vscode-webview-frame';
  iframe.srcdoc = srcdoc;
  return iframe;
}

function vscodeShim() {
  return `
    <script>
      window.acquireVsCodeApi = function() {
        return {
          postMessage: function(message) { console.log('vscode.postMessage', message); },
          getState: function() { return {}; },
          setState: function() {}
        };
      };
    </script>`;
}

async function renderResults(stage, ctx) {
  let html = await fetch('/src/etl-sql-vscode/ui/dist/index.html').then(r => r.text());
  html = html.replace('<head>', `<head>${vscodeShim()}<script>window.VIEW_TYPE = 'results';</script>`);
  const iframe = makeFrame(html);
  stage.replaceChildren(iframe);
  iframe.addEventListener('load', () => {
    iframe.contentWindow.postMessage({ type: 'message', severity: 'Info', message: 'Sandbox run started.' }, '*');
    iframe.contentWindow.postMessage({
      type: 'results',
      isFirst: true,
      columns: ['customer_id', 'region', 'revenue'],
      rows: [
        { customer_id: 1001, region: 'North', revenue: 24850.75 },
        { customer_id: 1002, region: 'West', revenue: 18420.25 },
      ],
    }, '*');
    iframe.contentWindow.postMessage({ type: 'done', exitCode: 0 }, '*');
  }, { once: true });
  ctx.stat('results panel iframe + mocked protocol messages');
  return { dispose() { iframe.remove(); }, resize() {} };
}

async function renderPreview(stage, ctx) {
  const manifest = await fetch('/samples/08_Reporting/sales table.snapshot.json').then(r => r.json());
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
  ctx.stat('report preview iframe + sales table manifest');
  return { dispose() { iframe.remove(); }, resize() {} };
}

function sampleState() {
  return {
    pages: [{
      id: 'p1',
      name: 'Overview',
      mode: 'Dashboard',
      visuals: [
        { id: 'salesBar', type: 'BAR', title: 'Vendor Sales by day', gridCol: 1, gridColSpan: 8, gridRow: 1, gridRowSpan: 6, dataset: 'sales', mappings: { X: 'Date', Y: 'total', SERIES: 'Vendor' } },
        { id: 'kpiRev', type: 'CARD', title: 'Total Revenue', gridCol: 9, gridColSpan: 4, gridRow: 1, gridRowSpan: 3, dataset: 'sales', mappings: { VALUE: 'total' } },
      ],
    }],
    datasets: [{ name: 'sales', query: 'SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales' }],
  };
}

function renderDesigner(stage, ctx) {
  const state = sampleState();
  const initJson = JSON.stringify({ reportName: 'Sandbox Report', scriptText: '' }).replace(/</g, '\\u003c');
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
        return new Response(JSON.stringify({ script: "SET REPORT TITLE = 'Sandbox Report';\\nCREATE PAGE Overview AS (...);" }), { status: 200, headers: { 'Content-Type': 'application/json' } });
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
  ctx.stat('report designer iframe + vscode API shim');
  return { dispose() { iframe.remove(); }, resize() {} };
}

export default {
  id: 'vscode-webviews',
  title: 'VS Code webviews',
  subtitle: 'shimmed acquireVsCodeApi()',
  fixtures: [
    { id: 'results', label: 'Results panel' },
    { id: 'preview', label: 'Report preview' },
    { id: 'designer', label: 'Report designer' },
  ],
  async mount(stage, fixtureId, ctx) {
    if (fixtureId === 'preview') return renderPreview(stage, ctx);
    if (fixtureId === 'designer') return renderDesigner(stage, ctx);
    return renderResults(stage, ctx);
  },
};
