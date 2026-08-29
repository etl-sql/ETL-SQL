// UI Sandbox runtime — Category accordions, instant fuzzy filter, deep linking, theme switching & mounting.
import { categoryOrder, stories } from './stories/index.js';

const categoryIcons = {
  'Admin & Fleet': '⚙️',
  'Control Plane & SaaS': '🚀',
  'Orchestrator & Jobs': '⏱️',
  'Governance & Security': '🛡️',
  'Lineage & Graphs': '🕸️',
  'Designers & Visuals': '📊',
  'Script Editors & IDE': '📝',
  'Portal Shell & Views': '🖼️',
  'Other Surfaces': '📦'
};

// DOM Elements
const $sidebarCollapseBtn = document.getElementById('sidebarCollapseBtn');
const $sidebarToggleBtn   = document.getElementById('sidebarToggleBtn');
const $storiesNav       = document.getElementById('storiesNav');
const $searchInput      = document.getElementById('searchInput');
const $searchClearBtn   = document.getElementById('searchClearBtn');
const $filterChips      = document.getElementById('filterChips');
const $showingCountText = document.getElementById('showingCountText');
const $collapseAllBtn   = document.getElementById('collapseAllBtn');
const $storyCountBadge  = document.getElementById('storyCountBadge');

const $storyCategory    = document.getElementById('storyCategory');
const $storyTitle       = document.getElementById('storyTitle');
const $storySubtitle    = document.getElementById('storySubtitle');
const $fixtureLabel     = document.getElementById('fixtureLabel');
const $fixtureSel       = document.getElementById('fixtureSel');
const $themeToggleBtn   = document.getElementById('themeToggleBtn');
const $copyLinkBtn      = document.getElementById('copyLinkBtn');
const $reloadBtn        = document.getElementById('reloadBtn');
const $stat             = document.getElementById('stat');
const $stage            = document.getElementById('stage');

// State
let currentStory = stories[0];
let currentFixtureId = null;
let currentInstance = null;
let searchQuery = '';
let activeCategoryFilter = 'all';
let collapsedCategories = new Set();
let stageTheme = 'light'; // 'light' or 'dark'
let isSidebarCollapsed = false;

// Parse URL Hash on initial load
function parseUrlHash() {
  try {
    if (localStorage.getItem('etlsql_sandbox_sidebar_collapsed') === 'true') {
      isSidebarCollapsed = true;
    }
  } catch { /* ignore */ }

  const hash = window.location.hash.replace(/^#/, '');
  if (!hash) return;
  const params = new URLSearchParams(hash);
  const storyId = params.get('story');
  const fix = params.get('fixture');
  const theme = params.get('theme');
  const q = params.get('q');
  const cat = params.get('filter');
  const sidebar = params.get('sidebar');

  if (storyId) {
    const found = stories.find((s) => s.id === storyId);
    if (found) currentStory = found;
  }
  if (fix) currentFixtureId = fix;
  if (theme === 'dark' || theme === 'light') stageTheme = theme;
  if (sidebar === 'collapsed' || sidebar === '0') {
    isSidebarCollapsed = true;
  } else if (sidebar === 'expanded' || sidebar === '1') {
    isSidebarCollapsed = false;
  }
  if (q) {
    searchQuery = q;
    $searchInput.value = q;
    $searchClearBtn.style.display = 'block';
  }
  if (cat) {
    activeCategoryFilter = cat;
    document.querySelectorAll('.chip-btn').forEach((btn) => {
      btn.classList.toggle('is-active', btn.dataset.filter === cat);
    });
  }
}

// Update URL Hash with current state
function syncUrlHash() {
  const params = new URLSearchParams();
  if (currentStory) params.set('story', currentStory.id);
  if ($fixtureSel.value) params.set('fixture', $fixtureSel.value);
  if (stageTheme !== 'light') params.set('theme', stageTheme);
  if (isSidebarCollapsed) params.set('sidebar', 'collapsed');
  if (searchQuery) params.set('q', searchQuery);
  if (activeCategoryFilter !== 'all') params.set('filter', activeCategoryFilter);

  const newHash = '#' + params.toString();
  if (window.location.hash !== newHash) {
    window.history.replaceState(null, '', newHash);
  }
}

function setSidebarCollapsed(collapsed) {
  isSidebarCollapsed = Boolean(collapsed);
  document.body.classList.toggle('sidebar-collapsed', isSidebarCollapsed);
  if ($sidebarToggleBtn) {
    $sidebarToggleBtn.setAttribute('aria-expanded', String(!isSidebarCollapsed));
    $sidebarToggleBtn.title = isSidebarCollapsed ? 'Expand sidebar (Ctrl+B or [)' : 'Collapse sidebar (Ctrl+B or [)';
  }
  try {
    localStorage.setItem('etlsql_sandbox_sidebar_collapsed', String(isSidebarCollapsed));
  } catch { /* ignore */ }
  syncUrlHash();
  window.dispatchEvent(new Event('resize'));
  try { currentInstance?.resize?.(); } catch { /* ignore */ }
}

// Group stories by ordered category
function getGroupedStories() {
  const groups = new Map();
  for (const cat of categoryOrder) {
    groups.set(cat, []);
  }

  for (const s of stories) {
    const cat = s.category || 'Other Surfaces';
    if (!groups.has(cat)) groups.set(cat, []);
    groups.get(cat).push(s);
  }
  return groups;
}

// Filter stories based on query and category chip
function filterStories(storyList) {
  const q = searchQuery.trim().toLowerCase();
  return storyList.filter((s) => {
    if (activeCategoryFilter !== 'all' && (s.category || 'Other Surfaces') !== activeCategoryFilter) {
      return false;
    }
    if (!q) return true;
    const matchTitle = s.title.toLowerCase().includes(q);
    const matchSub = (s.subtitle || '').toLowerCase().includes(q);
    const matchId = (s.id || '').toLowerCase().includes(q);
    const matchCat = (s.category || '').toLowerCase().includes(q);
    return matchTitle || matchSub || matchId || matchCat;
  });
}

function highlightMatch(text, query) {
  if (!query || !text) return text;
  const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const regex = new RegExp(`(${escaped})`, 'gi');
  return text.replace(regex, '<span class="search-highlight">$1</span>');
}

// Render the sidebar navigation tree
function renderSidebar() {
  $storyCountBadge.textContent = stories.length;
  $storiesNav.replaceChildren();

  const grouped = getGroupedStories();
  let totalVisible = 0;
  let totalGroupsWithItems = 0;
  let collapsedGroupsCount = 0;
  const isSearching = searchQuery.trim().length > 0;

  for (const [category, allInCat] of grouped.entries()) {
    const visibleStories = filterStories(allInCat);
    if (visibleStories.length === 0) continue;
    totalVisible += visibleStories.length;
    totalGroupsWithItems++;

    const groupEl = document.createElement('div');
    groupEl.className = 'category-group';
    groupEl.dataset.category = category;

    // Auto-expand if searching, else respect user collapse state
    const isCollapsed = !isSearching && collapsedCategories.has(category);
    if (isCollapsed) {
      groupEl.classList.add('is-collapsed');
      collapsedGroupsCount++;
    }

    // Header
    const headerBtn = document.createElement('button');
    headerBtn.className = 'category-header';
    headerBtn.title = `Click to collapse/expand ${category}`;

    const iconSpan = document.createElement('span');
    iconSpan.className = 'category-icon';
    iconSpan.textContent = categoryIcons[category] || '📁';

    const titleSpan = document.createElement('span');
    titleSpan.className = 'category-title-text';
    titleSpan.textContent = category;

    const countSpan = document.createElement('span');
    countSpan.className = 'category-count';
    countSpan.textContent = visibleStories.length;

    const chevronSpan = document.createElement('span');
    chevronSpan.className = 'category-chevron';
    chevronSpan.textContent = '▼';

    headerBtn.append(iconSpan, titleSpan, countSpan, chevronSpan);
    headerBtn.addEventListener('click', () => {
      if (collapsedCategories.has(category)) {
        collapsedCategories.delete(category);
        groupEl.classList.remove('is-collapsed');
      } else {
        collapsedCategories.add(category);
        groupEl.classList.add('is-collapsed');
      }
      updateCollapseAllButtonState(totalGroupsWithItems);
    });

    // Stories list in category
    const itemsContainer = document.createElement('div');
    itemsContainer.className = 'category-items';

    for (const story of visibleStories) {
      const btn = document.createElement('button');
      btn.className = 'story-link' + (story === currentStory ? ' is-active' : '');
      btn.dataset.storyId = story.id;

      const titleRow = document.createElement('div');
      titleRow.className = 'story-link-title-row';

      const tSpan = document.createElement('span');
      tSpan.className = 'story-link-title';
      tSpan.innerHTML = highlightMatch(story.title, searchQuery);
      titleRow.appendChild(tSpan);

      if (story.fixtures && story.fixtures.length > 1) {
        const badge = document.createElement('span');
        badge.className = 'story-link-badge';
        badge.textContent = `${story.fixtures.length}fx`;
        badge.title = `${story.fixtures.length} fixtures available`;
        titleRow.appendChild(badge);
      }

      const sSpan = document.createElement('span');
      sSpan.className = 'story-link-sub';
      sSpan.innerHTML = highlightMatch(story.subtitle || story.id, searchQuery);

      btn.append(titleRow, sSpan);
      btn.addEventListener('click', () => {
        currentStory = story;
        currentFixtureId = null;
        selectStory(true);
      });

      itemsContainer.appendChild(btn);
    }

    groupEl.append(headerBtn, itemsContainer);
    $storiesNav.appendChild(groupEl);
  }

  // Update showing count text
  if (totalVisible === stories.length) {
    $showingCountText.textContent = `Showing all ${stories.length} stories`;
  } else {
    $showingCountText.textContent = `Showing ${totalVisible} of ${stories.length} stories`;
  }

  updateCollapseAllButtonState(totalGroupsWithItems);

  // Empty search state
  if (totalVisible === 0) {
    const emptyDiv = document.createElement('div');
    emptyDiv.className = 'no-results';
    emptyDiv.innerHTML = `
      <span>No stories match <strong>"${searchQuery}"</strong></span>
      <button id="clearEmptySearchBtn">Clear search</button>
    `;
    emptyDiv.querySelector('#clearEmptySearchBtn').addEventListener('click', () => {
      searchQuery = '';
      $searchInput.value = '';
      $searchClearBtn.style.display = 'none';
      renderSidebar();
    });
    $storiesNav.appendChild(emptyDiv);
  }
}

function updateCollapseAllButtonState(totalGroups) {
  if (collapsedCategories.size >= totalGroups && totalGroups > 0) {
    $collapseAllBtn.textContent = 'Expand all';
  } else {
    $collapseAllBtn.textContent = 'Collapse all';
  }
}

// Render fixture dropdown options
function renderFixtures() {
  $fixtureSel.replaceChildren();
  const list = currentStory.fixtures ?? [];

  for (const f of list) {
    const o = document.createElement('option');
    o.value = f.id;
    o.textContent = f.label;
    if (f.id === currentFixtureId) o.selected = true;
    $fixtureSel.appendChild(o);
  }

  if (list.length > 0 && !currentFixtureId) {
    currentFixtureId = list[0].id;
  }

  $fixtureLabel.style.display = list.length > 1 ? 'flex' : 'none';
}

// Mount the current story into the stage
async function mount() {
  if (currentInstance) {
    try { currentInstance.dispose?.(); } catch { /* ignore */ }
    currentInstance = null;
  }

  $stage.replaceChildren();
  $stat.textContent = 'Mounting component...';

  const fixtureVal = $fixtureSel.value || currentFixtureId;
  const ctx = {
    stat: (t) => { $stat.textContent = t; }
  };

  try {
    currentInstance = await currentStory.mount($stage, fixtureVal, ctx);
    if (!$stat.textContent || $stat.textContent === 'Mounting component...') {
      $stat.textContent = `Mounted "${currentStory.title}" (${fixtureVal || 'default'})`;
    }
  } catch (err) {
    const pre = document.createElement('pre');
    pre.className = 'sandbox-err';
    pre.textContent = `Mount failed for "${currentStory.title}" (${fixtureVal}):\n${err.stack || err.message}`;
    $stage.replaceChildren(pre);
    $stat.textContent = `Error in ${currentStory.title}`;
    console.error(err);
  }

  syncUrlHash();
}

function selectStory(autoExpand = true) {
  if (autoExpand && currentStory.category && collapsedCategories.has(currentStory.category)) {
    collapsedCategories.delete(currentStory.category);
  }

  $storyCategory.textContent = currentStory.category || 'SURFACE';
  $storyTitle.textContent = currentStory.title;
  $storySubtitle.textContent = currentStory.subtitle || currentStory.id;

  renderSidebar();
  renderFixtures();
  mount();

  // Scroll active item into view if expanded
  if (!collapsedCategories.has(currentStory.category)) {
    const activeBtn = $storiesNav.querySelector('.story-link.is-active');
    if (activeBtn) {
      activeBtn.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
  }
}

// Theme handling
function applyTheme(theme) {
  stageTheme = theme;
  if (theme === 'dark') {
    document.body.classList.remove('stage-theme-light');
    document.body.classList.add('stage-theme-dark');
    $themeToggleBtn.textContent = '🌙 Dark';
    $themeToggleBtn.classList.add('is-active');
  } else {
    document.body.classList.remove('stage-theme-dark');
    document.body.classList.add('stage-theme-light');
    $themeToggleBtn.textContent = '☀️ Light';
    $themeToggleBtn.classList.remove('is-active');
  }
  syncUrlHash();
}

// Copy link to clipboard
function copyDeepLink() {
  const url = window.location.href;
  navigator.clipboard.writeText(url).then(() => {
    const originalText = $copyLinkBtn.textContent;
    $copyLinkBtn.textContent = '✓ Copied!';
    $copyLinkBtn.style.borderColor = 'var(--accent-green)';
    $copyLinkBtn.style.color = 'var(--accent-green)';
    setTimeout(() => {
      $copyLinkBtn.textContent = originalText;
      $copyLinkBtn.style.borderColor = '';
      $copyLinkBtn.style.color = '';
    }, 2000);
  });
}

// Keyboard navigation (Arrow keys, Focus shortcut, Sidebar toggle)
window.addEventListener('keydown', (e) => {
  // Focus search box with '/' or 'Ctrl+K'
  if ((e.key === '/' || (e.key === 'k' && (e.ctrlKey || e.metaKey))) && document.activeElement !== $searchInput) {
    e.preventDefault();
    if (isSidebarCollapsed) {
      setSidebarCollapsed(false);
    }
    $searchInput.focus();
    $searchInput.select();
    return;
  }

  // Toggle sidebar with 'Ctrl+B', 'Cmd+B', or '[' when not typing in inputs
  if (
    ((e.key === 'b' || e.key === 'B') && (e.ctrlKey || e.metaKey)) ||
    (e.key === '[' && !e.ctrlKey && !e.metaKey && !e.altKey && !e.target.matches('input, select, textarea, [contenteditable]'))
  ) {
    e.preventDefault();
    setSidebarCollapsed(!isSidebarCollapsed);
    return;
  }

  // Escape clears search or blurs
  if (e.key === 'Escape' && document.activeElement === $searchInput) {
    if ($searchInput.value) {
      $searchInput.value = '';
      searchQuery = '';
      $searchClearBtn.style.display = 'none';
      renderSidebar();
    } else {
      $searchInput.blur();
    }
    return;
  }

  // Arrow navigation through visible stories
  if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && document.activeElement !== $searchInput && !e.target.matches('input, select, textarea, [contenteditable]')) {
    e.preventDefault();
    const visibleStories = filterStories(stories);
    if (visibleStories.length === 0) return;
    const currentIndex = visibleStories.indexOf(currentStory);
    let nextIndex = currentIndex;

    if (e.key === 'ArrowDown') {
      nextIndex = currentIndex < visibleStories.length - 1 ? currentIndex + 1 : 0;
    } else if (e.key === 'ArrowUp') {
      nextIndex = currentIndex > 0 ? currentIndex - 1 : visibleStories.length - 1;
    }

    if (nextIndex !== currentIndex && visibleStories[nextIndex]) {
      currentStory = visibleStories[nextIndex];
      currentFixtureId = null;
      selectStory(true);
    }
  }
});

// Search input events
$searchInput.addEventListener('input', (e) => {
  searchQuery = e.target.value;
  $searchClearBtn.style.display = searchQuery ? 'block' : 'none';
  renderSidebar();
  syncUrlHash();
});

$searchClearBtn.addEventListener('click', () => {
  $searchInput.value = '';
  searchQuery = '';
  $searchClearBtn.style.display = 'none';
  $searchInput.focus();
  renderSidebar();
  syncUrlHash();
});

// Filter chips
$filterChips.addEventListener('click', (e) => {
  const btn = e.target.closest('.chip-btn');
  if (!btn) return;
  $filterChips.querySelectorAll('.chip-btn').forEach((b) => b.classList.remove('is-active'));
  btn.classList.add('is-active');
  activeCategoryFilter = btn.dataset.filter;
  renderSidebar();
  syncUrlHash();
});

// Collapse / Expand All
$collapseAllBtn.addEventListener('click', () => {
  const grouped = getGroupedStories();
  const visibleCategories = Array.from(grouped.keys()).filter((cat) => filterStories(grouped.get(cat)).length > 0);

  if (collapsedCategories.size >= visibleCategories.length) {
    collapsedCategories.clear();
  } else {
    for (const cat of visibleCategories) {
      collapsedCategories.add(cat);
    }
  }
  renderSidebar();
});

// Sidebar collapse buttons
$sidebarCollapseBtn?.addEventListener('click', () => {
  setSidebarCollapsed(true);
});

$sidebarToggleBtn?.addEventListener('click', () => {
  setSidebarCollapsed(!isSidebarCollapsed);
});

// Action buttons
$fixtureSel.addEventListener('change', () => {
  currentFixtureId = $fixtureSel.value;
  mount();
});

$themeToggleBtn.addEventListener('click', () => {
  applyTheme(stageTheme === 'light' ? 'dark' : 'light');
});

$copyLinkBtn.addEventListener('click', copyDeepLink);
$reloadBtn.addEventListener('click', mount);

window.addEventListener('resize', () => currentInstance?.resize?.());

// Initialize
parseUrlHash();
applyTheme(stageTheme);
setSidebarCollapsed(isSidebarCollapsed);
selectStory();
