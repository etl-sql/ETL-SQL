// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-context.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-context.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */
/** Shared types and DOM helpers for designer. */
import { _feedback } from './designer-util.js';
export const feedback = _feedback;
/** Query helpers retain the source's direct-access behaviour while keeping DOM types native. */
export function queryElement(root, selector) {
    return root.querySelector(selector);
}
export function queryElements(root, selector) {
    return root.querySelectorAll(selector);
}
/**
 * The name an inspector group is remembered by: its summary's own text, without the count its
 * `<span>` carries. Keyed on the whole text, a group closed itself whenever its count changed,
 * because "Analytics 1" is not the group the author opened as "Analytics".
 */
export function inspectorGroupKey(details) {
    const summary = details.querySelector('summary');
    if (!summary)
        return '';
    return Array.from(summary.childNodes)
        .filter(node => node.nodeType === 3)
        .map(node => node.textContent || '')
        .join('')
        .trim();
}
export function controlTarget(event) {
    return event.target;
}
export function checkedTarget(event) {
    return event.target.checked;
}
export function eventElement(event) {
    return event.target;
}
export function closestElement(event, selector) {
    return event.target?.closest(selector);
}
export function datasetValue(element, key) {
    return element.dataset[key];
}
export const VCATEGORIES = [
    {
        name: 'Charts',
        types: [
            ['BAR', '#3b82f6'], ['LINE', '#06b6d4'], ['AREA', '#0891b2'], ['PIE', '#8b5cf6'],
            ['DONUT', '#a855f7'], ['HBAR', '#6366f1'], ['SCATTER', '#6366f1'], ['GAUGE', '#a855f7'],
            ['FUNNEL', '#d946ef'], ['TREEMAP', '#ec4899'], ['HEATMAP', '#f43f5e'], ['COMBO', '#0ea5e9'],
            ['BOXPLOT', '#14b8a6'], ['WATERFALL', '#10b981'], ['BUBBLE', '#06b6d4'], ['RADAR', '#8b5cf6'],
            ['CANDLESTICK', '#f59e0b'], ['MAP', '#10b981'], ['GANTT', '#8b5cf6'], ['SANKEY', '#14b8a6'],
            ['SUNBURST', '#d946ef'], ['NETWORK', '#6366f1'], ['TRELLIS', '#64748b'], ['MATRIX', '#475569'],
            ['CUSTOM', '#8b5cf6']
        ]
    },
    {
        name: 'Data & Content',
        types: [
            ['TABLE', '#64748b'], ['CARD', '#10b981'], ['TEXT', '#f59e0b'], ['IMAGE', '#ec4899'], ['HTML', '#059669']
        ]
    },
    {
        name: 'Filters & Inputs',
        types: [
            ['SLICER', '#f97316'], ['MULTISELECT', '#f97316'], ['DATEPICKER', '#e11d48'], ['RELDATEPICKER', '#e11d48'],
            ['SLIDER', '#f59e0b'], ['SEARCH', '#0ea5e9'], ['CHECKBOX', '#10b981'], ['TEXTBOX', '#64748b'], ['NUMBERBOX', '#64748b']
        ]
    },
    {
        name: 'Layout & Actions',
        types: [
            ['CONTAINER', '#475569'], ['BUTTON', '#a855f7']
        ]
    }
];
export const VTYPES = VCATEGORIES.flatMap(c => c.types);
