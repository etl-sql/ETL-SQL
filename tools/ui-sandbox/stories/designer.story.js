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
  };
}

function blankState() {
  return { pages: [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }], datasets: [] };
}

export default {
  id: 'designer',
  title: 'Report designer',
  subtitle: 'createDesigner()',
  fixtures: [
    { id: 'sales', label: 'Sales dashboard' },
    { id: 'blank', label: 'Blank canvas' },
    { id: 'scm',   label: 'Sales + source control' },
  ],
  async mount(stage, fixtureId, ctx) {
    const ds = fixtureId === 'blank' ? blankState() : sampleState();
    const mod = await importFresh(DESIGNER_JS);
    const scm = fixtureId === 'scm';
    const opts = {
      designState: ds,
      reportName: 'Sandbox Report',
      authFetch: makeMockApi(ds),
    };
    if (scm) {
      // Exercise the Portal save+commit path: reportId routes Save through /api/designer/save
      // (mock), source control enabled reveals the separate Commit button.
      opts.reportId = 1;
      opts.reportVersion = 1;
      opts.sourceRevision = 'sandbox0';
      opts.sourceControlEnabled = true;
      opts.onSave = () => ctx.stat('onSave (mock) — not called while source control is on');
    } else {
      opts.onSaveScript = async (script) => ctx.stat(`saved (mock) · ${script.length} chars`);
    }
    const inst = mod.createDesigner(stage, opts);
    ctx.stat(scm
      ? 'Save writes catalog only (stays on page); use the separate Commit button to record in Git (mock).'
      : 'drag visuals from the left · Script toggle uses the mock parse/generate');
    return inst;
  },
};
