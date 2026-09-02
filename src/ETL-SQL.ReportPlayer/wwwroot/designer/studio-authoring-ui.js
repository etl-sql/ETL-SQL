/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-ui.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Presentation primitives shared by Studio's authoring surfaces.
 *
 * Pure functions: markup in, markup out. No host, no network, no state. They live here rather than
 * inside one surface because the wizards, the query workbench, and every surface added later must
 * render a SQL preview, a sample grid, and an inline note the same way — a second implementation is
 * how "preview before write" starts to mean something different in each dialog.
 */

/** The design-time sample budget both hosts enforce; grids scroll rather than truncating. */
export const STUDIO_SAMPLE_PREVIEW_ROWS = 50;

export function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

/**
 * The exact Report-SQL a surface is about to write, and one sentence saying what it does.
 *
 * Contract rule 4 hangs off this: whatever appears here must be what lands in the buffer, so the
 * label is the only presentational part a caller may vary.
 *
 * The sentence is not optional. Showing only the SQL asks the reader to already know the dialect —
 * which is exactly what the author who most needs a wizard does not have — and turns the preview
 * into a wall to click past rather than a description they can check the intent against. A missing
 * explanation throws, for the same reason `inlineMarkup` throws on an unknown segment: every caller
 * lives in this repo, and a preview that silently explains nothing is the failure this exists to
 * prevent.
 *
 * @param {string} sql the exact statement or clause about to be written.
 * @param {string} explanation one plain sentence: what will be added or changed, and where.
 * @param {string} [label] the preview's heading.
 */
export function sqlPreviewMarkup(sql, explanation, label = 'Writes this Report-SQL') {
    if (typeof explanation !== 'string' || !explanation.trim()) {
        throw new TypeError('sqlPreviewMarkup: every SQL preview needs one sentence explaining what it changes.');
    }
    return `<div class="etlsql-studio-sql-preview"><span>${escapeHtml(label)}</span>`
        + `<p class="etlsql-studio-sql-explains">${escapeHtml(explanation)}</p>`
        + `<pre>${escapeHtml(sql)}</pre></div>`;
}

/**
 * The same sentence, for a write whose exact text cannot honestly be previewed.
 *
 * A step that adds several visuals writes through design state, and the canonical patcher decides
 * the final bytes — so quoting a statement here would be a guess, and rule 4 says a preview must be
 * what actually lands. The explanation is still owed to the reader, so it is rendered on its own in
 * the same frame as a SQL preview rather than being dropped.
 */
export function mutationExplanationMarkup(explanation, label = 'Changes this') {
    if (typeof explanation !== 'string' || !explanation.trim()) {
        throw new TypeError('mutationExplanationMarkup: a mutation needs one sentence explaining what it changes.');
    }
    return `<div class="etlsql-studio-sql-preview is-summary"><span>${escapeHtml(label)}</span>`
        + `<p class="etlsql-studio-sql-explains">${escapeHtml(explanation)}</p></div>`;
}

/**
 * Renders a structured inline model to markup.
 *
 * A bare string is plain text and is escaped. Emphasis is asked for explicitly, as an array of
 * segments — a string, or one of `{ text }`, `{ strong }`, `{ em }`, `{ code }`, `{ br: true }` —
 * each of which escapes its own content. There is deliberately no "raw markup" segment: the whole
 * point is that a caller cannot hand a server message straight into innerHTML by accident.
 *
 * An unrecognised segment throws rather than rendering nothing. A note that silently loses a
 * sentence is the failure shape this repo keeps paying for; a segment shape is a programming error
 * and every caller lives in this repo, so it should fail where it is written.
 */
export function inlineMarkup(content) {
    if (content === null || content === undefined) return '';
    if (typeof content === 'string' || typeof content === 'number') return escapeHtml(content);
    if (Array.isArray(content)) return content.map(inlineMarkup).join('');
    if (content.br) return '<br>';
    if ('text' in content) return escapeHtml(content.text);
    if ('strong' in content) return `<strong>${escapeHtml(content.strong)}</strong>`;
    if ('em' in content) return `<em>${escapeHtml(content.em)}</em>`;
    if ('code' in content) return `<code>${escapeHtml(content.code)}</code>`;
    throw new TypeError(`inlineMarkup: unsupported segment ${JSON.stringify(content)}`);
}

/**
 * An inline note. `content` is a structured model rendered by `inlineMarkup`, so plain text — a
 * server error message included — is escaped by default and emphasis has to be asked for.
 */
export function noteMarkup(content, tone = 'info') {
    return `<div class="etlsql-studio-guided-note is-${escapeHtml(tone)}">${inlineMarkup(content)}</div>`;
}

/**
 * A scrollable grid of sampled rows. `sample` is `{ columns, rows, rowCount }`; columns may be plain
 * names or column objects, and rows may be objects or positional arrays, because the data-sample and
 * run endpoints do not agree on a shape.
 */
export function sampleGridMarkup(sample, limit = STUDIO_SAMPLE_PREVIEW_ROWS) {
    const columns = (sample?.columns || []).map(column => (typeof column === 'string' ? column : column?.name))
        .filter(Boolean);
    const rows = sample?.rows || [];
    const resolved = columns.length
        ? columns
        : (rows[0] && !Array.isArray(rows[0]) ? Object.keys(rows[0]) : []);

    if (!resolved.length) return noteMarkup('The sample came back with no columns.', 'warning');

    const cell = (row, column, index) => (Array.isArray(row) ? row[index] : row?.[column]);
    const count = sample?.rowCount ?? rows.length;
    return `<div class="etlsql-studio-sample-grid"><table>
        <thead><tr>${resolved.map(column => `<th>${escapeHtml(column)}</th>`).join('')}</tr></thead>
        <tbody>${rows.slice(0, limit).map(row =>
            `<tr>${resolved.map((column, index) => `<td>${escapeHtml(String(cell(row, column, index) ?? ''))}</td>`).join('')}</tr>`).join('')}</tbody>
        </table></div>
        <p class="etlsql-studio-guided-hint">${count} row${count === 1 ? '' : 's'} sampled · ${resolved.length} field${resolved.length === 1 ? '' : 's'}</p>`;
}
