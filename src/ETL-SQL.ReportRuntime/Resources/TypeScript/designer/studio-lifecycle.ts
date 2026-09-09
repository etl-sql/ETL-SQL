/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document lease renewal and release lifecycle for Studio hosts.
 */

export interface LeaseDocument {
    lease?: { acquired?: boolean };
    canSave?: boolean;
    readOnlyReason?: string;
}

export interface LeaseLifecycleInputs<Document extends LeaseDocument> {
    state: { documents: Document[] };
    options: {
        onRenewDocument?: (document: Document) => Promise<Partial<NonNullable<Document['lease']>> | null | undefined | void>;
        onCloseDocument?: (document: Document, options: { keepalive: boolean }) => unknown;
        leaseRenewIntervalMs?: number;
    };
    documentContext: (document: Document) => { previewAbort?: AbortController | null; dagAbort?: AbortController | null };
    feedback: Pick<NonNullable<typeof globalThis.ETLSQLFeedback>, 'notify'>;
}

export function createStudioLeaseLifecycle<Document extends LeaseDocument>({ state, options, documentContext, feedback }: LeaseLifecycleInputs<Document>) {
    const renewTimer = options.onRenewDocument ? window.setInterval(async () => {
        for (const document of state.documents.filter(item => item.lease?.acquired)) {
            try {
                const lease = await options.onRenewDocument!(document);
                if (lease) document.lease = { ...document.lease, ...lease, acquired: true };
            } catch (error) {
                document.lease = { ...document.lease, acquired: false };
                document.canSave = false;
                document.readOnlyReason = (error as { message?: string } | null | undefined)?.message || 'The edit lease expired. Reopen the report to continue editing.';
                feedback.notify(document.readOnlyReason, { title: 'Edit Lease Lost', tone: 'warning' });
            }
        }
    }, options.leaseRenewIntervalMs || 240000) : null;

    const releaseOnPageHide = () => {
        for (const document of state.documents.filter(item => item.lease?.acquired)) {
            void options.onCloseDocument?.(document, { keepalive: true });
        }
    };
    if (options.onCloseDocument) window.addEventListener('pagehide', releaseOnPageHide);

    return {
        dispose() {
            for (const document of state.documents.filter(item => item.lease?.acquired)) {
                void options.onCloseDocument?.(document, { keepalive: false });
            }
            window.removeEventListener('pagehide', releaseOnPageHide);
            if (renewTimer) window.clearInterval(renewTimer);
            for (const document of state.documents) {
                documentContext(document).previewAbort?.abort();
                documentContext(document).dagAbort?.abort();
            }
        }
    };
}
