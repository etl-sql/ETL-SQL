import { auth } from '/js/api.js?v=0.17.0';

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

  function inlineMarkdown(value) {
    return escapeHtml(value)
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/\*([^*]+)\*/g, '<em>$1</em>')
      .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');
  }

  function markdownToHtml(markdown) {
    const lines = String(markdown || '').replace(/\r\n/g, '\n').split('\n');
    const out = [];
    let inCode = false;
    let codeLang = '';
    let inList = false;
    let inBlockquote = false;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const trimmed = line.trim();

      if (trimmed.startsWith('```')) {
        if (inList) { out.push('</ul>'); inList = false; }
        if (inBlockquote) { out.push('</blockquote>'); inBlockquote = false; }

        if (!inCode) {
          codeLang = trimmed.substring(3).trim();
          out.push(`<pre><code class="language-${escapeHtml(codeLang)}">`);
          inCode = true;
        } else {
          out.push('</code></pre>');
          inCode = false;
        }
        continue;
      }

      if (inCode) {
        out.push(escapeHtml(line));
        out.push('\n');
        continue;
      }

      if (!trimmed) {
        if (inList) { out.push('</ul>'); inList = false; }
        if (inBlockquote) { out.push('</blockquote>'); inBlockquote = false; }
        continue;
      }

      // Blockquotes / Alerts
      if (trimmed.startsWith('>')) {
        if (inList) { out.push('</ul>'); inList = false; }
        const quoteText = trimmed.replace(/^>\s*/, '');
        if (!inBlockquote) {
          out.push('<blockquote>');
          inBlockquote = true;
        }
        out.push(`<p>${inlineMarkdown(quoteText)}</p>`);
        continue;
      } else if (inBlockquote) {
        out.push('</blockquote>');
        inBlockquote = false;
      }

      // Headings
      const heading = /^(#{1,4})\s+(.+)$/.exec(trimmed);
      if (heading) {
        if (inList) { out.push('</ul>'); inList = false; }
        const level = heading[1].length;
        out.push(`<h${level}>${inlineMarkdown(heading[2])}</h${level}>`);
        continue;
      }

      // Lists
      const bullet = /^[-*]\s+(.+)$/.exec(trimmed);
      if (bullet) {
        if (!inList) { out.push('<ul>'); inList = true; }
        out.push(`<li>${inlineMarkdown(bullet[1])}</li>`);
        continue;
      }

      if (inList) { out.push('</ul>'); inList = false; }
      out.push(`<p>${inlineMarkdown(trimmed)}</p>`);
    }

    if (inList) out.push('</ul>');
    if (inBlockquote) out.push('</blockquote>');
    if (inCode) out.push('</code></pre>');

    return out.join('\n');
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
        ${markdownToHtml(doc.markdown)}
      `;

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
