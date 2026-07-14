(function () {
  const search = document.getElementById('search');
  const results = document.getElementById('results');
  const documentPane = document.getElementById('document');
  let activePath = null;

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
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  }

  function markdownToHtml(markdown) {
    const lines = String(markdown || '').replace(/\r\n/g, '\n').split('\n');
    const out = [];
    let inCode = false;
    let inList = false;
    for (const line of lines) {
      if (line.trim().startsWith('```')) {
        if (inList) { out.push('</ul>'); inList = false; }
        out.push(inCode ? '</code></pre>' : '<pre><code>');
        inCode = !inCode;
        continue;
      }
      if (inCode) {
        out.push(escapeHtml(line) + '\n');
        continue;
      }
      const trimmed = line.trim();
      if (!trimmed) {
        if (inList) { out.push('</ul>'); inList = false; }
        continue;
      }
      const heading = /^(#{1,3})\s+(.+)$/.exec(trimmed);
      if (heading) {
        if (inList) { out.push('</ul>'); inList = false; }
        out.push(`<h${heading[1].length}>${inlineMarkdown(heading[2])}</h${heading[1].length}>`);
        continue;
      }
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
    if (inCode) out.push('</code></pre>');
    return out.join('\n');
  }

  async function loadResults(query) {
    const url = query
      ? `/api/docs/search?q=${encodeURIComponent(query)}&limit=50`
      : '/api/docs/search?limit=50';
    const response = await fetch(url);
    if (!response.ok) {
      results.innerHTML = '<p class="docs-empty">Documentation is unavailable.</p>';
      return [];
    }
    const items = await response.json();
    results.innerHTML = items.map(item => `
      <button class="docs-result${item.path === activePath ? ' active' : ''}" data-path="${escapeHtml(item.path)}">
        <div class="docs-result-title">${escapeHtml(item.title)}</div>
        <div class="docs-result-meta">${escapeHtml(item.section)} / ${escapeHtml(item.path)}</div>
      </button>
    `).join('') || '<p class="docs-empty">No matching documents.</p>';
    return items;
  }

  async function openDocument(path) {
    activePath = path;
    const response = await fetch(`/api/docs/document?path=${encodeURIComponent(path)}`);
    if (!response.ok) {
      documentPane.innerHTML = '<p class="docs-empty">Document not found.</p>';
      return;
    }
    const doc = await response.json();
    documentPane.innerHTML = markdownToHtml(doc.markdown);
    await loadResults(search.value.trim());
  }

  let timer = null;
  search.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(() => loadResults(search.value.trim()), 150);
  });

  results.addEventListener('click', event => {
    const button = event.target.closest('[data-path]');
    if (button) openDocument(button.dataset.path);
  });

  loadResults('').then(items => {
    const first = items.find(item => item.path === 'README.md') || items[0];
    if (first) openDocument(first.path);
  });
})();
