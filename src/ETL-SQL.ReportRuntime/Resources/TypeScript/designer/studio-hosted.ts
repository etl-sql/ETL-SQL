/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Desktop Studio hosted Portal target picker, sign-in handoff, saved-revision execution,
 * Stop, session expiry/recovery, and target-scoped result ownership (ST58).
 */

import type { RunTraceEvent } from './run-results.js';
import type { StudioDom, StudioDomElement, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { _escapeHtml, _feedback, errorMessage, queryElement, queryElements } from './studio-context.js';

export interface HostedServerEntryDto {
    alias: string;
    label: string;
    origin: string;
    environment?: string | null;
}

export interface HostedUserDto {
    id: number;
    username: string;
    email?: string | null;
}

export interface HostedPendingSubmissionDto {
    idempotencyKey: string;
    origin: string;
    tenantId: string;
    userId: number;
    username: string;
    pipelineId: number;
    pipelineName: string;
    version: number;
    jobName: string;
    status: string;
    documentId?: string | null;
    documentPath?: string | null;
    createdAtUtc: string;
}

export interface HostedActiveRunDto {
    runId: number;
    pipelineId: number;
    pipelineName: string;
    jobName: string;
    version: number;
    origin: string;
    tenantId: string;
    userId: number;
    username: string;
    cancelRequested: boolean;
    documentId?: string | null;
    documentPath?: string | null;
}

export interface HostedStatusDto {
    mode: 'local' | 'hosted';
    targetKey: string;
    statusLabel: string;
    server: HostedServerEntryDto | null;
    signedIn: boolean;
    tenantId?: string | null;
    user: HostedUserDto | null;
    scopes: string[];
    servers: HostedServerEntryDto[];
    pendingSubmission: HostedPendingSubmissionDto | null;
    activeRun: HostedActiveRunDto | null;
}

export interface HostedRunnablePipelineDto {
    id: number;
    name: string;
    folderPath: string;
    version: number;
    canAuthor: boolean;
}

interface HostedRunEventDto {
    level: string;
    text: string;
}

interface HostedPipelineRunStatementDto {
    statement: string;
    durationMs: number;
    rowsProcessed: number;
    failed: boolean;
}

interface HostedPipelineRunDto {
    runId: number;
    pipelineId: number;
    jobName: string;
    status: string;
    isTerminal: boolean;
    errorMessage?: string | null;
    rowsProcessed: number;
    scriptHash?: string | null;
    logTruncated: boolean;
    statements: HostedPipelineRunStatementDto[];
}

interface HostedRunOperationDto {
    operationId: string;
    documentId?: string | null;
    documentPath?: string | null;
    ownerTargetKey: string;
    ownerOrigin: string;
    ownerTenantId: string;
    ownerUserId: number;
    ownerUsername: string;
    pipelineId: number;
    pipelineName: string;
    version: number;
    jobName: string;
    state: string;
    isTerminal: boolean;
    runId?: number | null;
    cancelRequested: boolean;
    message?: string | null;
    refusal?: { code: string; error: string; currentVersion?: number | null } | null;
    finalRun?: HostedPipelineRunDto | null;
    pendingSubmission?: HostedPendingSubmissionDto | null;
    events: HostedRunEventDto[];
}

export interface StudioHostedExecutionContext {
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly modalBackdrop: StudioDomElement;
    readonly modalBox: StudioDomElement;
    readonly repaintActiveTarget: () => void;
    readonly setDocumentTraceForTarget: (document: StudioRuntimeDocument, targetKey: string, trace: RunTraceEvent[]) => void;
    readonly setupModalAccessibility: (box: HTMLElement, backdrop: HTMLElement, onClose: () => void, titleId?: string) => () => void;
    readonly shell: StudioDom;
    readonly state: StudioRuntimeState;
    readonly updateRunControls: () => void;
}

export interface StudioHostedExecutionHandle {
    readonly getStatus: () => HostedStatusDto;
    readonly getCurrentTargetKey: () => string;
    readonly isHostedMode: () => boolean;
    readonly isRunActiveFor: (doc: StudioRuntimeDocument | null, targetKey: string) => boolean;
    readonly refreshStatus: () => Promise<HostedStatusDto>;
    readonly openTargetPicker: () => Promise<void>;
    readonly runActiveDocument: (doc: StudioRuntimeDocument) => Promise<void>;
    readonly stopActiveRun: (doc: StudioRuntimeDocument) => Promise<void>;
    readonly recoverPendingSubmission: (mode?: 'check' | 'retry') => Promise<void>;
    readonly dispose: () => void;
}

const POLL_INTERVAL_MS = 250;

function defaultLocalStatus(): HostedStatusDto {
    return {
        mode: 'local',
        targetKey: 'local',
        statusLabel: 'Local (Offline)',
        server: null,
        signedIn: false,
        tenantId: null,
        user: null,
        scopes: [],
        servers: [],
        pendingSubmission: null,
        activeRun: null,
    };
}

function normalizeBaseName(pathOrName: string): string {
    const base = pathOrName.split('/').pop()?.split('\\').pop() || '';
    return base.trim().toLowerCase();
}

function stripScriptExt(name: string): string {
    return name.replace(/\.(etlsql|rptsql)$/i, '').toLowerCase();
}

export function createStudioHostedExecution(hostContext: StudioHostedExecutionContext): StudioHostedExecutionHandle {
    let status: HostedStatusDto = defaultLocalStatus();
    let disposed = false;
    let activeModalCleanup: (() => void) | null = null;
    const activeOperations = new Map<string, { operationId: string; doc: StudioRuntimeDocument; targetKey: string }>();

    const targetBtn = queryElement<HTMLButtonElement>(hostContext.shell, '[data-hosted-target-btn]');
    const bannerEl = queryElement<HTMLElement>(hostContext.shell, '[data-hosted-banner]');

    function opKey(docId: string, targetKey: string): string {
        return `${docId}||${targetKey}`;
    }

    function applyStatus(next: HostedStatusDto) {
        const prevTargetKey = status.targetKey;
        status = next;
        renderHeaderAndBanner();
        if (prevTargetKey !== next.targetKey) {
            hostContext.repaintActiveTarget();
        } else {
            hostContext.updateRunControls();
        }
    }

    function renderHeaderAndBanner() {
        if (targetBtn) {
            targetBtn.textContent = status.statusLabel || 'Local (Offline)';
            targetBtn.dataset.hostedMode = status.mode;
            targetBtn.dataset.hostedSignedIn = status.signedIn ? 'true' : 'false';
            targetBtn.classList.toggle('is-hosted', status.mode === 'hosted' && status.signedIn);
            targetBtn.classList.toggle('is-warning', status.mode === 'hosted' && !status.signedIn);
        }

        if (!bannerEl) return;

        if (status.mode === 'hosted' && !status.signedIn && status.server) {
            bannerEl.hidden = false;
            bannerEl.innerHTML = `
                <div class="etlsql-hosted-banner-content">
                    <span><strong>Hosted session expired or signed out</strong> (${_escapeHtml(status.server.label)}). Local editor buffers are preserved and will not run locally.</span>
                    <div class="etlsql-hosted-banner-actions">
                        <button type="button" class="etlsql-studio-btn btn-primary" data-hosted-reauth>Sign In Again</button>
                        <button type="button" class="etlsql-studio-btn" data-hosted-switch-local>Switch to Local (Offline)</button>
                    </div>
                </div>`;
            queryElement(bannerEl, '[data-hosted-reauth]')?.addEventListener('click', () => {
                if (status.server) void connectServer(status.server.alias);
            });
            queryElement(bannerEl, '[data-hosted-switch-local]')?.addEventListener('click', () => {
                void disconnectToLocal();
            });
            return;
        }

        if (status.mode === 'hosted' && status.signedIn && status.pendingSubmission) {
            const p = status.pendingSubmission;
            const retryBtn = p.status.toLowerCase() === 'notfound'
                ? '<button type="button" class="etlsql-studio-btn" data-hosted-retry-same-key>Retry Same Request</button>'
                : '';
            bannerEl.hidden = false;
            bannerEl.innerHTML = `
                <div class="etlsql-hosted-banner-content">
                    <span><strong>Submission unconfirmed</strong> (request <code>${_escapeHtml(p.idempotencyKey)}</code> · ${_escapeHtml(p.pipelineName)} v${p.version})</span>
                    <div class="etlsql-hosted-banner-actions">
                        <button type="button" class="etlsql-studio-btn btn-primary" data-hosted-recover>Check / Recover Run</button>
                        ${retryBtn}
                    </div>
                </div>`;
            queryElement(bannerEl, '[data-hosted-recover]')?.addEventListener('click', () => {
                void recoverPendingSubmission('check');
            });
            queryElement(bannerEl, '[data-hosted-retry-same-key]')?.addEventListener('click', () => {
                void recoverPendingSubmission('retry');
            });
            return;
        }

        bannerEl.hidden = true;
        bannerEl.innerHTML = '';
    }

    function closeActiveModal() {
        if (activeModalCleanup) {
            const fn = activeModalCleanup;
            activeModalCleanup = null;
            fn();
        }
    }

    async function refreshStatus(): Promise<HostedStatusDto> {
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/status');
            if (response.ok) {
                const next = await response.json() as HostedStatusDto;
                applyStatus(next);
                return next;
            }
        } catch {
            // Keep Local (Offline) default when status probe is unavailable.
        }
        renderHeaderAndBanner();
        return status;
    }

    async function connectServer(alias: string): Promise<boolean> {
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/connect', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ alias }),
            });
            const payload = await response.json().catch(() => ({})) as { error?: string; status?: HostedStatusDto } & Partial<HostedStatusDto>;
            if (!response.ok) {
                if (payload.status) applyStatus(payload.status);
                _feedback.notify(payload.error || `Sign-in failed (${response.status}).`, {
                    title: 'Hosted Sign-In Failed',
                    tone: 'error',
                });
                return false;
            }
            applyStatus(payload as HostedStatusDto);
            _feedback.notify(`Connected to ${status.statusLabel}`, {
                title: 'Hosted Portal Connected',
                tone: 'success',
            });
            return true;
        } catch (error) {
            _feedback.notify(errorMessage(error) || 'Could not reach Studio host to sign in.', {
                title: 'Hosted Sign-In Failed',
                tone: 'error',
            });
            return false;
        }
    }

    async function disconnectToLocal(): Promise<void> {
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/disconnect', {
                method: 'POST',
            });
            if (response.ok) {
                const next = await response.json() as HostedStatusDto;
                applyStatus(next);
                return;
            }
        } catch {
            // Fall back to local state if host call failed.
        }
        applyStatus(defaultLocalStatus());
    }

    async function openTargetPicker(): Promise<void> {
        await refreshStatus();
        closeActiveModal();

        const renderModal = () => {
            const titleId = 'etlsql-hosted-target-title';
            const serverRows = status.servers.length
                ? status.servers.map(s => {
                    const isCurrent = status.mode === 'hosted' && status.server?.alias === s.alias;
                    return `
                        <div class="etlsql-hosted-server-row ${isCurrent ? 'is-active' : ''}" data-hosted-server-row="${_escapeHtml(s.alias)}">
                            <div class="etlsql-hosted-server-meta">
                                <strong>${_escapeHtml(s.label)}</strong>
                                <code>${_escapeHtml(s.origin)}</code>
                                ${s.environment ? `<span class="etlsql-hosted-env-badge">${_escapeHtml(s.environment)}</span>` : ''}
                            </div>
                            <div class="etlsql-hosted-server-actions">
                                <button type="button" class="etlsql-studio-btn btn-primary" data-hosted-connect="${_escapeHtml(s.alias)}">${isCurrent && status.signedIn ? 'Switch Account' : 'Sign In'}</button>
                                <button type="button" class="etlsql-studio-btn" data-hosted-remove-server="${_escapeHtml(s.alias)}">Remove</button>
                            </div>
                        </div>`;
                }).join('')
                : '<p class="etlsql-hosted-empty">No trusted Portal servers registered yet. Add one below to sign in.</p>';

            const activeDetails = status.mode === 'hosted' && status.server
                ? `<div class="etlsql-hosted-current-card" data-hosted-current-details>
                    <div><strong>Target:</strong> Hosted — ${_escapeHtml(status.server.label)} (${_escapeHtml(status.server.origin)})</div>
                    <div><strong>Tenant:</strong> ${_escapeHtml(status.tenantId || '(not signed in)')} · <strong>User:</strong> ${_escapeHtml(status.user?.username || '(not signed in)')}</div>
                    <div><strong>Scopes:</strong> ${_escapeHtml(status.scopes.join(', ') || 'studio.read, studio.run')}</div>
                    <div class="etlsql-hosted-current-actions">
                        <button type="button" class="etlsql-studio-btn" data-hosted-disconnect>Disconnect &amp; Switch to Local (Offline)</button>
                    </div>
                </div>`
                : `<div class="etlsql-hosted-current-card" data-hosted-current-details>
                    <div><strong>Target:</strong> Local (Offline)</div>
                    <div>Scripts run locally on this workstation. No Portal network call or hosted token is used.</div>
                </div>`;

            hostContext.modalBox.innerHTML = `
                <div class="etlsql-hosted-modal" data-hosted-target-modal>
                    <div class="etlsql-hosted-modal-header">
                        <h3 id="${titleId}">Execution Target</h3>
                        <button type="button" class="etlsql-studio-btn" data-hosted-modal-close aria-label="Close">Close</button>
                    </div>
                    ${activeDetails}
                    <div class="etlsql-hosted-section">
                        <div class="etlsql-hosted-section-header">
                            <strong>Local Workstation</strong>
                            <button type="button" class="etlsql-studio-btn" data-hosted-select-local ${status.mode === 'local' ? 'disabled' : ''}>Use Local (Offline)</button>
                        </div>
                    </div>
                    <div class="etlsql-hosted-section">
                        <strong>Trusted Hosted Portal Servers</strong>
                        <div class="etlsql-hosted-server-list">${serverRows}</div>
                    </div>
                    <form class="etlsql-hosted-add-form" data-hosted-add-server-form>
                        <strong>Add Trusted Portal Server</strong>
                        <div class="etlsql-hosted-form-grid">
                            <input type="text" name="alias" placeholder="Alias (e.g. dev)" required />
                            <input type="text" name="label" placeholder="Label (e.g. Dev Portal)" required />
                            <input type="url" name="origin" placeholder="https://portal.company.internal" required />
                            <input type="text" name="environment" placeholder="Environment (optional)" />
                        </div>
                        <div class="etlsql-hosted-form-error" data-hosted-form-error role="alert" hidden></div>
                        <button type="submit" class="etlsql-studio-btn" data-hosted-add-server-submit>Save Trusted Server</button>
                    </form>
                </div>`;

            queryElement(hostContext.modalBox, '[data-hosted-modal-close]')?.addEventListener('click', closeActiveModal);
            queryElement(hostContext.modalBox, '[data-hosted-select-local]')?.addEventListener('click', async () => {
                await disconnectToLocal();
                closeActiveModal();
            });
            queryElement(hostContext.modalBox, '[data-hosted-disconnect]')?.addEventListener('click', async () => {
                await disconnectToLocal();
                closeActiveModal();
            });
            queryElements(hostContext.modalBox, '[data-hosted-connect]').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const alias = btn.dataset.hostedConnect;
                    if (!alias) return;
                    (btn as HTMLButtonElement).disabled = true;
                    const ok = await connectServer(alias);
                    if (ok) {
                        closeActiveModal();
                    } else {
                        renderModal();
                    }
                });
            });
            queryElements(hostContext.modalBox, '[data-hosted-remove-server]').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const alias = btn.dataset.hostedRemoveServer;
                    if (!alias) return;
                    const response = await hostContext.authFetch(
                        hostContext.apiBase + '/api/hosted/servers/' + encodeURIComponent(alias),
                        { method: 'DELETE' });
                    if (response.ok) {
                        applyStatus(await response.json() as HostedStatusDto);
                        renderModal();
                    }
                });
            });
            const addForm = queryElement<HTMLFormElement>(hostContext.modalBox, '[data-hosted-add-server-form]');
            addForm?.addEventListener('submit', async event => {
                event.preventDefault();
                const errorEl = queryElement<HTMLElement>(hostContext.modalBox, '[data-hosted-form-error]');
                const formData = new FormData(addForm);
                const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/servers', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        alias: String(formData.get('alias') || '').trim(),
                        label: String(formData.get('label') || '').trim(),
                        origin: String(formData.get('origin') || '').trim(),
                        environment: String(formData.get('environment') || '').trim() || null,
                    }),
                });
                const payload = await response.json().catch(() => ({})) as { error?: string } & Partial<HostedStatusDto>;
                if (!response.ok) {
                    if (errorEl) {
                        errorEl.textContent = payload.error || 'Could not save trusted server.';
                        errorEl.hidden = false;
                    }
                    return;
                }
                applyStatus(payload as HostedStatusDto);
                renderModal();
            });
        };

        renderModal();
        activeModalCleanup = hostContext.setupModalAccessibility(
            hostContext.modalBox as unknown as HTMLElement,
            hostContext.modalBackdrop as unknown as HTMLElement,
            () => { activeModalCleanup = null; },
            'etlsql-hosted-target-title');
    }

    function confirmDirtyRun(doc: StudioRuntimeDocument): Promise<boolean> {
        closeActiveModal();
        return new Promise<boolean>(resolve => {
            const titleId = 'etlsql-hosted-dirty-title';
            let settled = false;
            const finish = (choice: boolean) => {
                if (settled) return;
                settled = true;
                closeActiveModal();
                resolve(choice);
            };

            hostContext.modalBox.innerHTML = `
                <div class="etlsql-hosted-modal" data-hosted-dirty-modal>
                    <h3 id="${titleId}">Unsaved Local Edits</h3>
                    <p><strong>${_escapeHtml(doc.name || doc.path || 'This script')}</strong> has unsaved local edits.</p>
                    <p>Hosted execution runs the last saved Portal catalog revision. Your local buffer will be preserved and will not be sent to the server.</p>
                    <div class="etlsql-hosted-modal-actions">
                        <button type="button" class="etlsql-studio-btn btn-primary" data-hosted-dirty-run>Run Last Saved Revision</button>
                        <button type="button" class="etlsql-studio-btn" data-hosted-dirty-cancel>Cancel</button>
                    </div>
                </div>`;

            queryElement(hostContext.modalBox, '[data-hosted-dirty-run]')?.addEventListener('click', () => finish(true));
            queryElement(hostContext.modalBox, '[data-hosted-dirty-cancel]')?.addEventListener('click', () => finish(false));

            activeModalCleanup = hostContext.setupModalAccessibility(
                hostContext.modalBox as unknown as HTMLElement,
                hostContext.modalBackdrop as unknown as HTMLElement,
                () => {
                    activeModalCleanup = null;
                    if (!settled) {
                        settled = true;
                        resolve(false);
                    }
                },
                titleId);
        });
    }

    function pickRunnablePipeline(items: HostedRunnablePipelineDto[]): Promise<HostedRunnablePipelineDto | null> {
        closeActiveModal();
        return new Promise<HostedRunnablePipelineDto | null>(resolve => {
            const titleId = 'etlsql-hosted-pick-title';
            let settled = false;
            const finish = (choice: HostedRunnablePipelineDto | null) => {
                if (settled) return;
                settled = true;
                closeActiveModal();
                resolve(choice);
            };

            const rows = items.map(item => `
                <button type="button" class="etlsql-hosted-pipeline-choice" data-hosted-pick-pipeline="${item.id}">
                    <strong>${_escapeHtml(item.name)}</strong>
                    <span>${_escapeHtml(item.folderPath)} · v${item.version}</span>
                </button>`).join('');

            hostContext.modalBox.innerHTML = `
                <div class="etlsql-hosted-modal" data-hosted-pipeline-modal>
                    <h3 id="${titleId}">Select Saved Portal Pipeline</h3>
                    <p>Choose which saved revision to run on ${_escapeHtml(status.server?.label || 'Portal')} (${_escapeHtml(status.tenantId || '')}):</p>
                    <div class="etlsql-hosted-pipeline-list">${rows}</div>
                    <div class="etlsql-hosted-modal-actions">
                        <button type="button" class="etlsql-studio-btn" data-hosted-pick-cancel>Cancel</button>
                    </div>
                </div>`;

            queryElements(hostContext.modalBox, '[data-hosted-pick-pipeline]').forEach(btn => {
                btn.addEventListener('click', () => {
                    const id = Number(btn.dataset.hostedPickPipeline);
                    const found = items.find(i => i.id === id) || null;
                    finish(found);
                });
            });
            queryElement(hostContext.modalBox, '[data-hosted-pick-cancel]')?.addEventListener('click', () => finish(null));

            activeModalCleanup = hostContext.setupModalAccessibility(
                hostContext.modalBox as unknown as HTMLElement,
                hostContext.modalBackdrop as unknown as HTMLElement,
                () => {
                    activeModalCleanup = null;
                    if (!settled) {
                        settled = true;
                        resolve(null);
                    }
                },
                titleId);
        });
    }

    async function resolvePipelineForDoc(doc: StudioRuntimeDocument): Promise<HostedRunnablePipelineDto | null> {
        const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/pipelines');
        if (!response.ok) {
            const payload = await response.json().catch(() => ({})) as { code?: string; error?: string; status?: HostedStatusDto };
            if (payload.status) applyStatus(payload.status);
            if (response.status === 401 || payload.code === 'session_expired') {
                hostContext.setDocumentTraceForTarget(doc, status.targetKey, [
                    { type: 'clear', resetHistory: true },
                    { type: 'status', status: 'failed' },
                    { type: 'show', tab: 'messages' },
                    { type: 'message', level: 'warn', text: payload.error || 'Hosted Portal session expired. Sign in again to continue. Nothing ran locally.' },
                    { type: 'done', exitCode: 1, status: 'Session Expired' },
                ]);
                return null;
            }
            hostContext.setDocumentTraceForTarget(doc, status.targetKey, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'error', text: payload.error || `Could not list runnable pipelines (${response.status}). Nothing ran locally.` },
                { type: 'done', exitCode: 1, status: 'Not run' },
            ]);
            return null;
        }

        const page = await response.json() as { items?: HostedRunnablePipelineDto[] };
        const items = page.items || [];
        if (!items.length) {
            hostContext.setDocumentTraceForTarget(doc, status.targetKey, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'warn', text: 'No runnable Portal pipelines are available for this signed-in account.' },
                { type: 'done', exitCode: 1, status: 'Not run' },
            ]);
            return null;
        }

        const docBase = normalizeBaseName(doc.name || doc.path || '');
        const docStem = stripScriptExt(docBase);
        const exactMatches = items.filter(item => {
            const itemBase = normalizeBaseName(item.name);
            return itemBase === docBase || stripScriptExt(itemBase) === docStem;
        });

        if (exactMatches.length === 1) return exactMatches[0];
        return pickRunnablePipeline(exactMatches.length > 1 ? exactMatches : items);
    }

    function buildOperationTrace(op: HostedRunOperationDto): RunTraceEvent[] {
        const header = `Hosted Target: ${op.ownerOrigin} / ${op.ownerTenantId} · ${op.ownerUsername} · ${op.jobName} (v${op.version})`;
        const trace: RunTraceEvent[] = [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: op.isTerminal ? (op.state === 'completed' && op.finalRun?.status?.toUpperCase() === 'SUCCESS' ? 'running' : 'failed') : 'running' },
            { type: 'show', tab: 'messages' },
            { type: 'message', level: 'sys', text: header },
        ];

        for (const ev of op.events || []) {
            const level = ev.level === 'error' ? 'error' : ev.level === 'warn' ? 'warn' : ev.level === 'info' ? 'info' : 'sys';
            trace.push({ type: 'message', level, text: ev.text });
        }

        if (!op.isTerminal) return trace;

        if (op.state === 'completed' && op.finalRun) {
            const upper = (op.finalRun.status || '').toUpperCase();
            const succeeded = upper === 'SUCCESS';
            const cancelled = upper === 'CANCELLED';
            trace.push({
                type: 'done',
                exitCode: succeeded ? 0 : 1,
                status: succeeded ? 'Complete' : cancelled ? 'Cancelled' : 'Failed',
            });
            return trace;
        }

        if (op.state === 'session_expired') {
            trace.push({
                type: 'message',
                level: 'warn',
                text: op.message || 'Hosted Portal session expired. Sign in again as the original user to recover. Nothing ran locally.',
            });
            trace.push({ type: 'done', exitCode: 1, status: 'Session Expired' });
            return trace;
        }

        if (op.state === 'refused') {
            trace.push({
                type: 'message',
                level: 'error',
                text: op.message || op.refusal?.error || 'Hosted run was refused.',
            });
            trace.push({ type: 'done', exitCode: 1, status: 'Refused' });
            return trace;
        }

        if (op.state === 'unconfirmed_unknown' || op.state === 'unconfirmed_not_found') {
            trace.push({
                type: 'message',
                level: 'warn',
                text: op.message || 'Hosted submission is unconfirmed. Check or recover using the original request key.',
            });
            trace.push({ type: 'done', exitCode: 1, status: 'Unconfirmed' });
            return trace;
        }

        trace.push({ type: 'message', level: 'error', text: op.message || 'Hosted run did not complete.' });
        trace.push({ type: 'done', exitCode: 1, status: 'Failed' });
        return trace;
    }

    async function pollOperation(doc: StudioRuntimeDocument, initialOp: HostedRunOperationDto): Promise<void> {
        const ownerTargetKey = initialOp.ownerTargetKey;
        const trackingKey = opKey(doc.id, ownerTargetKey);
        activeOperations.set(trackingKey, { operationId: initialOp.operationId, doc, targetKey: ownerTargetKey });
        hostContext.updateRunControls();
        hostContext.setDocumentTraceForTarget(doc, ownerTargetKey, buildOperationTrace(initialOp));

        try {
            let current = initialOp;
            while (!disposed && !current.isTerminal) {
                await new Promise<void>(resolve => { setTimeout(resolve, POLL_INTERVAL_MS); });
                if (disposed) return;

                const response = await hostContext.authFetch(
                    hostContext.apiBase + '/api/hosted/runs/' + encodeURIComponent(initialOp.operationId));
                if (!response.ok) break;
                current = await response.json() as HostedRunOperationDto;
                hostContext.setDocumentTraceForTarget(doc, ownerTargetKey, buildOperationTrace(current));
            }
        } finally {
            const existing = activeOperations.get(trackingKey);
            if (existing?.operationId === initialOp.operationId) {
                activeOperations.delete(trackingKey);
            }
            await refreshStatus();
            hostContext.updateRunControls();
        }
    }

    async function runActiveDocument(doc: StudioRuntimeDocument): Promise<void> {
        if (status.mode !== 'hosted') return;

        if (!status.signedIn) {
            hostContext.setDocumentTraceForTarget(doc, status.targetKey, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'warn', text: 'Hosted Portal sign-in is required before running. Sign in again or explicitly switch to Local (Offline). Nothing ran locally.' },
                { type: 'done', exitCode: 1, status: 'Sign-In Required' },
            ]);
            renderHeaderAndBanner();
            return;
        }

        if (status.pendingSubmission) {
            hostContext.setDocumentTraceForTarget(doc, status.targetKey, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'warn', text: `Check or recover unconfirmed submission ${status.pendingSubmission.idempotencyKey} before starting a new run.` },
                { type: 'done', exitCode: 1, status: 'Unconfirmed' },
            ]);
            renderHeaderAndBanner();
            return;
        }

        if (doc.isDirty) {
            const proceed = await confirmDirtyRun(doc);
            if (!proceed) return;
        }

        const pipeline = await resolvePipelineForDoc(doc);
        if (!pipeline) return;

        const runTargetKey = status.targetKey;
        const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/runs', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                pipelineId: pipeline.id,
                documentId: doc.id,
                documentPath: doc.path || doc.name || null,
            }),
        });

        const payload = await response.json().catch(() => ({})) as { code?: string; error?: string; status?: HostedStatusDto } & Partial<HostedRunOperationDto>;
        if (!response.ok) {
            if (payload.status) applyStatus(payload.status);
            hostContext.setDocumentTraceForTarget(doc, runTargetKey, [
                { type: 'clear', resetHistory: true },
                { type: 'status', status: 'failed' },
                { type: 'show', tab: 'messages' },
                { type: 'message', level: 'error', text: payload.error || `Hosted run could not start (${response.status}). Nothing ran locally.` },
                { type: 'done', exitCode: 1, status: payload.code === 'session_expired' ? 'Session Expired' : 'Not run' },
            ]);
            return;
        }

        await pollOperation(doc, payload as HostedRunOperationDto);
    }

    async function recoverPendingSubmission(mode: 'check' | 'retry' = 'check'): Promise<void> {
        const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/runs/recover', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mode }),
        });

        const payload = await response.json().catch(() => ({})) as { error?: string; status?: HostedStatusDto } & Partial<HostedRunOperationDto>;
        if (!response.ok) {
            if (payload.status) applyStatus(payload.status);
            _feedback.notify(payload.error || 'Could not recover pending hosted submission.', {
                title: 'Hosted Recovery',
                tone: 'warning',
            });
            return;
        }

        const op = payload as HostedRunOperationDto;
        const ownerDoc = (op.documentId && hostContext.state.documents.find(d => d.id === op.documentId))
            || hostContext.getActiveDoc();
        if (!ownerDoc) {
            await refreshStatus();
            return;
        }

        await pollOperation(ownerDoc, op);
    }

    async function stopActiveRun(doc: StudioRuntimeDocument): Promise<void> {
        const tracking = activeOperations.get(opKey(doc.id, status.targetKey));
        if (!tracking) return;
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + '/api/hosted/runs/stop', {
                method: 'POST',
            });
            if (response.ok) {
                const payload = await response.json() as { message?: string; status?: HostedStatusDto };
                if (payload.status) applyStatus(payload.status);
            }
        } catch (error) {
            _feedback.notify(errorMessage(error) || 'Failed to send hosted stop request.', {
                title: 'Hosted Stop Failed',
                tone: 'warning',
            });
        }
    }

    targetBtn?.addEventListener('click', () => {
        void openTargetPicker();
    });

    void refreshStatus();

    return {
        getStatus: () => status,
        getCurrentTargetKey: () => status.targetKey,
        isHostedMode: () => status.mode === 'hosted',
        isRunActiveFor: (doc, targetKey) => Boolean(doc && activeOperations.has(opKey(doc.id, targetKey))),
        refreshStatus,
        openTargetPicker,
        runActiveDocument,
        stopActiveRun,
        recoverPendingSubmission,
        dispose: () => {
            disposed = true;
            closeActiveModal();
            activeOperations.clear();
        },
    };
}
