import { importFresh, DESIGNER_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';
import { readDesignerSnapshot } from '/src/ETL-SQL.Portal/wwwroot/js/authoring-responses.js';
import { showAuthoringError } from '/src/ETL-SQL.Portal/wwwroot/js/portal-authoring.js';

export default {
  id: 'portal-authoring',
  title: 'Portal authoring responses',
  subtitle: 'Snapshot column mappings and safe loading failures',
  fixtures: [
    { id: 'snapshot', label: 'Portal snapshot columns' },
    { id: 'unavailable', label: 'Authoring unavailable' },
    { id: 'invalid-snapshot', label: 'Invalid snapshot response' }
  ],
  async mount(stage, fixtureId, ctx) {
    stage.replaceChildren();
    const snapshot = {
      reportName: 'Regional revenue', builtAt: '2026-10-07T12:00:00Z',
      sampleRows: { Revenue: [['North', '120'], ['South', '80'], ['West', null]] },
      columns: { Revenue: ['Region', 'Revenue'] },
      metadata: { isSampled: false, rlsEnforced: false, totalRows: 3, returnedRows: 3 }
    };
    if (fixtureId === 'unavailable' || fixtureId === 'invalid-snapshot') {
      let error = new Error('Server message: <img src=x onerror=document.body.dataset.injected=1>');
      if (fixtureId === 'invalid-snapshot') {
        try { readDesignerSnapshot({ ...snapshot, columns: { Revenue: 'invalid' } }); }
        catch (failure) { error = failure; }
      }
      showAuthoringError(stage, error, () => {
        stage.querySelector('p').textContent = 'Retry requested.';
        ctx.stat('Retry action activated.');
      });
      return { dispose() {} };
    }
    const state = {
      pages: [{ id: 'p1', name: 'Main', mode: 'Dashboard', visuals: [{
        id: 'v1', name: 'Revenue', type: 'BAR', title: 'Regional revenue',
        gridCol: 1, gridRow: 1, gridColSpan: 12, gridRowSpan: 6, dataset: 'data',
        mappings: { X: 'Region', Y: 'Revenue' }, options: {}
      }] }], datasets: [{ id: 'data', name: 'data', query: 'SELECT Region, Revenue FROM #sales' }]
    };
    const { createDesigner } = await importFresh(DESIGNER_JS);
    const designer = createDesigner(stage, {
      designState: state, snapshotPackage: readDesignerSnapshot(snapshot),
      reportName: snapshot.reportName, authFetch: makeMockApi(state), hideSidebar: true, hideProps: true
    });
    ctx.stat('Mapped Region and Revenue from the Portal snapshot response.');
    return designer;
  }
};
