// One header vocabulary for every authenticated Portal shell page. Pages declare only which
// destination is active; branding, identity, module gating, responsive navigation, and sign-out
// keep their existing owners and bind to these stable ids after this markup is rendered.

const destinations = [
  ['reports', 'navReports', '/index.html', 'Reports'],
  ['governance', 'navGovernance', '/index.html#governance', 'Governance'],
  ['studio', 'studioNav', '/studio.html', 'Studio'],
  ['docs', 'docsNav', '/docs.html', 'Docs'],
  ['orchestrator', 'orchestratorNav', '/orchestrator.html', 'Orchestrator'],
  ['admin', 'adminNav', '/admin.html', 'Admin'],
];

export function renderPortalHeader(header = document.querySelector('[data-portal-header]')) {
  if (!header || header.dataset.portalHeaderRendered === 'true') return header;
  const active = header.dataset.active || '';
  const hideLogout = header.dataset.logoutHidden === 'true';
  const links = destinations.map(([key, id, href, label]) => {
    const classes = key === active ? ' class="active"' : '';
    // The server owns these four visibility decisions. Hidden is the safe pre-response state,
    // including on the destination's own page; its route filter remains the real boundary.
    const gated = ['studio', 'docs', 'orchestrator', 'admin'].includes(key);
    return `<a href="${href}" id="${id}"${classes}${gated ? ' style="display:none"' : ''}>${label}</a>`;
  }).join('');

  header.classList.add('topbar');
  header.innerHTML = `
    <button class="mobile-menu-btn" id="mobileMenuBtn" aria-label="Open navigation menu" type="button">☰</button>
    <span class="topbar-brand"><span class="brand-mark">E</span> <span class="portal-brand-name">ETL-SQL Portal</span></span>
    <nav class="topbar-nav" aria-label="Portal">${links}</nav>
    <span class="topbar-spacer"></span>
    <span class="topbar-user" id="topbarUser"></span>
    <button class="theme-toggle-btn" id="themeToggleBtn" aria-label="Toggle dark mode" type="button">🌓</button>
    <button class="topbar-btn" id="logoutBtn"${hideLogout ? ' style="display:none"' : ''}>Sign Out</button>`;
  header.dataset.portalHeaderRendered = 'true';
  return header;
}
