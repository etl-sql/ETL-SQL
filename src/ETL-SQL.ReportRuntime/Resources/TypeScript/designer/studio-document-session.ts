/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document switching, opening, creation, and catalog publication.
 */

import { _escapeHtml, _feedback, errorMessage, getStoredProjectionPreference, queryElement } from './studio-context.js';
import type { LeaseDocument } from './studio-lifecycle.js';

import type { StudioDesignState, StudioDomElement, StudioDynamic, StudioOptions, StudioRuntimeContext, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { REPORT_WORKFLOW_TEMPLATES, STUDIO_ROUTES, STUDIO_STARTER_SCRIPTS, STUDIO_WORKSPACE_ROUTES } from './studio-contracts.js';
import type { SnapshotLike } from './studio-data.js';
import { updateSnapshotPackageFromManifest } from './studio-data.js';
import type { StudioLeaseLifecycleHandle } from './studio-lifecycle.js';

export interface StudioDocumentSessionContext {
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly authoringRequest: (route: string, { method, body, query, fallbackError, accept }?: { method?: string; body?: any; query?: Record<string, any> | null; fallbackError?: string | null; accept?: string | null; }) => Promise<any>;
    readonly checkRecoverableDraft: (doc: StudioRuntimeDocument | null | undefined) => void;
    codeMirrorDebounce: number | null;
    readonly documentContext: (document?: StudioRuntimeDocument | null) => StudioRuntimeContext;
    readonly ensureReportWorkflow: (doc: StudioRuntimeDocument, { askWhenAmbiguous }?: { askWhenAmbiguous?: boolean | undefined; }) => Promise<"dashboard" | "paginated" | null>;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly handleSave: () => Promise<boolean | undefined>;
    readonly hasCapability: (capability: string) => boolean;
    readonly homeStage: StudioDomElement;
    isSettingDocumentContent: boolean;
    readonly leaseLifecycle: StudioLeaseLifecycleHandle<LeaseDocument>;
    readonly modalBackdrop: StudioDomElement;
    readonly modalBox: StudioDomElement;
    readonly opts: StudioOptions;
    readonly paintResults: (context: StudioRuntimeContext) => void;
    readonly rememberLineEnding: (doc: StudioRuntimeDocument) => void;
    readonly renderReportWorkflowChrome: (doc: StudioRuntimeDocument | null, designState?: StudioDesignState | undefined) => void;
    readonly renderSidebarContent: (activity: string) => void;
    readonly renderStudioHome: () => void;
    readonly renderTabs: () => void;
    readonly renderVisualStage: () => void;
    readonly setContextualRailVisibility: () => void;
    readonly setProjection: (mode: string) => void;
    readonly setupModalAccessibility: (box: HTMLElement, backdrop: HTMLElement, onClose: () => void, titleId?: string) => () => void;
    readonly state: StudioRuntimeState;
    readonly updateRunControls: () => void;
    readonly updateSnapshotPackage: (snapshot: SnapshotLike | null) => void;
    readonly withLineEnding: (text: string, ending: string) => string;
}

export function createStudioDocumentSession(hostContext: StudioDocumentSessionContext) {

    async function switchDoc(docId: string) {
        const currentDoc = hostContext.getActiveDoc();
        if (currentDoc && hostContext.state.editorInstance) {
            const editor = hostContext.state.editorInstance as StudioDynamic;
            currentDoc.content = editor.getValue();
            if (editor.getState) {
                currentDoc.editorState = editor.getState();
            }
            if (editor.getScrollPosition) {
                const scroll = editor.getScrollPosition();
                currentDoc.scrollTop = scroll.top;
                currentDoc.scrollLeft = scroll.left;
            }
            hostContext.documentContext(currentDoc).previewAbort?.abort();
        }
        clearTimeout(hostContext.codeMirrorDebounce || undefined);
        hostContext.state.designerInstance?.invalidateScriptApply?.();

        hostContext.state.activeDocId = docId;
        hostContext.state.selectedVisualId = null;

        if (hostContext.state.designerInstance) {
            hostContext.state.designerInstance.dispose?.();
            hostContext.state.designerInstance = null;
        }

        hostContext.renderTabs();

        if (docId === '__home__') {
            hostContext.state.resultsPanel?.clear();
            hostContext.state.resultsPanel?.setDiagnostics([]);
            hostContext.renderStudioHome();
            hostContext.setContextualRailVisibility();
            if (hostContext.state.activeActivity) {
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
            hostContext.updateRunControls();
            return;
        }

        const newDoc = hostContext.getActiveDoc();
        if (newDoc) {
            hostContext.rememberLineEnding(newDoc);
            await hostContext.ensureReportWorkflow(newDoc);
            // The tab can close or change while workflow detection waits on the host.
            if (hostContext.getActiveDoc() !== newDoc) return;
            const context = hostContext.documentContext(newDoc);
            hostContext.homeStage.style.display = 'none';
            hostContext.paintResults(context);
            hostContext.updateSnapshotPackage(context.snapshot);
            hostContext.setProjection(newDoc.projection || 'split');
            if (hostContext.state.editorInstance) {
                hostContext.isSettingDocumentContent = true;
                try {
                    const editor = hostContext.state.editorInstance as StudioDynamic;
                    if (editor.setState && editor.createDocState) {
                        if (!newDoc.editorState) {
                            newDoc.editorState = editor.createDocState(newDoc.content);
                        }
                        editor.setState(newDoc.editorState);
                        if (newDoc.scrollTop != null || newDoc.scrollLeft != null) {
                            editor.setScrollPosition?.({
                                top: newDoc.scrollTop ?? 0,
                                left: newDoc.scrollLeft ?? 0
                            });
                        }
                    } else {
                        editor.setValue(newDoc.content);
                    }
                } finally {
                    hostContext.isSettingDocumentContent = false;
                }
            }
            hostContext.renderVisualStage();
            hostContext.setContextualRailVisibility();
            if (hostContext.state.activeActivity) {
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
        }
        hostContext.updateRunControls();
    }

    async function closeDoc(docId: string) {
        const docIndex = hostContext.state.documents.findIndex(d => d.id === docId);
        if (docIndex < 0) return;

        const doc = hostContext.state.documents[docIndex];
        if (doc) {
            doc.editorState = null;
            hostContext.leaseLifecycle.removeDraft(doc as any);
        }
        if (doc.isDirty) {
            const saveBeforeClose = await _feedback.confirm(`Save changes to ${doc.name} before closing?`, {
                title: 'Unsaved Changes',
                confirmLabel: 'Yes',
                cancelLabel: 'No'
            });
            if (saveBeforeClose) {
                await hostContext.handleSave();
                if (doc.isDirty) return;
            }
        }

        try {
            await hostContext.opts.onCloseDocument?.(doc, { keepalive: false });
        } catch (error) {
            console.warn('Failed to release the document lease:', error);
        }

        hostContext.state.documents.splice(docIndex, 1);
        if (hostContext.state.activeDocId === docId) {
            if (hostContext.state.documents.length > 0) {
                hostContext.state.activeDocId = hostContext.state.documents[Math.max(0, docIndex - 1)].id;
            } else {
                hostContext.state.activeDocId = '__home__';
            }
        }
        await switchDoc(hostContext.state.activeDocId);
    }

    async function promptForCatalogReport(): Promise<{ name: string; folderId: any } | null> {
        if (!hostContext.state.catalogFolders.length) {
            // A dead end with no explanation is the worst first impression the Portal can give, so
            // say what is missing and who can grant it.
            _feedback.notify(
                'Studio saves reports into catalog folders, and you do not have write access to any. '
                + 'Ask a Portal administrator to grant you Manage permission on a folder, then reopen Studio.',
                { title: 'No writable folder', tone: 'warning' });
            return null;
        }

        const defaultFolder = hostContext.state.catalogFolders[0];
        return new Promise<{ name: string; folderId: any } | null>(resolve => {
            const titleId = 'etlsql-catalog-create-title';
            hostContext.modalBox.innerHTML = `
                <h2 id="${titleId}">Create catalog report</h2>
                <label>Report name<input type="text" data-catalog-report-name value="Untitled report" autocomplete="off"></label>
                <label>Folder<select data-catalog-report-folder>${hostContext.state.catalogFolders.map((folder: StudioDynamic) => `<option value="${_escapeHtml(folder.id)}">${_escapeHtml(folder.path || folder.name)}</option>`).join('')}</select></label>
                <div class="etlsql-studio-modal-actions">
                    <button type="button" class="etlsql-studio-btn" data-catalog-create-cancel>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-catalog-create-confirm>Create</button>
                </div>`;

            let settled = false;
            let cleanupModal: (() => void) | null = null;
            const finish = (value: { name: string; folderId: any } | null) => {
                if (settled) return;
                settled = true;
                cleanupModal?.();
                hostContext.modalBox.innerHTML = '';
                resolve(value);
            };

            cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, () => finish(null), titleId);
            queryElement(hostContext.modalBox, '[data-catalog-create-cancel]').addEventListener('click', () => finish(null));
            queryElement(hostContext.modalBox, '[data-catalog-create-confirm]').addEventListener('click', () => {
                const name = queryElement<HTMLInputElement>(hostContext.modalBox, '[data-catalog-report-name]').value.trim();
                const folderId = queryElement<HTMLSelectElement>(hostContext.modalBox, '[data-catalog-report-folder]').value || defaultFolder.id;
                if (!name) return;
                finish({ name, folderId });
            });
            const nameInput = queryElement<HTMLInputElement>(hostContext.modalBox, '[data-catalog-report-name]');
            nameInput.focus();
            nameInput.select();
        });
    }

    async function handlePublishCatalogReport(targetDoc?: StudioRuntimeDocument | null) {
        const doc = targetDoc || hostContext.getActiveDoc();
        if (!doc) return false;
        if (!hostContext.opts.onCreateDocument) {
            _feedback.notify('Publishing to a catalog is not supported in this workspace environment.', { title: 'Publish Unavailable', tone: 'warning' });
            return false;
        }
        if (doc.reportId) {
            _feedback.notify(`'${doc.name}' is already published in the catalog. Use Save (Ctrl+S) to persist updates.`, { title: 'Already Published', tone: 'info' });
            return false;
        }
        if (!hostContext.hasCapability('ReportPublish')) {
            _feedback.notify('Publishing to the catalog requires the ReportPublish capability. Contact an administrator to request publishing rights.', { title: 'Publish Restricted', tone: 'warning' });
            return false;
        }
        if (!hostContext.hasCapability('ScriptSave')) {
            _feedback.notify('Publishing to the catalog requires the ScriptSave capability.', { title: 'Publish Restricted', tone: 'warning' });
            return false;
        }

        const request = await promptForCatalogReport();
        if (!request) return false;

        const currentContent = hostContext.state.editorInstance ? hostContext.state.editorInstance.getValue() : doc.content;
        doc.content = currentContent;

        const folder = hostContext.state.catalogFolders.find((f: StudioDynamic) => String(f.id) === String(request.folderId));
        const folderPath = folder ? (folder.path || folder.name) : '';

        try {
            const workflow = (doc.reportWorkflow || (doc.path?.endsWith('.paginated.rptsql') ? 'paginated' : 'dashboard')) as 'dashboard' | 'paginated';
            const created = await hostContext.opts.onCreateDocument({
                ...request,
                type: 'report',
                workflow,
                scriptText: hostContext.withLineEnding(doc.content, doc.lineEnding || '\n')
            });
            created.reportWorkflow = workflow;
            hostContext.state.catalogReports.push(created);
            doc.reportId = created.id;
            doc.version = created.version ?? 1;
            doc.sourceRevision = created.sourceRevision ?? null;
            doc.name = created.name;
            doc.path = `${folderPath}/${created.name}.rptsql`.replace(/^\//, '');
            doc.isDirty = false;
            hostContext.leaseLifecycle.removeDraft(doc as any);
            if (hostContext.opts.onRenewDocument) {
                const lease = await hostContext.opts.onRenewDocument(doc);
                doc.lease = lease;
            }
            hostContext.renderTabs();
            _feedback.notify(`Published report '${created.name}' to the catalog.`, { title: 'Report Published', tone: 'success' });
            return true;
        } catch (error) {
            _feedback.notify('Publish failed: ' + errorMessage(error), { title: 'Publish Failed', tone: 'error' });
            return false;
        }
    }

    async function createNewFile(type: string, { seed = false } = {}) {
        const reportWorkflow = type === 'paginated' ? 'paginated' : type === 'dashboard' || type === 'report' ? 'dashboard' : null;
        const isReportType = Boolean(reportWorkflow);
        if (hostContext.opts.onCreateDocument && !seed) {
            if (!isReportType) {
                _feedback.notify('Portal catalog currently supports Report-SQL (.rptsql) documents.', { title: 'Create Document', tone: 'warning' });
                return;
            }
            if (!hostContext.hasCapability('ReportPublish')) {
                // Separate learning/drafting from publishing:
                // An author without ReportPublish is not blocked; they can design, preview, and test in private practice draft mode.
                _feedback.notify(
                    'Opening in private draft mode. You can design, preview, and test reports; publishing to the catalog requires the ReportPublish capability.',
                    { title: 'Practice Draft Mode', tone: 'info' });
            } else if (!hostContext.hasCapability('ScriptSave')) {
                _feedback.notify(
                    'Creating a catalog report requires the ScriptSave capability. Ask a Portal administrator to grant it, '
                    + 'or open an existing report to explore Studio in the meantime.',
                    { title: 'Create Report', tone: 'warning' });
                return;
            } else {
                const request = await promptForCatalogReport();
                if (!request) return;
                try {
                    const workflowKey = (reportWorkflow || 'dashboard') as 'dashboard' | 'paginated';
                    const scriptText = REPORT_WORKFLOW_TEMPLATES[workflowKey];
                    const created = await hostContext.opts.onCreateDocument({ ...request, type: 'report', workflow: reportWorkflow, scriptText });
                    created.reportWorkflow = reportWorkflow;
                    hostContext.state.catalogReports.push(created);
                    const pref = getStoredProjectionPreference();
                    const targetProj = (pref === 'code' || pref === 'split') ? pref : (reportWorkflow === 'dashboard' ? 'canvas' : 'split');
                    await openCatalogReport(created, targetProj);
                    return;
                } catch (error) {
                    _feedback.notify(errorMessage(error) || 'The report could not be created.', { title: 'Create Report Failed', tone: 'error' });
                    return;
                }
            }
        }

        const rptCount = hostContext.state.documents.filter(d => (d.path || '').endsWith('.rptsql')).length + 1;
        const etlCount = hostContext.state.documents.filter(d => (d.path || '').endsWith('.etlsql')).length + 1;

        let path;
        let content;
        let proj;

        if (isReportType) {
            path = reportWorkflow === 'paginated' ? `untitled_paginated_${rptCount}.rptsql` : `untitled_dashboard_${rptCount}.rptsql`;
            const workflowKey = (reportWorkflow || 'dashboard') as 'dashboard' | 'paginated';
            content = seed ? STUDIO_STARTER_SCRIPTS.report : REPORT_WORKFLOW_TEMPLATES[workflowKey];
            // A new dashboard opens on the canvas it is about to be built on. Splitting the window
            // with a script the author has not written yet teaches the wrong first lesson — the
            // script is the escape hatch, and the projection buttons keep it one click away.
            // If the author has learned and expressed a preference for 'code' or 'split' view,
            // respect that preference.
            const pref = getStoredProjectionPreference();
            if (pref === 'code' || pref === 'split') {
                proj = pref;
            } else {
                proj = reportWorkflow === 'dashboard' ? 'canvas' : 'split';
            }
        } else if (type === 'etl') {
            path = `untitled_pipeline_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.etl : '';
            proj = 'split';
        } else {
            path = `untitled_query_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.sql : '';
            proj = 'code';
        }

        const newDoc: StudioRuntimeDocument = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: path,
            name: path,
            content: content,
            isDirty: isReportType || Boolean(seed),
            projection: proj,
            reportWorkflow
        };

        hostContext.state.documents.push(newDoc);
        await switchDoc(newDoc.id);
        if (seed && isReportType) await hydrateSeededReport(newDoc);
    }

    async function hydrateSeededReport(document: StudioRuntimeDocument) {
        if (!document || hostContext.getActiveDoc() !== document) return;
        const context = hostContext.documentContext(document);
        try {
            const manifest = await hostContext.authoringRequest(STUDIO_ROUTES.preview, {
                body: { script: document.content, runEveryPage: true },
                fallbackError: 'The sample dashboard could not be previewed.',
            });
            if (hostContext.getActiveDoc() !== document) return;
            updateSnapshotPackageFromManifest(context, manifest);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.renderReportWorkflowChrome(document, hostContext.state.designerInstance?.getState?.() as any);
            if (hostContext.state.activeActivity === 'catalog' || hostContext.state.activeActivity === 'palette') {
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
        } catch (error) {
            _feedback.notify(errorMessage(error) || 'The sample dashboard could not be previewed.', {
                title: 'Sample data unavailable',
                tone: 'warning',
            });
        }
    }

    async function openCatalogReport(report: StudioDynamic, proj = 'split') {
        const pref = getStoredProjectionPreference();
        const effectiveProj = (pref === 'code' || pref === 'split') ? pref : proj;
        const existing = hostContext.state.documents.find(doc => doc.reportId === report.id);
        if (existing) {
            existing.projection = effectiveProj;
            if (!existing.lease?.acquired && hostContext.hasCapability('ScriptSave')) {
                void hostContext.leaseLifecycle.reacquire(existing as any, { silent: true });
            }
            await switchDoc(existing.id);
            return existing;
        }

        try {
            const opened = await hostContext.opts.onOpenDocument?.(report);
            if (!opened) return null;
            const newDoc = {
                ...opened,
                id: opened.id || `catalog-${report.id}`,
                reportId: report.id,
                path: opened.path || `${report.folderPath || ''}/${report.name}.rptsql`.replace(/^\//, ''),
                name: opened.name || `${report.name}.rptsql`,
                content: opened.content || '',
                isDirty: false,
                projection: effectiveProj
            };
            hostContext.state.documents.push(newDoc);
            await switchDoc(newDoc.id);
            if (newDoc.readOnlyReason) {
                _feedback.notify(newDoc.readOnlyReason, { title: 'Opened Read-only', tone: 'warning' });
            }
            hostContext.checkRecoverableDraft(newDoc);
            return newDoc;
        } catch (error) {
            _feedback.notify(errorMessage(error) || 'The catalog report could not be opened.', { title: 'Open Report Failed', tone: 'error' });
            return null;
        }
    }

    async function openWorkspaceFile(filePath: string, proj = 'split'): Promise<boolean> {
        const existing = hostContext.state.documents.find(d => d.path === filePath);
        if (existing) {
            existing.projection = proj;
            await switchDoc(existing.id);
            return true;
        }

        let content = '';
        let sourceRevision = null;
        let loadFailed = false;
        let failMessage = '';
        try {
            const res = await hostContext.authFetch(hostContext.apiBase + STUDIO_WORKSPACE_ROUTES.files + '?path=' + encodeURIComponent(filePath));
            if (res.ok) {
                const data = await res.json();
                content = data.content ?? '';
                sourceRevision = data.sourceRevision ?? null;
            } else {
                loadFailed = true;
                if (res.status === 404) {
                    failMessage = `File not found (404): ${filePath}`;
                } else if (res.status === 403) {
                    failMessage = `Access denied (403): ${filePath}`;
                } else {
                    failMessage = `Server error (${res.status}) opening ${filePath}`;
                }
            }
        } catch (e: unknown) {
            loadFailed = true;
            failMessage = `Network error opening ${filePath}: ${(e as { message?: string })?.message || 'Host unreachable'}`;
        }

        if (loadFailed) {
            _feedback.notify(
                `${failMessage}. The file was not opened to prevent overwriting.`,
                {
                    title: 'Open File Failed',
                    tone: 'error',
                    action: {
                        label: 'Retry',
                        onSelect: () => { void openWorkspaceFile(filePath, proj); }
                    }
                }
            );
            return false;
        }

        const newDoc: StudioRuntimeDocument = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: filePath,
            name: filePath.split('/').pop()?.split('\\').pop() || filePath,
            content: content,
            sourceRevision,
            isDirty: false,
            contentRevision: 0,
            projection: proj
        };

        hostContext.state.documents.push(newDoc);
        await switchDoc(newDoc.id);
        hostContext.checkRecoverableDraft(newDoc);
        return true;
    }

    return { switchDoc, closeDoc, handlePublishCatalogReport, createNewFile, openCatalogReport, openWorkspaceFile };
}
