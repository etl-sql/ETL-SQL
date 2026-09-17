// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-lifecycle.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document lease renewal, re-acquisition, and recoverable draft lifecycle for Studio hosts.
 */
import { detectPlaintextSecrets } from './studio-security.js';
const DRAFT_PREFIX = 'etlsql_studio_draft:';
function getDraftKey(doc) {
    if (doc.reportId != null)
        return `${DRAFT_PREFIX}report_${doc.reportId}`;
    return `${DRAFT_PREFIX}path_${encodeURIComponent(doc.path || String(doc.id || 'untitled'))}`;
}
export function isDraftStoragePermitted(scriptText, options) {
    if (options?.allowDraftStorage === false)
        return false;
    const mode = options?.deploymentMode || '';
    if (mode === 'Strict' || mode === 'ZeroTrust')
        return false;
    if (detectPlaintextSecrets(scriptText).length > 0)
        return false;
    return true;
}
export function saveDraftRecord(document, options) {
    if (typeof localStorage === 'undefined')
        return false;
    const content = document.content || '';
    if (!document.isDirty) {
        removeDraftRecord(document);
        return false;
    }
    if (!isDraftStoragePermitted(content, options)) {
        removeDraftRecord(document);
        return false;
    }
    try {
        const key = getDraftKey(document);
        const record = {
            id: document.id,
            reportId: document.reportId,
            path: document.path || '',
            content,
            timestamp: Date.now(),
            version: document.version,
            sourceRevision: document.sourceRevision
        };
        localStorage.setItem(key, JSON.stringify(record));
        return true;
    }
    catch {
        return false;
    }
}
export function removeDraftRecord(document) {
    if (typeof localStorage === 'undefined')
        return;
    try {
        const key = getDraftKey(document);
        localStorage.removeItem(key);
    }
    catch {
        // storage unavailable or disabled
    }
}
export function getRecoverableDraftRecord(document, options) {
    if (typeof localStorage === 'undefined')
        return null;
    try {
        const key = getDraftKey(document);
        const raw = localStorage.getItem(key);
        if (!raw)
            return null;
        const record = JSON.parse(raw);
        if (!record || typeof record.content !== 'string') {
            localStorage.removeItem(key);
            return null;
        }
        if (!isDraftStoragePermitted(record.content, options)) {
            localStorage.removeItem(key);
            return null;
        }
        if (record.content === (document.content || '')) {
            localStorage.removeItem(key);
            return null;
        }
        return record;
    }
    catch {
        return null;
    }
}
export function createStudioLeaseLifecycle({ state, options, documentContext, feedback }) {
    const deploymentMode = options.deploymentMode || state.deploymentMode || '';
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
    const renewTimer = options.onRenewDocument ? window.setInterval(async () => {
        for (const document of state.documents) {
            if (!document.lease)
                continue;
            if (document.lease.acquired) {
                try {
                    const lease = await options.onRenewDocument(document);
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
        feedback.notify('Network connection lost. Unsaved changes are preserved as local drafts.', { title: 'Offline Mode', tone: 'warning' });
    };
    if (typeof window !== 'undefined') {
        window.addEventListener('online', onOnline);
        window.addEventListener('offline', onOffline);
    }
    const releaseOnPageHide = () => {
        for (const document of state.documents) {
            if (document.isDirty) {
                saveDraftRecord(document, { allowDraftStorage: options.allowDraftStorage, deploymentMode });
            }
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
        saveDraft(doc) {
            return saveDraftRecord(doc, { allowDraftStorage: options.allowDraftStorage, deploymentMode });
        },
        removeDraft(doc) {
            removeDraftRecord(doc);
        },
        getRecoverableDraft(doc) {
            return getRecoverableDraftRecord(doc, { allowDraftStorage: options.allowDraftStorage, deploymentMode });
        },
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
