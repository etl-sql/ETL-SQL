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

import type { SampleGridInput } from './studio-authoring-ui.js';
import type { STUDIO_ROUTES } from './studio-contracts.js';

export type WorkbenchResult = Partial<Pick<RunDesignerResponse, 'columns' | 'rows' | 'rowCount'>> & {
    trace?: { type: string; data?: SampleGridInput | null }[];
};
export type WorkbenchResponse = WorkbenchResult & Partial<ParseDesignerResponse>;
export type WorkbenchRequest = (route: string, options: {
    body: Pick<RunDesignerRequest, 'script'> & { connectionRef?: RunDesignerRequest['connectionRef'] | null; documentUri?: RunDesignerRequest['documentUri'] | null };
    fallbackError: string;
}) => Promise<WorkbenchResponse>;

type WorkbenchRoutes = Pick<typeof STUDIO_ROUTES, 'analyze' | 'complete' | 'hover' | 'parse' | 'run'>;
export interface QueryWorkbenchOptions {
    connection?: string | null;
    context?: 'remote' | 'engine';
    value?: string;
    routes?: WorkbenchRoutes;
    request?: WorkbenchRequest;
    editorTransport?: { url: (route: string) => string; authFetch: typeof fetch };
    documentUri?: () => string;
    scriptText?: () => string;
    label?: string | null;
    runLabel?: string;
    onChange?: ((value: string) => void) | null;
    onSample?: ((sample: SampleGridInput | null) => void) | null;
}
interface WorkbenchEditor {
    getValue: () => string;
    focus: () => void;
    dispose: () => void;
}

/**
 * Composes the script sent to the preview/run endpoint.
 *
 * Preserves the statement's execution context:
 * - When `context` is 'remote' and a connection is given, wraps the query in
 *   `EXECUTE <connection> BEGIN … END;` so remote SQL runs in the connection's native dialect,
 *   faithful to the eventual statement the execution task will write.
 * - When `context` is 'engine' (default), runs directly as an ETL-SQL statement.
 * - In both cases, only the connection preamble (the CREATE CONNECTION declaration) is prepended;
 *   preceding variables or staging statements are not executed.
 */
export function composeWorkbenchScript(
    query: string,
    preamble = '',
    options: { connection?: string | null; context?: 'remote' | 'engine' } = {}
): string {
    const trimmed = String(query || '').trim().replace(/;+$/, '');
    if (!trimmed) return '';
    const cleanPreamble = preamble ? (preamble.endsWith('\n') ? preamble : `${preamble}\n`) : '';
    if (options.context === 'remote' && options.connection) {
        const body = trimmed
            .split('\n')
            .map(line => (line.length ? `    ${line}` : line))
            .join('\n');
        return `${cleanPreamble}EXECUTE ${options.connection}\nBEGIN\n${body}\nEND;`;
    }
    return `${cleanPreamble}${trimmed};`;
}

/**
 * Mounts the query editor without writing to the host document.
 * Parameter annotations also describe the emitted JavaScript to remaining checkJs callers.
 *
 * @param {HTMLElement} host
 * @param {Object} options
 * @param {string|null} [options.connection] Alias the query runs against, or null. Used for the run
 *   and for the `CREATE CONNECTION` preamble, so an alias resolves as it will at run time.
 * @param {'remote'|'engine'} [options.context] Execution context. 'remote' wraps in `EXECUTE <conn> BEGIN … END;`.
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
export async function createQueryWorkbench(host: HTMLElement, {
    connection = null,
    context = 'engine',
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
}: QueryWorkbenchOptions = {}): Promise<WorkbenchEditor> {
    const defaultLabel = context === 'remote'
        ? `Remote query · ${connection || 'no connection'}`
        : `Query · ${connection || 'no connection'}`;
    host.innerHTML = `
        <div class="etlsql-studio-workbench-toolbar">
            <span>${escapeHtml(label ?? defaultLabel)}</span>
            <button type="button" class="etlsql-studio-btn" data-workbench-run>${escapeHtml(runLabel)}</button>
        </div>
        <div class="etlsql-studio-workbench-editor" data-workbench-editor></div>
        <div class="etlsql-studio-workbench-output" data-workbench-output></div>`;

    const editorHostEl = host.querySelector<HTMLElement>('[data-workbench-editor]')!;
    const output = host.querySelector<HTMLElement>('[data-workbench-output]')!;
    const runButton = host.querySelector<HTMLButtonElement>('[data-workbench-run]')!;

    let editor: WorkbenchEditor | null = null;
    try {
        editor = await createScriptEditor(/** @type {HTMLElement} */ (editorHostEl), {
            value,
            analyzeUrl: editorTransport!.url(routes!.analyze),
            completeUrl: editorTransport!.url(routes!.complete),
            hoverUrl: editorTransport!.url(routes!.hover),
            diagnosticsPanel: false,
            authFetch: editorTransport!.authFetch,
            documentUri,
            onChange: (next: string) => onChange?.(next),
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

    const setOutput = (markup: string) => { output.innerHTML = markup; };

    runButton.addEventListener('click', async () => {
        const query = editor!.getValue().trim().replace(/;$/, '');
        if (!query) return setOutput(noteMarkup('Write a query first.', 'warning'));
        /** @type {HTMLButtonElement} */ (runButton).disabled = true;
        setOutput('<div class="etlsql-studio-loading">Running…</div>');
        try {
            // The query runs through the same bounded semantic path as the eventual statement:
            // its CREATE CONNECTION statement comes along, but preceding variables and staging
            // statements are not silently executed. A remote-context task wraps its body in
            // EXECUTE <connection> BEGIN … END; so native dialects execute faithfully.
            const preamble = await connectionPreamble(connection, scriptText(), { request, routes });
            const script = composeWorkbenchScript(query, preamble, { connection, context });
            const result = await request!(routes!.run, {
                body: { script, connectionRef: connection || null, documentUri: documentUri() || null },
                fallbackError: 'The query could not be run.',
            });
            const sample = firstResultSet(result);
            if (!sample) {
                onSample?.(null);
                setOutput(noteMarkup('The query ran successfully but returned no result set.', 'info'));
                return;
            }
            onSample?.(sample);
            setOutput(sampleGridMarkup(sample));
        } catch (error) {
            onSample?.(null);
            setOutput(noteMarkup((error as Error).message || 'The query failed.', 'error'));
        } finally {
            /** @type {HTMLButtonElement} */ (runButton).disabled = false;
        }
    });

    return {
        getValue: () => editor!.getValue(),
        focus: () => editor!.focus?.(),
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
export async function connectionPreamble(connection: string | null, script: string, { request, routes }: { request?: WorkbenchRequest; routes?: { parse?: string } } = {}): Promise<string> {
    if (!connection) return '';
    if (!request || !routes?.parse) throw new Error('The query workbench was mounted without a parse route.');

    const text = String(script || '');
    if (!text.trim()) return '';

    let parsed;
    try {
        parsed = await request(routes.parse, { body: { script: text }, fallbackError: 'The script could not be parsed.' });
    } catch (error) {
        throw new Error(`The script has to parse before an embedded query can resolve its connections: ${(error as Error).message}`, { cause: error });
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
export function firstResultSet(result: WorkbenchResult | null | undefined): SampleGridInput | null {
    if (Array.isArray(result?.rows)) {
        return {
            columns: result.columns || [],
            rows: result.rows,
            rowCount: result.rowCount ?? result.rows.length,
        };
    }
    const entry = (result?.trace || []).find(item => item.type === 'resultset' && item.data);
    if (!entry) return null;
    const rows = entry.data!.rows || [];
    return { columns: entry.data!.columns || [], rows, rowCount: entry.data!.rowCount ?? rows.length };
}
