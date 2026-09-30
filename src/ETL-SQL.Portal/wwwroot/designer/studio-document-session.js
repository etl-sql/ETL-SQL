// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-document-session.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-document-session.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Document switching, opening, creation, and catalog publication.
 */
import { _escapeHtml, _feedback, catalogExtension, errorMessage, getStoredProjectionPreference, queryElement } from './studio-context.js';
import { REPORT_WORKFLOW_TEMPLATES, STUDIO_ROUTES, STUDIO_STARTER_SCRIPTS, STUDIO_WORKSPACE_ROUTES } from './studio-contracts.js';
import { updateSnapshotPackageFromManifest } from './studio-data.js';
export function createStudioDocumentSession(hostContext) {
    async function switchDoc(docId) {
        const currentDoc = hostContext.getActiveDoc();
        if (currentDoc && hostContext.state.editorInstance) {
            const editor = hostContext.state.editorInstance;
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
            if (hostContext.getActiveDoc() !== newDoc)
                return;
            const context = hostContext.documentContext(newDoc);
            hostContext.homeStage.style.display = 'none';
            hostContext.paintResults(context);
            hostContext.updateSnapshotPackage(context.snapshot);
            hostContext.setProjection(newDoc.projection || 'split');
            if (hostContext.state.editorInstance) {
                hostContext.isSettingDocumentContent = true;
                try {
                    const editor = hostContext.state.editorInstance;
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
                    }
                    else {
                        editor.setValue(newDoc.content);
                    }
                }
                finally {
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
    async function closeDoc(docId) {
        const docIndex = hostContext.state.documents.findIndex(d => d.id === docId);
        if (docIndex < 0)
            return;
        const doc = hostContext.state.documents[docIndex];
        if (doc) {
            doc.editorState = null;
            hostContext.leaseLifecycle.removeDraft(doc);
        }
        if (doc.isDirty) {
            const saveBeforeClose = await _feedback.confirm(`Save changes to ${doc.name} before closing?`, {
                title: 'Unsaved Changes',
                confirmLabel: 'Yes',
                cancelLabel: 'No'
            });
            if (saveBeforeClose) {
                await hostContext.handleSave();
                if (doc.isDirty)
                    return;
            }
        }
        try {
            await hostContext.opts.onCloseDocument?.(doc, { keepalive: false });
        }
        catch (error) {
            console.warn('Failed to release the document lease:', error);
        }
        hostContext.state.documents.splice(docIndex, 1);
        if (hostContext.state.activeDocId === docId) {
            if (hostContext.state.documents.length > 0) {
                hostContext.state.activeDocId = hostContext.state.documents[Math.max(0, docIndex - 1)].id;
            }
            else {
                hostContext.state.activeDocId = '__home__';
            }
        }
        await switchDoc(hostContext.state.activeDocId);
    }
    async function promptForCatalogReport(noun = 'report') {
        if (!hostContext.state.catalogFolders.length) {
            // A dead end with no explanation is the worst first impression the Portal can give, so
            // say what is missing and who can grant it.
            _feedback.notify('Studio saves reports into catalog folders, and you do not have write access to any. '
                + 'Ask a Portal administrator to grant you Manage permission on a folder, then reopen Studio.', { title: 'No writable folder', tone: 'warning' });
            return null;
        }
        const defaultFolder = hostContext.state.catalogFolders[0];
        return new Promise(resolve => {
            const titleId = 'etlsql-catalog-create-title';
            hostContext.modalBox.innerHTML = `
                <h2 id="${titleId}">Create catalog ${_escapeHtml(noun)}</h2>
                <label>${_escapeHtml(noun.charAt(0).toUpperCase() + noun.slice(1))} name<input type="text" data-catalog-report-name value="Untitled ${_escapeHtml(noun)}" autocomplete="off"></label>
                <label>Folder<select data-catalog-report-folder>${hostContext.state.catalogFolders.map((folder) => `<option value="${_escapeHtml(folder.id)}">${_escapeHtml(folder.path || folder.name)}</option>`).join('')}</select></label>
                <div class="etlsql-studio-modal-actions">
                    <button type="button" class="etlsql-studio-btn" data-catalog-create-cancel>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-catalog-create-confirm>Create</button>
                </div>`;
            let settled = false;
            let cleanupModal = null;
            const finish = (value) => {
                if (settled)
                    return;
                settled = true;
                cleanupModal?.();
                hostContext.modalBox.innerHTML = '';
                resolve(value);
            };
            cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, () => finish(null), titleId);
            queryElement(hostContext.modalBox, '[data-catalog-create-cancel]').addEventListener('click', () => finish(null));
            queryElement(hostContext.modalBox, '[data-catalog-create-confirm]').addEventListener('click', () => {
                const name = queryElement(hostContext.modalBox, '[data-catalog-report-name]').value.trim();
                const folderId = queryElement(hostContext.modalBox, '[data-catalog-report-folder]').value || defaultFolder.id;
                if (!name)
                    return;
                finish({ name, folderId });
            });
            const nameInput = queryElement(hostContext.modalBox, '[data-catalog-report-name]');
            nameInput.focus();
            nameInput.select();
        });
    }
    async function handlePublishCatalogReport(targetDoc) {
        const doc = targetDoc || hostContext.getActiveDoc();
        if (!doc)
            return false;
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
        if (!request)
            return false;
        const currentContent = hostContext.state.editorInstance ? hostContext.state.editorInstance.getValue() : doc.content;
        doc.content = currentContent;
        const folder = hostContext.state.catalogFolders.find((f) => String(f.id) === String(request.folderId));
        const folderPath = folder ? (folder.path || folder.name) : '';
        try {
            const pipeline = (doc.path || '').toLowerCase().endsWith('.etlsql');
            const workflow = pipeline
                ? null
                : (doc.reportWorkflow || (doc.path?.endsWith('.paginated.rptsql') ? 'paginated' : 'dashboard'));
            const created = await hostContext.opts.onCreateDocument({
                ...request,
                type: pipeline ? 'pipeline' : 'report',
                kind: pipeline ? 'Pipeline' : 'Report',
                workflow,
                scriptText: hostContext.withLineEnding(doc.content, doc.lineEnding || '\n')
            });
            if (workflow)
                created.reportWorkflow = workflow;
            hostContext.state.catalogReports.push(created);
            doc.reportId = created.id;
            doc.version = created.version ?? 1;
            doc.sourceRevision = created.sourceRevision ?? null;
            doc.name = created.name;
            doc.path = `${folderPath}/${created.name}${catalogExtension(created)}`.replace(/^\//, '');
            doc.isDirty = false;
            hostContext.leaseLifecycle.removeDraft(doc);
            if (hostContext.opts.onRenewDocument) {
                const lease = await hostContext.opts.onRenewDocument(doc);
                doc.lease = lease;
            }
            hostContext.renderTabs();
            _feedback.notify(`Published report '${created.name}' to the catalog.`, { title: 'Report Published', tone: 'success' });
            return true;
        }
        catch (error) {
            _feedback.notify('Publish failed: ' + errorMessage(error), { title: 'Publish Failed', tone: 'error' });
            return false;
        }
    }
    async function createNewFile(type, { seed = false } = {}) {
        const reportWorkflow = type === 'paginated' ? 'paginated' : type === 'dashboard' || type === 'report' ? 'dashboard' : null;
        const isReportType = Boolean(reportWorkflow);
        if (hostContext.opts.onCreateDocument && !seed) {
            if (!isReportType && !hostContext.opts.catalogPipelines) {
                _feedback.notify('This catalog keeps Report-SQL (.rptsql) documents only.', { title: 'Create Document', tone: 'warning' });
                return;
            }
            if (!isReportType && hostContext.hasCapability('ReportPublish') && hostContext.hasCapability('ScriptSave')) {
                // A pipeline or query goes into the catalog as soon as it is named, as a report does.
                const request = await promptForCatalogReport(type === 'etl' ? 'pipeline' : 'query');
                if (!request)
                    return;
                try {
                    const created = await hostContext.opts.onCreateDocument({ ...request, type: 'pipeline', kind: 'Pipeline', workflow: null, scriptText: '' });
                    hostContext.state.catalogReports.push(created);
                    await openCatalogReport(created, type === 'etl' ? 'split' : 'code');
                }
                catch (error) {
                    _feedback.notify(errorMessage(error) || 'The pipeline could not be created.', { title: 'Create Pipeline Failed', tone: 'error' });
                }
                return;
            }
            if (!hostContext.hasCapability('ReportPublish')) {
                // Separate learning/drafting from publishing:
                // An author without ReportPublish is not blocked; they can design, preview, and test in private practice draft mode.
                _feedback.notify('Opening in private draft mode. You can design, preview, and test reports; publishing to the catalog requires the ReportPublish capability.', { title: 'Practice Draft Mode', tone: 'info' });
            }
            else if (!hostContext.hasCapability('ScriptSave')) {
                _feedback.notify('Creating a catalog report requires the ScriptSave capability. Ask a Portal administrator to grant it, '
                    + 'or open an existing report to explore Studio in the meantime.', { title: 'Create Report', tone: 'warning' });
                return;
            }
            else {
                const request = await promptForCatalogReport();
                if (!request)
                    return;
                try {
                    const workflowKey = (reportWorkflow || 'dashboard');
                    const scriptText = REPORT_WORKFLOW_TEMPLATES[workflowKey];
                    const created = await hostContext.opts.onCreateDocument({ ...request, type: 'report', workflow: reportWorkflow, scriptText });
                    created.reportWorkflow = reportWorkflow;
                    hostContext.state.catalogReports.push(created);
                    const pref = getStoredProjectionPreference();
                    const targetProj = (pref === 'code' || pref === 'split') ? pref : (reportWorkflow === 'dashboard' ? 'canvas' : 'split');
                    await openCatalogReport(created, targetProj);
                    return;
                }
                catch (error) {
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
            const workflowKey = (reportWorkflow || 'dashboard');
            content = seed ? STUDIO_STARTER_SCRIPTS.report : REPORT_WORKFLOW_TEMPLATES[workflowKey];
            // A new dashboard opens on the canvas it is about to be built on. Splitting the window
            // with a script the author has not written yet teaches the wrong first lesson — the
            // script is the escape hatch, and the projection buttons keep it one click away.
            // If the author has learned and expressed a preference for 'code' or 'split' view,
            // respect that preference.
            const pref = getStoredProjectionPreference();
            if (pref === 'code' || pref === 'split') {
                proj = pref;
            }
            else {
                proj = reportWorkflow === 'dashboard' ? 'canvas' : 'split';
            }
        }
        else if (type === 'etl') {
            path = `untitled_pipeline_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.etl : '';
            proj = 'split';
        }
        else {
            path = `untitled_query_${etlCount}.etlsql`;
            content = seed ? STUDIO_STARTER_SCRIPTS.sql : '';
            proj = 'code';
        }
        const newDoc = {
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
        if (seed && isReportType)
            await hydrateSeededReport(newDoc);
    }
    async function hydrateSeededReport(document) {
        if (!document || hostContext.getActiveDoc() !== document)
            return;
        const context = hostContext.documentContext(document);
        try {
            const manifest = await hostContext.authoringRequest(STUDIO_ROUTES.preview, {
                body: { script: document.content, runEveryPage: true },
                fallbackError: 'The sample dashboard could not be previewed.',
            });
            if (hostContext.getActiveDoc() !== document)
                return;
            updateSnapshotPackageFromManifest(context, manifest);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.renderReportWorkflowChrome(document, hostContext.state.designerInstance?.getState?.());
            if (hostContext.state.activeActivity === 'catalog' || hostContext.state.activeActivity === 'palette') {
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The sample dashboard could not be previewed.', {
                title: 'Sample data unavailable',
                tone: 'warning',
            });
        }
    }
    async function openCatalogReport(report, proj = 'split') {
        const pref = getStoredProjectionPreference();
        const effectiveProj = (pref === 'code' || pref === 'split') ? pref : proj;
        const existing = hostContext.state.documents.find(doc => doc.reportId === report.id);
        if (existing) {
            existing.projection = effectiveProj;
            if (!existing.lease?.acquired && hostContext.hasCapability('ScriptSave')) {
                void hostContext.leaseLifecycle.reacquire(existing, { silent: true });
            }
            await switchDoc(existing.id);
            return existing;
        }
        try {
            const opened = await hostContext.opts.onOpenDocument?.(report);
            if (!opened)
                return null;
            const newDoc = {
                ...opened,
                id: opened.id || `catalog-${report.id}`,
                reportId: report.id,
                path: opened.path || `${report.folderPath || ''}/${report.name}${catalogExtension(report)}`.replace(/^\//, ''),
                name: opened.name || `${report.name}${catalogExtension(report)}`,
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
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The catalog report could not be opened.', { title: 'Open Report Failed', tone: 'error' });
            return null;
        }
    }
    async function openWorkspaceFile(filePath, proj = 'split') {
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
            }
            else {
                loadFailed = true;
                if (res.status === 404) {
                    failMessage = `File not found (404): ${filePath}`;
                }
                else if (res.status === 403) {
                    failMessage = `Access denied (403): ${filePath}`;
                }
                else {
                    failMessage = `Server error (${res.status}) opening ${filePath}`;
                }
            }
        }
        catch (e) {
            loadFailed = true;
            failMessage = `Network error opening ${filePath}: ${e?.message || 'Host unreachable'}`;
        }
        if (loadFailed) {
            _feedback.notify(`${failMessage}. The file was not opened to prevent overwriting.`, {
                title: 'Open File Failed',
                tone: 'error',
                action: {
                    label: 'Retry',
                    onSelect: () => { void openWorkspaceFile(filePath, proj); }
                }
            });
            return false;
        }
        const newDoc = {
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
