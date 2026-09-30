/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-lifecycle.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document lease renewal, re-acquisition, and recoverable draft lifecycle for Studio hosts.
 *
 * Recovery drafts live on the host (the Portal database, or the self-installed host's app data),
 * never in browser storage: a script is not left on a shared workstation, and a draft survives a
 * crashed or cleared browser. The host decides whether drafts are kept at all; a host that keeps
 * none simply supplies no draft callbacks.
 */
import { detectPlaintextSecrets } from './studio-security.js';
/**
 * Drafts that Studio kept in localStorage before v0.20.0. They are read once so an upgrade does not
 * lose them, offered like a host draft, and removed; nothing new is written to browser storage.
 */
const LEGACY_DRAFT_PREFIX = 'etlsql_studio_draft:';
function legacyDraftKey(doc) {
    if (doc.reportId != null)
        return `${LEGACY_DRAFT_PREFIX}report_${doc.reportId}`;
    return `${LEGACY_DRAFT_PREFIX}path_${encodeURIComponent(doc.path || String(doc.id || 'untitled'))}`;
}
function takeLegacyDraft(doc) {
    try {
        const key = legacyDraftKey(doc);
        const raw = localStorage.getItem(key);
        if (!raw)
            return null;
        const record = JSON.parse(raw);
        return typeof record?.content === 'string'
            ? { content: record.content, baseVersion: record.version, baseSourceRevision: record.sourceRevision, updatedAt: record.timestamp }
            : null;
    }
    catch {
        return null;
    }
}
function removeLegacyDraft(doc) {
    try {
        localStorage.removeItem(legacyDraftKey(doc));
    }
    catch {
        // storage unavailable or disabled
    }
}
/** A draft may leave the browser only without a plaintext credential in it. */
export function draftMayBeKept(scriptText) {
    return detectPlaintextSecrets(scriptText).length === 0;
}
export function createStudioLeaseLifecycle({ state, options, documentContext, feedback }) {
    async function reacquire(document, { silent = false } = {}) {
        if (!options.onRenewDocument && !options.onReacquireDocument)
            return false;
        try {
            let result = null;
            if (options.onReacquireDocument) {
                result = await options.onReacquireDocument(document);
            }
            else if (options.onRenewDocument) {
                result = await options.onRenewDocument(document);
            }
            if (result === false) {
                document.lease = { ...document.lease, acquired: false };
                document.canSave = false;
                if (!silent)
                    feedback.notify('Could not reacquire edit lease.', { title: 'Lease Unavailable', tone: 'warning' });
                return false;
            }
            const leaseData = result?.lease || result || {};
            document.lease = { ...document.lease, ...leaseData, acquired: true };
            document.canSave = true;
            document.readOnlyReason = null;
            document.leaseRenewalFailures = 0;
            const newVersion = result?.version ?? leaseData.version;
            if (newVersion !== undefined && document.version !== undefined && newVersion !== document.version) {
                if (document.isDirty) {
                    feedback.notify('This report was modified on the server while disconnected. Your local edits are preserved, but saving will require resolving the conflict.', { title: 'Server Revision Conflict', tone: 'warning' });
                }
                else if (result?.content !== undefined) {
                    document.content = result.content;
                    document.version = newVersion;
                }
            }
            else if (newVersion !== undefined) {
                document.version = newVersion;
            }
            if (!silent) {
                feedback.notify('Edit lease reacquired successfully.', { title: 'Connected', tone: 'success' });
            }
            return true;
        }
        catch (error) {
            document.lease = { ...document.lease, acquired: false };
            document.canSave = false;
            const message = error?.message || 'Failed to reacquire edit lease.';
            document.readOnlyReason = message;
            if (!silent) {
                feedback.notify(message, { title: 'Reacquire Failed', tone: 'error' });
            }
            return false;
        }
    }
    async function reacquireAll() {
        for (const document of state.documents) {
            if (document.lease && !document.lease.acquired) {
                await reacquire(document, { silent: true });
            }
        }
    }
    /**
     * A renewal that fails once is usually a blip: a dropped request, a host restarting. Dropping the
     * lease on it would make the author reacquire for nothing, so a failure is retried after each of
     * the delays. A refusal the host means (the lease is someone else's, or access was withdrawn)
     * is not retried.
     */
    async function renewWithRetry(document) {
        const delays = options.leaseRenewRetryDelaysMs ?? [2000, 5000];
        for (let attempt = 0;; attempt++) {
            try {
                return await options.onRenewDocument(document);
            }
            catch (error) {
                document.leaseRenewalFailures = (document.leaseRenewalFailures ?? 0) + 1;
                const definitive = [403, 404, 409].includes(Number(error?.status));
                if (definitive || attempt >= delays.length)
                    throw error;
                await new Promise(resolve => setTimeout(resolve, delays[attempt]));
            }
        }
    }
    const renewTimer = options.onRenewDocument ? window.setInterval(async () => {
        for (const document of state.documents) {
            if (!document.lease)
                continue;
            if (document.lease.acquired) {
                try {
                    const lease = await renewWithRetry(document);
                    if (lease) {
                        document.lease = { ...document.lease, ...lease, acquired: true };
                        document.leaseRenewalFailures = 0;
                    }
                }
                catch (error) {
                    document.lease = { ...document.lease, acquired: false };
                    document.canSave = false;
                    const reason = error?.message || 'The edit lease expired. Reopen the report to continue editing.';
                    document.readOnlyReason = reason;
                    feedback.notify(reason, {
                        title: 'Edit Lease Lost',
                        tone: 'warning',
                        action: {
                            label: 'Reacquire',
                            onSelect: () => { void reacquire(document); }
                        }
                    });
                }
            }
        }
    }, options.leaseRenewIntervalMs || 240000) : null;
    const onOnline = () => {
        feedback.notify('Network connection restored. Reconnecting edit leases…', { title: 'Back Online', tone: 'info' });
        void reacquireAll();
    };
    const onOffline = () => {
        feedback.notify('Network connection lost. Keep this tab open: your edits are here, and they are kept as a draft again once the connection returns.', { title: 'Offline', tone: 'warning' });
    };
    if (typeof window !== 'undefined') {
        window.addEventListener('online', onOnline);
        window.addEventListener('offline', onOffline);
    }
    async function saveDraft(document, { keepalive = false } = {}) {
        if (!options.onSaveDraft)
            return 'unavailable';
        const content = document.content || '';
        if (!document.isDirty) {
            removeDraft(document);
            return 'unavailable';
        }
        if (!draftMayBeKept(content))
            return 'refused';
        try {
            return await options.onSaveDraft(document, {
                content,
                baseVersion: document.version,
                baseSourceRevision: document.sourceRevision
            }, { keepalive });
        }
        catch {
            // Unreachable host: the edits are still in this tab and are kept on the next save.
            return 'unavailable';
        }
    }
    function removeDraft(document) {
        removeLegacyDraft(document);
        void options.onRemoveDraft?.(document)?.catch?.(() => { });
    }
    async function getRecoverableDraft(document) {
        let draft;
        try {
            draft = (await options.onLoadDraft?.(document)) ?? null;
        }
        catch {
            draft = null;
        }
        draft ??= takeLegacyDraft(document);
        if (!draft || draft.content === (document.content || '') || !draftMayBeKept(draft.content)) {
            if (draft)
                removeDraft(document);
            return null;
        }
        return draft;
    }
    const releaseOnPageHide = () => {
        for (const document of state.documents) {
            if (document.isDirty)
                void saveDraft(document, { keepalive: true });
        }
        for (const document of state.documents.filter(item => item.lease?.acquired)) {
            void options.onCloseDocument?.(document, { keepalive: true });
        }
    };
    if (options.onCloseDocument && typeof window !== 'undefined') {
        window.addEventListener('pagehide', releaseOnPageHide);
    }
    return {
        reacquire,
        reacquireAll,
        saveDraft,
        removeDraft,
        getRecoverableDraft,
        get draftsKept() { return Boolean(options.onSaveDraft); },
        dispose() {
            for (const document of state.documents.filter(item => item.lease?.acquired)) {
                void options.onCloseDocument?.(document, { keepalive: false });
            }
            if (typeof window !== 'undefined') {
                window.removeEventListener('pagehide', releaseOnPageHide);
                window.removeEventListener('online', onOnline);
                window.removeEventListener('offline', onOffline);
                if (renewTimer)
                    window.clearInterval(renewTimer);
            }
            for (const document of state.documents) {
                documentContext(document).previewAbort?.abort();
                documentContext(document).dagAbort?.abort();
            }
        }
    };
}
