// Shared, dependency-free Markdown renderer for trusted documentation text. Raw HTML is never
// interpreted; every text fragment is escaped and link protocols are allow-listed.
const escapeHtml = value => String(value ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

function safeHref(value) {
  const href = String(value || '').trim();
  if (/^(https?:|mailto:)/i.test(href) || /^(#|\/|\.\.?\/)/.test(href)) return href;
  return '#';
}

function plainInline(value) {
  return escapeHtml(value)
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>');
}

function inlineMarkdown(value) {
  const source = String(value || '');
  const token = /`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)/g;
  let output = ''; let cursor = 0; let match;
  while ((match = token.exec(source))) {
    output += plainInline(source.slice(cursor, match.index));
    if (match[1] !== undefined) output += `<code>${escapeHtml(match[1])}</code>`;
    else {
      const href = safeHref(match[3]);
      const external = /^https?:/i.test(href) ? ' target="_blank" rel="noopener noreferrer"' : '';
      output += `<a href="${escapeHtml(href)}"${external}>${plainInline(match[2])}</a>`;
    }
    cursor = token.lastIndex;
  }
  return output + plainInline(source.slice(cursor));
}

function isTableSeparator(line) {
  const cells = line.trim().replace(/^\||\|$/g, '').split('|').map(cell => cell.trim());
  return cells.length > 0 && cells.every(cell => /^:?-{3,}:?$/.test(cell));
}

function tableCells(line) {
  return line.trim().replace(/^\||\|$/g, '').split('|').map(cell => cell.trim());
}

export function renderMarkdown(markdown, { copyButtons = true } = {}) {
  const lines = String(markdown || '').replace(/\\n/g, '\n').replace(/\r\n/g, '\n').split('\n');
  const out = []; let list = null; let quote = false; let code = false; let language = ''; let codeIndex = 0;
  const closeList = () => { if (list) { out.push(`</${list}>`); list = null; } };
  const closeQuote = () => { if (quote) { out.push('</blockquote>'); quote = false; } };

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index]; const trimmed = line.trim();
    if (trimmed.startsWith('```')) {
      closeList(); closeQuote();
      if (!code) {
        language = trimmed.slice(3).trim();
        out.push(`<div class="md-code"><div class="md-code-head"><span>${escapeHtml(language || 'text')}</span>${copyButtons ? `<button type="button" class="btn btn-outline btn-xs" data-md-copy="${codeIndex++}">Copy</button>` : ''}</div><pre><code class="language-${escapeHtml(language)}">`);
        code = true;
      } else { out.push('</code></pre></div>'); code = false; }
      continue;
    }
    if (code) { out.push(`${escapeHtml(line)}\n`); continue; }
    if (!trimmed) { closeList(); closeQuote(); continue; }

    if (index + 1 < lines.length && trimmed.includes('|') && isTableSeparator(lines[index + 1])) {
      closeList(); closeQuote();
      const headers = tableCells(trimmed); index += 2;
      const rows = [];
      while (index < lines.length && lines[index].trim().includes('|') && lines[index].trim()) {
        rows.push(tableCells(lines[index])); index++;
      }
      index--;
      out.push(`<div class="md-table-wrap"><table class="md-table"><thead><tr>${headers.map(cell => `<th>${inlineMarkdown(cell)}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${headers.map((_, i) => `<td>${inlineMarkdown(row[i] || '')}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`);
      continue;
    }

    const heading = /^(#{1,6})\s+(.+)$/.exec(trimmed);
    if (heading) { closeList(); closeQuote(); const level = heading[1].length; out.push(`<h${level}>${inlineMarkdown(heading[2])}</h${level}>`); continue; }

    if (trimmed.startsWith('>')) {
      closeList();
      const text = trimmed.replace(/^>\s*/, '');
      const admonition = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(.*)$/i.exec(text);
      if (!quote) { out.push(`<blockquote${admonition ? ` class="md-admonition md-${admonition[1].toLowerCase()}"` : ''}>`); quote = true; }
      if (admonition) out.push(`<strong class="md-admonition-title">${admonition[1]}</strong>${admonition[2] ? `<p>${inlineMarkdown(admonition[2])}</p>` : ''}`);
      else out.push(`<p>${inlineMarkdown(text)}</p>`);
      continue;
    }
    closeQuote();

    const bullet = /^[-*]\s+(.+)$/.exec(trimmed); const ordered = /^\d+[.)]\s+(.+)$/.exec(trimmed);
    if (bullet || ordered) {
      const nextList = ordered ? 'ol' : 'ul';
      if (list !== nextList) { closeList(); list = nextList; out.push(`<${list}>`); }
      out.push(`<li>${inlineMarkdown((bullet || ordered)[1])}</li>`); continue;
    }
    closeList();
    out.push(`<p>${inlineMarkdown(trimmed)}</p>`);
  }
  closeList(); closeQuote(); if (code) out.push('</code></pre></div>');
  return `<div class="markdown-body">${out.join('\n')}</div>`;
}

export function bindMarkdownActions(root) {
  root?.querySelectorAll('[data-md-copy]').forEach(button => button.addEventListener('click', async () => {
    const code = button.closest('.md-code')?.querySelector('code')?.textContent || '';
    await navigator.clipboard?.writeText(code);
    const label = button.textContent; button.textContent = 'Copied';
    setTimeout(() => { button.textContent = label; }, 1200);
  }));
}
