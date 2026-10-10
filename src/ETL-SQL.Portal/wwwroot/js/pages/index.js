/**
 * Page module for index.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { adminApi, auth, authApi, catalogApi, dataQualityApi, foldersApi, governanceApi, reportsApi, studioApi } from '../api.js';
import { createReaderSubscriptions } from '../reader-subscriptions.js';
import { buildReportFrameDocument, createReportViewer } from '../report-viewer.js';
import { formatBuiltAt, timeAgo } from '../report-format.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { lineageRowsToCsv, renderLineageRow } from '../lineage-ui.js';
import { createLineageCatalog } from '../lineage-catalog.js';
import { createDataQualityQueue } from '../data-quality-queue.js';
import { createGovernancePortal } from '../governance-portal.js';
import { renderDag } from '../../designer/designer.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';

renderPortalHeader();
installDialogAccessibility();
if (!auth.isLoggedIn()) { window.location.href = '/login.html'; }
applyPortalBranding();
initTheme();

function transitionDOM(updateCallback) {
  if (!document.startViewTransition) { updateCallback(); return; }
  const transition = document.startViewTransition(updateCallback);
  // Starting a transition while one is still running skips the earlier one, rejecting its ready and
  // finished promises with AbortError. That is expected whenever a user navigates faster than the
  // animation, so consume those rejections rather than leaving unhandled rejections on the page.
  // updateCallbackDone is deliberately left alone: a throw inside updateCallback is a real error.
  transition.ready.catch(() => { });
  transition.finished.catch(() => { });
}

// ── Bootstrap ──────────────────────────────────────────────────────────────────
let currentFolderId   = null;
let currentFolderName = '';
let isAdmin           = false;
let canDesign         = false;
let currentReports    = [];
let reportSearchTerm  = '';
let reportSortMode    = 'name';

// Browser back/forward: restore the correct view
window.addEventListener('popstate', e => {
  const s = e.state;
  if (!s) return;
  if (s.view === 'folder') {
    currentFolderId   = s.folderId;
    currentFolderName = s.folderName;
    reportViewer.activePage = null;
    loadReportList(s.folderId, s.folderName, /*pushState=*/false);
  } else if (s.view === 'report') {
    currentFolderId   = s.folderId;
    currentFolderName = s.folderName;
    reportViewer.activePage = s.page || null;
    openReport(s.id, /*pushState=*/false);
  }
});

window.addEventListener('hashchange', () => openReportHashView());

let canGovernanceOverview = false;
let canQuarantine = false;

/// The Governance view to open when no specific one was asked for. Lineage is the floor because it
/// is open to every authenticated user; landing anyone on a view they are refused would make the
/// section look broken at the exact moment they first try it.
function defaultGovernanceMode() {
  if (canGovernanceOverview) return 'overview';
  if (canQuarantine) return 'quarantine';
  return 'lineage';
}

async function init() {
  try {
    const identity = getSessionIdentity(auth.getToken());
    renderSessionIdentity(identity, document.getElementById('topbarUser'));
    isAdmin = hasRole(identity, 'Admin');

    // Governance itself stays visible to everyone: Lineage Search is open to any
    // authenticated user, and tracing where a number came from is exactly what a report consumer
    // needs them for. Its individual views are not all open, and each is revealed only to the roles
    // its API accepts — offering a view that answers 403 reads as the product being broken rather
    // than as a permission the user lacks.
    const isGovUser = hasRole(identity, 'Admin', 'GovernanceManager', 'StewardshipManager', 'DataSteward', 'GovernanceViewer', 'StewardshipViewer');
    if (isGovUser) {
      document.getElementById('govNavOverview').style.display = '';
      document.getElementById('govNavWorkqueue').style.display = '';
      document.getElementById('govNavExceptions').style.display = '';
      document.getElementById('govNavGlossary').style.display = '';
      document.getElementById('govNavQuality').style.display = '';
      document.getElementById('govNavSettings').style.display = '';
    }
    if (hasRole(identity, 'Admin', 'DataSteward')) {
      document.getElementById('govNavQuarantine').style.display = '';
    }

    canGovernanceOverview = isGovUser;
    canQuarantine = hasRole(identity, 'Admin', 'DataSteward');
  } catch {
    // Identity is unreadable, so no role-gated entry is revealed. The two flags above keep
    // whatever the page was initialised with, which is the closed position.
  }
  // The top-level entries — Admin, Orchestrator, Docs, Studio — come from one server answer.
  await applyNavigationSafely();

  try {
    // Still asked separately: this decides whether *this page* offers authoring actions, which is
    // a different question from whether the Studio entry point is offered at all.
    const studio = await studioApi.session();
    canDesign = studio.capabilities.includes('ScriptRead') && studio.capabilities.includes('ScriptSave');
  } catch { canDesign = false; }

  document.getElementById('logoutBtn').addEventListener('click', () => authApi.logout());
  document.getElementById('navReportHome').addEventListener('click', e => {
    e.preventDefault();
    history.replaceState(history.state, '', window.location.pathname);
    showReportLibraryHome();
  });
  document.getElementById('navSubscriptions').addEventListener('click', e => {
    e.preventDefault();
    setReportHash('subscriptions');
    showMySubscriptions();
  });
  document.getElementById('navRecent').addEventListener('click', e => {
    e.preventDefault();
    setReportHash('recent');
    showRecentlyViewed();
  });
  document.getElementById('navFavorites').addEventListener('click', e => {
    e.preventDefault();
    setReportHash('favorites');
    showFavorites();
  });
  document.getElementById('navGovernance')?.addEventListener('click', e => {
    e.preventDefault();
    const mode = defaultGovernanceMode();
    setReportHash(`governance/${mode}`);
    showGovernanceCatalog(mode);
  });
  [
    ['Overview', 'overview', 'overview'],
    ['Workqueue', 'workqueue', 'workqueue'],
    ['Exceptions', 'exceptions', 'exceptions'],
    ['Glossary', 'glossary', 'glossary'],
    ['Quality', 'quality', 'quality'],
    ['Quarantine', 'quarantine', 'quarantine'],
    ['Lineage', 'lineage', 'lineage'],
    ['Settings', 'settings', 'settings'],
  ].forEach(([name, route, mode]) => {
    document.getElementById(`govNav${name}`)?.addEventListener('click', e => {
      e.preventDefault();
      setReportHash(`governance/${route}`);
      showGovernanceCatalog(mode);
    });
  });

  await loadFolderTree();
  openReportHashView();
}

// Deep link into impact analysis, used by the Orchestrator triage board to answer "this job failed
// — what is downstream of it". Parameters ride in the query string rather than the hash because the
// hash router lowercases the whole route, which would corrupt a case-sensitive target name.
//
// Applied once and then stripped from the URL: leaving them there would make every later mode
// change inside governance snap back to the linked target.
let impactDeepLinkConsumed = false;

function applyImpactDeepLink() {
  if (impactDeepLinkConsumed || !lineageCatalog) return;

  const params = new URLSearchParams(window.location.search);
  const name = params.get('impactName');
  if (!name) return;

  impactDeepLinkConsumed = true;
  lineageCatalog.showImpact({
    kind: params.get('impactKind') || 'table',
    name,
    column: params.get('impactColumn') || '',
    direction: params.get('impactDirection') || 'downstream',
    depth: params.get('impactDepth'),
  });

  const url = new URL(window.location.href);
  for (const key of ['impactKind', 'impactName', 'impactColumn', 'impactDirection', 'impactDepth']) {
    url.searchParams.delete(key);
  }
  history.replaceState(history.state, '', `${url.pathname}${url.search}${url.hash}`);
}

function setReportHash(hash) {
  const next = `#${hash}`;
  if (window.location.hash !== next) history.replaceState(history.state, '', next);
}

function openReportHashView() {
  const rawHash = window.location.hash.replace(/^#/, '').toLowerCase();
  const parts = rawHash.split('/');
  const mainView = parts[0];
  const subView = parts[1] || '';

  if (mainView === 'subscriptions') {
    showMySubscriptions();
  } else if (mainView === 'recent') {
    showRecentlyViewed();
  } else if (mainView === 'favorites') {
    showFavorites();
  } else if (mainView === 'governance' || mainView === 'lineage') {
    let mode = defaultGovernanceMode();
    if (subView === 'lineage') mode = 'lineage';
    if (subView === 'impact') mode = 'impact';
    if (subView === 'overview') {
      mode = canGovernanceOverview ? 'overview' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    if (subView === 'workqueue') {
      mode = canGovernanceOverview ? 'workqueue' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    if (subView === 'exceptions') {
      mode = canGovernanceOverview ? 'exceptions' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    if (subView === 'glossary') {
      mode = canGovernanceOverview ? 'glossary' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    if (subView === 'quarantine') {
      mode = canQuarantine ? 'quarantine' : 'lineage';
      if (!canQuarantine) setReportHash('governance/lineage');
    }
    if (subView === 'quality') {
      mode = canGovernanceOverview ? 'quality' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    if (subView === 'settings') {
      mode = canGovernanceOverview ? 'settings' : 'lineage';
      if (!canGovernanceOverview) setReportHash('governance/lineage');
    }
    showGovernanceCatalog(mode);
  } else if (/^report-\d+$/.test(mainView)) {
    const id = parseInt(mainView.split('-')[1], 10);
    if (id > 0) openReport(id);
  } else {
    showReportLibraryHome();
  }
}

function setSidebarViewActive(view, govMode = 'quarantine') {
  // Any other view owns the main area now; a subscription read still in flight must not repaint it.
  if (view !== 'subscriptions') readerSubscriptions.leave();
  reportViewer.leave();
  const isGov = ['governance', 'lineage', 'quarantine'].includes(view) ||
                view.startsWith('governance/') || view.startsWith('lineage/');

  const sidebar = document.getElementById('sidebar');
  if (sidebar) sidebar.style.display = '';

  const reportsSection = document.getElementById('reportsSidebarSection');
  const govSection = document.getElementById('governanceSidebarSection');

  if (reportsSection) reportsSection.style.display = isGov ? 'none' : '';
  if (govSection) govSection.style.display = isGov ? '' : 'none';

  if (!isGov) {
    const activeId = {
      library: 'navReportHome',
      favorites: 'navFavorites',
      recent: 'navRecent',
      subscriptions: 'navSubscriptions'
    }[view];
    document.querySelectorAll('#reportsSidebarSection .sidebar-nav-item').forEach(el =>
      el.classList.toggle('active', el.id === activeId));
  } else {
    const activeGovId = {
      overview: 'govNavOverview',
      workqueue: 'govNavWorkqueue',
      exceptions: 'govNavExceptions',
      glossary: 'govNavGlossary',
      quality: 'govNavQuality',
      quarantine: 'govNavQuarantine',
      lineage: 'govNavLineage',
      settings: 'govNavSettings'
    }[govMode] || 'govNavLineage';

    document.querySelectorAll('#governanceSidebarSection .sidebar-nav-item').forEach(el =>
      el.classList.toggle('active', el.id === activeGovId));
  }

  document.getElementById('navReports')?.classList.toggle('active', !isGov);
  document.getElementById('navGovernance')?.classList.toggle('active', isGov);
}

function clearFolderSelection() {
  document.querySelectorAll('.folder-item').forEach(el => el.classList.remove('active'));
}

async function showReportLibraryHome() {
  clearFolderSelection();
  setSidebarViewActive('library');
  currentFolderId = null;
  currentFolderName = '';
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading your report home…</span></div>`;
  try {
    const home = await catalogApi.consumerHome(8);
    transitionDOM(() => {
      $main.innerHTML = `
        <section class="consumer-home" aria-labelledby="consumerHomeTitle">
          <div class="consumer-home-hero">
            <div><span class="library-kicker">Report catalog</span><h2 id="consumerHomeTitle">Find the report you need</h2><p>Search every folder, description, tag, owner, domain, steward, certification, and lineage term.</p></div>
            <form class="global-report-search" id="globalReportSearch" role="search">
              <label for="globalReportSearchInput">Search the report catalog</label>
              <div><input id="globalReportSearchInput" type="search" placeholder="Revenue, customer, steward, table…" autocomplete="off"><button class="btn btn-primary" type="submit">Search</button></div>
            </form>
          </div>
          <div id="consumerHomeSections">
            ${renderConsumerSection('Favorites', home.favorites || [], 'Favorite reports appear here for quick return.')}
            ${renderConsumerSection('Recently viewed', home.recent || [], 'Reports you open appear here.')}
            ${renderConsumerSection('Featured', home.featured || [], 'Certified and stewarded reports appear here.')}
            ${renderConsumerSection('Popular', home.popular || [], 'Frequently used reports appear here.')}
          </div>
          <div id="globalSearchResults" aria-live="polite"></div>
        </section>`;
      wireConsumerReportCards($main, showReportLibraryHome);
      document.getElementById('globalReportSearch')?.addEventListener('submit', event => {
        event.preventDefault();
        runGlobalReportSearch(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('globalReportSearchInput'))?.value || '');
      });
    });
  } catch {
    $main.innerHTML = `
      <div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Report home did not load</h2><p>Refresh the page or browse a folder from the navigation.</p>
        <button class="btn btn-outline" id="retryConsumerHomeBtn">Retry</button>
      </div>`;
    document.getElementById('retryConsumerHomeBtn')?.addEventListener('click', showReportLibraryHome);
  }
}

function renderConsumerSection(title, reports, emptyText) {
  const cards = reports.map(renderConsumerReportCard).join('');
  return `<section class="consumer-section" aria-labelledby="consumer-${title.replace(/\s+/g, '-').toLowerCase()}">
    <div class="consumer-section-header"><h3 id="consumer-${title.replace(/\s+/g, '-').toLowerCase()}">${esc(title)}</h3><span>${reports.length} report${reports.length === 1 ? '' : 's'}</span></div>
    ${cards ? `<div class="consumer-card-grid">${cards}</div>` : `<p class="consumer-section-empty">${esc(emptyText)}</p>`}
  </section>`;
}

function renderConsumerReportCard(report) {
  const folderPath = report.path ? report.path.replace(/\/[^/]*$/, '') : '';
  const glyph = String(report.category || report.domain || report.name || 'R').trim().charAt(0).toUpperCase();
  return `<article class="consumer-report-card" data-id="${report.id}" data-folder-id="${report.folderId}" data-folder-path="${escAttr(folderPath)}">
    <div class="consumer-report-icon" aria-hidden="true">${esc(glyph || 'R')}</div>
    <div class="consumer-report-copy"><div class="report-card-title-row"><h4><a href="#report-${report.id}" class="report-card-link">${esc(report.name || '')}</a></h4>
      <button class="favorite-btn ${report.isFavorite ? 'is-active' : ''}" data-favorite-id="${report.id}" title="${report.isFavorite ? 'Remove favorite' : 'Add favorite'}" type="button">${report.isFavorite ? '★' : '☆'}</button></div>
      <p>${esc(report.description || report.path || 'No description provided.')}</p>
      <div class="consumer-report-footer">${renderReportStatusBadge(report)}<span>${esc(reportActivityLine(report))}</span></div>
    </div></article>`;
}

function wireConsumerReportCards(root, onFavoriteChanged = null) {
  wireFavoriteButtons(root, onFavoriteChanged);
  root.querySelectorAll('.consumer-report-card').forEach(card => card.addEventListener('click', event => {
    if (event.target.closest('.favorite-btn')) return;
    event.preventDefault();
    currentFolderId = +card.dataset.folderId || null;
    currentFolderName = card.dataset.folderPath || 'Reports';
    openReport(+card.dataset.id);
  }));
}

async function runGlobalReportSearch(query) {
  const q = query.trim();
  const results = document.getElementById('globalSearchResults');
  const sections = document.getElementById('consumerHomeSections');
  if (!results || !sections) return;
  if (!q) {
    results.replaceChildren();
    sections.hidden = false;
    return;
  }
  sections.hidden = true;
  results.innerHTML = `<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Searching the catalog…</span></div>`;
  try {
    const matches = await catalogApi.search(q, 50);
    const folders = matches.filter(item => String(item.type || '').toLowerCase() === 'folder');
    const reports = matches.filter(item => String(item.type || '').toLowerCase() === 'report');
    results.innerHTML = `<section class="consumer-search-results"><div class="consumer-section-header"><div><span class="library-kicker">Search results</span><h3>${matches.length} match${matches.length === 1 ? '' : 'es'} for “${esc(q)}”</h3></div><button class="btn btn-outline" id="clearGlobalSearchBtn" type="button">Clear</button></div>
      ${folders.length ? `<div class="consumer-folder-results">${folders.map(folder => `<button type="button" class="consumer-folder-result" data-folder-id="${folder.id}" data-folder-name="${escAttr(folder.name || '')}"><span class="folder-icon folder-icon-parent" aria-hidden="true"></span><span><strong>${esc(folder.name || '')}</strong><small>${esc(folder.path || '')}</small></span></button>`).join('')}</div>` : ''}
      ${reports.length ? `<div class="consumer-card-grid">${reports.map(renderConsumerReportCard).join('')}</div>` : ''}
      ${matches.length === 0 ? `<div class="empty-state empty-state-panel"><div class="empty-state-icon empty-state-icon-search" aria-hidden="true"></div><h3>No catalog matches</h3><p>Try a broader name, tag, owner, domain, steward, or source table.</p></div>` : ''}
    </section>`;
    document.getElementById('clearGlobalSearchBtn')?.addEventListener('click', () => {
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('globalReportSearchInput')).value = '';
      results.replaceChildren(); sections.hidden = false; document.getElementById('globalReportSearchInput').focus();
    });
    results.querySelectorAll('.consumer-folder-result').forEach(button => button.addEventListener('click', () => selectFolder(+/** @type {HTMLElement} */ (button).dataset.folderId, /** @type {HTMLElement} */ (button).dataset.folderName)));
    wireConsumerReportCards(results, () => runGlobalReportSearch(q));
  } catch {
    results.innerHTML = `<div class="empty-state empty-state-panel empty-state-error"><h3>Search did not complete</h3><p>Try again after checking your portal connection.</p></div>`;
  }
}

// ── Folder tree ────────────────────────────────────────────────────────────────
async function loadFolderTree() {
  try {
    const folders = await foldersApi.list();
    const $tree = document.getElementById('folderTree');
    $tree.innerHTML = '';
    if (!folders.length) {
      $tree.innerHTML = '<div class="sidebar-note">No folders yet.</div>';
      return;
    }
    renderFolderChildren($tree, buildTree(folders), 0);
  } catch {
    document.getElementById('folderTree').innerHTML =
      `<div class="sidebar-note sidebar-note-error">Error loading folders.</div>`;
  }
}

function buildTree(flat) {
  const map = Object.fromEntries(flat.map(f => [f.id, { ...f, children: [] }]));
  const roots = [];
  flat.forEach(f => {
    if (f.parentId && map[f.parentId]) map[f.parentId].children.push(map[f.id]);
    else roots.push(map[f.id]);
  });
  return roots;
}

function renderFolderChildren($parent, nodes, depth) {
  nodes.forEach(node => {
    const $item = document.createElement('div');
    $item.className = 'folder-item';
    $item.dataset.id = node.id;
    $item.setAttribute('role', 'treeitem');
    $item.setAttribute('tabindex', '0');
    $item.setAttribute('aria-selected', 'false');
    if (node.children.length) {
      $item.setAttribute('aria-expanded', 'true');
    }
    $item.style.marginLeft = `${depth * 10}px`;
    $item.innerHTML = `<span class="folder-icon ${node.children.length ? 'folder-icon-parent' : 'folder-icon-leaf'}" aria-hidden="true"></span>
                       <span class="folder-name">${esc(node.name)}</span>`;
    $item.addEventListener('click', () => selectFolder(node.id, node.name));
    $item.addEventListener('keydown', e => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        selectFolder(node.id, node.name);
      }
    });
    $parent.appendChild($item);

    if (node.children.length) {
      const $sub = document.createElement('div');
      $sub.className = 'folder-children';
      $sub.setAttribute('role', 'group');
      renderFolderChildren($sub, node.children, depth + 1);
      $parent.appendChild($sub);
    }
  });
}

function selectFolder(id, name) {
  setSidebarViewActive('library');
  document.querySelectorAll('.folder-item').forEach(el => {
    const isActive = /** @type {HTMLElement} */ (el).dataset.id == id;
    el.classList.toggle('active', isActive);
    el.setAttribute('aria-selected', isActive ? 'true' : 'false');
  });
  currentFolderId   = id;
  currentFolderName = name;
  loadReportList(id, name);
}

// ── Report list ────────────────────────────────────────────────────────────────
async function loadReportList(folderId, folderName, pushState = true) {
  setSidebarViewActive('library');
  if (pushState) {
    history.pushState({ view: 'folder', folderId, folderName }, '', window.location.pathname);
  }
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading reports…</span></div>`;
  try {
    const reports = await reportsApi.list(folderId);
    currentReports = reports;
    reportSearchTerm = '';
    reportSortMode = 'name';
    renderReportList(reports, folderName);
  } catch {
    $main.innerHTML = `
      <div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Reports did not load</h2>
        <p>Refresh this folder or try again after checking your portal connection.</p>
        <button class="btn btn-outline" id="retryReportsBtn">Retry</button>
      </div>`;
    document.getElementById('retryReportsBtn')?.addEventListener('click', () => loadReportList(folderId, folderName));
  }
}

function renderReportList(reports, folderName) {
  const $main = document.getElementById('mainContent');
  const filteredReports = sortReports(filterReports(reports));

  if (!reports.length) {
    transitionDOM(() => {
      $main.innerHTML = `
        ${renderLibraryToolbar(folderName, reports, false)}
        <div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>No reports here yet</h2>
          <p>This folder is ready. Publish a report into it from Admin or the ETL-SQL publish workflow.</p>
        </div>`;
      wireLibraryToolbar();
    });
    return;
  }

  const cards = filteredReports.map(r => {
    const statusBadge = renderReportStatusBadge(r);
    const name = r.name || '';
    const description = r.description || 'No description provided.';
    const previewHtml = buildReportFrameDocument(document, r.id, true).replace(/"/g, '&quot;');

    return `
      <div class="report-card" data-id="${r.id}">
        <div class="report-preview" data-srcdoc="${previewHtml}">
          <div class="report-preview-placeholder">
            <div class="mock-header"></div>
            <div class="mock-grid">
              <div class="mock-widget">📊</div>
              <div class="mock-widget">📈</div>
            </div>
            <div class="mock-footer"></div>
          </div>
        </div>
        <div class="report-card-body">
          <div class="report-card-title-row">
            <span class="report-glyph" aria-hidden="true"></span>
            <h3><a href="#report-${r.id}" class="report-card-link" data-id="${r.id}">${esc(name)}</a></h3>
            <button class="favorite-btn ${r.isFavorite ? 'is-active' : ''}" data-favorite-id="${r.id}" title="${r.isFavorite ? 'Remove favorite' : 'Add favorite'}" type="button">${r.isFavorite ? '★' : '☆'}</button>
          </div>
          <p>${esc(description)}</p>
          <div class="report-status-row">
            ${statusBadge}
            ${r.scriptChanged ? '<span class="badge badge-warning">Script changed</span>' : ''}
            <span class="report-activity">${esc(reportActivityLine(r))}</span>
          </div>
        </div>
      </div>`;
  }).join('');

  transitionDOM(() => {
    $main.innerHTML = `
      ${renderLibraryToolbar(folderName, reports, true, filteredReports.length)}
      ${filteredReports.length
        ? `<div class="report-grid">${cards}</div>`
        : `<div class="empty-state empty-state-panel">
            <div class="empty-state-icon empty-state-icon-search" aria-hidden="true"></div>
            <h2>No matching reports</h2>
            <p>Clear the search box or try a broader report name or description.</p>
          </div>`}`;

    wireLibraryToolbar();
    wireFavoriteButtons($main);
    $main.querySelectorAll('.report-card').forEach(el => {
      el.addEventListener('click', e => {
        if (/** @type {Element} */ (e.target).closest('.favorite-btn')) return;
        e.preventDefault();
        openReport(+/** @type {HTMLElement} */ (el).dataset.id);
      });

      const previewEl = el.querySelector('.report-preview');
      if (previewEl) {
        previewEl.addEventListener('mouseenter', () => {
          if (previewEl.querySelector('iframe')) return;
          const srcdoc = /** @type {HTMLElement} */ (previewEl).dataset.srcdoc;
          const iframe = document.createElement('iframe');
          iframe.title = `${el.querySelector('h3').textContent} preview`;
          iframe.srcdoc = srcdoc;
          iframe.loading = "lazy";
          previewEl.innerHTML = '';
          previewEl.appendChild(iframe);
        }, { once: true });
      }
    });
  });
}

function renderLibraryToolbar(folderName, reports, showControls, visibleCount = reports.length) {
  const countText = `${visibleCount} of ${reports.length} report${reports.length !== 1 ? 's' : ''}`;
  return `
    <div class="library-toolbar">
      <div class="library-title">
        <span class="library-kicker">Folder</span>
        <h2>${esc(folderName)}</h2>
      </div>
      <span class="badge badge-ok">${esc(countText)}</span>
      <div class="library-toolbar-spacer"></div>
      ${showControls ? `
        <label class="library-search">
          <span class="search-icon" aria-hidden="true"></span>
          <input id="reportSearchInput" type="search" placeholder="Search reports" value="${escAttr(reportSearchTerm)}">
        </label>
        <select id="reportSortSelect" class="library-sort" aria-label="Sort reports">
          <option value="name"${reportSortMode === 'name' ? ' selected' : ''}>Name</option>
          <option value="recent"${reportSortMode === 'recent' ? ' selected' : ''}>Last run</option>
          <option value="stale"${reportSortMode === 'stale' ? ' selected' : ''}>Needs refresh</option>
        </select>
      ` : ''}
      <button class="btn btn-outline" id="refreshReportsBtn" type="button">Refresh</button>
    </div>`;
}

function wireLibraryToolbar() {
  document.getElementById('refreshReportsBtn')?.addEventListener('click', () => {
    if (currentFolderId) loadReportList(currentFolderId, currentFolderName);
  });
  document.getElementById('reportSearchInput')?.addEventListener('input', e => {
    reportSearchTerm = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value;
    renderReportList(currentReports, currentFolderName);
    document.getElementById('reportSearchInput')?.focus();
  });
  document.getElementById('reportSortSelect')?.addEventListener('change', e => {
    reportSortMode = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value;
    renderReportList(currentReports, currentFolderName);
  });
}

// ── Recently viewed ───────────────────────────────────────────────────────────
async function showRecentlyViewed() {
  clearFolderSelection();
  setSidebarViewActive('recent');
  currentFolderId = null;
  currentFolderName = 'Recently Viewed';
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading recent reports…</span></div>`;

  try {
    const reports = await catalogApi.recent(30);
    renderCatalogReportList('Recently Viewed', reports, 'Open a report snapshot and it will appear here.');
  } catch {
    $main.innerHTML = `
      <div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Recent reports did not load</h2>
        <p>Refresh the page or try again after checking the portal connection.</p>
      </div>`;
  }
}

async function showFavorites() {
  clearFolderSelection();
  setSidebarViewActive('favorites');
  currentFolderId = null;
  currentFolderName = 'Favorites';
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading favorite reports…</span></div>`;

  try {
    const reports = await catalogApi.favorites(50);
    renderCatalogReportList('Favorites', reports, 'Mark reports as favorites and they will appear here.');
  } catch {
    $main.innerHTML = `
      <div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Favorites did not load</h2>
        <p>Refresh the page or try again after checking the portal connection.</p>
      </div>`;
  }
}

function renderCatalogReportList(title, reports, emptyText) {
  const $main = document.getElementById('mainContent');
  const rows = reports.map(r => {
    const statusBadge = renderReportStatusBadge(r);
    const folderPath = r.path ? r.path.replace(/\/[^/]*$/, '') : '';
    return `
      <div class="catalog-report-row" data-id="${r.id}" data-folder-id="${r.folderId}" data-folder-path="${escAttr(folderPath)}">
        <span class="report-glyph" aria-hidden="true"></span>
        <div class="catalog-report-main">
          <div class="report-card-title-row">
            <h3><a href="#report-${r.id}" class="report-card-link" data-id="${r.id}">${esc(r.name || '')}</a></h3>
            <button class="favorite-btn ${r.isFavorite ? 'is-active' : ''}" data-favorite-id="${r.id}" title="${r.isFavorite ? 'Remove favorite' : 'Add favorite'}" type="button">${r.isFavorite ? '★' : '☆'}</button>
          </div>
          <p>${esc(r.description || r.path || 'No description provided.')}</p>
          <div class="report-status-row">
            ${statusBadge}
            ${r.certification ? `<span class="badge badge-ok">${esc(r.certification)}</span>` : ''}
            ${r.scriptChanged ? '<span class="badge badge-warning">Script changed</span>' : ''}
          </div>
        </div>
        <div class="catalog-report-meta">
          <span>${esc(reportActivityLine(r))}</span>
        </div>
      </div>`;
  }).join('');

  transitionDOM(() => {
    $main.innerHTML = `
      <div class="library-toolbar">
        <div class="library-title">
          <span class="library-kicker">Catalog</span>
          <h2>${esc(title)}</h2>
        </div>
        <span class="badge badge-refresh">${reports.length} report${reports.length === 1 ? '' : 's'}</span>
        <div class="library-toolbar-spacer"></div>
        <button class="btn btn-outline" id="refreshCatalogBtn" type="button">Refresh</button>
      </div>
      ${reports.length
        ? `<div class="catalog-report-list">${rows}</div>`
        : `<div class="empty-state empty-state-panel">
            <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
            <h2>No ${esc(title.toLowerCase())} reports</h2>
            <p>${esc(emptyText)}</p>
          </div>`}`;

    document.getElementById('refreshCatalogBtn')?.addEventListener('click', () =>
      title === 'Favorites' ? showFavorites() : showRecentlyViewed());
    wireFavoriteButtons($main, () => title === 'Favorites' ? showFavorites() : showRecentlyViewed());
    $main.querySelectorAll('.catalog-report-row').forEach(el => {
      el.addEventListener('click', e => {
        if (/** @type {Element} */ (e.target).closest('.favorite-btn')) return;
        e.preventDefault();
        currentFolderId = +/** @type {HTMLElement} */ (el).dataset.folderId;
        currentFolderName = /** @type {HTMLElement} */ (el).dataset.folderPath || 'Reports';
        openReport(+/** @type {HTMLElement} */ (el).dataset.id);
      });
    });
  });
}

// Every Governance surface here is durable and server-backed. The overview dashboard used to be
// excluded from production because it kept its findings, decisions, glossary, and scoring in browser
// memory and substituted demo assets when its API failed — evidence that could not survive a
// refresh, and fiction that did not announce itself. It now reads and writes /api/governance/*, so
// it ships alongside lineage and quarantine.
let lineageCatalog = null;
let dataQualityQueue = null;
let governancePortal = null;

function disposeGovernanceModules(keep) {
  if (keep !== 'lineage' && lineageCatalog) { lineageCatalog.dispose(); lineageCatalog = null; }
  if (keep !== 'quarantine' && dataQualityQueue) { dataQualityQueue.dispose(); dataQualityQueue = null; }
  if (keep !== 'overview' && governancePortal) { governancePortal.dispose(); governancePortal = null; }
}

function showGovernanceCatalog(mode = 'lineage') {
  if (mode === 'overview' || mode === 'workqueue' || mode === 'exceptions' || mode === 'glossary' || mode === 'settings' || mode === 'quality') {
    disposeGovernanceModules('overview');
    if (!governancePortal) {
      governancePortal = createGovernancePortal({
        host: document.getElementById('mainContent'),
        governanceApi,
        dataQualityApi,
        prepare: (currentTab) => {
          setSidebarViewActive('governance', currentTab);
          clearFolderSelection();
        }
      });
    }
    governancePortal.setTab(mode);
    return;
  }

  if (governancePortal) { governancePortal.dispose(); governancePortal = null; }
  if (mode !== 'quarantine') {
    if (dataQualityQueue) { dataQualityQueue.dispose(); dataQualityQueue = null; }
    if (!lineageCatalog) {
      lineageCatalog = createLineageCatalog({
        host: document.getElementById('mainContent'),
        adminApi, catalogApi, renderDag, renderLineageRow, lineageRowsToCsv,
        allowAudit: isAdmin,
        onModeChange: nextMode => {
          const route = nextMode === 'history' ? 'lineage' : nextMode;
          setReportHash(`governance/${route}`);
          setSidebarViewActive('governance', route);
        },
        openReport, timeAgo, formatBuiltAt,
        prepare: (currentMode) => {
          setSidebarViewActive('governance', currentMode === 'history' ? 'lineage' : currentMode);
          clearFolderSelection();
        }
      });
    }
    lineageCatalog.setMode(mode === 'lineage' ? 'history' : mode);
    applyImpactDeepLink();
  } else {
    if (lineageCatalog) { lineageCatalog.dispose(); lineageCatalog = null; }
    if (!dataQualityQueue) {
      dataQualityQueue = createDataQualityQueue({
        host: document.getElementById('mainContent'),
        dataQualityApi,
        prepare: () => {
          setSidebarViewActive('governance', 'quarantine');
          clearFolderSelection();
        }
      });
    }
    dataQualityQueue.show();
  }
}

function wireFavoriteButtons(root, onChanged = null) {
  root.querySelectorAll('[data-favorite-id]').forEach(btn => {
    btn.addEventListener('click', async e => {
      e.preventDefault();
      e.stopPropagation();
      const id = +btn.dataset.favoriteId;
      const isActive = btn.classList.contains('is-active');
      btn.disabled = true;
      try {
        if (isActive) {
          await reportsApi.unfavorite(id);
          btn.classList.remove('is-active');
          btn.textContent = '☆';
          btn.title = 'Add favorite';
        } else {
          await reportsApi.favorite(id);
          btn.classList.add('is-active');
          btn.textContent = '★';
          btn.title = 'Remove favorite';
        }
        onChanged?.();
      } catch (err) {
        ETLSQLFeedback.notify(err.message || 'Favorite update failed.', { title: 'Favorite not changed', tone: 'error' });
      } finally {
        btn.disabled = false;
      }
    });
  });
}

function filterReports(reports) {
  const term = reportSearchTerm.trim().toLowerCase();
  if (!term) return reports;
  return reports.filter(r =>
    String(r.name || '').toLowerCase().includes(term) ||
    String(r.description || '').toLowerCase().includes(term));
}

function sortReports(reports) {
  return [...reports].sort((a, b) => {
    if (reportSortMode === 'recent') {
      return new Date(b.snapshotBuiltAt || 0).getTime() - new Date(a.snapshotBuiltAt || 0).getTime();
    }
    if (reportSortMode === 'stale') {
      return Number(b.isStale) - Number(a.isStale) ||
             new Date(b.snapshotBuiltAt || 0).getTime() - new Date(a.snapshotBuiltAt || 0).getTime();
    }
    return String(a.name || '').localeCompare(String(b.name || ''));
  });
}

function renderReportStatusBadge(report) {
  if (report.lastRefreshStatus === 'Failed')
    return `<span class="badge badge-error" title="${escAttr(report.lastRefreshError || 'Last refresh failed')}">Failed</span>`;
  if (report.lastRefreshStatus === 'Cancelled')
    return `<span class="badge badge-warning">Cancelled</span>`;
  if (report.lastRefreshStatus === 'Running')
    return `<span class="badge badge-running">Refreshing</span>`;
  if (report.isStale)
    return `<span class="badge badge-stale">Stale</span>`;
  return '';
}

function reportActivityLine(report) {
  const viewedAt = report.lastViewedAt ? new Date(report.lastViewedAt).getTime() : 0;
  const builtAt = report.snapshotBuiltAt ? new Date(report.snapshotBuiltAt).getTime() : 0;
  const completedAt = report.lastRefreshCompletedAt ? new Date(report.lastRefreshCompletedAt).getTime() : 0;
  if (report.lastRefreshStatus === 'Failed')
    return completedAt ? `Run failed ${timeAgo(report.lastRefreshCompletedAt)}` : 'Last run failed';
  if (report.lastRefreshStatus === 'Running') return 'Running now';
  if (report.lastRefreshStatus === 'Cancelled')
    return completedAt ? `Run cancelled ${timeAgo(report.lastRefreshCompletedAt)}` : 'Last run cancelled';
  if (viewedAt >= builtAt && viewedAt > 0) return `Viewed ${timeAgo(report.lastViewedAt)}`;
  if (builtAt > 0) return `Updated ${timeAgo(report.snapshotBuiltAt)}`;
  return 'Ready for first run';
}

// ── Report viewer ──────────────────────────────────────────────────────────────
const reportViewer = createReportViewer({
  root: document,
  canDesign: () => canDesign,
  notify: (message, options) => { ETLSQLFeedback.notify(message, options); },
  renderDag,
  subscribe: id => openSubscribeModal(id),
  // A report opened from a link has no folder behind it; Back then returns to the library home.
  back: () => { if (currentFolderId) loadReportList(currentFolderId, currentFolderName); else showReportLibraryHome(); },
  entered: (id, push, page) => {
    readerSubscriptions.leave();
    if (push) {
      history.pushState({ view: 'report', id, page, folderId: currentFolderId, folderName: currentFolderName },
        '', window.location.pathname);
    }
  },
  // userTriggered: the reader chose a tab, so Back should return to the previous one.
  // Otherwise it is an initial load or restore, which replaces the current entry.
  pageChanged: (id, page, userTriggered) => {
    const state = { view: 'report', id, page, folderId: currentFolderId, folderName: currentFolderName };
    if (userTriggered) history.pushState(state, '', window.location.href);
    else history.replaceState(state, '', window.location.href);
  }
});
window.addEventListener('pagehide', () => reportViewer.dispose(), { once: true });
function openReport(id, isPushState = true, initialPage = null) { return reportViewer.open(id, isPushState, initialPage); }
document.querySelectorAll('.modal-overlay').forEach(modal => {
  modal.addEventListener('click', e => {
    if (e.target === modal) /** @type {HTMLElement} */ (modal).style.display = 'none';
  });
});

document.addEventListener('keydown', e => {
  if (e.key !== 'Escape') return;
  const openModal = [...document.querySelectorAll('.modal-overlay')]
    .reverse()
    .find(m => /** @type {HTMLElement} */ (m).style.display !== 'none');
  if (openModal) /** @type {HTMLElement} */ (openModal).style.display = 'none';
});

// ── Subscriptions ─────────────────────────────────────────────────────────────
const readerSubscriptions = createReaderSubscriptions({
  root: document,
  notify: (message, options) => { ETLSQLFeedback.notify(message, options); },
  confirm: (message, options) => ETLSQLFeedback.confirm(message, options),
  entered: () => {
    clearFolderSelection();
    setSidebarViewActive('subscriptions');
    currentFolderId = null;
    currentFolderName = 'My Subscriptions';
  }
});
window.addEventListener('pagehide', () => readerSubscriptions.dispose(), { once: true });
function openSubscribeModal(reportId) { void readerSubscriptions.subscribe(reportId); }
function showMySubscriptions() { void readerSubscriptions.show(); }
// ── Utilities ──────────────────────────────────────────────────────────────────
function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function escAttr(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

init();
