// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-editor.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/script-editor.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * script-editor.js — split out of designer.js, TODO.md §2.
 * The CodeMirror-backed rptsql script editor.
 */
import { escapeHtml } from './designer-util.js';
import { _loadCm, _getRptsqlLang, _getRptsqlHighlightStyle } from './rptsql-language.js';
export function completionKind(kind) {
    switch (String(kind ?? '').toLowerCase()) {
        case 'keyword': return 'keyword';
        case 'function': return 'function';
        case 'table': return 'class';
        case 'column': return 'property';
        case 'variable': return 'variable';
        case 'alias': return 'variable';
        case 'connection': return 'namespace';
        case 'connector': return 'namespace';
        case 'path': return 'file';
        case 'optionname': return 'property';
        case 'optionvalue': return 'constant';
        case 'snippet': return 'text';
        default: return 'text';
    }
}
export function markdownToTooltipHtml(markdown) {
    const lines = String(markdown || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    const html = [];
    let inCode = false;
    let codeLines = [];
    const renderInline = (value) => escapeHtml(value)
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    const flushCode = () => {
        html.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`);
        codeLines = [];
    };
    for (const line of lines) {
        if (line.trimStart().startsWith('```')) {
            if (inCode) {
                flushCode();
                inCode = false;
            }
            else {
                inCode = true;
                codeLines = [];
            }
            continue;
        }
        if (inCode) {
            codeLines.push(line);
            continue;
        }
        const heading = line.match(/^(#{1,6})\s+(.+)$/);
        if (heading) {
            const level = Math.min(6, heading[1].length);
            html.push(`<div class="etlsql-editor-hover-heading etlsql-editor-hover-heading-${level}">${renderInline(heading[2].trim())}</div>`);
            continue;
        }
        const bullet = line.match(/^\s*[-*]\s+(.+)$/);
        if (bullet) {
            html.push(`<div class="etlsql-editor-hover-bullet">${renderInline(bullet[1].trim())}</div>`);
            continue;
        }
        if (!line.trim()) {
            html.push('<div class="etlsql-editor-hover-gap"></div>');
            continue;
        }
        html.push(`<div class="etlsql-editor-hover-line">${renderInline(line.trim())}</div>`);
    }
    if (inCode)
        flushCode();
    return html.join('');
}
export function diagnosticSeverity(d) {
    const severity = String(d?.severity ?? '').toLowerCase();
    if (severity.includes('error') || d?.severity === 0)
        return 'error';
    if (severity.includes('info') || severity.includes('hint'))
        return 'info';
    return 'warning';
}
/**
 * What a caller may hand `createScriptEditor`.
 *
 * Written out rather than inferred, because every option here is read on one branch and the shape
 * TypeScript infers from a single call site does not carry the rest — so a caller passing a real
 * option was reported as passing an unknown one, and a caller passing a misspelt one was not.
 *
 * @typedef {Object} ScriptEditorOptions
 * @property {string}   [value='']          Initial script content.
 * @property {boolean}  [readOnly=false]
 * @property {Function} [onChange]          Called with the full new value on each change.
 * @property {string}   [analyzeUrl]        Endpoint for real parser/linter diagnostics.
 * @property {string}   [completeUrl]       Endpoint for context-aware completions.
 * @property {string}   [hoverUrl]          Endpoint for hover documentation.
 * @property {string}   [connectionRef]     Shared connection alias for schema completions.
 * @property {Function} [authFetch]         Fetch wrapper used for the endpoints above.
 * @property {Function} [onDiagnostics]     Called with returned diagnostics.
 * @property {Function} [onCursorActivity]  Called with (position, text) as the caret moves.
 * @property {string|(() => string)} [documentUri] Which document's registered connections to ask
 *   about. A function when the host's answer changes over the editor's lifetime.
 * @property {number}   [analyzeDebounceMs=450] How long to wait after a keystroke before analysing.
 * @property {boolean}  [analyzeOnLoad=true]    Analyse the initial content as well as later edits.
 * @property {boolean}  [diagnosticsPanel=true] Mount the diagnostics gutter and panel.
 */
/**
 * The editor handle `createScriptEditor` hands back.
 *
 * @typedef {Object} ScriptEditorHandle
 * @property {() => string} getValue
 * @property {() => string} getSelection      Every non-empty range, joined by newlines. Plural on
 *   purpose: a multi-range selection is one of the things the host runs.
 * @property {() => *} getCurrentStatement    The statement under the caret.
 * @property {boolean} hasCompletion          Whether a completion endpoint was configured.
 * @property {() => boolean} triggerCompletion
 * @property {(text: string) => void} setValue
 * @property {(text: string) => ({from: number, to: number}|null)} replaceAll Dispatches only the
 *   span that changed, so the caret and scroll position survive a GUI-generated edit.
 * @property {(from: number, to: number) => void} revealRange
 * @property {(fromLine: number, toLine?: number) => void} revealLines
 * @property {(line: number, column?: number) => void} gotoLine
 * @property {() => void} focus            Puts the caret back in the editor.
 * @property {() => number} getCursorLine
 * @property {() => Promise<*>} analyze
 * @property {() => void} undo
 * @property {() => void} redo
 * @property {() => void} dispose
 * @property {() => *} [getState]
 * @property {(state: *) => void} [setState]
 * @property {(text: string) => *} [createDocState]
 * @property {() => { top: number, left: number }} [getScrollPosition]
 * @property {(pos: { top?: number, left?: number }) => void} [setScrollPosition]
 */
/**
 * Mount a CodeMirror 6 rptsql editor into `container`.
 *
 * Dynamically loads designer/codemirror/codemirror-bundle.min.js the first
 * time it is called, then initialises a CodeMirror EditorView with the custom
 * rptsql language mode.
 *
 * @param {HTMLElement} container
 * @param {ScriptEditorOptions} [opts]
 * @returns {Promise<ScriptEditorHandle>}
 *   Returns a promise so callers can await the dynamic bundle load.
 */
export async function createScriptEditor(container, opts = {}) {
    container.classList.add('etlsql-editor-container');
    const cm = await _loadCm();
    const { EditorState, EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection, defaultKeymap, history, historyKeymap, indentWithTab, syntaxHighlighting, bracketMatching, searchKeymap, highlightSelectionMatches, autocompletion, completionKeymap, linter, lintGutter, } = cm;
    const analyzeUrl = opts.analyzeUrl || null;
    const completeUrl = opts.completeUrl || null;
    const hoverUrl = opts.hoverUrl || null;
    const analyzeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const completeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const hoverFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const debounceMs = Number.isFinite(opts.analyzeDebounceMs) ? opts.analyzeDebounceMs : 450;
    const hasCmLint = Boolean(analyzeUrl && typeof linter === 'function');
    const completionKeys = Array.isArray(completionKeymap) ? completionKeymap : [];
    const historyKeys = Array.isArray(historyKeymap) ? historyKeymap : [];
    const acceptCompletionKey = completionKeys.find(binding => binding?.key === 'Enter' && typeof binding.run === 'function');
    // Reuse the bundle's Ctrl-Space -> startCompletion binding so the toolbar button and an
    // OS-safe alternate key can invoke completion without importing the minified internal.
    // Windows commonly swallows Ctrl-Space for IME/input-language switching, so we also bind Ctrl-.
    const startCompletionKey = completionKeys.find(binding => binding?.key === 'Ctrl-Space' && typeof binding.run === 'function');
    const keymaps = [
        ...(acceptCompletionKey ? [{ ...acceptCompletionKey, key: 'Tab' }] : []),
        ...(startCompletionKey ? [{ ...startCompletionKey, key: 'Ctrl-.' }] : []),
        ...completionKeys,
        indentWithTab,
        ...(Array.isArray(defaultKeymap) ? defaultKeymap : []),
        ...(Array.isArray(historyKeymap) ? historyKeymap : []),
        ...(Array.isArray(searchKeymap) ? searchKeymap : []),
    ];
    function etlSqlTokenRange(state, pos) {
        const line = state.doc.lineAt(pos);
        const text = line.text;
        let offset = Math.max(0, Math.min(text.length, pos - line.from));
        const isTokenCharacter = (character) => /[\w@#&$]/.test(character || '');
        if (!isTokenCharacter(text[offset]) && offset > 0 && isTokenCharacter(text[offset - 1]))
            offset--;
        if (!isTokenCharacter(text[offset]))
            return null;
        let from = offset;
        let to = offset + 1;
        while (from > 0 && isTokenCharacter(text[from - 1]))
            from--;
        while (to < text.length && isTokenCharacter(text[to]))
            to++;
        return { from: line.from + from, to: line.from + to };
    }
    const extensions = [
        lineNumbers(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        bracketMatching(),
        syntaxHighlighting(_getRptsqlHighlightStyle(cm), { fallback: true }),
        highlightSelectionMatches(),
        keymap.of(keymaps),
        _getRptsqlLang(cm),
        ...(opts.onCursorActivity ? [cm.EditorView.updateListener.of((update) => {
                if (update.selectionSet || update.focusChanged) {
                    const pos = update.state.selection.main.head;
                    opts.onCursorActivity(pos, update.state.doc.toString());
                }
            })] : []),
        // Accept schema/session explorer drags. Scoped to our private MIME type so
        // ordinary text drag-and-drop keeps CodeMirror's default behaviour.
        EditorView.domEventHandlers({
            mousedown(event, view) {
                if (event.button !== 0 || event.detail !== 2)
                    return false;
                const pos = view.posAtCoords({ x: event.clientX, y: event.clientY });
                if (pos == null)
                    return false;
                const range = etlSqlTokenRange(view.state, pos);
                if (!range)
                    return false;
                event.preventDefault();
                view.dispatch({ selection: { anchor: range.from, head: range.to } });
                view.focus();
                return true;
            },
            dragover(event) {
                if (!event.dataTransfer?.types?.includes('application/x-etlsql-snippet'))
                    return false;
                event.preventDefault();
                event.dataTransfer.dropEffect = 'copy';
                return true;
            },
            drop(event, view) {
                const snippet = event.dataTransfer?.getData('application/x-etlsql-snippet');
                if (!snippet)
                    return false;
                event.preventDefault();
                const pos = view.posAtCoords({ x: event.clientX, y: event.clientY })
                    ?? view.state.selection.main.head;
                view.dispatch({
                    changes: { from: pos, insert: snippet },
                    selection: { anchor: pos + snippet.length },
                });
                view.focus();
                return true;
            },
        }),
        EditorState.readOnly.of(opts.readOnly ?? false),
    ];
    if (hasCmLint && typeof lintGutter === 'function')
        extensions.push(lintGutter());
    let analyzeTimer = null;
    let analyzeAbort = null;
    let analyzeRequest = null;
    let analyzeRequestScript = null;
    let view = null;
    let diagPanel = null;
    let hoverTimer = null;
    let hoverHideTimer = null;
    let hoverAbort = null;
    let hoverTip = null;
    let hoverKey = '';
    function cursorLineColumn(state, pos) {
        const line = state.doc.lineAt(pos);
        return { line: line.number - 1, column: pos - line.from };
    }
    function currentDocumentUri() {
        return typeof opts.documentUri === 'function'
            ? opts.documentUri()
            : (opts.documentUri || 'portal-designer');
    }
    function currentStatement() {
        if (!view)
            return '';
        const script = view.state.doc.toString();
        const pos = view.state.selection.main.head;
        let start = script.lastIndexOf(';', Math.max(0, pos - 1));
        let end = script.indexOf(';', pos);
        start = start < 0 ? 0 : start + 1;
        // Keep the terminating ';' — the extracted text is parsed on its own and
        // statements like CREATE CONNECTION require it.
        end = end < 0 ? script.length : end + 1;
        return script.slice(start, end).trim();
    }
    function createCompletionSource() {
        if (!completeUrl || typeof autocompletion !== 'function')
            return null;
        return async (context) => {
            let word = context.matchBefore(/[\w@#&$.*]+/);
            const previous = context.state.sliceDoc(Math.max(0, context.pos - 1), context.pos);
            if (!word && context.explicit && (previous === '*' || previous === '.')) {
                word = { from: context.pos - 1, to: context.pos, text: previous };
            }
            if (!word && !context.explicit) {
                return null;
            }
            if (word && word.from === word.to && !context.explicit && !/[\s.]/.test(previous)) {
                return null;
            }
            const { line, column } = cursorLineColumn(context.state, context.pos);
            const res = await completeFetch(completeUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    script: context.state.doc.toString(),
                    line,
                    column,
                    connectionRef: opts.connectionRef || null,
                    documentUri: currentDocumentUri(),
                }),
            });
            if (!res?.ok)
                return null;
            const data = await res.json();
            const items = Array.isArray(data?.items) ? data.items : [];
            const defaultFrom = word?.from ?? context.pos;
            return {
                from: defaultFrom,
                options: items.map(item => ({
                    label: item.label,
                    apply: (editorView, _completion, from, to) => {
                        const docLine = editorView.state.doc.line(line + 1);
                        const startColumn = Number.isFinite(item.startColumn) ? item.startColumn : null;
                        const endColumn = Number.isFinite(item.endColumn) ? item.endColumn : null;
                        const applyFrom = startColumn === null ? from : Math.min(docLine.to, docLine.from + Math.max(0, startColumn));
                        const applyTo = endColumn === null ? to : Math.min(docLine.to, docLine.from + Math.max(0, endColumn));
                        editorView.dispatch({
                            changes: { from: applyFrom, to: applyTo, insert: item.insertText || item.label },
                            selection: { anchor: applyFrom + String(item.insertText || item.label).length },
                            scrollIntoView: true,
                            userEvent: 'input.complete',
                        });
                    },
                    type: completionKind(item.kind),
                    detail: item.detail || item.kind || '',
                    info: item.documentation || undefined,
                    boost: item.label === 'Expand columns' ? 99 : 0,
                })),
            };
        };
    }
    function wordAtPosition(state, pos) {
        const line = state.doc.lineAt(pos);
        const text = line.text;
        let offset = Math.max(0, Math.min(text.length, pos - line.from));
        const isWord = (ch) => /[\w@#&$]/.test(ch || '');
        if (!isWord(text[offset]) && offset > 0 && isWord(text[offset - 1]))
            offset--;
        if (!isWord(text[offset]))
            return null;
        let start = offset;
        let end = offset + 1;
        while (start > 0 && isWord(text[start - 1]))
            start--;
        while (end < text.length && isWord(text[end]))
            end++;
        return {
            word: text.slice(start, end),
            line: line.number - 1,
            column: start,
        };
    }
    function hideHover() {
        clearTimeout(hoverHideTimer);
        hoverTip?.remove();
        hoverTip = null;
        hoverKey = '';
    }
    function scheduleHideHover(delay = 180) {
        clearTimeout(hoverHideTimer);
        hoverHideTimer = setTimeout(() => hideHover(), delay);
    }
    async function showHover(evt) {
        if (!hoverUrl || !view)
            return;
        const pos = view.posAtCoords({ x: evt.clientX, y: evt.clientY });
        if (pos == null) {
            hideHover();
            return;
        }
        const word = wordAtPosition(view.state, pos);
        if (!word) {
            hideHover();
            return;
        }
        const nextKey = `${word.word}:${word.line}:${word.column}`;
        if (nextKey === hoverKey)
            return;
        hoverKey = nextKey;
        hoverAbort?.abort();
        hoverAbort = new AbortController();
        try {
            const res = await hoverFetch(hoverUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    word: word.word,
                    script: view.state.doc.toString(),
                    line: word.line,
                    column: word.column,
                    documentUri: currentDocumentUri(),
                }),
                signal: hoverAbort.signal,
            });
            if (!res?.ok) {
                hideHover();
                return;
            }
            const data = await res.json();
            if (!data?.markdown) {
                hideHover();
                return;
            }
            if (!hoverTip) {
                hoverTip = document.createElement('div');
                hoverTip.addEventListener('mouseenter', () => clearTimeout(hoverHideTimer));
                hoverTip.addEventListener('mouseleave', () => scheduleHideHover(120));
            }
            hoverTip.className = 'etlsql-editor-hover';
            hoverTip.innerHTML = markdownToTooltipHtml(data.markdown);
            document.body.appendChild(hoverTip);
            // Intelligent collision avoidance: if CodeMirror's lint diagnostic tooltip is open,
            // stack hover documentation neatly below (or above) it instead of overlapping.
            const lintTooltip = view?.dom?.querySelector('.cm-tooltip-lint, .cm-tooltip-hover, .cm-tooltip');
            if (lintTooltip && lintTooltip.isConnected) {
                const lintRect = lintTooltip.getBoundingClientRect();
                if (lintRect.width > 0 && lintRect.height > 0) {
                    const tipWidth = hoverTip.offsetWidth || 340;
                    const tipHeight = hoverTip.offsetHeight || 180;
                    const spaceBelow = window.innerHeight - lintRect.bottom;
                    const spaceAbove = lintRect.top;
                    let left = Math.max(12, Math.min(window.innerWidth - tipWidth - 16, lintRect.left));
                    let top;
                    if (spaceBelow >= tipHeight + 12 || spaceBelow >= spaceAbove) {
                        top = Math.min(window.innerHeight - tipHeight - 12, lintRect.bottom + 6);
                    }
                    else if (spaceAbove >= tipHeight + 12) {
                        top = Math.max(12, lintRect.top - tipHeight - 6);
                    }
                    else {
                        left = Math.min(window.innerWidth - tipWidth - 16, lintRect.right + 10);
                        top = Math.max(12, Math.min(window.innerHeight - tipHeight - 16, lintRect.top));
                    }
                    hoverTip.style.left = `${left}px`;
                    hoverTip.style.top = `${top}px`;
                    return;
                }
            }
            // Normal cursor positioning with viewport bounds protection
            const tipWidth = hoverTip.offsetWidth || 340;
            const tipHeight = hoverTip.offsetHeight || 180;
            let left = evt.clientX + 14;
            let top = evt.clientY + 18;
            if (left + tipWidth > window.innerWidth - 16) {
                left = Math.max(12, evt.clientX - tipWidth - 14);
            }
            if (top + tipHeight > window.innerHeight - 16) {
                top = Math.max(12, evt.clientY - tipHeight - 14);
            }
            hoverTip.style.left = `${left}px`;
            hoverTip.style.top = `${top}px`;
        }
        catch (err) {
            if (err?.name !== 'AbortError')
                hideHover();
        }
    }
    function attachHover() {
        if (!hoverUrl)
            return;
        container.addEventListener('mousemove', (evt) => {
            clearTimeout(hoverTimer);
            hoverTimer = setTimeout(() => showHover(evt), 450);
        });
        container.addEventListener('mouseleave', () => {
            clearTimeout(hoverTimer);
            hoverAbort?.abort();
            scheduleHideHover();
        });
    }
    function setDiagnosticsStatus(text, kind = 'neutral') {
        if (!diagPanel)
            return;
        const status = diagPanel.querySelector('.etlsql-editor-diagnostics-status');
        if (status) {
            status.textContent = text;
            status.dataset.kind = kind;
        }
    }
    function diagnosticOffset(doc, line, column) {
        const safeLine = Math.max(1, Math.min(doc.lines, (Number.isFinite(line) ? line : 0) + 1));
        const docLine = doc.line(safeLine);
        return Math.min(docLine.to, docLine.from + Math.max(0, Number.isFinite(column) ? column : 0));
    }
    function toCodeMirrorDiagnostic(doc, d) {
        const from = diagnosticOffset(doc, d.startLine, d.startColumn);
        const endLine = Number.isFinite(d.endLine) ? d.endLine : d.startLine;
        const endColumn = Number.isFinite(d.endColumn) ? d.endColumn : ((d.startColumn !== undefined) ? d.startColumn + 1 : 1);
        let to = diagnosticOffset(doc, endLine, endColumn);
        if (to <= from)
            to = Math.min(doc.length, from + 1);
        return {
            from,
            to,
            severity: diagnosticSeverity(d),
            source: d.source || d.code || 'ETL-SQL',
            message: d.message || d.code || 'Diagnostic',
        };
    }
    function renderDiagnostics(diagnostics) {
        opts.onDiagnostics?.(diagnostics);
        if (!diagPanel)
            return;
        const list = diagPanel.querySelector('.etlsql-editor-diagnostics-list');
        if (!list)
            return;
        list.innerHTML = '';
        if (!diagnostics.length) {
            setDiagnosticsStatus('No diagnostics', 'ok');
            return;
        }
        const errors = diagnostics.filter(d => String(d.severity).toLowerCase().includes('error') || d.severity === 0).length;
        setDiagnosticsStatus(`${diagnostics.length} diagnostic${diagnostics.length === 1 ? '' : 's'}${errors ? ` · ${errors} error${errors === 1 ? '' : 's'}` : ''}`, errors ? 'error' : 'warn');
        for (const d of diagnostics.slice(0, 50)) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'etlsql-editor-diagnostic';
            item.dataset.severity = String(d.severity ?? 'Warning').toLowerCase();
            const line = Number.isFinite(d.startLine) ? d.startLine + 1 : 1;
            const column = Number.isFinite(d.startColumn) ? d.startColumn + 1 : 1;
            item.innerHTML = `<span class="etlsql-editor-diagnostic-code">${escapeHtml(d.code || d.source || 'diagnostic')}</span><span class="etlsql-editor-diagnostic-pos">${line}:${column}</span><span class="etlsql-editor-diagnostic-msg">${escapeHtml(d.message || '')}</span>`;
            item.addEventListener('click', () => {
                if (!view)
                    return;
                const safeLine = Math.max(1, Math.min(view.state.doc.lines, line));
                const docLine = view.state.doc.line(safeLine);
                const pos = Math.min(docLine.to, docLine.from + Math.max(0, column - 1));
                view.dispatch({ selection: { anchor: pos }, effects: EditorView.scrollIntoView(pos, { y: 'center' }) });
                view.focus();
            });
            list.appendChild(item);
        }
    }
    async function fetchDiagnostics(script) {
        if (!analyzeUrl)
            return [];
        if (analyzeRequest && analyzeRequestScript === script)
            return await analyzeRequest;
        analyzeAbort?.abort();
        const controller = new AbortController();
        analyzeAbort = controller;
        analyzeRequestScript = script;
        analyzeRequest = (async () => {
            const res = await analyzeFetch(analyzeUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: currentDocumentUri() }),
                signal: controller.signal,
            });
            if (!res?.ok)
                throw new Error(res?.statusText || 'Analyze request failed');
            const data = await res.json();
            return data?.diagnostics ?? [];
        })();
        try {
            return await analyzeRequest;
        }
        finally {
            if (analyzeAbort === controller)
                analyzeAbort = null;
            if (analyzeRequestScript === script) {
                analyzeRequest = null;
                analyzeRequestScript = null;
            }
        }
    }
    async function runAnalysis(script) {
        if (!analyzeUrl)
            return;
        setDiagnosticsStatus('Analyzing...', 'neutral');
        try {
            renderDiagnostics(await fetchDiagnostics(script));
        }
        catch (err) {
            if (err?.name === 'AbortError')
                return;
            renderDiagnostics([{
                    startLine: 0,
                    startColumn: 0,
                    severity: 'Error',
                    code: 'ANALYZE_REQUEST',
                    source: 'Portal editor',
                    message: err?.message || 'Analyze request failed',
                }]);
        }
    }
    function scheduleAnalysis(script) {
        if (!analyzeUrl)
            return;
        clearTimeout(analyzeTimer);
        analyzeTimer = setTimeout(() => runAnalysis(script), debounceMs);
    }
    if (hasCmLint) {
        extensions.push(linter(async (editorView) => {
            try {
                const diagnostics = await fetchDiagnostics(editorView.state.doc.toString());
                renderDiagnostics(diagnostics);
                return diagnostics.map(d => toCodeMirrorDiagnostic(editorView.state.doc, d));
            }
            catch (err) {
                if (err?.name === 'AbortError')
                    return [];
                const diagnostic = {
                    startLine: 0,
                    startColumn: 0,
                    severity: 'Error',
                    code: 'ANALYZE_REQUEST',
                    source: 'Portal editor',
                    message: err?.message || 'Analyze request failed',
                };
                renderDiagnostics([diagnostic]);
                return [toCodeMirrorDiagnostic(editorView.state.doc, diagnostic)];
            }
        }, { delay: debounceMs }));
    }
    const completionSource = createCompletionSource();
    if (completionSource) {
        extensions.push(autocompletion({ override: [completionSource] }));
    }
    if (opts.onChange) {
        extensions.push(EditorView.updateListener.of((update) => {
            if (!update.docChanged)
                return;
            const text = update.state.doc.toString();
            opts.onChange(text);
            if (!hasCmLint)
                scheduleAnalysis(text);
        }));
    }
    else if (analyzeUrl && !hasCmLint) {
        extensions.push(EditorView.updateListener.of((update) => {
            if (update.docChanged)
                scheduleAnalysis(update.state.doc.toString());
        }));
    }
    const state = EditorState.create({ doc: opts.value ?? '', extensions });
    view = new EditorView({ state, parent: container });
    // The inline diagnostics list is for hosts with nowhere else to put diagnostics
    // (e.g. orchestrator.html). Hosts that own a Messages surface pass
    // `diagnosticsPanel: false` and consume opts.onDiagnostics instead — the lint
    // gutter and inline underlines already mark the offending lines in the editor.
    if (analyzeUrl && opts.diagnosticsPanel !== false) {
        container.classList.add('has-diagnostics');
        diagPanel = document.createElement('div');
        diagPanel.className = 'etlsql-editor-diagnostics';
        diagPanel.innerHTML = '<div class="etlsql-editor-diagnostics-status" data-kind="neutral" style="cursor:pointer; display:flex; align-items:center; justify-content:space-between;"><span>Diagnostics pending</span><span style="font-size:10px; color:var(--portal-text-muted, #9da7b1); padding-left:8px;">Toggle ▼</span></div><div class="etlsql-editor-diagnostics-list"></div>';
        const statusHeader = diagPanel.querySelector('.etlsql-editor-diagnostics-status');
        if (statusHeader) {
            statusHeader.addEventListener('click', () => {
                const isCollapsed = diagPanel.classList.toggle('collapsed');
                container.classList.toggle('diagnostics-collapsed', isCollapsed);
                const arrow = statusHeader.querySelector('span:last-child');
                if (arrow)
                    arrow.textContent = isCollapsed ? 'Toggle ▶' : 'Toggle ▼';
            });
        }
        container.appendChild(diagPanel);
    }
    if (analyzeUrl && opts.analyzeOnLoad !== false)
        scheduleAnalysis(opts.value ?? '');
    attachHover();
    return {
        getValue: () => view.state.doc.toString(),
        getSelection: () => {
            const ranges = view.state.selection.ranges
                .filter((range) => !range.empty)
                .map((range) => view.state.doc.sliceString(range.from, range.to));
            return ranges.join('\n');
        },
        getCurrentStatement: () => currentStatement(),
        hasCompletion: Boolean(completeUrl),
        triggerCompletion: () => {
            if (!startCompletionKey || !view)
                return false;
            view.focus();
            return startCompletionKey.run(view);
        },
        setValue: (text) => view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: text },
        }),
        /**
         * Bring the buffer to `text` by dispatching only the span that actually changed.
         *
         * A whole-document replacement (setValue) moves the cursor to the end, drops the scroll
         * position, and gives no clue *which* part changed — which matters most for GUI-generated
         * edits, where the point is to see what the canvas just wrote. Trimming the common prefix
         * and suffix keeps the caret where the author left it and yields the inserted range.
         *
         * @returns {{from:number,to:number}|null} the inserted range, or null when nothing changed.
         */
        replaceAll: (text) => {
            const current = view.state.doc.toString();
            const next = String(text ?? '');
            if (current === next)
                return null;
            let prefix = 0;
            const maxPrefix = Math.min(current.length, next.length);
            while (prefix < maxPrefix && current[prefix] === next[prefix])
                prefix++;
            let suffix = 0;
            const maxSuffix = Math.min(current.length - prefix, next.length - prefix);
            while (suffix < maxSuffix
                && current[current.length - 1 - suffix] === next[next.length - 1 - suffix])
                suffix++;
            const from = prefix;
            const to = current.length - suffix;
            const insert = next.slice(prefix, next.length - suffix);
            // Keep the caret where it was; CodeMirror maps it through the change for us, and
            // clamping guards the case where the caret sat inside the replaced span.
            const anchor = Math.min(view.state.selection.main.anchor, from);
            view.dispatch({
                changes: { from, to, insert },
                selection: { anchor },
                scrollIntoView: false,
            });
            return { from, to: from + insert.length };
        },
        /** Scrolls a document range into view without stealing focus from the canvas. */
        revealRange: (from, to) => {
            if (!view)
                return;
            const length = view.state.doc.length;
            const start = Math.max(0, Math.min(length, Number(from) || 0));
            const end = Math.max(start, Math.min(length, Number(to) || start));
            view.dispatch({ effects: EditorView.scrollIntoView(start, { y: 'center' }) });
            return { from: start, to: end };
        },
        /**
         * Scrolls to a run of lines and selects them, so the author can see what was just written.
         *
         * This is the second half of every canvas gesture that adds a statement. Dragging a chip
         * onto the map and being shown the ETL-SQL it produced is the whole point of the surface:
         * without it the canvas is something you operate, and with it, it is something you learn the
         * language from.
         *
         * A selection rather than a decoration, deliberately — it is the one highlight this editor
         * can make that the author can then copy, extend, or type over, and it survives the next
         * keystroke by getting out of the way rather than lingering as an artefact.
         *
         * @param fromLine 1-based first line.
         * @param toLine   1-based last line; defaults to `fromLine`.
         */
        revealLines: (fromLine, toLine = fromLine) => {
            if (!view)
                return null;
            const lines = view.state.doc.lines;
            const first = Math.max(1, Math.min(lines, Number(fromLine) || 1));
            const last = Math.max(first, Math.min(lines, Number(toLine) || first));
            const from = view.state.doc.line(first).from;
            const to = view.state.doc.line(last).to;
            view.dispatch({
                selection: { anchor: from, head: to },
                effects: EditorView.scrollIntoView(from, { y: 'center' }),
            });
            view.focus();
            return { from, to };
        },
        gotoLine: (line, column = 1) => {
            if (!view)
                return;
            const safeLine = Math.max(1, Math.min(view.state.doc.lines, Number(line) || 1));
            const docLine = view.state.doc.line(safeLine);
            const pos = Math.min(docLine.to, docLine.from + Math.max(0, (Number(column) || 1) - 1));
            view.dispatch({ selection: { anchor: pos }, effects: EditorView.scrollIntoView(pos, { y: 'center' }) });
            view.focus();
        },
        /** The 1-based line the caret sits on; 1 before the view exists. The mirror of gotoLine. */
        getCursorLine: () => {
            if (!view)
                return 1;
            return view.state.doc.lineAt(view.state.selection.main.head).number;
        },
        analyze: () => runAnalysis(view.state.doc.toString()),
        /**
         * Script-level undo/redo, reused from the bundle's own history keymap.
         *
         * The script is the authoritative artifact, so undoing a canvas action means undoing the
         * text it generated. This works because GUI mutations are applied as ranged transactions
         * (see replaceAll) rather than whole-document replacements — a host that binds these gets
         * working undo for palette and filter actions, not just for typing.
         */
        undo: () => {
            const binding = historyKeys.find((entry) => entry?.key === 'Mod-z');
            return binding?.run ? binding.run(view) : false;
        },
        redo: () => {
            const binding = historyKeys.find((entry) => entry?.key === 'Mod-y' || entry?.key === 'Mod-Shift-z');
            return binding?.run ? binding.run(view) : false;
        },
        /**
         * The query workbench has always called this, as `editor.focus?.()` — an optional call, so
         * it never threw and never focused anything either. It is a real member now.
         */
        focus: () => view.focus(),
        getState: () => view?.state,
        setState: (newState) => {
            if (view && newState) {
                view.setState(newState);
            }
        },
        createDocState: (text) => {
            return EditorState.create({ doc: text ?? '', extensions });
        },
        getScrollPosition: () => ({
            top: view?.scrollDOM?.scrollTop ?? 0,
            left: view?.scrollDOM?.scrollLeft ?? 0,
        }),
        setScrollPosition: (pos) => {
            if (view?.scrollDOM) {
                if (pos?.top != null)
                    view.scrollDOM.scrollTop = pos.top;
                if (pos?.left != null)
                    view.scrollDOM.scrollLeft = pos.left;
            }
        },
        dispose: () => {
            clearTimeout(analyzeTimer);
            clearTimeout(hoverTimer);
            clearTimeout(hoverHideTimer);
            analyzeAbort?.abort();
            hoverAbort?.abort();
            hideHover();
            view.destroy();
            diagPanel?.remove();
        },
    };
}
