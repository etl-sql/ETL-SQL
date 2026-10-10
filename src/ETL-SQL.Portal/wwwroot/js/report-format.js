/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/report-format.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
/** Text helpers shared by the catalog page's report surfaces. */
export function escapeHtml(value) {
    return String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
export function formatBuiltAt(value) {
    return value ? new Date(value).toLocaleString() : 'Never run';
}
/** A coarse age for a snapshot or run; anything in the future or older than a month is shown as a date. */
export function timeAgo(value, now = Date.now()) {
    if (!value)
        return 'Never run';
    const elapsed = now - new Date(value).getTime();
    if (!Number.isFinite(elapsed) || elapsed < 0)
        return formatBuiltAt(value);
    const minutes = Math.floor(elapsed / 60000);
    if (minutes < 1)
        return 'Just now';
    if (minutes < 60)
        return `${minutes} min ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24)
        return `${hours} hr ago`;
    const days = Math.floor(hours / 24);
    return days < 30 ? `${days} day${days === 1 ? '' : 's'} ago` : formatBuiltAt(value);
}
