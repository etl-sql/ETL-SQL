// Shared subscription delivery-history rendering for owner and administrator views.

function value(entry, name) {
    return entry?.[name] ?? entry?.[name[0].toLowerCase() + name.slice(1)];
}

function durationText(entry) {
    const start = value(entry, 'StartTime');
    const end = value(entry, 'EndTime');
    if (!start) return '—';
    if (!end) return 'running';
    const ms = Math.max(0, new Date(end).getTime() - new Date(start).getTime());
    if (ms < 1000) return `${ms} ms`;
    return `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)} s`;
}

export function summarizeSubscriptionHistory(entries) {
    const completed = (entries || []).filter(e => value(e, 'EndTime'));
    const latest = completed[0] || null;
    return {
        latest,
        status: latest ? String(value(latest, 'Status') || 'UNKNOWN').toUpperCase() : 'NO HISTORY',
        error: latest ? value(latest, 'ErrorMessage') : null,
        completedCount: completed.length,
    };
}

export function renderSubscriptionHistory(entries, { esc, formatDate }) {
    const list = entries || [];
    if (!list.length) {
        return '<div class="empty-state">No delivery attempts have been recorded for this subscription.</div>';
    }

    const rows = list.map(entry => {
        const status = String(value(entry, 'Status') || 'UNKNOWN').toUpperCase();
        const statusClass = status.toLowerCase();
        const error = value(entry, 'ErrorMessage') || '';
        return `<tr>
            <td><span class="status-badge ${esc(statusClass)}">${esc(status)}</span></td>
            <td>${esc(formatDate(value(entry, 'StartTime')))}</td>
            <td>${esc(durationText(entry))}</td>
            <td>${esc(value(entry, 'RowsProcessed') ?? '—')}</td>
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
