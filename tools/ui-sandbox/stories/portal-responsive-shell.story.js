const fixtures = [
  { id: 'reports', label: 'Reports at 390px' },
  { id: 'admin', label: 'Admin at 390px' },
];

function pageMarkup(fixtureId) {
  const isAdmin = fixtureId === 'admin';
  const workspace = isAdmin ? 'Administration' : 'Report Library';
  const sidebar = isAdmin ? '' : `
    <aside class="sidebar" id="sidebar">
      <div class="sidebar-hdr"><span>Folders</span><span class="sidebar-hint">Report library</span></div>
      <nav class="sidebar-nav" aria-label="Report views">
        <a class="sidebar-nav-item active" href="#">Report Library</a>
        <a class="sidebar-nav-item" href="#">Favorites</a>
        <a class="sidebar-nav-item" href="#">Recently Viewed</a>
      </nav>
    </aside>`;
  return `<!doctype html>
    <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="stylesheet" href="/src/ETL-SQL.Portal/wwwroot/css/portal.css"></head>
    <body><div class="app-shell">
      <header class="topbar">
        <button class="mobile-menu-btn" id="mobileMenuBtn" aria-label="Open navigation menu" type="button">☰</button>
        <span class="topbar-brand"><span class="brand-mark">E</span><span class="portal-brand-name">ETL-SQL Portal</span></span>
        <nav class="topbar-nav"><a href="#reports"${isAdmin ? '' : ' class="active"'}>Reports</a><a href="#governance">Governance</a><a href="#docs">Docs</a><a href="#orchestrator">Orchestrator</a><a href="#admin"${isAdmin ? ' class="active"' : ''}>Admin</a></nav>
        <span class="topbar-spacer"></span><span class="topbar-user" id="topbarUser">alex.publisher@example.com</span>
        <button class="theme-toggle-btn" id="themeToggleBtn" type="button">Theme</button><button class="topbar-btn" id="logoutBtn">Sign Out</button>
      </header>
      <div class="app-body${isAdmin ? ' admin-body' : ''}">${sidebar}<main class="main-content${isAdmin ? ' admin-content' : ''}">
        <div class="admin-page-header"><div><span class="library-kicker">Workspace</span><h2 class="page-title">${workspace}</h2><p>Open the menu to exercise the modal drawer, focus loop, Escape close, and background containment.</p></div></div>
        <div class="admin-tabs" role="tablist"><button class="admin-tab active">Overview</button><button class="admin-tab">Long workspace tab</button><button class="admin-tab">Operations</button><button class="admin-tab">Settings</button></div>
        <div class="card"><div class="card-header"><h3>Responsive content patterns</h3><div class="admin-action-group"><input class="admin-filter-input" placeholder="Filter records"><button class="btn btn-outline">Secondary</button><button class="btn btn-primary">Primary action</button></div></div>
          <div class="form-row"><div class="form-group"><label>Environment<input value="Production"></label></div><div class="form-group"><label>Owner<input value="Finance Operations"></label></div></div>
          <div id="sampleTableWrap"><table class="data-table"><thead><tr><th>Name</th><th>Status</th><th>Last execution</th><th>Owner</th><th>Actions</th></tr></thead><tbody><tr><td>Daily revenue validation</td><td>Healthy</td><td>2026-08-02 17:32</td><td>Finance Operations</td><td><div class="table-actions"><button class="btn btn-outline btn-sm">Open</button><button class="btn btn-outline btn-sm">History</button></div></td></tr></tbody></table></div>
        </div>
      </main></div>
    </div><script type="module">import { initTheme } from '/src/ETL-SQL.Portal/wwwroot/js/branding.js'; initTheme();<\/script></body></html>`;
}

export default {
  id: 'portal-responsive-shell',
  title: 'Portal responsive shell',
  subtitle: '390px global navigation drawer',
  fixtures,
  async mount(stage, fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = `Portal responsive shell — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:390px;max-width:100%;height:100%;min-height:680px;margin:0 auto;border:0;background:white;';
    frame.srcdoc = pageMarkup(fixtureId);
    stage.replaceChildren(frame);
    ctx.stat('390px viewport · open the navigation menu');
    return { resize() {} };
  },
};
