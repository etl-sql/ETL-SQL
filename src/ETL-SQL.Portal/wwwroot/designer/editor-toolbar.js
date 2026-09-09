// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/editor-toolbar.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/editor-toolbar.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * editor-toolbar.js — split out of designer.js, TODO.md §2.
 * The editor toolbar's icon set and button markup builder.
 */
import { escapeHtml } from './designer-util.js';
// Toolbar iconography. Inline stroke SVGs (currentColor, 16px) keep the workbench
// self-contained — no icon font or sprite sheet to ship to VS Code / Player / Portal.
export const _TOOLBAR_ICONS = {
    back: '<path d="M10 3 5 8l5 5"/><path d="M5.5 8H14"/><path d="M2.5 3.5v9"/>',
    sidebar: '<path d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5z"/><path d="M6.5 2v12"/>',
    theme: '<path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z"/>',
    commands: '<path d="m4 5 3 3-3 3"/><path d="M8.5 11h4"/>',
    addPage: '<path d="M3.5 2.5h6L12.5 5.5v8h-9z"/><path d="M9.5 2.5v3h3"/><path d="M8 8v4"/><path d="M6 10h4"/>',
    tidy: '<path d="M3 4h10"/><path d="M5 8h6"/><path d="M7 12h2"/><path d="M12 2l1.5 1.5L12 5"/>',
    split: '<path d="M2.5 3.5h11v9h-11z"/><path d="M8 3.5v9"/>',
    suggest: '<path d="m8 2 1.6 3.9L13.5 7.5 9.6 9.1 8 13l-1.6-3.9L2.5 7.5l3.9-1.6z"/>',
    flow: '<circle cx="3.5" cy="4" r="1.8"/><circle cx="12.5" cy="4" r="1.8"/><circle cx="8" cy="12" r="1.8"/><path d="M5.3 4h5.4"/><path d="M4.5 5.6 7 10.4"/><path d="M11.5 5.6 9 10.4"/>',
    runSelected: '<path d="M2.5 3.5h3"/><path d="M2.5 12.5h3"/><path d="m7.5 3.5 6 4.5-6 4.5z"/>',
    run: '<path d="m4 2.5 9 5.5-9 5.5z"/>',
    preview: '<path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8"/><circle cx="8" cy="8" r="1.9"/>',
    apply: '<path d="M2 3.5h12"/><path d="M2 8h12"/><path d="M2 12.5h7"/>',
    save: '<path d="M3 2.5h7.5L13.5 5.5V13a.5.5 0 0 1-.5.5H3a.5.5 0 0 1-.5-.5V3a.5.5 0 0 1 .5-.5"/><path d="M5 2.5v4h5v-4"/><path d="M5 13.5v-4h6v4"/>',
    close: '<path d="m4 4 8 8"/><path d="m12 4-8 8"/>',
    cancel: '<rect x="4" y="4" width="8" height="8" rx="1"/>',
    commit: '<circle cx="4" cy="8" r="1.75"/><circle cx="12" cy="4" r="1.75"/><circle cx="12" cy="12" r="1.75"/><path d="M5.75 8h1.5c1.8 0 2.6-1.2 3.1-2.5"/><path d="M5.75 8h1.5c1.8 0 2.6 1.2 3.1 2.5"/>',
    format: '<path d="M2 3.5h12"/><path d="M2 7.5h8"/><path d="M2 11.5h12"/><path d="M2 15.5h6"/>',
    formatSettings: '<path d="M8 2.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11z"/><path d="M8 1v2m0 10v2m-6-7h2m10 0h2m-2.1-4.9-1.4 1.4m-7 7-1.4 1.4m0-9.8 1.4 1.4m7 7 1.4 1.4"/>',
    connection: '<path d="M4 2.5a3.5 3.5 0 0 0 7 0v2H4z"/><path d="M6 6.5v4a1.5 1.5 0 0 0 3 0v-4"/><path d="M7.5 12v2"/>',
};
export function toolbarIcon(name) {
    return `<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.4"
        stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${_TOOLBAR_ICONS[name] || ''}</svg>`;
}
// Icon-only by default; `label` is reserved for the primary action so the toolbar
// still reads at a glance. Everything carries a title + aria-label for a11y.
/**
 * @param {Object} button
 * @param {string} button.attr    The data-attribute the click handler binds to.
 * @param {string} button.icon    A key into `_TOOLBAR_ICONS`.
 * @param {string} button.title   Tooltip and aria-label.
 * @param {string} [button.label] Visible text. Reserved for the primary action.
 * @param {boolean} [button.primary]
 * @param {string} [button.key]   Keyboard shortcut, appended to the tooltip.
 */
export function toolbarButton({ attr, icon, title, label, primary, key }) {
    const hint = key ? `${title} (${key})` : title;
    return `<button type="button" class="etlsql-tool-btn${primary ? ' etlsql-tool-btn-primary' : ''}${label ? ' etlsql-tool-btn-labelled' : ''}"
        ${attr} title="${escapeHtml(hint)}" aria-label="${escapeHtml(title)}">${toolbarIcon(icon)}${label ? `<span>${escapeHtml(label)}</span>` : ''}</button>`;
}
