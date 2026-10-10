import { importFresh } from '../util.js';

export default {
  id: 'portal-operational-charts',
  title: 'Portal operational charts',
  category: 'Orchestrator & Jobs',
  subtitle: 'Portal adapter: rendering and graph selection',
  fixtures: [{ id: 'bars', label: 'Run metrics' }, { id: 'graph', label: 'Job dependencies' }],
  async mount(stage, fixtureId, ctx) {
    await importFresh('/src/ETL-SQL.Portal/wwwroot/js/native-charts.js');
    const card = document.createElement('section');
    card.style.cssText = 'width:100%;max-width:720px;padding:16px;';
    const heading = document.createElement('h3');
    heading.textContent = fixtureId === 'graph' ? 'Job dependencies' : 'Rows processed';
    const host = document.createElement('div');
    host.style.cssText = 'height:240px;width:100%;';
    const selection = document.createElement('p');
    selection.setAttribute('aria-live', 'polite');
    selection.textContent = 'Select a chart mark.';
    card.append(heading, host, selection);
    stage.replaceChildren(card);
    const chart = window.nativeCharts.init(host);
    chart.setOption(fixtureId === 'graph' ? {
      title: { text: 'Jobs: extract, validate, load' },
      series: [{ type: 'graph', data: [{ name: 'extract' }, { name: 'validate' }, { name: 'load' }],
        links: [{ source: 'extract', target: 'validate' }, { source: 'validate', target: 'load' }] }]
    } : {
      title: { text: 'Rows processed per run' }, series: [{ data: [420, 560, 380, 610] }]
    });
    chart.on('click', event => { selection.textContent = `Selected: ${event.name || `run ${event.dataIndex + 1}`}`; });
    ctx.stat(fixtureId === 'graph' ? '3 jobs, 2 dependencies' : '4 runs');
    return { resize: () => chart.resize(), dispose: () => chart.dispose() };
  }
};
