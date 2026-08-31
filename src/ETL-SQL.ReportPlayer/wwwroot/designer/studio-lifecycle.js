/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document lease renewal and release lifecycle for Studio hosts.
 */

export function createStudioLeaseLifecycle({ state, options, documentContext, feedback }) {
    const renewTimer = options.onRenewDocument ? window.setInterval(async () => {
        for (const document of state.documents.filter(item => item.lease?.acquired)) {
            try {
                const lease = await options.onRenewDocument(document);
                if (lease) document.lease = { ...document.lease, ...lease, acquired: true };
            } catch (error) {
                document.lease = { ...document.lease, acquired: false };
                document.canSave = false;
                document.readOnlyReason = error?.message || 'The edit lease expired. Reopen the report to continue editing.';
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
