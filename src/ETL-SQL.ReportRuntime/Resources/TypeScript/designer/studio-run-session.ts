/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Execution sessions, stop controls, diagnostics, and result snapshots.
 */

import { _readErrorText, errorMessage, queryElement } from './studio-context.js';

import { normalizeRunTrace } from './designer.js';
import type { RunDiagnostic, RunResultPayload, RunTraceEvent } from './run-results.js';
import type { PipelineRunTracking, StudioDom, StudioPipelineRunHost, StudioPipelineRunStatus, StudioRunRequest, StudioRuntimeContext, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import type { SnapshotLike } from './studio-data.js';
import { updateSnapshotPackage as writeSnapshotPackage, type StudioDataContext } from './studio-data.js';

export interface StudioRunSessionContext {
    readonly activeDocumentContext: () => StudioRuntimeContext;
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly documentContext: (document?: StudioRuntimeDocument | null) => StudioRuntimeContext;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    /** Present where the host runs a saved catalog pipeline outside the authoring process. */
    readonly pipelineRuns?: StudioPipelineRunHost;
    readonly shell: StudioDom;
    readonly state: StudioRuntimeState;
}


/** How often a submitted pipeline run is asked for its status. */
const PIPELINE_RUN_POLL_MS = 750;
/** Consecutive unanswered status reads before the run's outcome is reported as unknown. */
const PIPELINE_RUN_MAX_MISSED_READS = 8;

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
        if (context.runActive && context.pipelineRun) {
            // A pipeline run belongs to the Orchestrator. Stop asks it to stop that run; abandoning
            // the request here would stop nothing.
            void requestPipelineStop(doc);
            return;
        }
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

    /** A saved catalog pipeline on a host that runs pipelines somewhere other than here. */
    function runsAsSavedPipeline(doc: StudioRuntimeDocument | null): boolean {
        return Boolean(doc && hostContext.pipelineRuns && doc.reportId && /\.etlsql$/i.test(doc.path || ''));
    }

    const pause = (milliseconds: number) => new Promise<void>(resolve => { setTimeout(resolve, milliseconds); });

    // Run on a saved pipeline hands its saved version to the host and then follows one run by the
    // id the host returned. Nothing here sends script text: what runs is what was saved.
    async function executePipelineRun(doc: StudioRuntimeDocument) {
        const runs = hostContext.pipelineRuns;
        if (!runs) return;
        const context = hostContext.documentContext(doc);
        if (context.runActive) return;
        if (doc.isDirty) {
            setDocumentTrace(doc, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'Not run' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'warn', text: 'Run uses the saved revision, and this pipeline has unsaved edits. Save it (Ctrl+S), then run again. Nothing was run.' },
            ]);
            return;
        }

        const tracked: PipelineRunTracking = { runId: null, accepted: '', status: '', stop: 'none', stopNote: null };
        context.pipelineRun = tracked;
        context.runActive = true;
        context.stopRequested = false;
        context.currentRunId = null;
        updateRunControls();
        setDocumentTrace(doc, [
            ...runStatusTrace(`Submitting saved version ${doc.version ?? ''} to the Orchestrator…`, 'running'),
            { type: 'show', tab: 'messages' },
        ]);
        hostContext.state.resultsPanel?.startElapsed();
        let runId: number | null = null;
        let accepted = '';
        try {
            const submitted = await runs.submit(doc);
            runId = submitted.runId;
            tracked.runId = runId;
            context.currentRunId = String(runId);
            accepted = `Run ${runId} accepted by the Orchestrator for saved version ${submitted.version} (${submitted.scriptHash.slice(0, 19)}…).`;
            tracked.accepted = accepted;
            // Stop pressed while the submission was in flight applies to the run it produced.
            if (tracked.stop === 'pending') void requestPipelineStop(doc);
            const final = await followPipelineRun(doc, runs, runId, tracked);
            if (final) setDocumentTrace(doc, pipelineRunTrace(tracked, final));
        } catch (error) {
            const reason = errorMessage(error) || 'The request did not complete.';
            setDocumentTrace(doc, runId === null
                // Refused before a run existed, so there is no outcome to be unsure about.
                ? [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'error', text: reason },
                    { type: 'message', level: 'sys', text: 'Nothing was run.' },
                    { type: 'done', exitCode: 1, status: 'Not run' },
                ]
                // Submitted, then lost. The run is the Orchestrator's and may well have finished.
                : [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'message', level: 'sys', text: accepted },
                    ...(tracked.stopNote ? [{ type: 'message', level: 'warn', text: tracked.stopNote }] : []),
                    { type: 'message', level: 'warn', text: `Run ${runId} can no longer be followed from here: ${reason} Its outcome is unknown; it may still be running or have finished. Look it up by its run ID before running again.` },
                    { type: 'done', exitCode: 1, status: tracked.stop === 'none' ? 'Unknown' : 'Cancelled (Unconfirmed)' },
                ]);
        } finally {
            if (context.pipelineRun === tracked) context.pipelineRun = null;
            context.runActive = false;
            hostContext.state.resultsPanel?.stopElapsed();
            updateRunControls();
        }
    }

    /**
     * Polls one run until it is terminal. Every answer is checked against the id that was
     * submitted, so a reply about any other run is an error and never a result.
     */
    async function followPipelineRun(
        doc: StudioRuntimeDocument, runs: StudioPipelineRunHost, runId: number, tracked: PipelineRunTracking
    ): Promise<StudioPipelineRunStatus | null> {
        let missed = 0;
        for (;;) {
            // A closed tab stops asking. The run itself is not affected.
            if (!hostContext.state.documents.includes(doc)) return null;
            let run: StudioPipelineRunStatus;
            try {
                run = await runs.read(doc, runId);
                missed = 0;
            } catch (error) {
                if (++missed >= PIPELINE_RUN_MAX_MISSED_READS) throw error;
                await pause(PIPELINE_RUN_POLL_MS * Math.min(missed, 4));
                continue;
            }
            if (run.runId !== runId) throw new Error(`The host answered about run ${run.runId}.`);
            if (run.isTerminal) return run;
            if (run.status !== tracked.status) {
                tracked.status = run.status;
                paintPipelineProgress(doc, tracked);
            }
            await pause(PIPELINE_RUN_POLL_MS);
        }
    }

    function paintPipelineProgress(doc: StudioRuntimeDocument, tracked: PipelineRunTracking) {
        setDocumentTrace(doc, [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: 'running' },
            { type: 'show', tab: 'messages' },
            { type: 'message', level: 'sys', text: tracked.accepted },
            ...(tracked.status
                ? [{ type: 'message', level: 'sys', text: `Run ${tracked.runId} is ${tracked.status.toLowerCase()} in the Orchestrator.` }]
                : []),
            ...(tracked.stopNote ? [{ type: 'message', level: 'warn', text: tracked.stopNote }] : []),
        ]);
    }

    // Stop is a request to the Orchestrator about one run id. What the panel says afterwards is what
    // was actually confirmed: accepted is not stopped, and an unanswered request is said to be so.
    async function requestPipelineStop(doc: StudioRuntimeDocument) {
        const runs = hostContext.pipelineRuns;
        const tracked = hostContext.documentContext(doc).pipelineRun;
        if (!runs || !tracked || tracked.stop === 'requested' || tracked.stop === 'accepted') return;
        if (tracked.runId === null) {
            tracked.stop = 'pending';
            return;
        }
        const runId = tracked.runId;
        tracked.stop = 'requested';
        tracked.stopNote = `Stop requested for run ${runId}…`;
        paintPipelineProgress(doc, tracked);
        try {
            await runs.cancel(doc, runId);
            tracked.stop = 'accepted';
            tracked.stopNote = `The Orchestrator accepted the stop request for run ${runId}. Waiting for it to confirm the run has stopped.`;
        } catch (error) {
            tracked.stop = 'unconfirmed';
            tracked.stopNote = `Stop was not confirmed for run ${runId}: ${errorMessage(error) || 'the request did not complete.'} The run may still be running; Studio keeps following it.`;
        }
        // The run may already have reached its end while the request was in flight.
        if (hostContext.documentContext(doc).pipelineRun === tracked) paintPipelineProgress(doc, tracked);
    }

    function pipelineRunTrace(tracked: PipelineRunTracking, run: StudioPipelineRunStatus): RunTraceEvent[] {
        const succeeded = run.status.toUpperCase() === 'SUCCESS';
        const cancelled = run.status.toUpperCase() === 'CANCELLED';
        const trace: RunTraceEvent[] = [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: succeeded ? 'running' : 'failed' },
            { type: 'show', tab: 'messages' },
            { type: 'message', level: 'sys', text: tracked.accepted },
        ];
        for (const statement of run.statements) {
            trace.push({
                type: 'message',
                level: statement.failed ? 'error' : 'info',
                text: `${statement.failed ? 'Failed' : 'Ran'}: ${statement.statement} (${statement.rowsProcessed} rows, ${statement.durationMs} ms)`,
            });
        }
        if (run.logTruncated)
            trace.push({ type: 'message', level: 'warn', text: 'This log is bounded and some statements are not shown. The Orchestrator keeps failures and the slowest statements.' });
        if (run.errorMessage) trace.push({ type: 'message', level: cancelled ? 'info' : 'error', text: run.errorMessage });
        if (cancelled) {
            trace.push({ type: 'message', level: 'warn', text: `Cancellation confirmed by the Orchestrator: run ${run.runId} was stopped.` });
        } else if (tracked.stop !== 'none') {
            // Asked to stop, and it ended some other way. Say that instead of implying it was stopped.
            trace.push({ type: 'message', level: 'warn', text: `Stop was requested, but run ${run.runId} had already finished as ${run.status}. It was not cancelled.` });
        }
        trace.push({
            type: 'message',
            level: succeeded ? 'info' : cancelled ? 'warn' : 'error',
            text: `Run ${run.runId} finished with status ${run.status}. ${run.rowsProcessed} rows processed.`,
        });
        trace.push({ type: 'done', exitCode: succeeded ? 0 : 1, status: succeeded ? 'Complete' : pipelineOutcome(run.status) });
        return trace;
    }

    function pipelineOutcome(status: string): string {
        const upper = status.toUpperCase();
        return upper === 'CANCELLED' ? 'Cancelled' : upper === 'FAILURE' || upper === 'FAILED' ? 'Failed' : status;
    }

    return { updateSnapshotPackage, setDocumentTrace, paintResults, setDocumentDiagnostics, updateRunControls, handleStopRun, executeRun, executePipelineRun, runsAsSavedPipeline };
}
