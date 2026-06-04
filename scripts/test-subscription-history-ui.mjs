// Unit tests for subscription delivery-history rendering.
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(pathToFileURL(path.resolve(
    'src/ETL-SQL.ReportPortal/wwwroot/js/subscription-history-ui.js')).href);
const { renderSubscriptionHistory, summarizeSubscriptionHistory } = mod;

function assert(condition, message) {
    if (!condition) throw new Error(`FAIL: ${message}`);
}

const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[c]));
const formatDate = value => value || '—';
const entries = [
    {
        status: 'FAILURE',
        startTime: '2026-06-04T13:04:00Z',
        endTime: '2026-06-04T13:04:03Z',
        rowsProcessed: 0,
        errorMessage: 'SMTP <secret> refused',
    },
    {
        status: 'SUCCESS',
        startTime: '2026-06-03T13:04:00Z',
        endTime: '2026-06-03T13:04:08Z',
        rowsProcessed: 42,
    },
];

const summary = summarizeSubscriptionHistory(entries);
assert(summary.status === 'FAILURE', 'latest completed status is summarized');
assert(summary.completedCount === 2, 'completed attempts are counted');

const html = renderSubscriptionHistory(entries, { esc, formatDate });
assert(html.includes('FAILURE') && html.includes('SUCCESS'), 'statuses render');
assert(html.includes('3.0 s') && html.includes('8.0 s'), 'durations render');
assert(html.includes('SMTP &lt;secret&gt; refused'), 'error text is escaped');
assert(!html.includes('SMTP <secret> refused'), 'raw error markup is not emitted');

const empty = renderSubscriptionHistory([], { esc, formatDate });
assert(empty.includes('No delivery attempts'), 'empty state renders');

console.log('subscription-history-ui tests passed');
