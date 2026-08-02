const fixtures = [
  { id: 'desktop', label: 'Catalog desktop' },
  { id: 'mobile', label: 'Catalog mobile' },
];

function studioMarkup() {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <link rel="stylesheet" href="/src/ETL-SQL.Portal/wwwroot/css/portal.css"></head><body>
    <div class="app-shell"><header class="topbar"><span class="topbar-brand"><span class="brand-mark">E</span><span>ETL-SQL Portal</span></span><nav class="topbar-nav"><a>Reports</a><a>Governance</a><a class="active">Studio</a><a>Docs</a></nav></header>
    <main class="studio-home"><section class="studio-intro"><div><span class="library-kicker">Catalog authoring</span><h1>Studio</h1><p>Open a governed report from the catalog, then work in Code or Design without leaving its folder boundary.</p></div><button class="btn btn-primary">New report</button></section>
    <section class="studio-workbench"><div class="studio-workbench-toolbar"><label class="studio-search"><span>Find a report</span><input placeholder="Search name or folder"></label><span class="studio-mode-policy">Catalog-only authoring</span></div>
    <section class="studio-folder-group"><header><span class="studio-folder-mark"></span><h2>/Finance/Reporting</h2><span>2 reports</span></header><div class="studio-report-grid">
      ${[['Regional revenue','Daily revenue and margin by operating region.'],['Close readiness','Exceptions that block the monthly close.']].map(([name, description]) => `<article class="studio-report-card"><div class="studio-report-copy"><h3>${name}</h3><p>${description}</p><small>Updated Aug 2, 2026</small></div><div class="studio-mode-rail"><a><span>▦</span><strong>Design</strong><small>Canvas</small></a><a><span>&lt;/&gt;</span><strong>Code</strong><small>Report-SQL</small></a></div></article>`).join('')}
    </div></section></section></main></div></body></html>`;
}

export default {
  id: 'portal-studio',
  title: 'Portal Studio home',
  subtitle: 'Catalog-scoped authoring with equal Code and Design lanes',
  fixtures,
  async mount(stage, fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = `Portal Studio — ${fixtureId}`;
    frame.style.cssText = `display:block;width:${fixtureId === 'mobile' ? '390px' : '100%'};max-width:100%;height:100%;min-height:680px;margin:0 auto;border:0;background:white;`;
    frame.srcdoc = studioMarkup();
    stage.replaceChildren(frame);
    ctx.stat(fixtureId === 'mobile' ? '390px catalog viewport' : 'Catalog desktop viewport');
    return { resize() {} };
  },
};
