// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/html-preview.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/html-preview.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * html-preview.js — split out of designer.js, TODO.md §2.
 * Sanitizer and budgets for the designer's HTML-visual preview.
 */
const HTML_PREVIEW_ELEMENTS = new Set([
    'DIV', 'SPAN', 'SECTION', 'ARTICLE', 'ASIDE', 'HEADER', 'FOOTER', 'NAV', 'MAIN',
    'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'P', 'BR', 'HR', 'PRE', 'CODE', 'BLOCKQUOTE',
    'EM', 'STRONG', 'I', 'B', 'U', 'S', 'SMALL', 'SUB', 'SUP', 'MARK', 'ABBR', 'TIME',
    'CITE', 'Q', 'DFN', 'VAR', 'KBD', 'SAMP', 'UL', 'OL', 'LI', 'DL', 'DT', 'DD',
    'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TH', 'TD', 'CAPTION', 'COLGROUP', 'COL',
    'IMG', 'FIGURE', 'FIGCAPTION', 'PICTURE', 'SOURCE', 'A', 'BUTTON', 'DETAILS',
    'SUMMARY', 'DATA', 'METER', 'PROGRESS', 'OUTPUT'
]);
const HTML_PREVIEW_GLOBAL_ATTRIBUTES = new Set([
    'class', 'id', 'title', 'lang', 'dir', 'role', 'tabindex', 'hidden'
]);
export const HTML_PREVIEW_ELEMENT_ATTRIBUTES = {
    A: new Set(['href', 'target', 'rel']),
    IMG: new Set(['src', 'alt', 'width', 'height', 'loading']),
    BUTTON: new Set(['type', 'disabled', 'data-action', 'data-param', 'data-value']),
    TD: new Set(['colspan', 'rowspan', 'scope', 'headers']),
    TH: new Set(['colspan', 'rowspan', 'scope', 'headers']),
    COL: new Set(['span']), COLGROUP: new Set(['span']),
    OL: new Set(['start', 'type', 'reversed']), TIME: new Set(['datetime']),
    METER: new Set(['min', 'max', 'low', 'high', 'optimum', 'value']),
    PROGRESS: new Set(['max', 'value']), DATA: new Set(['value']), ABBR: new Set(['title']),
    BLOCKQUOTE: new Set(['cite']), Q: new Set(['cite']),
    SOURCE: new Set(['srcset', 'type', 'media']), DETAILS: new Set(['open'])
};
export const HTML_PREVIEW_BUDGETS = {
    templateBytes: 64 * 1024,
    cssBytes: 32 * 1024,
    templateNodes: 200,
    outputNodes: 10000,
    outputBytes: 2 * 1024 * 1024,
    renderWork: 20000,
    rows: 500
};
export function _isSafeHtmlPreviewUrl(value) {
    const url = String(value || '').trim();
    /* eslint-disable-next-line no-control-regex -- matching control characters is the point:
           a URL carrying one is how a javascript: scheme gets past a prefix check. */
    if (/[^\S\r\n]*[\u0000-\u001f\u007f]/.test(url))
        return false;
    if (/^(https?:|mailto:|tel:|#)/i.test(url))
        return true;
    if (/^data:image\/(png|jpeg|gif|webp)(;|,)/i.test(url))
        return true;
    if (!/^data:image\/svg\+xml(?:;charset=[^;,]+)?(?:;base64)?,/i.test(url))
        return false;
    try {
        const comma = url.indexOf(',');
        const header = url.slice(0, comma);
        const payload = url.slice(comma + 1);
        const svg = /;base64/i.test(header) ? atob(payload) : decodeURIComponent(payload);
        return !/<\s*(?:script|foreignObject)\b|\bon[a-z]+\s*=|(?:href|src)\s*=\s*['"]?\s*javascript:/i.test(svg);
    }
    catch {
        return false;
    }
}
export function _copyHtmlPreviewNode(source, ownerDocument, violations) {
    if (source.nodeType === Node.TEXT_NODE)
        return ownerDocument.createTextNode(source.nodeValue || '');
    if (source.nodeType !== Node.ELEMENT_NODE || !HTML_PREVIEW_ELEMENTS.has(source.tagName)) {
        if (source.nodeType === Node.ELEMENT_NODE)
            violations.push(`Element <${source.tagName.toLowerCase()}> is not allowed.`);
        return null;
    }
    const target = ownerDocument.createElement(source.tagName.toLowerCase());
    const elementAttributes = HTML_PREVIEW_ELEMENT_ATTRIBUTES[source.tagName] || new Set();
    let hasAlt = false;
    for (const attribute of source.attributes) {
        const name = attribute.name.toLowerCase();
        hasAlt ||= name === 'alt';
        const allowed = HTML_PREVIEW_GLOBAL_ATTRIBUTES.has(name)
            || name.startsWith('aria-')
            || (name.startsWith('data-etl-') && name !== 'data-etl-embed-id' && name !== 'data-etl-microchart-id')
            || elementAttributes.has(name);
        if (!allowed || name.startsWith('on') || name === 'style') {
            violations.push(`Attribute '${name}' is not allowed on <${source.tagName.toLowerCase()}>.`);
            continue;
        }
        if (['href', 'src', 'cite', 'srcset'].includes(name) && !_isSafeHtmlPreviewUrl(attribute.value)) {
            violations.push(`URL attribute '${name}' is not allowed.`);
            continue;
        }
        if (source.tagName === 'BUTTON' && name === 'type' && attribute.value.toLowerCase() !== 'button') {
            violations.push("Only type='button' is allowed on HTML visual buttons.");
            continue;
        }
        target.setAttribute(name, attribute.value);
    }
    if (source.tagName === 'IMG' && !hasAlt) {
        violations.push('HTML visual images require an alt attribute.');
        return null;
    }
    for (const child of source.childNodes) {
        const copied = _copyHtmlPreviewNode(child, ownerDocument, violations);
        if (copied)
            target.appendChild(copied);
    }
    return target;
}
export function _validateHtmlPreviewCss(css) {
    const normalized = String(css || '').replace(/\/\*[\s\S]*?\*\//g, '');
    if (/@import|@font-face|expression\s*\(|-moz-binding|behavior\s*:|javascript\s*:|url\s*\(\s*['"]?\s*(?:https?:|\/\/)|url\s*\(\s*['"]?\s*data:(?!image\/)|var\s*\(\s*--(?!etl-)|\\/i.test(normalized))
        return 'Scoped CSS contains a disallowed construct.';
    const unsupportedAtRule = normalized.match(/@(?!media\b|keyframes\b)[A-Za-z-]+/i);
    return unsupportedAtRule ? `CSS at-rule '${unsupportedAtRule[0]}' is not allowed.` : null;
}
