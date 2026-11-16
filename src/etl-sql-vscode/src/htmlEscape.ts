/** Escapes text for an HTML text node or a double- or single-quoted attribute value. */
export function escapeHtml(value: unknown): string {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/**
 * Renders a notebook result set as an HTML table. Column names and cell values come from query
 * results, which are untrusted data, so every one is escaped before it reaches `text/html` output.
 */
export function formatNotebookTable(columns: readonly string[], rows: readonly Record<string, unknown>[]): string {
    let html = '<div style="max-height: 400px; overflow: auto; border: 1px solid #444; border-radius: 4px;">';
    html += '<table style="border-collapse: collapse; width: 100%; font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size);">';
    html += '<thead><tr>';
    for (const col of columns) {
        html += `<th style="border: 1px solid #444; padding: 8px; text-align: left; background-color: #333;">${escapeHtml(col)}</th>`;
    }
    html += '</tr></thead><tbody>';
    for (const row of rows) {
        html += '<tr>';
        for (const col of columns) {
            html += `<td style="border: 1px solid #444; padding: 8px;">${escapeHtml(row[col])}</td>`;
        }
        html += '</tr>';
    }
    html += '</tbody></table></div>';
    return html;
}
