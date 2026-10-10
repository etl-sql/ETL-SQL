/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/triage-format.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
export function esc(value) {
    const replacements = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
    return String(value ?? '').replace(/[&<>"']/g, character => replacements[character]);
}
export function fmtDateTime(value) {
    if (!value)
        return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
}
export function fmtTime(value) {
    if (!value)
        return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleTimeString();
}
export function formatOverdue(minutes) {
    if (!Number.isFinite(minutes) || minutes < 0)
        return '—';
    const rounded = Math.round(minutes);
    if (rounded < 60)
        return `${rounded} min`;
    const hours = rounded / 60;
    if (hours < 48)
        return `${hours.toFixed(hours < 10 ? 1 : 0)} h`;
    return `${(hours / 24).toFixed(1)} d`;
}
export function fmtDuration(start, end) {
    if (!start || !end)
        return '—';
    const seconds = (new Date(end).getTime() - new Date(start).getTime()) / 1000;
    if (!Number.isFinite(seconds) || seconds < 0)
        return '—';
    if (seconds < 60)
        return `${seconds.toFixed(1)}s`;
    if (seconds < 3600)
        return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
    return `${(seconds / 3600).toFixed(1)}h`;
}
export function fmtMetricDuration(milliseconds) {
    if (milliseconds < 1000)
        return `${Math.round(milliseconds)} ms`;
    if (milliseconds < 60_000)
        return `${(milliseconds / 1000).toFixed(milliseconds < 10_000 ? 1 : 0)} s`;
    return `${Math.floor(milliseconds / 60_000)}m ${Math.round((milliseconds % 60_000) / 1000)}s`;
}
export function fmtBytes(value) {
    let bytes = value;
    if (bytes === 0)
        return '—';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let unit = 0;
    while (bytes >= 1024 && unit < units.length - 1) {
        bytes /= 1024;
        unit += 1;
    }
    return `${bytes.toFixed(unit === 0 || bytes >= 10 ? 0 : 1)} ${units[unit]}`;
}
