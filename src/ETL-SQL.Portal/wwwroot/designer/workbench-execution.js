// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/workbench-execution.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/workbench-execution.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Run, cancellation, and save operations for a script workbench.
 */
import { asButton } from './workbench-context.js';
import { _feedback } from './designer-util.js';
import { normalizeRunTrace } from './run-results.js';
export function createWorkbenchExecution(context) {
    // scope: 'script' runs the whole file (Run); 'selection' runs the highlighted text
    // or the statement under the cursor (Run Selected) — see the roadmap's toolbar schema.
    // Hosts signal a destructive-statement refusal with a RUN_DESTRUCTIVE diagnostic code.
    function isDestructiveRefusal(result) {
        return result?.success === false
            && (result.diagnostics ?? []).some((d) => d?.code === 'RUN_DESTRUCTIVE');
    }
    function setRunning(isRunning) {
        context.root.classList.toggle('is-running', isRunning);
        const runBtn = asButton(context.container.querySelector('[data-run]'));
        const runSelBtn = asButton(context.container.querySelector('[data-run-selected]'));
        if (runBtn)
            runBtn.disabled = isRunning;
        if (runSelBtn)
            runSelBtn.disabled = isRunning;
    }
    async function run(scope = 'script', confirmDestructive = false) {
        if (!context.opts.runUrl && !context.opts.onRun)
            return;
        const script = context.editor.getValue();
        let runText = script;
        let runLabel = 'script';
        if (scope === 'selection') {
            const selected = context.editor.getSelection?.()?.trim();
            if (selected) {
                runText = selected;
                runLabel = 'selection';
            }
            else {
                const current = context.editor.getCurrentStatement?.()?.trim();
                if (current) {
                    runText = current;
                    runLabel = 'statement at cursor';
                }
                else {
                    context.resultsPanel.replay([
                        { type: 'clear', resetHistory: true },
                        { type: 'status', status: 'idle' },
                        { type: 'message', level: 'warn', text: 'No query or statement selected to run. Select SQL text or place the cursor inside a statement.' },
                    ]);
                    return;
                }
            }
        }
        const clientRunId = 'run_' + Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
        context.resultsPanel.replay([
            { type: 'clear', resetHistory: true },
            { type: 'status', status: 'running' },
            { type: 'message', level: 'sys', text: scope === 'selection' ? `Running ${runLabel}. [Run ID: ${clientRunId}]` : `Running script. [Run ID: ${clientRunId}]` },
        ]);
        setRunning(true);
        context.resultsPanel.startElapsed();
        try {
            context.runAbort?.abort();
            context.runAbort = new AbortController();
            const result = context.opts.onRun
                ? await context.opts.onRun({ script, selection: runText, connectionRef: context.opts.connectionRef || null, confirmDestructive, signal: context.runAbort.signal })
                : await (async () => {
                    const fetcher = context.opts.authFetch ?? ((url, init) => fetch(url, init));
                    const res = await fetcher(context.opts.runUrl, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ script, selection: runText, connectionRef: context.opts.connectionRef || null, documentUri: context.getDocumentUri(), confirmDestructive, clientRunId }),
                        signal: context.runAbort.signal,
                    });
                    if (!res?.ok) {
                        if (res.status === 499) {
                            return {
                                success: false,
                                message: `Run cancelled by server (server-confirmed). [Run ID: ${clientRunId}]`,
                                diagnostics: [{ line: 0, column: 0, severity: 'warn', message: `Run cancelled by server. [Run ID: ${clientRunId}]`, code: 'CANCELLED' }]
                            };
                        }
                        if (res.status === 408) {
                            return {
                                success: false,
                                message: `Run timed out on server. [Run ID: ${clientRunId}]`,
                                diagnostics: [{ line: 0, column: 0, severity: 'error', message: `Run timed out on server. [Run ID: ${clientRunId}]`, code: 'TIMEOUT' }]
                            };
                        }
                        throw new Error(await res.text());
                    }
                    return await res.json();
                })();
            // The host refuses destructive statements until they are acknowledged. Ask once, then
            // re-run confirmed rather than making the user edit the script to get past the guard.
            if (!confirmDestructive && isDestructiveRefusal(result)) {
                setRunning(false);
                context.resultsPanel.stopElapsed();
                const confirmed = await _feedback?.confirm?.(result.message, { title: 'Run despite validation findings?', impact: 'Running may execute a script that did not pass validation.', confirmLabel: 'Run anyway', danger: true, auditAction: 'designer.run.override' });
                if (confirmed) {
                    await run(scope, true);
                }
                else {
                    context.resultsPanel.replay([
                        { type: 'message', level: 'warn', text: `Run cancelled — destructive statements not confirmed. [Run ID: ${clientRunId}]` },
                        { type: 'done', exitCode: 1, status: 'Cancelled' },
                    ]);
                }
                return;
            }
            context.resultsPanel.replay(normalizeRunTrace(result, runText));
        }
        catch (err) {
            if (err?.name === 'AbortError') {
                context.resultsPanel.replay([
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'warn', text: `Run stopped by client. Cancellation is unconfirmed — server may have completed execution. [Run ID: ${clientRunId}]` },
                    { type: 'done', exitCode: 1, status: 'Cancelled (Unconfirmed)' },
                ]);
                return;
            }
            context.resultsPanel.replay([
                { type: 'clear', resetHistory: true },
                { type: 'message', level: 'error', text: err?.message || 'Run failed.' },
                { type: 'done', exitCode: 1 },
            ]);
        }
        finally {
            setRunning(false);
            context.resultsPanel.stopElapsed();
        }
    }
    function cancelRun() {
        context.runAbort?.abort();
    }
    async function save() {
        if (context.activeFileHandle) {
            try {
                const writable = await context.activeFileHandle.createWritable();
                await writable.write(context.editor.getValue());
                await writable.close();
                _feedback?.notify?.('The script was saved.', { title: 'Saved', tone: 'success', auditAction: 'designer.file.save' });
            }
            catch (err) {
                _feedback?.notify?.('Browser save failed: ' + err.message, { title: 'Save failed', tone: 'error' });
            }
            return;
        }
        if (context.activeDirectoryHandle) {
            const requestedPath = await _feedback?.prompt?.('Choose a path for the new script.', { title: 'Save script as', label: 'Relative file path', value: context.currentFilePath || 'new-script.etlsql', required: true, pattern: /\.(?:etlsql|rptsql)$/i, patternMessage: 'Use an .etlsql or .rptsql filename.', confirmLabel: 'Save script', auditAction: 'designer.file.save-as' });
            if (!requestedPath)
                return;
            try {
                context.activeFileHandle = await context.activeDirectoryHandle.getFileHandle(requestedPath, { create: true });
                const writable = await context.activeFileHandle.createWritable();
                await writable.write(context.editor.getValue());
                await writable.close();
                context.currentFilePath = requestedPath;
                const titleEl = context.root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = requestedPath;
                }
                await context.renderDirectoryTree(context.activeDirectoryHandle);
                _feedback?.notify?.('The script was saved.', { title: 'Saved', tone: 'success', auditAction: 'designer.file.save-as' });
            }
            catch (err) {
                _feedback?.notify?.('Browser save failed: ' + err.message, { title: 'Save failed', tone: 'error' });
            }
            return;
        }
        if (context.opts.onSave) {
            await context.opts.onSave?.(context.editor.getValue(), context.currentFilePath);
        }
        else {
            if (!context.currentFilePath) {
                const requestedPath = await _feedback?.prompt?.('Choose a path for the new script.', { title: 'Save script as', label: 'Relative file path', value: 'new-script.etlsql', required: true, pattern: /\.(?:etlsql|rptsql)$/i, patternMessage: 'Use an .etlsql or .rptsql filename.', confirmLabel: 'Save script', auditAction: 'designer.file.save-as' });
                if (!requestedPath)
                    return;
                context.currentFilePath = requestedPath;
                const titleEl = context.root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = context.currentFilePath;
                }
            }
            try {
                const fetcher = context.opts.authFetch ?? fetch;
                const res = await fetcher('/api/files', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path: context.currentFilePath, content: context.editor.getValue() })
                });
                if (!res.ok)
                    throw new Error(`HTTP ${res.status}`);
                await context.loadFiles();
            }
            catch (err) {
                console.error(err);
                _feedback?.notify?.(`Error saving file: ${err.message}`, { title: 'Save failed', tone: 'error' });
            }
        }
    }
    async function apply() {
        await context.opts.onApply?.(context.editor.getValue());
    }
    return { run, cancelRun, save, apply };
}
