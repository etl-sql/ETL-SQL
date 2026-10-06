// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-util.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Escaping, formatting, URL safety, and option lookup.
 */
// escHtml (defined below) escapes &<>"' for safe interpolation into innerHTML.
// Manifest-derived strings (titles, icons, names, error text) are treated as
// untrusted: a report author or a crafted server response must not be able to
// inject markup/script.
// Returns the URL only if it uses a safe scheme; otherwise returns '#'. Blocks
// javascript:, data:, vbscript: and similar from reaching href/src or location.
export function safeUrl(value) {
    const raw = String(value == null ? '' : value).trim();
    // Allow relative URLs (no scheme) and explicit http(s)/mailto.
    if (/^(?:https?:|mailto:)/i.test(raw))
        return raw;
    if (/^[a-z][a-z0-9+.-]*:/i.test(raw))
        return '#'; // some other scheme — reject
    return raw; // scheme-less (relative) URL
}
/**
 * Fills an `OPEN_URL(TEMPLATE = ...)` template from the clicked row. Only the fields the author
 * declared in `PARAMS` may be interpolated, and every value is URL-encoded, so a row value can
 * never introduce a query parameter, a path segment, or a scheme of its own. A placeholder that
 * names an undeclared or missing field resolves to the empty string rather than being left in
 * the URL.
 * @param {{url?: string, urlParams?: string[]}} action
 * @param {any[]} rowData
 * @param {string[]} columns
 * @returns {string}
 */
export function interpolateUrlTemplate(action, rowData, columns) {
    const template = String(action.url || '');
    const allowed = new Set((action.urlParams || []).map(name => String(name).toLowerCase()));
    const cols = columns || [];
    const row = rowData || [];
    return template.replace(/\{([^{}]+)\}/g, (_match, name) => {
        const field = String(name).trim();
        if (!allowed.has(field.toLowerCase()))
            return '';
        const index = cols.findIndex(col => String(col).toLowerCase() === field.toLowerCase());
        if (index < 0)
            return '';
        const value = row[index];
        return value == null ? '' : encodeURIComponent(String(value));
    });
}
export function getOption(options, key) {
    if (!options)
        return null;
    const lookup = key.toLowerCase();
    for (let k in options) {
        if (k.toLowerCase() === lookup)
            return options[k];
    }
    return null;
}
export function getStyle(styles, key) {
    if (!styles)
        return null;
    const lookup = key.toLowerCase();
    for (let k in styles) {
        if (k.toLowerCase() === lookup)
            return styles[k];
    }
    return null;
}
export function getParam(params, name) {
    if (!params || !name)
        return undefined;
    const lookup = name.toLowerCase();
    for (let k in params) {
        if (k.toLowerCase() === lookup)
            return params[k];
    }
    return undefined;
}
export function parseMultiParameter(value) {
    if (value == null || String(value).trim() === '')
        return [];
    const text = String(value).trim();
    if (text.startsWith('[')) {
        try {
            const parsed = JSON.parse(text);
            if (Array.isArray(parsed))
                return parsed.map(v => String(v));
        }
        catch { /* accept legacy comma-separated values below */ }
    }
    return text.split(',').map(v => v.trim()).filter(Boolean);
}
export function noDataEl(msg) {
    const div = document.createElement('div');
    div.className = 'no-data';
    div.textContent = msg;
    return div;
}
// Accepts "ON", "TRUE", "1" (case-insensitive) — mirrors server-side IsOn()
export function isOn(val) {
    if (!val)
        return false;
    const v = String(val).toUpperCase();
    return v === 'ON' || v === 'TRUE' || v === '1';
}
export function isOff(val) {
    if (val === null || val === undefined)
        return false;
    const v = String(val).toUpperCase();
    return v === 'OFF' || v === 'FALSE' || v === '0';
}
export function inputTypeForParameter(meta) {
    const type = (meta && meta.type ? String(meta.type) : '').toUpperCase();
    if (['INT', 'INTEGER', 'BIGINT', 'SMALLINT', 'TINYINT', 'DECIMAL', 'NUMERIC', 'FLOAT', 'DOUBLE', 'REAL', 'MONEY'].includes(type)) {
        return 'number';
    }
    if (['BOOL', 'BOOLEAN', 'BIT'].includes(type)) {
        return 'checkbox';
    }
    if (['DATE', 'DATETIME', 'DATETIME2', 'DATETIMEOFFSET'].includes(type)) {
        return 'date';
    }
    return 'text';
}
// `MAX_WIDTH = 1440` and `BREAKPOINT = 768` are written unitless as often as they are written
// '1440px', and both reach here as strings. A bare number is pixels.
export function toCssLength(value) {
    const text = String(value == null ? '' : value).trim();
    if (!text)
        return null;
    return /^-?\d+(\.\d+)?$/.test(text) ? text + 'px' : text;
}
export function toPixels(value) {
    const length = toCssLength(value);
    if (!length)
        return 0;
    const parsed = parseFloat(length);
    return Number.isFinite(parsed) ? parsed : 0;
}
// Dims non-selected bars in the source chart while keeping selected bars at full opacity.
// Operates on per-item itemStyle.opacity so original colors are always preserved.
// ── Card ────────────────────────────────────────────────────────────────
export function abbreviateNumber(num, formatHint) {
    const abs = Math.abs(num);
    let suffix = '', divisor = 1;
    if (abs >= 1e9) {
        suffix = 'B';
        divisor = 1e9;
    }
    else if (abs >= 1e6) {
        suffix = 'M';
        divisor = 1e6;
    }
    else if (abs >= 1e3) {
        suffix = 'K';
        divisor = 1e3;
    }
    const isCurrency = formatHint && formatHint.charAt(0).toUpperCase() === 'C';
    const prefix = isCurrency ? '$' : '';
    const abbreviated = num / divisor;
    const decimals = suffix ? 2 : 0;
    const sign = num < 0 ? '-' : '';
    return sign + prefix + abbreviated.toFixed(decimals) + suffix;
}
// Markdown → HTML renderer supporting: headers, bold, italic, inline code, links,
// fenced code blocks, blockquotes, unordered/ordered lists, tables, horizontal rules.
/**
 * Inline markdown only — bold, italic, code and safe links — with everything else escaped.
 *
 * Six call sites (card, chart and visual titles and subtitles) already called this by name and
 * it existed only as a function nested inside `simpleMarkdown`, so every one of them threw a
 * ReferenceError and took the rest of that render with it. A title wants inline formatting and
 * nothing else: `simpleMarkdown` would wrap it in block elements. So this is the shared one and
 * `simpleMarkdown` uses it for its own inline runs.
 *
 * @param {string} text
 * @returns {string} HTML. The input is escaped first, so only the markup produced here is live.
 */
export function renderInlineMarkdown(text) {
    return escHtml(text)
        .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
        .replace(/\*(.+?)\*/g, '<em>$1</em>')
        .replace(/`(.+?)`/g, '<code>$1</code>')
        .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) => {
        // Only allow safe protocols
        const safe = /^(https?:|mailto:|\/)/i.test(url.trim());
        if (!safe)
            return escHtml(label);
        return `<a href="${escHtml(url)}" target="_blank" rel="noopener noreferrer">${escHtml(label)}</a>`;
    });
}
export function simpleMarkdown(src) {
    if (!src)
        return '';
    // Unescape ETL-SQL escaped newlines
    const raw = String(src).replace(/\\n/g, '\n');
    // Phase 1: extract fenced code blocks to protect them from other processing
    const codeBlocks = [];
    const withoutCode = raw.replace(/```([^\n]*)\n([\s\S]*?)```/g, (_, lang, code) => {
        const escaped = escHtml(code.replace(/\n$/, ''));
        const cls = lang.trim() ? ` class="language-${escHtml(lang.trim())}"` : '';
        codeBlocks.push(`<pre><code${cls}>${escaped}</code></pre>`);
        return `\x00CODE${codeBlocks.length - 1}\x00`;
    });
    // Phase 2: process line-by-line blocks
    const lines = withoutCode.split('\n');
    const out = [];
    let i = 0;
    const inlineFormat = renderInlineMarkdown;
    while (i < lines.length) {
        const line = lines[i];
        const trimmed = line.trim();
        // Code block placeholder
        /* eslint-disable no-control-regex -- \x00 is the sentinel this renderer wraps
           extracted code blocks in, chosen because markdown source cannot contain it. */
        if (/^\x00CODE\d+\x00$/.test(trimmed)) {
            const idx = parseInt(trimmed.replace(/\x00CODE(\d+)\x00/, '$1'), 10);
            /* eslint-enable no-control-regex */
            out.push(codeBlocks[idx]);
            i++;
            continue;
        }
        // Horizontal rule: --- or *** or ___ (3+ chars, only that char)
        if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
            out.push('<hr>');
            i++;
            continue;
        }
        // ATX headings
        const hMatch = trimmed.match(/^(#{1,6})\s+(.+)$/);
        if (hMatch) {
            const level = hMatch[1].length;
            out.push(`<h${level}>${inlineFormat(hMatch[2])}</h${level}>`);
            i++;
            continue;
        }
        // Blockquote: collect consecutive > lines
        if (trimmed.startsWith('> ')) {
            const bqLines = [];
            while (i < lines.length && lines[i].trim().startsWith('> ')) {
                bqLines.push(inlineFormat(lines[i].trim().replace(/^>\s?/, '')));
                i++;
            }
            out.push(`<blockquote>${bqLines.join('<br>')}</blockquote>`);
            continue;
        }
        // Unordered list: collect consecutive - or * lines
        if (/^[-*]\s/.test(trimmed)) {
            const items = [];
            while (i < lines.length && /^[-*]\s/.test(lines[i].trim())) {
                items.push(`<li>${inlineFormat(lines[i].trim().replace(/^[-*]\s/, ''))}</li>`);
                i++;
            }
            out.push(`<ul>${items.join('')}</ul>`);
            continue;
        }
        // Ordered list: collect consecutive N. lines
        if (/^\d+\.\s/.test(trimmed)) {
            const items = [];
            while (i < lines.length && /^\d+\.\s/.test(lines[i].trim())) {
                items.push(`<li>${inlineFormat(lines[i].trim().replace(/^\d+\.\s/, ''))}</li>`);
                i++;
            }
            out.push(`<ol>${items.join('')}</ol>`);
            continue;
        }
        // Markdown table: lines starting with |
        if (trimmed.startsWith('|') && trimmed.includes('|', 1)) {
            const tableLines = [];
            while (i < lines.length && lines[i].trim().startsWith('|') && lines[i].trim().includes('|', 1)) {
                tableLines.push(lines[i]);
                i++;
            }
            let tableHtml = '<div class="md-table-wrapper"><table class="md-table">';
            tableLines.forEach((tl, idx) => {
                if (/^\s*\|[\s|:-]+\|\s*$/.test(tl))
                    return; // separator row
                const cells = tl.split('|').map(s => s.trim()).filter((_, ci, a) => ci > 0 && ci < a.length - 1);
                const tag = idx === 0 ? 'th' : 'td';
                tableHtml += '<tr>' + cells.map(c => `<${tag}>${inlineFormat(c)}</${tag}>`).join('') + '</tr>';
            });
            tableHtml += '</table></div>';
            out.push(tableHtml);
            continue;
        }
        // Blank line → paragraph break
        if (trimmed === '') {
            out.push('<br>');
            i++;
            continue;
        }
        // Plain text line with inline formatting
        out.push(inlineFormat(trimmed) + '<br>');
        i++;
    }
    return out.join('\n');
}
// ── Helpers ─────────────────────────────────────────────────────────────
export function errorEl(detail) {
    const el = document.createElement('details');
    el.className = 'error-card';
    const summary = document.createElement('summary');
    summary.textContent = 'Error loading data';
    el.appendChild(summary);
    if (detail) {
        const pre = document.createElement('pre');
        pre.textContent = detail;
        el.appendChild(pre);
    }
    return el;
}
export function escHtml(s) {
    return String(s == null ? '' : s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}
export function cssClassToken(value, fallback) {
    const token = String(value == null ? '' : value)
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9_-]+/g, '-')
        .replace(/^-+|-+$/g, '');
    return token || fallback;
}
function parseHexColor(hex) {
    const h = hex.replace('#', '');
    const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    return [parseInt(full.slice(0, 2), 16), parseInt(full.slice(2, 4), 16), parseInt(full.slice(4, 6), 16)];
}
export function interpolateColor(fromHex, toHex, t) {
    const [r1, g1, b1] = parseHexColor(fromHex);
    const [r2, g2, b2] = parseHexColor(toHex);
    const r = Math.round(r1 + (r2 - r1) * t);
    const g = Math.round(g1 + (g2 - g1) * t);
    const b = Math.round(b1 + (b2 - b1) * t);
    return `rgb(${r},${g},${b})`;
}
export function formatValue(value, format) {
    if (value == null || value === '' || !format)
        return value;
    const num = parseFloat(value);
    if (isNaN(num))
        return value;
    const type = format.charAt(0).toUpperCase();
    const prec = parseInt(format.substring(1));
    const precision = isNaN(prec) ? undefined : prec;
    try {
        switch (type) {
            case 'C':
                return new Intl.NumberFormat('en-US', {
                    style: 'currency', currency: 'USD',
                    minimumFractionDigits: precision, maximumFractionDigits: precision
                }).format(num);
            case 'N':
                return new Intl.NumberFormat('en-US', {
                    minimumFractionDigits: precision, maximumFractionDigits: precision
                }).format(num);
            case 'P':
                // If the value is > 1.0, it might be already in percent (e.g. 85 instead of 0.85)
                // But standard C# P format for 0.85 is 85%.
                // We'll follow C# behavior: num * 100.
                return new Intl.NumberFormat('en-US', {
                    style: 'percent',
                    minimumFractionDigits: precision, maximumFractionDigits: precision
                }).format(num);
            default:
                return value;
        }
    }
    catch {
        return value;
    }
}
/**
 * Safely parses an SVG string by verifying absence of XML DOCTYPE/ENTITY expansions
 * and validating that the root element is an SVG element.
 */
export function parseSafeSvg(rawSvg) {
    const raw = String(rawSvg ?? '').trim();
    if (!raw || /<!DOCTYPE|<!ENTITY/i.test(raw))
        return null;
    if (typeof DOMParser === 'undefined')
        return null;
    try {
        const parsed = new DOMParser().parseFromString(raw, 'image/svg+xml');
        const svg = parsed.documentElement;
        if (!svg || svg.nodeName.toLowerCase() !== 'svg' || parsed.querySelector('parsererror')) {
            return null;
        }
        return svg;
    }
    catch {
        return null;
    }
}
/**
 * Safely renders an SVG string into a target element using imported DOM nodes instead of innerHTML.
 * Falls back to innerHTML in mock test environments that lack DOMParser / importNode.
 */
export function renderSafeSvgInto(target, rawSvg) {
    const raw = String(rawSvg ?? '').trim();
    if (!raw || /<!DOCTYPE|<!ENTITY/i.test(raw))
        return false;
    const svg = parseSafeSvg(raw);
    if (svg && typeof document !== 'undefined' && typeof document.importNode === 'function') {
        target.appendChild(document.importNode(svg, true));
        return true;
    }
    target.innerHTML = raw;
    return true;
}
