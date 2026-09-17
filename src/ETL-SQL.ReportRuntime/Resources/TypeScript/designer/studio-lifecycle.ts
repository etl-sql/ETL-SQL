/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document lease renewal, re-acquisition, and recoverable draft lifecycle for Studio hosts.
 */

import { detectPlaintextSecrets } from './studio-security.js';

export interface LeaseDocument {
    id?: string;
    reportId?: string | number;
    path?: string;
    name?: string;
    content?: string;
    isDirty?: boolean;
    version?: unknown;
    sourceRevision?: unknown;
    lease?: { acquired?: boolean; [key: string]: unknown };
    canSave?: boolean;
    readOnlyReason?: string | null;
    leaseRenewalFailures?: number;
    [key: string]: unknown;
}

export interface DraftRecord {
    id?: string;
    reportId?: string | number;
    path: string;
    content: string;
    timestamp: number;
    version?: unknown;
    sourceRevision?: unknown;
}

export interface LeaseLifecycleOptions<Document extends LeaseDocument> {
    onRenewDocument?: (document: Document) => Promise<Partial<NonNullable<Document['lease']>> | null | undefined | void>;
    onCloseDocument?: (document: Document, options: { keepalive: boolean }) => unknown;
    onReacquireDocument?: (document: Document) => Promise<{ lease?: Partial<NonNullable<Document['lease']>>; version?: unknown; sourceRevision?: unknown; content?: string } | null | undefined | void>;
    leaseRenewIntervalMs?: number;
    allowDraftStorage?: boolean;
    deploymentMode?: string;
}

export interface LeaseLifecycleInputs<Document extends LeaseDocument> {
    state: { documents: Document[]; deploymentMode?: string };
    options: LeaseLifecycleOptions<Document>;
    documentContext: (document: Document) => { previewAbort?: AbortController | null; dagAbort?: AbortController | null };
    feedback: Pick<NonNullable<typeof globalThis.ETLSQLFeedback>, 'notify'>;
}

export interface StudioLeaseLifecycleHandle<Document extends LeaseDocument> {
    reacquire: (document: Document, options?: { silent?: boolean }) => Promise<boolean>;
    reacquireAll: () => Promise<void>;
    saveDraft: (document: Document) => boolean;
    removeDraft: (document: Document) => void;
    getRecoverableDraft: (document: Document) => DraftRecord | null;
    dispose: () => void;
}

const DRAFT_PREFIX = 'etlsql_studio_draft:';

function getDraftKey(doc: LeaseDocument): string {
    if (doc.reportId != null) return `${DRAFT_PREFIX}report_${doc.reportId}`;
    return `${DRAFT_PREFIX}path_${encodeURIComponent(doc.path || String(doc.id || 'untitled'))}`;
}

export function isDraftStoragePermitted(
    scriptText: string,
    options?: { allowDraftStorage?: boolean; deploymentMode?: string }
): boolean {
    if (options?.allowDraftStorage === false) return false;
    const mode = options?.deploymentMode || '';
    if (mode === 'Strict' || mode === 'ZeroTrust') return false;
    if (detectPlaintextSecrets(scriptText).length > 0) return false;
    return true;
}

export function saveDraftRecord(
    document: LeaseDocument,
    options?: { allowDraftStorage?: boolean; deploymentMode?: string }
): boolean {
    if (typeof localStorage === 'undefined') return false;
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
        const record: DraftRecord = {
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
    } catch {
        return false;
    }
}

export function removeDraftRecord(document: LeaseDocument): void {
    if (typeof localStorage === 'undefined') return;
    try {
        const key = getDraftKey(document);
        localStorage.removeItem(key);
    } catch {
        // storage unavailable or disabled
    }
}

export function getRecoverableDraftRecord(
    document: LeaseDocument,
    options?: { allowDraftStorage?: boolean; deploymentMode?: string }
): DraftRecord | null {
    if (typeof localStorage === 'undefined') return null;
    try {
        const key = getDraftKey(document);
        const raw = localStorage.getItem(key);
        if (!raw) return null;
        const record = JSON.parse(raw) as DraftRecord;
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
    } catch {
        return null;
    }
}

export function createStudioLeaseLifecycle<Document extends LeaseDocument>({
    state,
    options,
    documentContext,
    feedback
}: LeaseLifecycleInputs<Document>): StudioLeaseLifecycleHandle<Document> {
    const deploymentMode = options.deploymentMode || state.deploymentMode || '';

    async function reacquire(document: Document, { silent = false }: { silent?: boolean } = {}): Promise<boolean> {
        if (!options.onRenewDocument && !options.onReacquireDocument) return false;
        try {
            let result: any = null;
            if (options.onReacquireDocument) {
                result = await options.onReacquireDocument(document);
            } else if (options.onRenewDocument) {
                result = await options.onRenewDocument(document);
            }
            if (result === false) {
                document.lease = { ...document.lease, acquired: false };
                document.canSave = false;
                if (!silent) feedback.notify('Could not reacquire edit lease.', { title: 'Lease Unavailable', tone: 'warning' });
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
                    feedback.notify(
                        'This report was modified on the server while disconnected. Your local edits are preserved, but saving will require resolving the conflict.',
                        { title: 'Server Revision Conflict', tone: 'warning' }
                    );
                } else if (result?.content !== undefined) {
                    document.content = result.content;
                    document.version = newVersion;
                }
            } else if (newVersion !== undefined) {
                document.version = newVersion;
            }

            if (!silent) {
                feedback.notify('Edit lease reacquired successfully.', { title: 'Connected', tone: 'success' });
            }
            return true;
        } catch (error: any) {
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

    async function reacquireAll(): Promise<void> {
        for (const document of state.documents) {
            if (document.lease && !document.lease.acquired) {
                await reacquire(document, { silent: true });
            }
        }
    }

    const renewTimer = options.onRenewDocument ? window.setInterval(async () => {
        for (const document of state.documents) {
            if (!document.lease) continue;
            if (document.lease.acquired) {
                try {
                    const lease = await options.onRenewDocument!(document);
                    if (lease) {
                        document.lease = { ...document.lease, ...lease, acquired: true };
                        document.leaseRenewalFailures = 0;
                    }
                } catch (error: any) {
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
        saveDraft(doc: Document) {
            return saveDraftRecord(doc, { allowDraftStorage: options.allowDraftStorage, deploymentMode });
        },
        removeDraft(doc: Document) {
            removeDraftRecord(doc);
        },
        getRecoverableDraft(doc: Document) {
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
                if (renewTimer) window.clearInterval(renewTimer);
            }
            for (const document of state.documents) {
                documentContext(document).previewAbort?.abort();
                documentContext(document).dagAbort?.abort();
            }
        }
    };
}
