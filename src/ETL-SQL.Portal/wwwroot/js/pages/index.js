/**
 * Page module for index.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { adminApi, auth, authApi, catalogApi, dataQualityApi, foldersApi, governanceApi, reportsApi, studioApi, subscriptionsApi } from '../api.js';
import { renderSubscriptionHistory } from '../subscription-history-ui.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { lineageRowsToCsv, renderDependencies, renderLineageRow } from '../lineage-ui.js';
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
let currentReportId   = null;
let currentFolderName = '';
let currentActivePage = null;  // tracks active report page for back/refresh
let isAdmin           = false;
let canDesign         = false;
let currentReports    = [];
let reportSearchTerm  = '';
let reportSortMode    = 'name';

// Track active page reported by the report iframe.
// userTriggered=true  → user clicked a tab → push a history entry (enables browser back)
// no userTriggered    → initial load or restore → replace current entry (no new back step)
window.addEventListener('message', e => {
  if (e.data && e.data.type === 'etl-page-changed') {
    currentActivePage = e.data.page;
    const state = { view: 'report', id: currentReportId, page: currentActivePage,
                    folderId: currentFolderId, folderName: currentFolderName };
    if (e.data.userTriggered) {
      history.pushState(state, '', window.location.href);
    } else {
      history.replaceState(state, '', window.location.href);
    }
  }
});

// Browser back/forward: restore the correct view
window.addEventListener('popstate', e => {
  const s = e.state;
  if (!s) return;
  if (s.view === 'folder') {
    currentFolderId   = s.folderId;
    currentFolderName = s.folderName;
    currentReportId   = null;
    currentActivePage = null;
    loadReportList(s.folderId, s.folderName, /*pushState=*/false);
  } else if (s.view === 'report') {
    currentFolderId   = s.folderId;
    currentFolderName = s.folderName;
    currentActivePage = s.page || null;
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
    const isGovUser = hasRole(identity, 'Admin', 'GovernanceManager', 'DataSteward', 'GovernanceViewer');
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

    canGovernanceOverview =
      hasRole(identity, 'Admin', 'GovernanceManager', 'DataSteward', 'GovernanceViewer');
    canQuarantine = hasRole(identity, 'Admin', 'DataSteward');
  } catch {}
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

  wireSubscribeModal();
  wireEditParamsModal();
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
  currentReportId = null;
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
  currentReportId   = null;
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
    const previewHtml = buildRuntimeHtml(r.id, true).replace(/"/g, '&quot;');

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

function formatDuration(ms) {
  const value = Number(ms);
  if (!Number.isFinite(value)) return '';
  if (value < 1000) return `${value} ms`;
  const seconds = Math.round(value / 100) / 10;
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  const remaining = Math.round(seconds % 60);
  return `${minutes}m ${remaining}s`;
}

function formatBuiltAt(value) {
  return value ? new Date(value).toLocaleString() : 'Never run';
}

function timeAgo(value) {
  if (!value) return 'Never run';
  const ms = Date.now() - new Date(value).getTime();
  if (!Number.isFinite(ms) || ms < 0) return formatBuiltAt(value);
  const minutes = Math.floor(ms / 60000);
  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hr ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} day${days === 1 ? '' : 's'} ago`;
  return formatBuiltAt(value);
}

// ── Report viewer ──────────────────────────────────────────────────────────────
async function openReport(id, isPushState = true, initialPage = null) {
  currentReportId = id;
  if (initialPage) currentActivePage = initialPage;
  if (isPushState) {
    history.pushState(
      { view: 'report', id, page: initialPage, folderId: currentFolderId, folderName: currentFolderName },
      '',
      window.location.pathname);
  }
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading report…</span></div>`;

  let report, snapshot;
  try {
    report = await reportsApi.get(id);
    snapshot = await reportsApi.getSnapshot(id);
  } catch (err) {
    if (err.status === 404 && report) { await renderNoSnapshot(report); return; }
    $main.innerHTML = `<div class="empty-state">Failed to load report: ${esc(err.message)}</div>`;
    return;
  }
  renderViewer(report, snapshot, initialPage);
}

async function renderNoSnapshot(report) {
  const id = report.id;
  const $main = document.getElementById('mainContent');
  let params = [];
  let parameterError = null;
  try {
    params = await reportsApi.getParameters(id);
  } catch (err) {
    parameterError = err.message || 'Parameter preflight failed.';
  }
  $main.innerHTML = `
    <div class="report-viewer">
      <div class="viewer-commandbar">
        <button class="btn btn-ghost btn-sm" id="backBtn">Back</button>
        <div class="viewer-header-info">
          <span class="library-kicker">Report</span>
          <h2>${esc(report.name)}</h2>
          <p>${esc(report.description || 'No snapshot is available yet.')}</p>
        </div>
        <div class="viewer-actions">
          <button class="btn btn-primary btn-sm" id="execBtn">Run Report</button>
          ${canDesign ? `<button class="btn btn-outline btn-sm" id="designBtn">Design</button>` : ''}
          <button class="btn btn-outline btn-sm" disabled title="Run the report before subscribing">Subscribe</button>
        </div>
      </div>
      <div class="viewer-body viewer-body-empty">
        <div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>${params.length ? 'Set parameters, then run' : 'Ready for first run'}</h2>
          <p>One run generates the first snapshot. Export and subscriptions become available after it completes.</p>
          ${parameterError ? `<div class="status-banner status-banner-danger"><span>Parameter preflight failed: ${esc(parameterError)}</span></div>` : ''}
          <div id="runParameterFields" class="param-grid"></div>
          <div id="runParameterError" class="error-msg" role="alert"></div>
        </div>
      </div>
    </div>`;

  renderParamFields('runParameterFields', params, {});
  const runButton = document.getElementById('execBtn');
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (runButton).disabled = Boolean(parameterError);
  runButton.addEventListener('click', () => {
    const validation = validateParamFields('runParameterFields', params);
    const error = document.getElementById('runParameterError');
    if (!validation.ok) {
      error.textContent = 'Complete the required report parameters before running.';
      error.classList.add('show');
      return;
    }
    error.textContent = '';
    error.classList.remove('show');
    runAndPoll(id, report, validation.values);
  });
  document.getElementById('backBtn').addEventListener('click', () =>
    currentFolderId ? loadReportList(currentFolderId, currentFolderName) : null);
  document.getElementById('designBtn')?.addEventListener('click', () => {
    window.location.href = `/designer.html?id=${id}`;
  });
}

function renderViewer(report, snapshot, initialPage = null) {
  const $main = document.getElementById('mainContent');
  const builtAt = formatBuiltAt(snapshot.builtAt);
  const staleHtml = snapshot.isStale
    ? `<div class="status-banner status-banner-warning">
         <span class="status-dot" aria-hidden="true"></span>
         <span><b>Needs refresh.</b> This snapshot was built ${esc(timeAgo(snapshot.builtAt))} (${esc(builtAt)}).</span>
         <button class="btn btn-sm btn-outline" id="staleRefreshBtn">Refresh Now</button>
       </div>` : '';

  $main.innerHTML = `
    <div class="report-viewer">
      <div class="viewer-commandbar">
        <button class="btn btn-ghost btn-sm" id="backBtn">Back</button>
        <div class="viewer-header-info">
          <span class="library-kicker">Report</span>
          <h2>${esc(report.name)}</h2>
          <p>${esc(report.description || 'No description provided.')}</p>
          <div class="viewer-meta">
            <span class="viewer-meta-pill">Built ${esc(timeAgo(snapshot.builtAt))}</span>
            <span class="viewer-meta-pill">${esc(builtAt)}</span>
            ${snapshot.isStale ? '<span class="viewer-meta-pill viewer-meta-warning">Stale</span>' : '<span class="viewer-meta-pill viewer-meta-ready">Ready</span>'}
          </div>
        </div>
        <div class="viewer-actions">
          <button class="btn btn-primary btn-sm" id="refreshBtn">Refresh</button>
          ${canDesign ? `<button class="btn btn-outline btn-sm" id="designBtn">Design</button>` : ''}
          <button class="btn btn-outline btn-sm" id="structureBtn">Structure</button>
          <button class="btn btn-outline btn-sm" id="dependenciesBtn">Dependencies</button>
          <button class="btn btn-outline btn-sm" id="historyBtn">History</button>
          <button class="btn btn-outline btn-sm" id="exportBtn">Export</button>
          <button class="btn btn-outline btn-sm" id="subBtn">Subscribe</button>
        </div>
      </div>
      ${staleHtml}
      <div class="viewer-body viewer-frame-shell" id="reportFrame">
        <div class="loading-state"><span class="spinner"></span><span>Rendering report…</span></div>
      </div>
    </div>`;

  document.getElementById('backBtn').addEventListener('click', () => {
    if (currentFolderId) loadReportList(currentFolderId, currentFolderName);
  });
  document.getElementById('designBtn')?.addEventListener('click', () => {
    window.location.href = `/designer.html?id=${report.id}`;
  });
  document.getElementById('refreshBtn').addEventListener('click', () => runAndPoll(report.id, report));
  document.getElementById('structureBtn').addEventListener('click', () => showStructure(report.id));
  document.getElementById('dependenciesBtn').addEventListener('click', () => showDependencies(report.id));
  document.getElementById('historyBtn').addEventListener('click', () => showHistory(report.id));
  document.getElementById('exportBtn').addEventListener('click', () => showExport(report.id));
  document.getElementById('subBtn').addEventListener('click', () => openSubscribeModal(report.id));
  const staleBtn = document.getElementById('staleRefreshBtn');
  if (staleBtn) staleBtn.addEventListener('click', () => runAndPoll(report.id, report));

  loadReportFrame(report.id, initialPage || currentActivePage);
}

function loadReportFrame(id, initialPage = null) {
  const $frame = document.getElementById('reportFrame');
  if (!$frame) return;
  const iframe = document.createElement('iframe');
  iframe.title = 'Report viewer';
  iframe.style.cssText = 'width:100%;height:100%;border:none;';
  iframe.srcdoc = buildRuntimeHtml(id, false, initialPage);
  $frame.innerHTML = '';
  $frame.appendChild(iframe);
}

/**
 * The CSP nonce this page was served with.
 *
 * The report viewer is a `srcdoc` iframe, which inherits this document's CSP — including
 * `script-src 'self' 'nonce-…'` — so the scripts in the document below have to carry the nonce or
 * the browser refuses to run them, and the report never boots.
 *
 * They used to carry it by accident. This template lived inside an inline block in index.html, and
 * `SecurityHeadersMiddleware` rewrites every literal `<script` in an .html response, so it rewrote
 * these ones too, inside a JavaScript string it had no idea it was editing. That does not happen to
 * a .js file, so the nonce is read here and written in on purpose.
 *
 * `getAttribute('nonce')` is empty by design once CSP is active — the `.nonce` property is the one
 * that still answers.
 */
const CSP_NONCE = /** @type {HTMLScriptElement | null} */ (
  document.querySelector('script[nonce]'))?.nonce ?? '';

function buildRuntimeHtml(id, isPreview = false, initialPage = null) {
  const token = auth.getToken() || '';
  const initialPageJs = initialPage
    ? `window.__INITIAL_PAGE__ = ${JSON.stringify(initialPage)};` : '';
  const isDark = document.body.classList.contains('theme-dark') ? 'true' : 'false';
  return `<!DOCTYPE html>
<html><head>
<meta charset="UTF-8">
<link rel="stylesheet" href="/css/report-runtime.css?v=0.18.0">
<script nonce="${CSP_NONCE}">
  window.__IS_WEB__     = true;
  window.__IS_PREVIEW__ = ${isPreview};
  window.__API_BASE__   = '/api/reports/${id}';
  ${initialPageJs}
  const _f = window.fetch.bind(window);
  window.fetch = (input, init={}) => {
    const url = typeof input === 'string' ? input : input.url;
    if (url.startsWith('/api/')) {
      const h = new Headers(init.headers||{});
      if (!h.has('Authorization')) h.set('Authorization', 'Bearer ${token.replace(/\\/g, "\\\\").replace(/'/g, "\\'")}');
      return _f(input, {...init, headers:h});
    }
    return _f(input, init);
  };
<\/script>
</head><body style="margin:0" class="${isDark === 'true' ? 'theme-dark' : ''}">
<div id="root"></div>
<script nonce="${CSP_NONCE}" src="/js/report-runtime.js?v=0.18.0"><\/script>
<script nonce="${CSP_NONCE}">
  if (${isDark} || (window.parent && window.parent.document.body.classList.contains('theme-dark'))) {
    document.body.classList.add('theme-dark');
  }
<\/script>
</body></html>`;
}


// ── Execute / Refresh with polling ────────────────────────────────────────────
async function runAndPoll(id, report = null, parameters = {}) {
  const savedPage = currentActivePage;  // remember which tab the user was on
  const $main = document.getElementById('mainContent');
  const title = report?.name || 'Report';
  const description = report?.description || 'The report is running. You can leave this view and return when it completes.';
  $main.innerHTML = `
    <div class="report-viewer">
      <div class="viewer-commandbar">
        <button class="btn btn-ghost btn-sm" id="backBtn">Back</button>
        <div class="viewer-header-info">
          <span class="library-kicker">Running</span>
          <h2>${esc(title)}</h2>
          <p>${esc(description)}</p>
        </div>
        <div class="viewer-actions">
          <button class="btn btn-primary btn-sm" disabled>Running</button>
          <button class="btn btn-outline btn-sm" disabled>Export</button>
          <button class="btn btn-outline btn-sm" disabled>Subscribe</button>
        </div>
      </div>
      <div class="status-banner status-banner-info" id="pollBanner">
        <span class="spinner"></span>
        <span><b>Running report.</b> Building a fresh snapshot and preserving your current page.</span>
      </div>
      <div class="viewer-body viewer-body-empty">
        <div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>Waiting for results</h2>
          <p>The viewer will refresh automatically when the run finishes.</p>
        </div>
      </div>
    </div>`;

  document.getElementById('backBtn').addEventListener('click', () => {
    if (currentFolderId) loadReportList(currentFolderId, currentFolderName);
  });

  let jobId;
  try {
    const res = await reportsApi.execute(id, parameters);
    jobId = res.jobId;
  } catch (err) {
    renderRunFailure(id, report, `Failed to start job: ${err.message}`);
    return;
  }

  for (let i = 0; i < 120; i++) {
    await sleep(1500);
    try {
      const job = await reportsApi.pollJob(jobId);
      if (job.status === 'Completed') {
        currentActivePage = savedPage;  // restore before openReport renders the iframe
        openReport(id, false);
        return;
      }
      if (job.status === 'Failed') {
        renderRunFailure(id, report, `Report execution failed: ${job.error || 'unknown error'}`);
        return;
      }
      if (job.status === 'Cancelled') {
        renderRunFailure(id, report, job.error || 'Report execution was cancelled.');
        return;
      }
    } catch {}
  }
  renderRunFailure(id, report, 'Timed out waiting for report execution.');
}

function renderRunFailure(id, report, message) {
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `
    <div class="report-viewer">
      <div class="viewer-commandbar">
        <button class="btn btn-ghost btn-sm" id="backBtn">Back</button>
        <div class="viewer-header-info">
          <span class="library-kicker">Run failed</span>
          <h2>${esc(report?.name || 'Report')}</h2>
          <p>${esc(message)}</p>
        </div>
        <div class="viewer-actions">
          <button class="btn btn-primary btn-sm" id="retryRunBtn">Retry</button>
          <button class="btn btn-outline btn-sm" ${report?.hasSnapshot ? 'id="subBtn"' : 'disabled title="A successful snapshot is required"'}>Subscribe</button>
        </div>
      </div>
      <div class="status-banner status-banner-danger">
        <span class="status-dot" aria-hidden="true"></span>
        <span><b>Run did not complete.</b> Review the message above, then retry when ready.</span>
      </div>
      <div class="viewer-body viewer-body-empty">
        <div class="empty-state empty-state-panel empty-state-error">
          <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
          <h2>Could not build snapshot</h2>
          <p>${esc(message)}</p>
        </div>
      </div>
    </div>`;

  document.getElementById('retryRunBtn').addEventListener('click', () => runAndPoll(id, report));
  document.getElementById('subBtn')?.addEventListener('click', () => openSubscribeModal(id));
  document.getElementById('backBtn').addEventListener('click', () => {
    if (currentFolderId) loadReportList(currentFolderId, currentFolderName);
  });
}

// ── Structure DAG modal ────────────────────────────────────────────────────────
let structureDagInstance = null;

async function showStructure(id) {
  const $modal = document.getElementById('structureModal');
  const $dag   = document.getElementById('structureDag');
  $modal.style.display = 'flex';
  $dag.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading structure…</span></div>';
  if (structureDagInstance) { structureDagInstance.dispose(); structureDagInstance = null; }

  try {
    const res = await fetch(`/api/reports/${id}/structure`, {
      headers: { Authorization: `Bearer ${auth.getToken()}` }
    });
    if (!res.ok) throw new Error(await res.text());
    const data = await res.json();
    $dag.innerHTML = '';
    structureDagInstance = renderDag($dag, data);
  } catch (err) {
    $dag.innerHTML = `<div class="empty-state">Failed to load structure: ${esc(err.message)}</div>`;
  }
}

document.getElementById('structureCloseBtn').addEventListener('click', () => {
  document.getElementById('structureModal').style.display = 'none';
  if (structureDagInstance) { structureDagInstance.dispose(); structureDagInstance = null; }
});

// ── Export modal ───────────────────────────────────────────────────────────────
function showExport(id) {
  const $modal = document.getElementById('exportModal');
  $modal.style.display = 'flex';
  const doExport = async (format) => {
    $modal.style.display = 'none';
    try { await reportsApi.exportFile(id, format); }
    catch (err) { ETLSQLFeedback.notify(err.message || 'Export failed.', { title: 'Export failed', tone: 'error' }); }
  };
  document.getElementById('exportCsvBtn').onclick  = () => doExport('csv');
  document.getElementById('exportXlsxBtn').onclick = () => doExport('xlsx');
  document.getElementById('exportPdfBtn').onclick  = () => doExport('pdf');
  document.getElementById('exportCancelBtn').onclick = () => { $modal.style.display = 'none'; };
}

async function showDependencies(id) {
  const $modal = document.getElementById('dependenciesModal');
  const $body = document.getElementById('dependenciesBody');
  $modal.style.display = 'flex';
  $body.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading dependencies…</span></div>`;

  try {
    const data = await reportsApi.dependencies(id);
    // Fetch downstream impact for each unique source table (cap at 10 sources)
    const sourceNames = [...new Set((data.sources || []).map(s => s.name || s.objectName).filter(Boolean))].slice(0, 10);
    let downstream = [];
    if (sourceNames.length > 0) {
      try {
        const results = await Promise.all(
          sourceNames.map(t => fetch(`/api/catalog/lineage/downstream?table=${encodeURIComponent(t)}`).then(r => r.ok ? r.json() : []))
        );
        const seen = new Map();
        for (const list of results) {
          for (const item of list) {
            const key = item.reportId ?? item.reportName ?? '';
            if (key && (!seen.has(key) || seen.get(key).lastSeen < item.lastSeen))
              seen.set(key, item);
          }
        }
        downstream = [...seen.values()].sort((a, b) => new Date(b.lastSeen).getTime() - new Date(a.lastSeen).getTime());
      } catch { /* catalog may be empty — silently skip */ }
    }
    $body.innerHTML = renderDependencies(data, downstream, { formatBuiltAt });
  } catch (err) {
    $body.innerHTML = `<div class="empty-state">Failed to load dependencies: ${esc(err.message)}</div>`;
  }
}

document.getElementById('dependenciesCloseBtn').addEventListener('click', () => {
  document.getElementById('dependenciesModal').style.display = 'none';
});

document.getElementById('dependenciesModal').addEventListener('click', e => {
  const link = /** @type {Element} */ (e.target).closest('[data-downstream-report-id]');
  if (!link) return;
  e.preventDefault();
  const rid = parseInt(/** @type {HTMLElement} */ (link).dataset.downstreamReportId, 10);
  if (Number.isInteger(rid) && rid > 0) openReport(rid);
});

async function showHistory(id) {
  const $modal = document.getElementById('historyModal');
  const $body = document.getElementById('historyBody');
  $modal.style.display = 'flex';
  $body.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading history…</span></div>`;

  try {
    const data = await reportsApi.history(id);
    $body.innerHTML = renderHistory(data);
  } catch (err) {
    $body.innerHTML = `<div class="empty-state">Failed to load history: ${esc(err.message)}</div>`;
  }
}

function renderHistory(data) {
  const drift = data.scriptChanged
    ? '<span class="viewer-meta-pill viewer-meta-warning">Script changed</span>'
    : '<span class="viewer-meta-pill viewer-meta-ready">Pinned hash current</span>';
  const snapshotRows = (data.snapshots || []).map(s => `
    <tr>
      <td>${esc(formatBuiltAt(s.builtAt))}</td>
      <td>${esc(s.builtBy)}</td>
      <td>${esc(s.hashMatched === null || s.hashMatched === undefined ? 'Unknown' : (s.hashMatched ? 'Matched' : 'Changed'))}</td>
      <td class="history-code-cell"><code>${esc(s.scriptHashAtRunTime || 'Not recorded')}</code></td>
    </tr>`).join('');
  const changeRows = (data.changes || []).map(c => `
    <tr>
      <td>${esc(formatBuiltAt(c.timestamp))}</td>
      <td>${esc(c.action)}</td>
      <td>${esc(c.userId ?? '')}</td>
      <td class="history-detail-cell">${esc(c.detail || '')}</td>
    </tr>`).join('');

  return `
    <div class="dependency-summary">
      <span>${esc(data.report?.folderPath || '')} / ${esc(data.report?.name || 'Report')}</span>
      ${drift}
    </div>
    <div class="history-hash-grid">
      <div><span>Published hash</span><code>${esc(data.publishedScriptHash || 'Not recorded')}</code></div>
      <div><span>Current hash</span><code>${esc(data.currentScriptHash || 'Unavailable')}</code></div>
    </div>
    ${renderHistoryTable('Snapshots', 'snapshot-history-table', ['Built', 'User', 'Hash', 'Runtime Hash'], snapshotRows)}
    ${renderHistoryTable('Changes', 'change-history-table', ['Time', 'Action', 'User', 'Detail'], changeRows)}`;
}

function renderHistoryTable(title, className, headers, rows) {
  return `
    <div class="dependency-section history-section">
      <h4>${esc(title)}</h4>
      ${rows ? `<div class="history-table-scroll"><table class="dependency-table history-table ${className}">
        <thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead>
        <tbody>${rows}</tbody>
      </table></div>` : '<p class="text-muted">No data available.</p>'}
    </div>`;
}

document.getElementById('historyCloseBtn').addEventListener('click', () => {
  document.getElementById('historyModal').style.display = 'none';
});

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

// ── Subscribe modal ────────────────────────────────────────────────────────────
let _subReportId = null;
let _subParams   = [];    // ReportParameterDto[]

async function openSubscribeModal(reportId) {
  _subReportId = reportId;
  _subParams   = [];

  // Reset form
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-name')).value     = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-schedule')).value = 'Daily';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-attime')).value   = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-format')).value   = 'PDF';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-email')).value    = '';
  document.getElementById('sub-error').textContent = '';
  document.getElementById('sub-error').classList.remove('show');
  document.getElementById('sub-params-section').style.display = 'none';
  document.getElementById('sub-params-fields').innerHTML = '';

  // Load SMTP aliases into dropdown
  const $smtp = document.getElementById('sub-smtp');
  try {
    const aliases = await subscriptionsApi.smtpAliases();
    $smtp.innerHTML = aliases.length
      ? aliases.map(a => `<option value="${esc(a)}">${esc(a)}</option>`).join('')
      : '<option value="">No SMTP connections configured</option>';
  } catch {
    $smtp.innerHTML = '<option value="">Error loading connections</option>';
  }

  // Fetch report INPUT parameters
  try {
    _subParams = await reportsApi.getParameters(reportId);
    if (_subParams.length) {
      document.getElementById('sub-params-section').style.display = '';
      renderParamFields('sub-params-fields', _subParams, {});
    }
  } catch { /* parameters section stays hidden */ }

  document.getElementById('subscribeModal').style.display = 'flex';
}

function wireSubscribeModal() {
  // Toggle SMTP group visibility based on format
  document.getElementById('sub-format').addEventListener('change', function() {
    document.getElementById('sub-smtp-group').style.display =
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (this).value === 'Link' ? 'none' : '';
  });

  document.getElementById('sub-cancelBtn').addEventListener('click', () => {
    document.getElementById('subscribeModal').style.display = 'none';
  });

  document.getElementById('sub-saveBtn').addEventListener('click', async () => {
    const $err = document.getElementById('sub-error');
    $err.classList.remove('show');

    const format = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-format')).value;
    const smtp   = format !== 'Link' ? /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-smtp')).value : null;

    // Collect parameters from dynamic fields
    const validation = validateParamFields('sub-params-fields', _subParams);
    if (!validation.ok) {
      $err.textContent = 'Complete the required report parameters before creating the subscription.';
      $err.classList.add('show');
      return;
    }
    const parameters = validation.values;

    const body = {
      reportId:       _subReportId,
      name:           /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-name')).value.trim() || null,
      schedule:       /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-schedule')).value,
      format,
      smtpAlias:      smtp || null,
      recipientEmail: /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-email')).value.trim() || null,
      atTime:         /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('sub-attime')).value || null,
      parameters:     Object.keys(parameters).length ? parameters : null
    };

    try {
      await subscriptionsApi.create(body);
      document.getElementById('subscribeModal').style.display = 'none';
      ETLSQLFeedback.notify('Future deliveries are now scheduled.', { title: 'Subscription created', tone: 'success', auditAction: 'subscription.create' });
    } catch (err) {
      $err.textContent = err.message || 'Failed to create subscription.';
      $err.classList.add('show');
    }
  });
}

// ── My Subscriptions view ──────────────────────────────────────────────────────
async function showMySubscriptions() {
  clearFolderSelection();
  setSidebarViewActive('subscriptions');
  currentFolderId = null;
  currentFolderName = 'My Subscriptions';
  const $main = document.getElementById('mainContent');
  $main.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading subscriptions…</span></div>`;

  try {
    const subs = await subscriptionsApi.list();
    mySubscriptions = subs;
    renderMySubscriptions(subs);
  } catch {
    $main.innerHTML = `
      <div class="empty-state empty-state-panel empty-state-error">
        <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
        <h2>Subscriptions did not load</h2>
        <p>Refresh the page or try again after checking the portal connection.</p>
      </div>`;
  }
}

let mySubscriptions = [];

function renderMySubscriptions(subs) {
  const $main = document.getElementById('mainContent');
  const activeCount = subs.filter(s => s.isActive).length;
  const rows = subs.map(s => `
    <tr>
      <td>
        <div class="sub-name">${esc(s.name || s.reportName)}</div>
        <div class="sub-report">${esc(s.reportName)}</div>
      </td>
      <td>
        <span class="sub-format">${esc(s.format)}</span>
        <div class="sub-report">${esc(s.recipients || (s.format === 'Link' ? 'Portal link' : 'Profile email'))}</div>
      </td>
      <td>
        <div>${esc(s.schedule || (s.deliverOnRefresh ? 'On refresh' : 'Manual'))}</div>
        <div class="sub-report">Next: ${esc(formatOptionalDate(s.nextRunAt))}</div>
      </td>
      <td class="text-sm text-muted">${esc(s.parameterSummary || 'Default parameters')}</td>
      <td>
        <span class="chip ${s.isActive ? 'chip-active' : 'chip-inactive'}">${s.isActive ? 'Active' : 'Paused'}</span>
        ${s.failCount ? `<div class="sub-warning">${s.failCount} failed send${s.failCount === 1 ? '' : 's'}</div>` : `<div class="sub-report">Last: ${esc(formatOptionalDate(s.lastSentAt))}</div>`}
      </td>
      <td>
        <div class="table-actions">
          <button class="btn btn-outline btn-sm" data-action="edit-params" data-id="${s.id}"
                  data-report-id="${s.reportId}" data-name="${escAttr(s.name || s.reportName)}"
                  data-params='${escAttr(JSON.stringify(s.parameters || {}))}'>
            Edit Params
          </button>
          <button class="btn btn-outline btn-sm" data-action="history" data-id="${s.id}"
                  data-name="${escAttr(s.name || s.reportName)}">History</button>
          <button class="btn btn-outline btn-sm" data-action="toggle" data-id="${s.id}"
                  data-active="${s.isActive}">${s.isActive ? 'Pause' : 'Resume'}</button>
          <button class="btn btn-outline btn-sm btn-danger-soft" data-action="delete" data-id="${s.id}">Delete</button>
        </div>
      </td>
    </tr>`).join('');

  $main.innerHTML = `
    <div class="library-toolbar">
      <div class="library-title">
        <span class="library-kicker">Delivery</span>
        <h2>My Subscriptions</h2>
      </div>
      <span class="badge badge-ok">${activeCount} active</span>
      <span class="badge badge-refresh">${subs.length} total</span>
      <div class="library-toolbar-spacer"></div>
      <button class="btn btn-outline" id="refreshSubscriptionsBtn" type="button">Refresh</button>
    </div>
    <div class="subs-table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th>Subscription</th><th>Delivery</th><th>Schedule</th>
            <th>Parameters</th><th>Status</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>${rows || `<tr><td colspan="6">
          <div class="empty-state empty-state-table">
            <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
            <h2>No subscriptions yet</h2>
            <p>Open a report and choose Subscribe to schedule delivery.</p>
          </div>
        </td></tr>`}</tbody>
      </table>
    </div>`;

  document.getElementById('refreshSubscriptionsBtn')?.addEventListener('click', () => showMySubscriptions());
  $main.querySelectorAll('[data-action]').forEach(btn => {
    btn.addEventListener('click', () => handleSubAction(btn));
  });
}

async function handleSubAction(btn) {
  const id     = +btn.dataset.id;
  const action = btn.dataset.action;
  if (action === 'delete') {
    if (!await ETLSQLFeedback.confirm('Delete this subscription?', { title: 'Delete subscription', impact: 'This stops future deliveries but does not remove generated report snapshots.', confirmLabel: 'Delete subscription', danger: true, auditAction: 'subscription.delete' })) return;
    const subscription = mySubscriptions.find(x => x.id === id);
    try { await subscriptionsApi.delete(id, subscription?.version); showMySubscriptions(); }
    catch (err) { ETLSQLFeedback.notify(err.message, { title: 'Subscription not deleted', tone: 'error' }); }
  } else if (action === 'toggle') {
    const isActive = btn.dataset.active === 'true';
    const subscription = mySubscriptions.find(x => x.id === id);
    try { await subscriptionsApi.update(id, { isActive: !isActive }, subscription?.version); showMySubscriptions(); }
    catch (err) { ETLSQLFeedback.notify(err.message, { title: 'Subscription not updated', tone: 'error' }); }
  } else if (action === 'history') {
    showSubscriptionHistory(id, btn.dataset.name);
  } else if (action === 'edit-params') {
    const currentParams = JSON.parse(btn.dataset.params || '{}');
    const reportId      = +btn.dataset.reportId;
    const label         = btn.dataset.name;
    const subscription = mySubscriptions.find(x => x.id === id);
    openEditParamsModal(id, reportId, label, currentParams, subscription?.version, () => showMySubscriptions());
  }
}

async function showSubscriptionHistory(id, name) {
  const $modal = document.getElementById('subscriptionHistoryModal');
  const $body = document.getElementById('subscriptionHistoryBody');
  document.getElementById('subscriptionHistoryTitle').textContent = `${name || 'Subscription'} Delivery History`;
  $modal.style.display = 'flex';
  $body.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading delivery history…</span></div>';
  try {
    const history = await subscriptionsApi.history(id);
    $body.innerHTML = renderSubscriptionHistory(history, {
      esc,
      formatDate: value => value ? new Date(value).toLocaleString() : '—',
    });
  } catch (err) {
    $body.innerHTML = `<div class="empty-state">Failed to load delivery history: ${esc(err.message)}</div>`;
  }
}

document.getElementById('subscriptionHistoryCloseBtn').addEventListener('click', () => {
  document.getElementById('subscriptionHistoryModal').style.display = 'none';
});

// ── Edit Parameters modal ─────────────────────────────────────────────────────
let _epSubId      = null;
let _epParams     = [];
let _epOnSaved    = null;
let _epVersion    = null;

async function openEditParamsModal(subId, reportId, label, currentValues, version, onSaved) {
  _epSubId   = subId;
  _epParams  = [];
  _epOnSaved = onSaved;
  _epVersion = version;

  document.getElementById('ep-subtitle').textContent = label;
  document.getElementById('ep-error').textContent    = '';
  document.getElementById('ep-error').classList.remove('show');
  document.getElementById('ep-fields').innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading parameters…</span></div>';
  document.getElementById('editParamsModal').style.display = 'flex';

  try {
    _epParams = await reportsApi.getParameters(reportId);
  } catch { _epParams = []; }

  if (!_epParams.length) {
    // Fall back to editing existing params as plain key-value pairs
    _epParams = Object.keys(currentValues).map(k => ({ name: k, type: 'TEXT', default: currentValues[k], required: false, description: null }));
  }

  renderParamFields('ep-fields', _epParams, currentValues);
}

function wireEditParamsModal() {
  document.getElementById('ep-cancelBtn').addEventListener('click', () => {
    document.getElementById('editParamsModal').style.display = 'none';
  });
  document.getElementById('ep-saveBtn').addEventListener('click', async () => {
    const $err = document.getElementById('ep-error');
    $err.classList.remove('show');
    const validation = validateParamFields('ep-fields', _epParams);
    if (!validation.ok) {
      $err.textContent = 'Complete the required report parameters before saving.';
      $err.classList.add('show');
      return;
    }
    const parameters = validation.values;
    try {
      await subscriptionsApi.update(_epSubId, { parameters: Object.keys(parameters).length ? parameters : null }, _epVersion);
      document.getElementById('editParamsModal').style.display = 'none';
      _epOnSaved?.();
    } catch (err) {
      $err.textContent = err.message || 'Failed to save parameters.';
      $err.classList.add('show');
    }
  });
}

// ── Parameter field rendering helpers ─────────────────────────────────────────
const RELDATE_PICKS = ['Today', 'D-1', 'D-7', 'D-30', 'M-1', 'M-3', 'Y-1'];

function renderParamFields(containerId, params, currentValues) {
  const $container = document.getElementById(containerId);
  $container.innerHTML = '';

  if (!params.length) {
    $container.innerHTML = '<div class="param-empty">This report does not define parameters.</div>';
    return;
  }

  params.forEach(p => {
    const inputId = `param-${containerId}-${p.name.replace(/[^a-z0-9]/gi, '_')}`;
    const current = currentValues[p.name] ?? p.default ?? '';
    const isReldate = p.type?.toUpperCase() === 'RELDATE';

    const $row = document.createElement('div');
    $row.className = 'param-row';

    const required = p.required ? '<span class="required-marker">Required</span>' : '<span class="optional-marker">Optional</span>';
    const hint  = p.description ? `<div class="param-hint">${esc(p.description)}</div>` : '';
    $row.innerHTML = `
      <div class="param-heading">
        <label class="param-label" for="${inputId}">${esc(p.name)}</label>
        <span class="param-type">${esc(p.type)}</span>
        ${required}
      </div>
      ${hint}
      <div class="inline-control">
        <input class="param-input" type="text" id="${inputId}" data-param="${esc(p.name)}"
               data-required="${p.required ? 'true' : 'false'}"
               value="${esc(String(current))}" placeholder="${p.required ? 'Required value' : 'Optional value'}">
        ${isReldate ? `
          <button type="button" class="btn btn-outline" data-date-picker="${inputId}-picker">📅</button>
          <input type="date" id="${inputId}-picker" class="sr-date-input" data-date-target="${inputId}">
        ` : ''}
      </div>
      ${isReldate ? `<div class="reldate-quickpicks">${RELDATE_PICKS.map(q => `<button type="button" data-pick="${q}" data-for="${inputId}">${q}</button>`).join('')}</div>` : ''}`;

    $container.appendChild($row);
  });

  // Wire quick-pick buttons
  $container.querySelectorAll('[data-pick]').forEach(btn => {
    btn.addEventListener('click', () => {
      const pick = /** @type {HTMLElement} */ (btn).dataset.pick === 'Today' ? 'D-0' : /** @type {HTMLElement} */ (btn).dataset.pick;
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById(/** @type {HTMLElement} */ (btn).dataset.for)).value = pick;
    });
  });
  $container.querySelectorAll('[data-date-picker]').forEach(btn => {
    btn.addEventListener('click', () => {
      /** @type {HTMLInputElement | HTMLSelectElement} */ (document.getElementById(/** @type {HTMLElement} */ (btn).dataset.datePicker)).showPicker();
    });
  });
  $container.querySelectorAll('[data-date-target]').forEach(input => {
    input.addEventListener('change', () => {
      /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById(/** @type {HTMLElement} */ (input).dataset.dateTarget)).value = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (input).value;
    });
  });
}

function collectParamValues(containerId, params) {
  const result = {};
  const $container = document.getElementById(containerId);
  $container.querySelectorAll('[data-param]').forEach(input => {
    const val = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (input).value.trim();
    if (val) result[/** @type {HTMLElement} */ (input).dataset.param] = val;
  });
  return result;
}

function validateParamFields(containerId, params) {
  const values = collectParamValues(containerId, params);
  let ok = true;
  const $container = document.getElementById(containerId);
  $container.querySelectorAll('[data-param]').forEach(input => {
    const missing = /** @type {HTMLElement} */ (input).dataset.required === 'true' && !/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (input).value.trim();
    input.classList.toggle('param-input-error', missing);
    const row = input.closest('.param-row');
    row?.classList.toggle('param-row-error', missing);
    ok = ok && !missing;
  });
  return { ok, values };
}

function formatOptionalDate(value) {
  return value ? new Date(value).toLocaleString() : 'Not scheduled';
}

// ── Export modal ───────────────────────────────────────────────────────────────
function showExportModal() {}  // defined inline above

// ── Utilities ──────────────────────────────────────────────────────────────────
function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function escAttr(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}
function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

init();
