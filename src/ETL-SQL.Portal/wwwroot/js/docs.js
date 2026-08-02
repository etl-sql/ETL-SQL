import { auth } from '/js/api.js?v=0.17.0';
import { bindMarkdownActions, renderMarkdown } from '/js/markdown-renderer.js?v=0.17.0';

(function () {
  const searchInput = document.getElementById('search');
  const resultsContainer = document.getElementById('results');
  const documentPane = document.getElementById('document');
  const categoryNav = document.getElementById('categoryNav');

  let activePath = null;
  let activeSection = 'All';

  function getAuthHeaders() {
    const headers = {};
    const token = auth.getToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;
    return headers;
  }

  function escapeHtml(value) {
    return String(value || '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  async function loadResults(query = '', section = 'All') {
    resultsContainer.innerHTML = '<div class="loading-state loading-state-compact">Searching...</div>';
    const params = new URLSearchParams({ limit: '100' });
    if (query) params.set('q', query);
    if (section && section !== 'All') params.set('section', section);

    try {
      const response = await fetch(`/api/docs/search?${params.toString()}`, {
        headers: getAuthHeaders()
      });

      if (!response.ok) {
        resultsContainer.innerHTML = '<p class="docs-empty" style="padding:12px;">Documentation service unavailable.</p>';
        return [];
      }

      const items = await response.json();
      if (!items || items.length === 0) {
        resultsContainer.innerHTML = '<p class="docs-empty" style="padding:12px;">No matching topics found.</p>';
        return [];
      }

      resultsContainer.innerHTML = items.map(item => `
        <div class="folder-item${item.path === activePath ? ' active' : ''}" data-path="${escapeHtml(item.path)}" title="${escapeHtml(item.title)}">
          <span class="folder-icon" aria-hidden="true"></span>
          <div style="min-width: 0; flex: 1;">
            <div style="font-weight: 600; text-overflow: ellipsis; overflow: hidden; white-space: nowrap;">${escapeHtml(item.title)}</div>
            <div class="docs-item-section">${escapeHtml(item.section)}</div>
          </div>
        </div>
      `).join('');

      return items;
    } catch {
      resultsContainer.innerHTML = '<p class="docs-empty" style="padding:12px;">Unable to load topics.</p>';
      return [];
    }
  }

  async function openDocument(path) {
    if (!path) return;
    activePath = path;

    // Highlight active item in sidebar
    resultsContainer.querySelectorAll('.folder-item').forEach(el => {
      el.classList.toggle('active', el.dataset.path === path);
    });

    documentPane.innerHTML = '<div class="loading-state">Loading document…</div>';

    try {
      const response = await fetch(`/api/docs/document?path=${encodeURIComponent(path)}`, {
        headers: getAuthHeaders()
      });

      if (!response.ok) {
        documentPane.innerHTML = '<p class="docs-empty">Document not found.</p>';
        return;
      }

      const doc = await response.json();
      documentPane.innerHTML = `
        <div style="font-size: .8em; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--portal-accent); margin-bottom: 6px;">
          ${escapeHtml(doc.section)}
        </div>
        ${renderMarkdown(doc.markdown)}
      `;
      bindMarkdownActions(documentPane);

      // Scroll document pane to top
      const mainContent = document.getElementById('mainContent');
      if (mainContent) mainContent.scrollTop = 0;
    } catch {
      documentPane.innerHTML = '<p class="docs-empty">Error loading document.</p>';
    }
  }

  // Event handlers
  let timer = null;
  if (searchInput) {
    searchInput.addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        loadResults(searchInput.value.trim(), activeSection);
      }, 150);
    });
  }

  if (categoryNav) {
    categoryNav.addEventListener('click', event => {
      const btn = event.target.closest('[data-section]');
      if (!btn) return;

      categoryNav.querySelectorAll('[data-section]').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      activeSection = btn.dataset.section;
      loadResults(searchInput.value.trim(), activeSection);
    });
  }

  if (resultsContainer) {
    resultsContainer.addEventListener('click', event => {
      const item = event.target.closest('[data-path]');
      if (item) openDocument(item.dataset.path);
    });
  }

  // Initial load
  loadResults('', 'All').then(items => {
    if (items && items.length > 0) {
      const defaultItem = items.find(i => i.path.toLowerCase().includes('select')) || items[0];
      if (defaultItem) openDocument(defaultItem.path);
    }
  });
})();
