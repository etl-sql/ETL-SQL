/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Execution sessions, stop controls, diagnostics, and result snapshots.
 */

import { _readErrorText, errorMessage, queryElement } from './studio-context.js';

import { normalizeRunTrace } from './designer.js';
import type { RunDiagnostic, RunResultPayload, RunTraceEvent } from './run-results.js';
import type { StudioDom, StudioRunRequest, StudioRuntimeContext, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import type { SnapshotLike } from './studio-data.js';
import { updateSnapshotPackage as writeSnapshotPackage, type StudioDataContext } from './studio-data.js';

export interface StudioRunSessionContext {
    readonly activeDocumentContext: () => StudioRuntimeContext;
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly documentContext: (document?: StudioRuntimeDocument | null) => StudioRuntimeContext;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly shell: StudioDom;
    readonly state: StudioRuntimeState;
}

export function createStudioRunSession(hostContext: StudioRunSessionContext) {

    function updateSnapshotPackage(snapshot: SnapshotLike | null) {
        writeSnapshotPackage(hostContext.activeDocumentContext() as unknown as StudioDataContext, snapshot);
    }

    // Results are a per-document trace replayed into one shared panel, not an HTML blob. The panel
    // owns the Results / Messages / Pipeline / Performance tabs, the result filter, CSV/Excel/JSON
    // export, and the column lineage bar — the workbench surface Studio previously did without.
    function setDocumentTrace(document: StudioRuntimeDocument, trace: RunTraceEvent[]) {
        const context = hostContext.documentContext(document);
        context.resultsTrace = Array.isArray(trace) ? trace : [];
        if (hostContext.getActiveDoc() === document) paintResults(context);
    }

    function paintResults(context: StudioRuntimeContext) {
        if (!hostContext.state.resultsPanel) return;
        hostContext.state.resultsPanel.clear();
        if (context.resultsTrace?.length) hostContext.state.resultsPanel.replay(context.resultsTrace);
        hostContext.state.resultsPanel.setDiagnostics(context.diagnostics || []);
    }

    // Studio owns the Messages surface, so lint diagnostics are routed to it rather than living only
    // as gutter squiggles. They belong to the buffer, so they survive clear() between runs.
    function setDocumentDiagnostics(document: StudioRuntimeDocument, list: RunDiagnostic[]) {
        const context = hostContext.documentContext(document);
        context.diagnostics = Array.isArray(list) ? list : [];
        if (hostContext.getActiveDoc() === document && hostContext.state.resultsPanel) {
            hostContext.state.resultsPanel.setDiagnostics(context.diagnostics);
        }
    }

    function updateRunControls() {
        const doc = hostContext.getActiveDoc();
        const context = doc ? hostContext.documentContext(doc) : null;
        const isRunning = Boolean(context?.runActive);

        const runBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="run"]');
        const stopBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="stop"]');
        const codeRunBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="code-run"]');
        const codeStopBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="code-stop"]');
        const runSelBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-action="run-selected"]');

        if (runBtn) runBtn.style.display = isRunning ? 'none' : '';
        if (stopBtn) stopBtn.style.display = isRunning ? '' : 'none';
        if (codeRunBtn) codeRunBtn.style.display = isRunning ? 'none' : '';
        if (codeStopBtn) codeStopBtn.style.display = isRunning ? '' : 'none';
        if (runSelBtn) runSelBtn.disabled = isRunning;
    }

    function handleStopRun() {
        const doc = hostContext.getActiveDoc();
        if (!doc) return;
        const context = hostContext.documentContext(doc);
        if (context.runActive && context.runAbort) {
            context.stopRequested = true;
            context.runAbort.abort();
            setDocumentTrace(doc, [
                { type: 'status', status: 'running' },
                { type: 'message', level: 'sys', text: `Stopping run... [Run ID: ${context.currentRunId || ''}]` },
            ]);
        }
    }

    // One run path for "Run all" and "Run selected". Results, messages, the execution pipeline, and
    // performance all flow into the shared results panel as a trace, so a failure lands on the
    // Messages tab with the real reason instead of being painted as a success.
    async function executeRun(doc: StudioRuntimeDocument, { script, selection = null, label, parameters = null }: StudioRunRequest) {
        const context = hostContext.documentContext(doc);
        context.runAbort?.abort();
        const controller = new AbortController();
        context.runAbort = controller;
        context.runActive = true;
        context.stopRequested = false;
        const clientRunId = 'run_' + Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
        context.currentRunId = clientRunId;
        updateRunControls();
        setDocumentTrace(doc, runStatusTrace(`Running ${label}… [Run ID: ${clientRunId}]`, 'running'));
        hostContext.state.resultsPanel?.startElapsed();
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.run, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal,
                body: JSON.stringify({
                    script,
                    ...(selection === null ? {} : { selection }),
                    connectionRef: typeof context.selectedSource === 'string'
                        ? context.selectedSource : context.selectedSource?.connection || null,
                    documentUri: doc.path || null,
                    clientRunId,
                    // Answers to the report's INPUT prompts, when the caller collected them. Absent
                    // means "run it as written", which is what every other run has always meant.
                    ...(parameters ? { parameters } : {}),
                    // Absent unless the author asked for a preview identity. Present, it changes
                    // only what the script's own row-level-security predicates see.
                    ...(hostContext.state.previewAs ? { previewAs: hostContext.state.previewAs } : {}),
                }),
            });

            if (!response.ok) {
                if (response.status === 499) {
                    setDocumentTrace(doc, [
                        { type: 'clear', resetHistory: true },
                        { type: 'status', status: 'failed' },
                        { type: 'message', level: 'warn', text: `Run cancelled by server (server-confirmed). [Run ID: ${clientRunId}]` },
                        { type: 'done', exitCode: 1, status: 'Cancelled' },
                    ]);
                    return;
                }
                if (response.status === 408) {
                    setDocumentTrace(doc, [
                        { type: 'clear', resetHistory: true },
                        { type: 'status', status: 'failed' },
                        { type: 'message', level: 'error', text: `Run timed out on server. [Run ID: ${clientRunId}]` },
                        { type: 'done', exitCode: 1, status: 'Timeout' },
                    ]);
                    return;
                }
                const reason = await _readErrorText(response);
                setDocumentTrace(doc, [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'error', text: `${reason} [Run ID: ${clientRunId}]` },
                    { type: 'done', exitCode: 1, status: 'Failed' },
                ]);
                return;
            }

            const data = await response.json() as RunResultPayload & { runId?: string };
            const effectiveRunId = data.runId || clientRunId;
            const trace = normalizeRunTrace(data, selection ?? script);
            const doneIndex = trace.findIndex(e => e.type === 'done');
            const runIdEvent: RunTraceEvent = { type: 'message', level: 'sys', text: `Run completed. [Run ID: ${effectiveRunId}]` };
            if (doneIndex >= 0) {
                trace.splice(doneIndex, 0, runIdEvent);
            } else {
                trace.push(runIdEvent);
            }
            setDocumentTrace(doc, trace);
        } catch (error) {
            if (error instanceof DOMException && error.name === 'AbortError') {
                if (context.stopRequested) {
                    setDocumentTrace(doc, [
                        { type: 'clear', resetHistory: true },
                        { type: 'status', status: 'failed' },
                        { type: 'message', level: 'warn', text: `Run stopped by client. Cancellation is unconfirmed — server may have completed execution. [Run ID: ${clientRunId}]` },
                        { type: 'done', exitCode: 1, status: 'Cancelled (Unconfirmed)' },
                    ]);
                } else {
                    setDocumentTrace(doc, [
                        { type: 'clear', resetHistory: true },
                        { type: 'status', status: 'failed' },
                        { type: 'message', level: 'warn', text: `Run cancelled. [Run ID: ${clientRunId}]` },
                        { type: 'done', exitCode: 1, status: 'Cancelled' },
                    ]);
                }
                return;
            }
            // A transport failure is a failed run. This once rendered a green
            // "In-Memory Run Completed" over stale sample rows, so a script that never executed
            // looked like it had succeeded.
            setDocumentTrace(doc, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'message', level: 'error', text: `${errorMessage(error) || 'The run did not complete.'} [Run ID: ${clientRunId}]` },
                { type: 'done', exitCode: 1, status: 'Failed' },
            ]);
        } finally {
            context.runActive = false;
            if (context.runAbort === controller) context.runAbort = null;
            hostContext.state.resultsPanel?.stopElapsed();
            updateRunControls();
        }
    }

    function runStatusTrace(text: string, tone: string): RunTraceEvent[] {
        return [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: tone },
            { type: 'message', level: tone === 'failed' ? 'error' : 'sys', text },
        ];
    }

    return { updateSnapshotPackage, setDocumentTrace, paintResults, setDocumentDiagnostics, updateRunControls, handleStopRun, executeRun };
}
