// Shared subscription delivery-history rendering for owner and administrator views.

type HistoryEntry = Partial<JobHistoryEntry> & {
    [Key in keyof JobHistoryEntry as Capitalize<Key>]?: JobHistoryEntry[Key];
};

function value<Key extends keyof JobHistoryEntry>(entry: HistoryEntry | null | undefined, name: Key): JobHistoryEntry[Key] | undefined {
    const capitalized = name[0].toUpperCase() + name.slice(1);
    return (entry?.[capitalized as keyof HistoryEntry] ?? entry?.[name]) as JobHistoryEntry[Key] | undefined;
}

function durationText(entry: HistoryEntry) {
    const start = value(entry, 'startTime');
    const end = value(entry, 'endTime');
    if (!start) return '—';
    if (!end) return 'running';
    const ms = Math.max(0, new Date(end).getTime() - new Date(start).getTime());
    if (ms < 1000) return `${ms} ms`;
    return `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)} s`;
}

export function summarizeSubscriptionHistory(entries: readonly HistoryEntry[] | null | undefined) {
    const completed = (entries || []).filter(e => value(e, 'endTime'));
    const latest = completed[0] || null;
    return {
        latest,
        status: latest ? String(value(latest, 'status') || 'UNKNOWN').toUpperCase() : 'NO HISTORY',
        error: latest ? value(latest, 'errorMessage') : null,
        completedCount: completed.length,
    };
}

export function renderSubscriptionHistory(entries: readonly HistoryEntry[] | null | undefined, { esc, formatDate }: {
    esc: (value: unknown) => string;
    formatDate: (value: string | null | undefined) => string;
}) {
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
