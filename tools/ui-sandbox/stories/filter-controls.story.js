export default {
  id: 'filter-controls',
  title: 'Search, textbox and date controls',
  fixtures: [{ id: 'clear-and-length', label: 'Clear button and max length' }, { id: 'dates', label: 'Date range and relative dates' }],
  async mount(stage, fixtureId, ctx) {
    const iframe = document.createElement('iframe');
    iframe.className = 'vscode-webview-frame';
    iframe.title = fixtureId === 'dates' ? 'Date controls' : 'Filter controls';
    iframe.src = fixtureId === 'dates' ? '/tools/ui-sandbox/date-controls.html' : '/tools/ui-sandbox/filter-controls.html';
    stage.replaceChildren(iframe);
    ctx.stat(fixtureId === 'dates' ? 'Canonical runtime · date ranges, disabled dates and relative quick picks' : 'Canonical runtime · SHOW_CLEAR = ON · MAX_LENGTH = 12');
    return { dispose() { iframe.remove(); }, resize() {} };
  },
};
