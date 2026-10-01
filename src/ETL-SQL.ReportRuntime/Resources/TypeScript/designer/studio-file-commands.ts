/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Save, Git diff, formatting, connection wizard, and exit commands.
 */

import { _escapeHtml, _feedback, _isUntitledPath, _studioIcon, errorMessage, queryElement, queryElements } from './studio-context.js';
import type { LeaseDocument } from './studio-lifecycle.js';

import { createConnectionWizard } from './connection-wizard.js';
import { declaredConnectionNames } from './studio-authoring.js';
import type { StudioDom, StudioDomElement, StudioDynamic, StudioOptions, StudioRuntimeContext, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import { buildSideBySideDiff } from './studio-git-diff.js';
import type { StudioLeaseLifecycleHandle } from './studio-lifecycle.js';
import { detectPlaintextSecrets as _detectPlaintextSecrets, secureStudioScriptForSave } from './studio-security.js';

export interface StudioFileCommandsContext {
    readonly activeScriptText: () => any;
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly documentContext: (document?: StudioRuntimeDocument | null) => StudioRuntimeContext;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly handlePublishCatalogReport: (targetDoc?: StudioRuntimeDocument | null) => Promise<boolean>;
    readonly hasCapability: (capability: string) => boolean;
    readonly hasGitHost: boolean;
    readonly leaseLifecycle: StudioLeaseLifecycleHandle<LeaseDocument>;
    readonly modalBackdrop: StudioDomElement;
    readonly modalBox: StudioDomElement;
    readonly opts: StudioOptions;
    readonly renderTabs: () => void;
    readonly renderVisualStage: () => void;
    readonly scheduleDraftSave: (doc: StudioRuntimeDocument | null | undefined) => void;
    readonly setupModalAccessibility: (box: HTMLElement, backdrop: HTMLElement, onClose: () => void, titleId?: string) => () => void;
    readonly shell: StudioDom;
    readonly sidebarContent: StudioDomElement;
    readonly sidebarTitle: StudioDomElement;
    readonly state: StudioRuntimeState;
}

export function createStudioFileCommands(hostContext: StudioFileCommandsContext) {
    let gitRenderRevision = 0;
    const pendingSaves = new WeakMap<StudioRuntimeDocument, Promise<boolean>>();

    async function renderGitSidebar() {
        hostContext.sidebarTitle.textContent = 'Source Control';
        const document = hostContext.getActiveDoc();
        const revision = ++gitRenderRevision;
        if (!hostContext.hasGitHost) {
            hostContext.sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" data-capability-state="git" role="status">
                    <span class="etlsql-studio-capability-label">Host capability</span>
                    <strong>Source control is unavailable</strong>
                    <p>This Studio host does not provide Git status or source-control actions.</p>
                </div>`;
            return;
        }
        if (!document || _isUntitledPath(document.path)) {
            hostContext.sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" role="status">
                    <span class="etlsql-studio-capability-label">Git diff</span>
                    <strong>Open a saved script</strong>
                    <p>Select a workspace script to compare it with Git history.</p>
                </div>`;
            return;
        }

        hostContext.sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading local Git history…</div>';
        try {
            const [status, history] = await Promise.all([
                hostContext.opts.onLoadGitStatus ? hostContext.opts.onLoadGitStatus() : Promise.resolve(null),
                hostContext.opts.onLoadGitHistory ? hostContext.opts.onLoadGitHistory(document) : Promise.resolve(null),
            ]);
            if (revision !== gitRenderRevision || hostContext.state.activeActivity !== 'git') return;
            if (!status?.isGitRepository || !history?.isGitRepository) {
                hostContext.sidebarContent.innerHTML = `
                    <div class="etlsql-studio-capability-state" role="status">
                        <span class="etlsql-studio-capability-label">Git diff</span>
                        <strong>No Git repository found</strong>
                        <p>Initialize Git and commit this script to enable revision comparisons.</p>
                    </div>`;
                return;
            }

            const changes = (status.modified?.length || 0) + (status.untracked?.length || 0) + (status.staged?.length || 0);
            hostContext.sidebarContent.innerHTML = `
                <div class="etlsql-studio-git-summary">
                    <span class="etlsql-studio-git-branch">${_studioIcon('git', 13)} ${_escapeHtml(status.branch || 'detached HEAD')}</span>
                    <span>${changes} change${changes === 1 ? '' : 's'}</span>
                </div>
                <div class="etlsql-sidebar-section-header"><span>Compare ${_escapeHtml(document.name)}</span></div>
                <button type="button" class="etlsql-studio-git-revision is-head" data-git-revision="HEAD">
                    <span class="etlsql-studio-git-revision-title">Working tree vs HEAD</span>
                    <span>Includes unsaved editor changes</span>
                </button>
                <div class="etlsql-sidebar-section-header"><span>Local history</span></div>
                <div class="etlsql-studio-git-history">
                    ${(history.entries || []).map((entry: StudioDynamic) => `
                        <button type="button" class="etlsql-studio-git-revision" data-git-revision="${_escapeHtml(entry.revision)}">
                            <span class="etlsql-studio-git-revision-title"><code>${_escapeHtml(entry.shortRevision)}</code> ${_escapeHtml(entry.subject)}</span>
                            <span>${_escapeHtml(entry.author)} · ${_escapeHtml(formatGitDate(entry.authoredAt))}</span>
                        </button>`).join('') || '<p class="etlsql-studio-git-empty">No commits contain this script yet.</p>'}
                </div>`;
            queryElements(hostContext.sidebarContent, '[data-git-revision]').forEach(button => {
                button.addEventListener('click', () => void openGitDiff(document, button.dataset.gitRevision));
            });
        } catch (error) {
            if (revision !== gitRenderRevision) return;
            hostContext.sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Git history could not be loaded</strong><p>${_escapeHtml(errorMessage(error) || 'Try the comparison again.')}</p></div>`;
        }
    }

    function formatGitDate(value: any) {
        if (!value) return '';
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? String(value || '') : date.toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' });
    }

    async function openGitDiff(document: StudioRuntimeDocument, revision: string) {
        const currentContent = hostContext.state.editorInstance && hostContext.getActiveDoc() === document
            ? hostContext.state.editorInstance.getValue()
            : document.content;
        const titleId = 'etlsql-git-diff-title';
        let cleanupModal: (() => void) | null = null;
        const closeGitDiff = () => {
            cleanupModal?.();
            hostContext.modalBox.innerHTML = '';
            hostContext.modalBox.classList.remove('etlsql-studio-git-diff-modal');
        };
        hostContext.modalBox.classList.add('etlsql-studio-git-diff-modal');
        cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, closeGitDiff, titleId);
        hostContext.modalBox.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Building comparison…</div>';
        try {
            if (!hostContext.opts.onLoadGitDiff) return;
            const comparison = await hostContext.opts.onLoadGitDiff(document, revision, currentContent);
            if (!comparison) return;
            const rows = buildSideBySideDiff(comparison.baselineContent, comparison.workingContent);
            hostContext.modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header etlsql-studio-git-diff-header">
                    <div><strong id="${titleId}">${_escapeHtml(comparison.path)}</strong><span>${rows.filter(row => row.kind !== 'equal').length} changed line${rows.filter(row => row.kind !== 'equal').length === 1 ? '' : 's'}</span></div>
                    <button type="button" class="etlsql-studio-sidebar-close" data-git-diff-close aria-label="Close Git comparison">${_studioIcon('close', 14)}</button>
                </div>
                <div class="etlsql-studio-git-diff-labels" aria-hidden="true">
                    <span>${_escapeHtml(comparison.baselineLabel)}</span><span>Working tree${document.isDirty ? ' · unsaved' : ''}</span>
                </div>
                <div class="etlsql-studio-git-diff-grid" role="table" aria-label="Side-by-side Git comparison">
                    ${rows.map(row => `<div class="etlsql-studio-git-diff-row is-${row.kind}" role="row">
                        <div class="etlsql-studio-git-diff-cell is-left" role="cell"><span class="etlsql-studio-git-line-number">${row.leftNumber ?? ''}</span><code>${_escapeHtml(row.leftText)}</code></div>
                        <div class="etlsql-studio-git-diff-cell is-right" role="cell"><span class="etlsql-studio-git-line-number">${row.rightNumber ?? ''}</span><code>${_escapeHtml(row.rightText)}</code></div>
                    </div>`).join('')}
                </div>`;
            queryElement(hostContext.modalBox, '[data-git-diff-close]').addEventListener('click', closeGitDiff);
            queryElement(hostContext.modalBox, '[data-git-diff-close]').focus();
        } catch (error) {
            hostContext.modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header"><strong id="${titleId}">Comparison unavailable</strong><button type="button" class="etlsql-studio-sidebar-close" data-git-diff-close aria-label="Close">${_studioIcon('close', 14)}</button></div>
                <div class="etlsql-studio-modal-body"><p>${_escapeHtml(errorMessage(error) || 'Git could not build this comparison.')}</p></div>`;
            queryElement(hostContext.modalBox, '[data-git-diff-close]').addEventListener('click', closeGitDiff);
            queryElement(hostContext.modalBox, '[data-git-diff-close]').focus();
        }
    }

    async function handleSave() {
        const doc = hostContext.getActiveDoc();
        if (!doc) return;
        if (doc.canSave === false || doc.readOnlyReason) {
            _feedback.notify(doc.readOnlyReason || 'This document is read-only.', { title: 'Save Unavailable', tone: 'warning' });
            return false;
        }
        if (_isUntitledPath(doc.path)) {
            if (hostContext.opts.onCreateDocument) {
                if (hostContext.hasCapability('ReportPublish')) {
                    const shouldPublish = await _feedback.confirm(
                        'This document is an unpublished draft. Would you like to publish it to the Portal Catalog now?',
                        { title: 'Publish Draft', confirmLabel: 'Publish to Catalog', cancelLabel: 'Save Local Draft' });
                    if (shouldPublish) {
                        return hostContext.handlePublishCatalogReport(doc);
                    }
                }
                // A report the catalog has not seen has nowhere to be kept yet; saying it was saved
                // would be how it gets lost. It stays unsaved in this tab until it is published.
                _feedback.notify('This report is not in the catalog yet, so it cannot be kept anywhere else. Keep this tab open, or publish it to the catalog to save it.',
                    { title: 'Not Saved', tone: 'warning' });
                return false;
            }

            const defaultExtension = doc.path.endsWith('.etlsql') ? '.etlsql' : doc.path.endsWith('.sql') ? '.sql' : '.rptsql';
            let saveName = await _feedback.prompt('Choose the filename to save in this workspace.', { title: 'Save as', label: 'Filename', value: doc.name, required: true, confirmLabel: 'Save' });
            if (!saveName?.trim()) return false;
            saveName = saveName.trim();
            if (!/\.(?:rptsql|etlsql|sql)$/i.test(saveName)) saveName += defaultExtension;
            doc.path = saveName;
            doc.name = saveName.split(/[\\/]/).pop();
        }
        const currentContent = hostContext.state.editorInstance ? hostContext.state.editorInstance.getValue() : doc.content;
        doc.content = currentContent;

        const secrets = _detectPlaintextSecrets(currentContent);
        if (secrets.length > 0) {
            const titleId = 'etlsql-secret-modal-title';
            hostContext.modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <span id="${titleId}" style="color:var(--portal-warning,#d29922); display:flex; align-items:center; gap:6px;">
                        ⚠️ Plaintext Secret Detected
                    </span>
                    <button type="button" class="etlsql-studio-sidebar-close" data-modal-close aria-label="Close">${_studioIcon('close', 12)}</button>
                </div>
                <div class="etlsql-studio-modal-body">
                    <p style="font-size:0.8125rem; color:var(--portal-text-soft,#8b949e); margin:0 0 12px;">
                        Found <strong>${secrets.length} plaintext credentials</strong> in script. ETL-SQL zero-trust policy requires encrypting credentials before commit or save.
                    </p>
                    <div style="background:rgba(210,153,34,0.1); border:1px solid rgba(210,153,34,0.3); border-radius:6px; padding:10px; font-size:0.75rem; font-family:monospace; margin-bottom:12px; max-height:100px; overflow:auto;">
                        ${secrets.map((s, index) => `<div>${index + 1}. ${_escapeHtml(s.label)} <span style="color:var(--portal-warning,#d29922);">(value hidden)</span></div>`).join('')}
                    </div>
                    <label style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e); font-weight:600; display:block; margin-bottom:4px;">
                        Enter Passphrase to Encrypt as <code>ENC:...</code>:
                    </label>
                    <input type="password" data-encrypt-passphrase placeholder="Passphrase" style="width:100%; box-sizing:border-box; background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.8125rem;">
                </div>
                <div class="etlsql-studio-modal-footer">
                    <button type="button" class="etlsql-studio-btn" data-modal-close>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-modal-encrypt>Encrypt & Save</button>
                </div>
            `;

            let cleanupModal: (() => void) | null = null;
            const closeModal = () => {
                cleanupModal?.();
                hostContext.modalBox.innerHTML = '';
            };

            cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, closeModal, titleId);
            queryElements(hostContext.modalBox, '[data-modal-close]').forEach(b => b.addEventListener('click', closeModal));

            queryElement(hostContext.modalBox, '[data-modal-encrypt]').addEventListener('click', async () => {
                const pass = queryElement<HTMLInputElement>(hostContext.modalBox, '[data-encrypt-passphrase]').value;
                if (!pass) {
                    _feedback.notify('Enter a passphrase to encrypt credentials.', { title: 'Passphrase Required', tone: 'warning' });
                    return;
                }

                const encryptButton = queryElement<HTMLButtonElement>(hostContext.modalBox, '[data-modal-encrypt]');
                encryptButton.disabled = true;
                encryptButton.setAttribute('aria-busy', 'true');
                try {
                    const encryptedScript = await secureStudioScriptForSave(currentContent, pass);
                    if (hostContext.state.editorInstance) hostContext.state.editorInstance.setValue(encryptedScript);
                    doc.content = encryptedScript;
                    closeModal();
                    await performSave(doc.content, doc.path);
                } catch (error) {
                    doc.isDirty = true;
                    hostContext.renderTabs();
                    _feedback.notify('Encryption failed: ' + errorMessage(error), { title: 'Script Not Saved', tone: 'error' });
                } finally {
                    encryptButton.disabled = false;
                    encryptButton.removeAttribute('aria-busy');
                }
            });
            queryElement<HTMLInputElement>(hostContext.modalBox, '[data-encrypt-passphrase]')?.focus();
            return;
        }

        return performSave(doc.content, doc.path);
    }

    /**
     * The line ending a document arrived with.
     *
     * CodeMirror normalises every document to a bare LF, so a file the author wrote with CRLF
     * comes back out of the editor with none of its endings intact. Saving that text rewrote
     * every line of the file on the first save - a whole-file diff, which is exactly what
     * Studio's own Git view then had to show, and what a reviewer had to read past. The ending
     * belongs to the file rather than to the editor, so it is recorded when the document is
     * opened and put back when it is written.
     *
     * A mixed file is decided by whichever ending dominates, because it has to be written one
     * way and the majority is the one that produces the smaller diff.
     */
    function documentLineEnding(text: string) {
        const source = String(text || '');
        const crlf = (source.match(/\r\n/g) || []).length;
        if (crlf === 0) return '\n';
        const total = (source.match(/\n/g) || []).length;
        return crlf * 2 >= total ? '\r\n' : '\n';
    }

    /**
     * Records a document's endings before the editor is allowed to normalise them away.
     *
     * Called from both places a document is first shown - the tab switch and the bootstrap that
     * opens the file the host was launched on - because the second one does not go through the
     * first, and it is the file the author is most likely to save.
     */
    function rememberLineEnding(doc: StudioRuntimeDocument) {
        if (doc && !doc.lineEnding) doc.lineEnding = documentLineEnding(doc.content);
    }

    function withLineEnding(text: string, ending: string) {
        const normalized = String(text || '').replace(/\r\n/g, '\n');
        return ending === '\r\n' ? normalized.replace(/\n/g, '\r\n') : normalized;
    }

    async function performSave(content: string, path: string) {
        const doc = hostContext.getActiveDoc();
        const previousSave = doc ? pendingSaves.get(doc) : undefined;
        const save = (async () => {
            if (previousSave) await previousSave;
            try {
                const savedState = hostContext.opts.onSave
                    ? await hostContext.opts.onSave(withLineEnding(content, doc?.lineEnding || '\n'), path, doc)
                    : null;
                if (doc) {
                    // COMPAT_BREAK: 0.20.0 — completion owns only the content submitted by this save.
                    const newerEdits = doc.content !== content || doc.path !== path;
                    const current = { content: doc.content, path: doc.path, name: doc.name };
                    if (savedState && typeof savedState === 'object') Object.assign(doc, savedState);
                    if (newerEdits) {
                        Object.assign(doc, current);
                        doc.isDirty = true;
                    } else {
                        doc.isDirty = false;
                        hostContext.leaseLifecycle.removeDraft(doc as any);
                    }
                }
                hostContext.renderTabs();
                _feedback.notify(`Saved ${path}${doc?.isDirty ? '; newer edits remain unsaved.' : ''}`, { title: 'File Saved', tone: 'success' });
                return true;
            } catch (error) {
                if (doc) doc.isDirty = true;
                hostContext.renderTabs();
                _feedback.notify('Save failed: ' + errorMessage(error), { title: 'File Not Saved', tone: 'error' });
                return false;
            }
        })();
        if (doc) pendingSaves.set(doc, save);
        try {
            return await save;
        } finally {
            if (doc && pendingSaves.get(doc) === save) pendingSaves.delete(doc);
        }
    }

    // Connection aliases already declared in the active document. Read from the script text rather
    // than the design state, because CREATE CONNECTION is exactly the kind of statement the design
    // state does not model.
    function handleOpenConnectionWizard({ onDone = null }: { onDone?: ((alias: string) => void) | null } = {}) {
        createConnectionWizard({
            // Without these the wizard cannot detect a collision or pick a free alias, so it would
            // happily suggest a name the script already uses.
            existingNames: declaredConnectionNames(hostContext.activeScriptText()),
            fetchSchemas: async () => {
                const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.connectorsSchema);
                if (!response.ok) throw new Error('Connector types could not be loaded.');
                return response.json();
            },
            onInsert: (sql, metadata) => {
                const doc = hostContext.getActiveDoc();
                if (!doc) return;
                const nextScript = `${sql.trim()}\n${hostContext.state.editorInstance?.getValue?.() || doc.content}`;
                doc.content = nextScript;
                doc.isDirty = true;
                hostContext.scheduleDraftSave(doc);
                if (hostContext.state.editorInstance) {
                    hostContext.state.editorInstance.setValue(nextScript);
                }
                hostContext.renderTabs();
                hostContext.renderVisualStage();
                _feedback.notify(`Created connection ${metadata.alias}`, { title: 'Connection Created', tone: 'success' });
                onDone?.(metadata.alias);
            }
        });
    }

    async function handleFormatDocument() {
        if (hostContext.state.isEditorDegraded) {
            _feedback.notify('Code formatting is unavailable in degraded textarea mode.', { title: 'Format Unavailable', tone: 'warning' });
            return;
        }
        const doc = hostContext.getActiveDoc();
        if (!doc || !hostContext.state.editorInstance) return;
        const before = (hostContext.state.editorInstance as StudioDynamic).getValue();
        try {
            const res = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.format, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: before, documentUri: doc.path || null })
            });
            if (!res.ok) {
                _feedback.notify(`The formatter is unavailable on this host (${res.status}).`, { title: 'Format Failed', tone: 'error' });
                return;
            }
            const data = await res.json();
            // Both hosts return { script, diagnostics }. Reading any other field silently no-ops,
            // which is how this path previously reported success while changing nothing.
            const formatted = typeof data?.script === 'string' ? data.script : null;
            const reason = data?.diagnostics?.[0]?.message || data?.diagnostics?.[0];
            if (formatted === null) {
                _feedback.notify('The formatter returned no script.', { title: 'Format Failed', tone: 'error' });
                return;
            }
            if (reason) {
                _feedback.notify(String(reason), { title: 'Document Not Formatted', tone: 'warning' });
                return;
            }
            if (formatted === before) {
                _feedback.notify('Already formatted — no changes needed.', { title: 'Document Formatted', tone: 'info' });
                return;
            }
            (hostContext.state.editorInstance as StudioDynamic).setValue(formatted);
            doc.content = formatted;
            doc.isDirty = true;
            doc.contentRevision = ((doc.contentRevision as number) || 0) + 1;
            hostContext.renderTabs();
            _feedback.notify('Formatted document', { title: 'Document Formatted', tone: 'success' });
        } catch (e) {
            _feedback.notify('Format failed: ' + errorMessage(e), { title: 'Format Failed', tone: 'error' });
        }
    }

    async function handleExitStudio() {
        if (!hostContext.opts.onExit) return;
        const activeRuns = hostContext.state.documents.filter(doc => hostContext.documentContext(doc).runActive);
        const dirtyDocuments = hostContext.state.documents.filter(doc => doc.isDirty);
        if (activeRuns.length) {
            const cancelRuns = await _feedback.confirm(
                `Cancel ${activeRuns.length} active run${activeRuns.length === 1 ? '' : 's'} and exit Studio?`,
                { title: 'Active Runs', confirmLabel: 'Cancel Runs & Exit', cancelLabel: 'Stay' });
            if (!cancelRuns) return;
            activeRuns.forEach(doc => hostContext.documentContext(doc).runAbort?.abort());
        }
        if (dirtyDocuments.length) {
            const discard = await _feedback.confirm(
                `${dirtyDocuments.length} document${dirtyDocuments.length === 1 ? ' has' : 's have'} unsaved changes. Exit without saving?`,
                { title: 'Unsaved Documents', confirmLabel: 'Exit Without Saving', cancelLabel: 'Stay' });
            if (!discard) return;
        }

        const exitButton = queryElement(hostContext.shell, '[data-action="exit"]');
        if (exitButton) {
            exitButton.disabled = true;
            exitButton.setAttribute('aria-busy', 'true');
        }
        try {
            const stopped = await hostContext.opts.onExit({
                force: activeRuns.length > 0 || dirtyDocuments.length > 0,
                activeRuns: activeRuns.length,
                dirtyDocuments: dirtyDocuments.length,
            });
            _feedback.notify(stopped ? 'The Studio host stopped.' : 'The Studio host did not stop before the timeout.', {
                title: stopped ? 'Studio Stopped' : 'Shutdown Incomplete',
                tone: stopped ? 'success' : 'warning',
            });
        } catch (error) {
            _feedback.notify(errorMessage(error) || 'Studio shutdown failed.', { title: 'Exit Failed', tone: 'error' });
        } finally {
            if (exitButton) {
                exitButton.disabled = false;
                exitButton.removeAttribute('aria-busy');
            }
        }
    }

    return { renderGitSidebar, handleSave, rememberLineEnding, withLineEnding, handleOpenConnectionWizard, handleFormatDocument, handleExitStudio };
}
