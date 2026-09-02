export default {
  id: 'filter-controls',
  title: 'Search and textbox options',
  fixtures: [{ id: 'clear-and-length', label: 'Clear button and max length' }],
  async mount(stage, _fixtureId, ctx) {
    const iframe = document.createElement('iframe');
    iframe.className = 'vscode-webview-frame';
    iframe.src = '/tools/ui-sandbox/filter-controls.html';
    stage.replaceChildren(iframe);
    ctx.stat('Canonical runtime · SHOW_CLEAR = ON · MAX_LENGTH = 12');
    return { dispose() { iframe.remove(); }, resize() {} };
  },
};
