/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-persistence.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report saves, source control, and edit-lease lifecycle.
 */
import { feedback, queryElement } from './designer-context.js';
import { editLeaseRetryDelay } from './run-results.js';
export function createDesignerPersistence(context) {
    const shortRev = (r) => (r ? String(r).slice(0, 8) : '');
    let leaseRequestInFlight = false;
    let leaseTimer = null;
    // ── API helper ────────────────────────────────────────────────────────────
    async function apiJson(url, method = 'GET', body = null, version = null) {
        const init = { method, headers: {} };
        if (version !== null && version !== undefined)
            init.headers['If-Match'] = `"${version}"`;
        if (body !== null) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }
        const res = await context._fetch(context.apiBase + url, init);
        if (!res)
            return null;
        if (!res.ok) {
            const payload = await res.json().catch(() => ({}));
            const payloadObject = payload && typeof payload === 'object' ? payload : {};
            const error = new Error(typeof payloadObject.error === 'string' ? payloadObject.error : res.statusText);
            error.status = res.status;
            error.payload = payloadObject;
            throw error;
        }
        if (res.status === 204)
            return null;
        return res.json();
    }
    function setScriptDiagnosticBadge(errorText) {
        const el = queryElement(context.topbar, '#dsgn-diagnostic-badge');
        if (!el)
            return;
        if (errorText) {
            /** @type {HTMLElement} */ (el).style.display = 'inline-flex';
            el.textContent = '⚠ Script syntax warning';
            /** @type {HTMLElement} */ (el).title = errorText;
        }
        else {
            /** @type {HTMLElement} */ (el).style.display = 'none';
            el.textContent = '';
            /** @type {HTMLElement} */ (el).title = '';
        }
    }
    function setScmStatus(text, kind) {
        const el = queryElement(context.topbar, '#dsgn-scm-status');
        if (!el)
            return;
        el.textContent = text || '';
        const colors = { success: '#16a34a', error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        /** @type {HTMLElement} */ (el).style.color = colors[kind] || colors.neutral;
        /** @type {HTMLElement} */ (el).style.marginLeft = '8px';
        /** @type {HTMLElement} */ (el).style.fontSize = '12px';
    }
    function setLeaseStatus(text, kind, title = '') {
        const status = queryElement(context.topbar, '#dsgn-lease-status');
        if (!status)
            return;
        status.textContent = text || '';
        /** @type {HTMLElement} */ (status).dataset.kind = kind || 'neutral';
        /** @type {HTMLElement} */ (status).title = title || text || '';
    }
    function scheduleLeaseAttempt(delayMs) {
        clearTimeout(leaseTimer ?? undefined);
        if (!context.leaseDisposed)
            leaseTimer = setTimeout(acquireEditLease, Math.max(1_000, delayMs));
    }
    async function acquireEditLease() {
        if (!context.reportId || context.opts.host !== 'portal' || context.leaseDisposed || leaseRequestInFlight)
            return;
        leaseRequestInFlight = true;
        if (context.leaseState !== 'held')
            setLeaseStatus('Claiming edit session…', 'pending');
        try {
            const lease = await apiJson('/api/designer/lease', 'POST', { reportId: context.reportId });
            if (!lease)
                return;
            if (context.leaseDisposed)
                return;
            context.leaseState = 'held';
            const expires = new Date(lease.expiresAt);
            setLeaseStatus('Editing session active', 'success', `This edit session is held by ${lease.owner || 'you'} until ${expires.toLocaleTimeString()}. It renews automatically.`);
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (context.topbar.querySelector('#dsgn-save')).disabled = false;
            // Renew with a wide safety margin. A successful renewal does not advance the report's
            // optimistic content version, so it cannot create a false save conflict.
            scheduleLeaseAttempt(120_000);
        }
        catch (error) {
            if (context.leaseDisposed)
                return;
            const problem = error;
            context.leaseState = problem.status === 409 ? 'held-by-other' : 'disconnected';
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (context.topbar.querySelector('#dsgn-save')).disabled = true;
            if (problem.status === 409) {
                const details = problem.payload && typeof problem.payload === 'object'
                    ? problem.payload : {};
                const owner = typeof details.owner === 'string' ? details.owner : 'Another author';
                const expires = typeof details.expiresAt === 'string' ? new Date(details.expiresAt) : null;
                const expiryText = expires && !Number.isNaN(expires.valueOf())
                    ? ` until ${expires.toLocaleTimeString()}` : '';
                setLeaseStatus(`${owner} is editing${expiryText}`, 'warning', 'Saving is paused. Studio will claim the session after the current lease expires.');
                scheduleLeaseAttempt(editLeaseRetryDelay(typeof details.expiresAt === 'string' ? details.expiresAt : ''));
            }
            else {
                setLeaseStatus('Edit session disconnected', 'error', 'Saving is paused while Studio reconnects to the lease service.');
                scheduleLeaseAttempt(15_000);
            }
        }
        finally {
            leaseRequestInFlight = false;
        }
    }
    function releaseEditLease({ keepalive = false } = {}) {
        clearTimeout(leaseTimer ?? undefined);
        if (!context.reportId || context.opts.host !== 'portal' || context.leaseState !== 'held')
            return Promise.resolve();
        context.leaseState = 'released';
        const url = context.apiBase + `/api/designer/lease/${context.reportId}`;
        if (keepalive) {
            // Best effort on navigation. authFetch retains the caller's normal authorization headers.
            try {
                return Promise.resolve(context._fetch(url, { method: 'DELETE', keepalive: true })).catch(() => { });
            }
            catch {
                return Promise.resolve();
            }
        }
        return apiJson(`/api/designer/lease/${context.reportId}`, 'DELETE').catch(() => { });
    }
    // ── Save ──────────────────────────────────────────────────────────────────
    async function saveReport() {
        if (context.reportId && context.opts.host === 'portal' && context.leaseState !== 'held') {
            feedback.notify('Saving is paused until this browser holds the report edit session.', { title: 'Edit session unavailable', tone: 'warning' });
            return;
        }
        context.reportName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(context.topbar, '#dsgn-name')).value.trim() || context.reportName;
        try {
            const currentScript = context.currentScriptText() || null;
            const r = await apiJson('/api/designer/generate', 'POST', { designState: context.state, script: currentScript });
            const script = r?.script ?? '';
            if (context.opts.onSaveScript) {
                await context.opts.onSaveScript(script);
                context.isDirty = false;
                context.opts.onSave?.();
                return;
            }
            if (context.reportId) {
                const saved = await apiJson('/api/designer/save', 'POST', { reportId: context.reportId, scriptText: script, baseRevision: context.sourceRevision }, context.reportVersion);
                context.reportVersion = saved?.version ?? context.reportVersion;
                context.sourceRevision = saved?.sourceRevision ?? context.sourceRevision;
                context.isDirty = false;
                if (context.sourceControlEnabled) {
                    // Save writes the catalog + script artifact only. Committing to Git is a
                    // separate, explicit step, so stay on the page and surface the Commit action
                    // instead of navigating away.
                    setScmStatus(`Saved v${context.reportVersion} · not yet committed`, 'pending');
                    const commitBtn = queryElement(context.topbar, '#dsgn-commit');
                    if (commitBtn) {
                        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ ((commitBtn)).disabled = false;
                    }
                }
                else {
                    context.opts.onSave?.();
                }
            }
            else {
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(context.saveModal, '#dsgn-modal-name')).value = context.reportName;
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(context.saveModal, '#dsgn-modal-folder')).value = context.folderId == null ? '' : String(context.folderId);
                /** @type {HTMLElement & {_script?: string}} */ (context.saveModal)._script = script;
                context.saveModal.style.display = 'flex';
            }
        }
        catch (e) {
            feedback.notify('Save failed: ' + context.errorText(e), { title: 'Save failed', tone: 'error' });
        }
    }
    // Explicit, separately reported source-control step. Commits the last-saved script
    // artifact to Git (and pushes if the server is configured to push on commit). This never
    // holds a database transaction — the server stages/commits under its own repository lease.
    async function commitScript() {
        if (!context.reportId)
            return;
        const commitBtn = queryElement(context.topbar, '#dsgn-commit');
        const prevTitle = commitBtn?.getAttribute('title') || 'Commit saved script to source control';
        if (commitBtn) {
            /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (commitBtn).disabled = true;
            commitBtn.setAttribute('aria-busy', 'true');
            commitBtn.setAttribute('title', 'Committing to source control');
        }
        setScmStatus('Committing to source control…', 'pending');
        try {
            const res = await apiJson(`/api/reports/${context.reportId}/script-source/commit`, 'POST', {});
            if (res?.committed) {
                context.sourceRevision = res.sourceRevision ?? context.sourceRevision;
                setScmStatus(`Committed ${shortRev(res.sourceRevision)}`, 'success');
            }
            else {
                setScmStatus(`Nothing to commit — working tree matches ${shortRev(res?.sourceRevision) || 'HEAD'}`, 'neutral');
            }
        }
        catch (e) {
            setScmStatus(`Commit failed: ${context.errorText(e)}`, 'error');
        }
        finally {
            if (commitBtn) {
                /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (commitBtn).disabled = false;
                commitBtn.removeAttribute('aria-busy');
                commitBtn.setAttribute('title', prevTitle);
            }
        }
    }
    async function saveAsNew() {
        const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(context.saveModal, '#dsgn-modal-name')).value.trim() || 'New Report';
        const folder = parseInt(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (queryElement(context.saveModal, '#dsgn-modal-folder')).value, 10) || null;
        const script = /** @type {HTMLElement & {_script?: string}} */ (context.saveModal)._script;
        try {
            const created = await apiJson('/api/studio/reports', 'POST', {
                name, folderId: folder, scriptText: script,
            });
            context.saveModal.style.display = 'none';
            context.opts.onSave?.(created);
        }
        catch (e) {
            feedback.notify('Save failed: ' + context.errorText(e), { title: 'Save failed', tone: 'error' });
        }
    }
    return { apiJson, setScriptDiagnosticBadge, acquireEditLease, releaseEditLease, saveReport, commitScript, saveAsNew };
}
