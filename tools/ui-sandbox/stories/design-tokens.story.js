export default {
  id: 'design-tokens',
  title: 'Design token runtime',
  subtitle: 'Deterministic report -> page -> container -> visual CSS variable resolution and inheritance',
  fixtures: [{ id: 'inheritance', label: 'Token cascade and component overrides' }],
  async mount(stage, _fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = 'Design tokens runtime fixture';
    frame.src = '/tools/ui-sandbox/design-tokens.html';
    frame.style.cssText = 'display:block;width:100%;min-height:640px;border:0;background:white';
    stage.replaceChildren(frame);
    ctx.stat('Canonical report-runtime.js — scoped --etl-* design tokens & safe DOM application');
    return { dispose() { frame.remove(); }, resize() {} };
  }
};
