/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * designer-util.js — split out of designer.js, TODO.md §2.
 * Small helpers used from every part of the designer.
 */

export const _feedback = globalThis.ETLSQLFeedback;

export function escapeHtml(value: unknown) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// `esc` and `escapeHtml` are NOT interchangeable: `esc` does not escape `>`.
// Preserved as-is because changing it is a behavior change outside this refactor;
// unifying the two escapers is follow-up work.
export const esc       = (s: unknown) => String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
