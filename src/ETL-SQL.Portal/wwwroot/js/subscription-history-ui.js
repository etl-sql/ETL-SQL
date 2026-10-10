/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/subscription-history-ui.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
// Shared subscription delivery-history rendering for owner and administrator views.
function value(entry, name) {
    const capitalized = name[0].toUpperCase() + name.slice(1);
    return (entry?.[capitalized] ?? entry?.[name]);
}
function durationText(entry) {
    const start = value(entry, 'startTime');
    const end = value(entry, 'endTime');
    if (!start)
        return '—';
    if (!end)
        return 'running';
    const ms = Math.max(0, new Date(end).getTime() - new Date(start).getTime());
    if (ms < 1000)
        return `${ms} ms`;
    return `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)} s`;
}
export function summarizeSubscriptionHistory(entries) {
    const completed = (entries || []).filter(e => value(e, 'endTime'));
    const latest = completed[0] || null;
    return {
        latest,
        status: latest ? String(value(latest, 'status') || 'UNKNOWN').toUpperCase() : 'NO HISTORY',
        error: latest ? value(latest, 'errorMessage') : null,
        completedCount: completed.length,
    };
}
export function renderSubscriptionHistory(entries, { esc, formatDate }) {
    const list = entries || [];
    if (!list.length) {
        return '<div class="empty-state">No delivery attempts have been recorded for this subscription.</div>';
    }
    const rows = list.map(entry => {
        const status = String(value(entry, 'status') || 'UNKNOWN').toUpperCase();
        const statusClass = status.toLowerCase();
        const error = value(entry, 'errorMessage') || '';
        return `<tr>
            <td><span class="status-badge ${esc(statusClass)}">${esc(status)}</span></td>
            <td>${esc(formatDate(value(entry, 'startTime')))}</td>
            <td>${esc(durationText(entry))}</td>
            <td>${esc(value(entry, 'rowsProcessed') ?? '—')}</td>
            <td class="subscription-history-error" title="${esc(error)}">${esc(error || '—')}</td>
        </tr>`;
    }).join('');
    return `<div class="history-table-scroll">
        <table class="dependency-table history-table subscription-history-table">
            <thead><tr><th>Status</th><th>Attempt</th><th>Duration</th><th>Rows</th><th>Error</th></tr></thead>
            <tbody>${rows}</tbody>
        </table>
    </div>`;
}
