export default {
  id: 'constrained-html-runtime',
  title: 'Constrained HTML runtime',
  subtitle: 'Sanitized components, semantic fallback, declarative actions, and bounded visual embedding',
  fixtures: [{ id: 'component', label: 'Embedded status component' }],
  async mount(stage, _fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = 'Constrained HTML runtime fixture';
    frame.src = '/tools/ui-sandbox/constrained-html-runtime.html';
    frame.style.cssText = 'display:block;width:100%;min-height:640px;border:0;background:white';
    stage.replaceChildren(frame);
    ctx.stat('Canonical report-runtime.js — sanitized DOM and bounded manifest embedding');
    return { dispose() { frame.remove(); }, resize() {} };
  }
};
