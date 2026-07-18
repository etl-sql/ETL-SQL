// Story: the shared portal script editor workbench (CodeMirror + run/results shell).
import { importFresh, DESIGNER_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

const SCRIPTS = {
  report:
`CREATE DATASET &sales AS (
  SELECT Date, Vendor, SUM(Amount) AS total
  FROM edw.Sales
);

CREATE VISUAL salesBar AS BAR (
  SOURCE = &sales,
  MAPPINGS (X = Date, Y = total, SERIES = Vendor)
);

CREATE PAGE Overview AS DASHBOARD( STRUCTURE = 'A', MAP ( 'A' = salesBar ) );`,
  etl:
`CREATE CONNECTION orders_csv AS FLATFILE('orders.csv', HEADER = 'ON');

SELECT
  region,
  TRY_CAST(total AS DECIMAL) AS total
INTO #orders
FROM orders_csv;

SELECT
  region,
  SUM(total) AS revenue  /* @d: total revenue by region; @pii: false; */
INTO #summary
FROM #orders
WHERE total > 0
GROUP BY region;`,
  diagnostics:
`CREATE CONNECTION c AS;

SELECT * FROM #stage;`,
  empty: '',
};

export default {
  id: 'script-editor',
  title: 'Script editor',
  subtitle: 'createScriptEditorWorkbench()',
  fixtures: [
    { id: 'report', label: 'Report SQL' },
    { id: 'etl',    label: 'ETL SQL (with tags)' },
    { id: 'diagnostics', label: 'Diagnostics' },
    { id: 'empty',  label: 'Empty' },
  ],
  async mount(stage, fixtureId, ctx) {
    const value = SCRIPTS[fixtureId] ?? '';
    const mod = await importFresh(DESIGNER_JS);
    const api = makeMockApi({});
    const workbench = await mod.createScriptEditorWorkbench(stage, {
      title: 'Script',
      runUrl: '/api/designer/run',
      // Same manifest-mode preview the portal designer uses: POST the script for a compiled
      // ReportManifest, render it in the sandboxed iframe host. mockApi serves the endpoint;
      // the sandbox preview host mirrors the portal's designer-preview.html.
      previewApiUrl: '/api/designer/preview',
      previewUrl: '/tools/ui-sandbox/designer-preview.html',
      connectionRef: 'demo',
      authFetch: api,
      editor: {
        value,
        analyzeUrl: '/api/designer/analyze',
        completeUrl: '/api/designer/complete',
        connectionRef: 'demo',
        authFetch: api,
        onChange: (v) => ctx.stat(`${v.length} chars · ${v.split('\n').length} lines`),
        onDiagnostics: (d) => ctx.stat(`${value.length} chars · ${d.length} diagnostics`),
      },
    });
    ctx.stat(`${value.length} chars · ${(value ? value.split('\n').length : 0)} lines`);
    return { dispose: () => workbench.dispose() };
  },
};
