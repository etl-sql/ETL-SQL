// Story: the full WYSIWYG report designer (createDesigner). Uses an injected mock
// fetch (mockApi) for parse/generate and onSaveScript to bypass the save API, so it
// runs with no portal server.
import { importFresh, DESIGNER_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

function sampleState() {
  return {
    pages: [{
      id: 'p1', name: 'Overview', mode: 'Dashboard',
      visuals: [
        { id: 'salesBar', type: 'BAR',   title: 'Vendor Sales by day', gridCol: 1, gridColSpan: 8, gridRow: 1, gridRowSpan: 6, dataset: 'sales', mappings: { X: 'Date', Y: 'total', SERIES: 'Vendor' } },
        { id: 'kpiRev',   type: 'CARD',  title: 'Total Revenue',       gridCol: 9, gridColSpan: 4, gridRow: 1, gridRowSpan: 3, dataset: 'sales', mappings: { VALUE: 'total' } },
        { id: 'detail',   type: 'TABLE', title: 'Detail',              gridCol: 9, gridColSpan: 4, gridRow: 4, gridRowSpan: 3, dataset: 'sales', mappings: {} },
      ],
    }],
    datasets: [{ name: 'sales', query: 'SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales' }],
    // Author bookmarks: shared, source-controlled report state. Values are the authored source text
    // (quoted for strings, bare for numbers) so the round-trip cannot retype them.
    bookmarks: [
      {
        id: 'bm_0', name: 'WestQ4', title: 'West, Q4', page: 'Overview', isDefault: true,
        parameters: [{ name: '@Region', value: "'West'" }, { name: '@Limit', value: '25' }],
        state: [{ objectName: 'detail', property: 'COLLAPSED', on: true }],
      },
      {
        id: 'bm_1', name: 'EastQ4', title: 'East, Q4', page: 'Overview', isDefault: false,
        parameters: [{ name: '@Region', value: "'East'" }],
        state: [],
      },
    ],
  };
}

function blankState() {
  // No `bookmarks` key at all: the patcher reads that as "this client does not edit bookmarks"
  // and leaves any already in the script alone, which is the state a fresh canvas starts in.
  return { pages: [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }], datasets: [] };
}

function customChartState() {
  return {
    pages: [{
      id: 'p1', name: 'Overview', mode: 'Dashboard',
      visuals: [
        {
          id: 'customGog',
          name: 'customGog',
          type: 'CUSTOM',
          title: 'Layered GoG Chart',
          gridCol: 1,
          gridColSpan: 12,
          gridRow: 1,
          gridRowSpan: 6,
          dataset: 'sales',
          mappings: {},
          options: {
            advanced_chart: `CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
        y_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
        bars = RECT (
            ENCODINGS (
                X = Date (TYPE = ORDINAL),
                Y = total (TYPE = QUANTITATIVE, SCALE = y_scale)
            )
        )
    )
)`
          }
        }
      ]
    }],
    datasets: [{ name: 'sales', query: 'SELECT Date, Vendor, SUM(Amount) AS total FROM edw.Sales' }]
  };
}

function customHtmlState() {
  return {
    pages: [{
      id: 'p1', name: 'Overview', mode: 'Dashboard',
      visuals: [
        {
          id: 'customHtml',
          name: 'customHtml',
          type: 'HTML',
          title: 'Cluster Node Cards',
          gridCol: 1,
          gridColSpan: 12,
          gridRow: 1,
          gridRowSpan: 6,
          dataset: 'nodes',
          mappings: {},
          options: {
            html_mode: 'REPEATER',
            html_template: '<article class="node-card"><h3>{{HostName}}</h3><p>CPU: {{CpuPercent}}</p></article>',
            html_style: '.node-card { padding: 10px; border: 1px solid #e2e8f0; }',
            html_fallback: 'Node: {{HostName}} (CPU: {{CpuPercent}})'
          }
        }
      ]
    }],
    datasets: [{ name: 'nodes', query: 'SELECT HostName, CpuPercent FROM #cluster_nodes' }]
  };
}

export default {
  id: 'designer',
  title: 'Report designer',
  subtitle: 'createDesigner()',
  fixtures: [
    { id: 'sales', label: 'Sales dashboard' },
    { id: 'blank', label: 'Blank canvas' },
    { id: 'scm',   label: 'Sales + source control' },
    { id: 'syntax-resilience', label: 'Transient syntax error resilience' },
    { id: 'custom-chart', label: 'Custom Grammar-of-Graphics Chart' },
    { id: 'custom-html',  label: 'Constrained HTML Component' },
  ],
  async mount(stage, fixtureId, ctx) {
    const ds = fixtureId === 'blank' ? blankState() : (fixtureId === 'custom-chart' ? customChartState() : (fixtureId === 'custom-html' ? customHtmlState() : sampleState()));
    const mod = await importFresh(DESIGNER_JS);
    const scm = fixtureId === 'scm';
    const isSyntaxResilience = fixtureId === 'syntax-resilience';
    const opts = {
      designState: ds,
      reportName: 'Sandbox Report',
      authFetch: makeMockApi(ds),
      previewUrl: '/tools/ui-sandbox/designer-preview.html',
    };
    if (scm) {
      // Exercise the Portal save+commit path: reportId routes Save through /api/designer/save
      // (mock), source control enabled reveals the separate Commit button.
      opts.reportId = 1;
      opts.reportVersion = 1;
      opts.sourceRevision = 'sandbox0';
      opts.sourceControlEnabled = true;
      opts.host = 'portal';
      opts.onSave = () => ctx.stat('onSave (mock) — not called while source control is on');
    } else {
      opts.onSaveScript = async (script) => ctx.stat(`saved (mock) · ${script.length} chars`);
    }
    const inst = mod.createDesigner(stage, opts);
    stage.__designerInstance = inst;
    if (isSyntaxResilience) {
      await inst.applyScriptText('SELECT 1; >>> SYNTAX_ERROR <<<');
      ctx.stat('Simulating transient syntax error · Canvas cards retained · Diagnostic badge active');
    } else {
      ctx.stat(scm
        ? 'Save writes catalog only (stays on page); use the separate Commit button to record in Git (mock).'
        : 'drag visuals from the left · Script toggle uses the mock parse/generate');
    }
    return inst;
  },
};
