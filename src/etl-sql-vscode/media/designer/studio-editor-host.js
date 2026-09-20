// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-editor-host.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-editor-host.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Script editor mounting and degraded-mode feedback.
 */
import { _feedback, queryElement } from './studio-context.js';
import { createScriptEditor } from './designer.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
export function createStudioEditorHost(hostContext) {
    let degradedBanner = null;
    function renderDegradedBanner() {
        if (!degradedBanner) {
            degradedBanner = document.createElement('div');
            degradedBanner.className = 'etlsql-studio-degraded-banner';
            degradedBanner.setAttribute('role', 'alert');
            degradedBanner.style.cssText = 'background: rgba(234, 179, 8, 0.15); border: 1px solid rgba(234, 179, 8, 0.4); border-radius: 4px; padding: 6px 10px; margin: 4px 8px; font-size: 12px; color: var(--portal-text, #f0f6fc); display: flex; align-items: center; justify-content: space-between; gap: 8px;';
            degradedBanner.innerHTML = `
                <span>⚠️ <strong>Degraded Editor Mode:</strong> CodeMirror could not be loaded. Running in basic textarea mode (syntax highlighting, lint diagnostics, and auto-completion are disabled).</span>
                <button type="button" class="etlsql-studio-btn etlsql-studio-btn-sm" data-action="retry-cm" style="padding: 2px 8px; font-size: 11px;">Retry</button>
            `;
            degradedBanner.querySelector('[data-action="retry-cm"]')?.addEventListener('click', () => {
                void mountScriptEditor();
            });
            hostContext.codeStage.insertBefore(degradedBanner, hostContext.editorHost);
        }
        degradedBanner.style.display = 'flex';
        const formatBtn = queryElement(hostContext.shell, '[data-action="code-format"]');
        if (formatBtn) {
            formatBtn.setAttribute('disabled', 'true');
            formatBtn.setAttribute('title', 'Formatting is unavailable in degraded textarea mode');
        }
    }
    function hideDegradedBanner() {
        if (degradedBanner)
            degradedBanner.style.display = 'none';
        const formatBtn = queryElement(hostContext.shell, '[data-action="code-format"]');
        if (formatBtn) {
            formatBtn.removeAttribute('disabled');
            formatBtn.setAttribute('title', 'Format Document');
        }
    }
    async function mountScriptEditor() {
        hostContext.editorHost.innerHTML = '';
        try {
            const activeDoc = hostContext.getActiveDoc();
            hostContext.state.editorInstance = await createScriptEditor(hostContext.editorHost, {
                value: activeDoc?.content || '',
                analyzeUrl: hostContext.apiBase + STUDIO_ROUTES.analyze,
                completeUrl: hostContext.apiBase + STUDIO_ROUTES.complete,
                hoverUrl: hostContext.apiBase + STUDIO_ROUTES.hover,
                // Studio owns the Messages surface, so the editor's own diagnostics panel stays off and
                // diagnostics are routed to the results panel instead of living only as gutter squiggles.
                diagnosticsPanel: false,
                onDiagnostics: (list) => {
                    const doc = hostContext.getActiveDoc();
                    if (doc)
                        hostContext.setDocumentDiagnostics(doc, list);
                },
                authFetch: hostContext.authFetch,
                documentUri: () => hostContext.getActiveDoc()?.path || 'untitled.rptsql',
                onChange: (newContent) => {
                    const doc = hostContext.getActiveDoc();
                    if (doc) {
                        const context = hostContext.documentContext(doc);
                        doc.content = newContent;
                        if (!hostContext.isSettingDocumentContent) {
                            doc.isDirty = true;
                            doc.contentRevision = (doc.contentRevision || 0) + 1;
                            hostContext.scheduleDraftSave(doc);
                        }
                        hostContext.renderTabs();
                        if (!hostContext.isSettingDocumentContent && !hostContext.isSyncingFromDesigner) {
                            clearTimeout(hostContext.codeMirrorDebounce || undefined);
                            if ((doc.path || '').endsWith('.etlsql')) {
                                hostContext.codeMirrorDebounce = setTimeout(() => {
                                    if (hostContext.getActiveDoc() === doc && doc.content === newContent)
                                        hostContext.renderVisualStage();
                                }, 400);
                            }
                            else if (hostContext.state.designerInstance) {
                                context.syncRevision++;
                                const revision = context.syncRevision;
                                context.previewAbort?.abort();
                                hostContext.state.designerInstance.invalidateScriptApply?.();
                                hostContext.codeMirrorDebounce = setTimeout(() => {
                                    void hostContext.synchronizeCodeToCanvas(doc, newContent, revision);
                                }, 400);
                            }
                        }
                    }
                }
            });
            hostContext.state.isEditorDegraded = false;
            hideDegradedBanner();
            if (activeDoc) {
                activeDoc.editorState = hostContext.state.editorInstance.getState?.();
            }
        }
        catch (e) {
            console.warn('[Studio] CodeMirror fallback', e);
            hostContext.state.isEditorDegraded = true;
            _feedback.notify?.('Script editor degraded: CodeMirror could not be loaded. Running in basic textarea mode.', { title: 'Editor Degraded', tone: 'warning' });
            renderDegradedBanner();
            const ta = document.createElement('textarea');
            ta.style.width = '100%';
            ta.style.height = '100%';
            ta.style.background = 'var(--portal-bg,#0d1117)';
            ta.style.color = 'var(--portal-text,#f0f6fc)';
            ta.style.fontFamily = 'monospace';
            ta.style.border = 'none';
            ta.style.padding = '12px';
            ta.value = hostContext.getActiveDoc()?.content || '';
            ta.oninput = () => {
                const doc = hostContext.getActiveDoc();
                if (doc) {
                    doc.content = ta.value;
                    doc.isDirty = true;
                    doc.contentRevision = (doc.contentRevision || 0) + 1;
                    hostContext.scheduleDraftSave(doc);
                    hostContext.renderTabs();
                    hostContext.renderVisualStage();
                }
            };
            hostContext.editorHost.appendChild(ta);
            hostContext.state.editorInstance = {
                getValue: () => ta.value,
                setValue: (v) => { ta.value = v; },
                gotoLine: () => { },
                // The textarea fallback can still show the author what a canvas gesture wrote, which is
                // the half of the loop that teaches. A no-op here would make the palette silently less
                // useful on exactly the hosts where CodeMirror failed to load.
                revealLines: (fromLine, toLine = fromLine) => {
                    const lines = ta.value.split('\n');
                    const first = Math.max(1, Math.min(lines.length, Number(fromLine) || 1));
                    const last = Math.max(first, Math.min(lines.length, Number(toLine) || first));
                    const from = lines.slice(0, first - 1).join('\n').length + (first > 1 ? 1 : 0);
                    const to = lines.slice(0, last).join('\n').length;
                    ta.focus();
                    ta.setSelectionRange(from, to);
                    return { from, to };
                },
                getSelection: () => ta.value.slice(ta.selectionStart, ta.selectionEnd),
                focus: () => ta.focus()
            };
        }
    }
    return { mountScriptEditor };
}
