// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * The embedded query builder: a real script editor, a run, and the rows it returned.
 *
 * This is a standalone authoring component, not a part of the dataset wizard, because the same
 * surface is needed wherever an author writes a query by hand without leaving the thing they are
 * building — the dataset wizard's "write a query" pane today, and the pipeline DAG's execution task
 * next. A second copy would drift: one would get completions and the other a textarea.
 *
 * It obeys the authoring component contract (see studio-authoring.js): host-neutral, no network of
 * its own beyond the injected `request`, and it never writes to the document. It returns the query
 * text; deciding what statement to build from it belongs to the caller.
 */

import { createScriptEditor } from './designer.js';
import { escapeHtml, noteMarkup, sampleGridMarkup } from './studio-authoring-ui.js';

/**
 * Mounts the workbench into `host`.
 *
 * The untyped `@param connection`/`@param value`/… lines this block used to carry were bound by
 * position, so they described `host` and then the whole options object, and every typed entry
 * after them was ignored. They are folded into the typed ones below.
 *
 * @param {HTMLElement} host
 * @param {Object} options
 * @param {string|null} [options.connection] Alias the query runs against, or null. Used for the run
 *   and for the `CREATE CONNECTION` preamble, so an alias resolves as it will at run time.
 * @param {string} [options.value] Starting query text.
 * @param {*} [options.routes] Route table; never a literal path.
 * @param {Function} [options.request] `(route, { body, fallbackError }) => Promise<json>` — the only
 *   network path.
 * @param {*} [options.editorTransport] `{ url(route), authFetch }` handed to the embedded editor,
 *   which owns its own transport. This module never calls it.
 * @param {() => string} [options.documentUri] So analysis and completion resolve the host document's
 *   schema exactly as the main editor does.
 * @param {() => string} [options.scriptText] The current buffer, read only to find the connection's
 *   declaration. The workbench never writes to it.
 * @param {string|null} [options.label] Toolbar caption, so a pipeline task can say what this query
 *   is for.
 * @param {string} [options.runLabel] Run button caption.
 * @param {Function|null} [options.onChange]
 * @param {Function|null} [options.onSample]
 * @returns {Promise<{getValue: () => string, focus: () => void, dispose: () => void}>}
 */
export async function createQueryWorkbench(host, {
    connection = null,
    value = '',
    routes,
    request,
    editorTransport,
    documentUri = () => 'untitled.rptsql',
    scriptText = () => '',
    label = null,
    runLabel = 'Run and preview',
    onChange = null,
    onSample = null,
} = {}) {
    host.innerHTML = `
        <div class="etlsql-studio-workbench-toolbar">
            <span>${escapeHtml(label ?? `Query · ${connection || 'no connection'}`)}</span>
            <button type="button" class="etlsql-studio-btn" data-workbench-run>${escapeHtml(runLabel)}</button>
        </div>
        <div class="etlsql-studio-workbench-editor" data-workbench-editor></div>
        <div class="etlsql-studio-workbench-output" data-workbench-output></div>`;

    const editorHostEl = host.querySelector('[data-workbench-editor]');
    const output = host.querySelector('[data-workbench-output]');
    const runButton = host.querySelector('[data-workbench-run]');

    let editor = null;
    try {
        editor = await createScriptEditor(/** @type {HTMLElement} */ (editorHostEl), {
            value,
            analyzeUrl: editorTransport.url(routes.analyze),
            completeUrl: editorTransport.url(routes.complete),
            hoverUrl: editorTransport.url(routes.hover),
            diagnosticsPanel: false,
            authFetch: editorTransport.authFetch,
            documentUri,
            onChange: next => onChange?.(next),
        });
    } catch {
        // The same fallback the main editor uses: a plain textarea still lets the author type a query
        // when CodeMirror cannot load, rather than leaving the pane empty.
        const textarea = document.createElement('textarea');
        textarea.className = 'etlsql-studio-workbench-fallback';
        textarea.spellcheck = false;
        textarea.value = value;
        textarea.addEventListener('input', () => onChange?.(textarea.value));
        editorHostEl.appendChild(textarea);
        editor = { getValue: () => textarea.value, focus: () => textarea.focus(), dispose: () => {} };
    }

    const setOutput = markup => { output.innerHTML = markup; };

    runButton.addEventListener('click', async () => {
        const query = editor.getValue().trim().replace(/;$/, '');
        if (!query) return setOutput(noteMarkup('Write a query first.', 'warning'));
        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (runButton).disabled = true;
        setOutput('<div class="etlsql-studio-loading">Running…</div>');
        try {
            // The query runs in the document's own context: its CREATE CONNECTION statements come
            // along, so an alias the script declares resolves exactly as it will at run time.
            const script = `${await connectionPreamble(connection, scriptText(), { request, routes })}${query};`;
            const sample = firstResultSet(await request(routes.run, {
                body: { script, connectionRef: connection || null, documentUri: documentUri() || null },
                fallbackError: 'The query could not be run.',
            }));
            if (!sample) throw new Error('The query ran but returned no result set.');
            onSample?.(sample);
            setOutput(sampleGridMarkup(sample));
        } catch (error) {
            onSample?.(null);
            setOutput(noteMarkup(error.message || 'The query failed.', 'error'));
        } finally {
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (runButton).disabled = false;
        }
    });

    return {
        getValue: () => editor.getValue(),
        focus: () => editor.focus?.(),
        dispose: () => { editor?.dispose?.(); host.innerHTML = ''; },
    };
}

/**
 * The document's own CREATE CONNECTION statement, so an embedded run resolves the same alias.
 *
 * The declaration comes from the canonical parse, not a text scan. The regex this replaced ended the
 * statement at the first `;`, which is wrong for every connection whose body contains one — inside a
 * quoted password, inside a comment, or spread over several lines with an option list — and it
 * interpolated the alias into a pattern, so a name with a regex metacharacter matched the wrong
 * statement or nothing at all. Either way the run failed with "unknown connection" against a script
 * that declares it.
 *
 * A script that does not parse throws rather than silently running without the preamble: the run
 * would fail anyway, and the parse error is the message that actually explains why.
 */
/**
 * @param {string|null} connection
 * @param {string} script
 * @param {Object} [options]
 * @param {Function} [options.request] Fetch wrapper.
 * @param {{parse?: string, [key: string]: *}} [options.routes] Route table the host serves.
 */
export async function connectionPreamble(connection, script, { request, routes } = {}) {
    if (!connection) return '';
    if (!request || !routes?.parse) throw new Error('The query workbench was mounted without a parse route.');

    const text = String(script || '');
    if (!text.trim()) return '';

    let parsed;
    try {
        parsed = await request(routes.parse, { body: { script: text }, fallbackError: 'The script could not be parsed.' });
    } catch (error) {
        throw new Error(`The script has to parse before an embedded query can resolve its connections: ${error.message}`);
    }
    if (parsed?.error) {
        throw new Error(`The script has to parse before an embedded query can resolve its connections: ${parsed.error}`);
    }

    const wanted = String(connection).trim().replace(/^\[|\]$/g, '').toLowerCase();
    const declaration = (parsed?.designState?.connections || [])
        .find(entry => String(entry?.name || '').trim().replace(/^\[|\]$/g, '').toLowerCase() === wanted);
    if (!declaration?.text) return '';

    // Hosts differ on whether the authored slice keeps its terminator; the run needs exactly one.
    return `${declaration.text.trim().replace(/;+$/, '')};\n`;
}

/** Both run shapes: a flat `{ columns, rows }` payload, or the first resultset inside a trace. */
export function firstResultSet(result) {
    if (Array.isArray(result?.rows) && result.rows.length) {
        return {
            columns: result.columns || [],
            rows: result.rows,
            rowCount: result.rowCount ?? result.rows.length,
        };
    }
    const entry = (result?.trace || []).find(item => item.type === 'resultset' && item.data);
    if (!entry) return null;
    const rows = entry.data.rows || [];
    return { columns: entry.data.columns || [], rows, rowCount: entry.data.rowCount ?? rows.length };
}
