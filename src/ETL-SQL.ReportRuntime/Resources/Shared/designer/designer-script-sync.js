/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-script-sync.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Script editor synchronization, parsing, and preview lifecycle.
 */
import { feedback, queryElement } from './designer-context.js';
import { createScriptEditorWorkbench } from './script-workbench.js';
export function createDesignerScriptSync(context) {
    let scriptApplySequence = 0;
    let suppressScriptSync = 0;
    let scriptSyncVersion = 0;
    let scriptEditor = null;
    function selectVisualInEditor(visualName) {
        const view = scriptEditor?.editor?.view;
        if (!context.isSplitActive || !view)
            return;
        const text = view.state.doc.toString();
        const patterns = [
            `CREATE VISUAL ${visualName}`,
            `CREATE CONTAINER ${visualName}`,
            `CREATE BUTTON ${visualName}`
        ];
        let foundIdx = -1;
        let matchLength = 0;
        for (const pattern of patterns) {
            const regex = new RegExp(`\\b${pattern.replace(/[-/\\^$*+?.()|[\]{}]/g, '\\$&')}\\b`, 'i');
            const match = text.match(regex);
            if (match && match.index !== undefined) {
                foundIdx = match.index;
                matchLength = match[0].length;
                break;
            }
        }
        if (foundIdx !== -1) {
            const from = foundIdx;
            const to = foundIdx + matchLength;
            view.dispatch({
                selection: { anchor: from, head: to },
                scrollIntoView: true
            });
        }
    }
    function handleEditorCursorActivity(pos, text) {
        if (!context.isSplitActive)
            return;
        clearTimeout(context.cursorTimeout ?? undefined);
        context.cursorTimeout = setTimeout(() => {
            const regex = /\bCREATE\s+(VISUAL|CONTAINER|BUTTON)\s+(\w+)/gi;
            let match;
            let activeVisualName = null;
            let bestDistance = Infinity;
            while ((match = regex.exec(text)) !== null) {
                const matchIndex = match.index;
                if (matchIndex <= pos) {
                    const distance = pos - matchIndex;
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        activeVisualName = match[2];
                    }
                }
            }
            if (activeVisualName) {
                const v = context.curVis().find(vis => String(vis.name).toUpperCase() === activeVisualName.toUpperCase());
                if (v && v.id !== context.selVisualId) {
                    context.selectVisual(v.id, { skipEditorSync: true });
                }
            }
        }, 100);
    }
    /**
     * The script as it is *now*.
     *
     * The host owns the buffer in Studio — the designer has no editor of its own there — so it must be
     * asked, not remembered. Falling back to `opts.script` meant every canvas write-back patched the
     * text as it stood when the designer mounted, silently discarding anything added since: the
     * CREATE CONNECTION the connection wizard wrote, the CREATE DATASET the data wizard wrote, and any
     * hand edit. Adding a visual then handed that stale result back as the new buffer.
     */
    function currentScriptText() {
        if (typeof context.opts.getScript === 'function') {
            const live = context.opts.getScript();
            if (typeof live === 'string')
                return live;
        }
        return scriptEditor ? scriptEditor.getValue() : (context.opts.script || context.opts.initialScript || '');
    }
    async function syncScriptFromGrid(requestVersion) {
        try {
            const currentScript = currentScriptText();
            const r = await context.apiJson('/api/designer/generate', 'POST', { designState: context.state, script: currentScript });
            if (requestVersion !== scriptSyncVersion)
                return;
            if (r?.script) {
                if (typeof context.opts.onScriptChange === 'function') {
                    context.opts.onScriptChange(r.script);
                }
                if (context.isSplitActive && scriptEditor && r.script !== currentScript) {
                    const view = scriptEditor.editor.view;
                    if (!view)
                        return;
                    const prevSel = view.state.selection.main;
                    scriptEditor.editor.setValue(r.script);
                    try {
                        const newLen = view.state.doc.length;
                        const anchor = Math.min(prevSel.anchor, newLen);
                        const head = Math.min(prevSel.head, newLen);
                        view.dispatch({ selection: { anchor, head } });
                    }
                    catch {
                        // Restoring the caret is best-effort: the regenerated document may
                        // have no position corresponding to the old one.
                    }
                }
            }
        }
        catch {
            // A failed regenerate leaves the script as it was. The grid and the script are
            // then out of step until the next edit, and nothing here says so — surfacing it
            // needs somewhere in the workbench UI to say it, which this does not have.
        }
    }
    /**
     * Re-render after ingesting script text, without writing the script back.
     *
     * The canvas regenerates its script from `state` alone, so anything the design state does not
     * model — a bare CREATE CONNECTION, hand-authored SQL that did not round-trip — is absent from
     * the regenerated text. Letting that regeneration run in response to the editor's own content
     * meant typing into the script pane produced: text -> canvas -> regenerate -> overwrite the text
     * the author had just typed. A canvas update caused *by* the script must never write back to it.
     */
    function renderAllFromScript() {
        suppressScriptSync++;
        try {
            context.renderAll();
        }
        finally {
            suppressScriptSync--;
        }
    }
    function syncScriptFromGridDebounced() {
        if (suppressScriptSync > 0)
            return;
        if (!context.isSplitActive && !scriptEditor && typeof context.opts.onScriptChange !== 'function')
            return;
        const requestVersion = ++scriptSyncVersion;
        clearTimeout(context.syncTimeout ?? undefined);
        context.syncTimeout = setTimeout(() => syncScriptFromGrid(requestVersion), 400);
    }
    // ── Script overlay ────────────────────────────────────────────────────────
    async function openScript() {
        let text;
        try {
            const currentScript = currentScriptText() || null;
            const r = await context.apiJson('/api/designer/generate', 'POST', { designState: context.state, script: currentScript });
            text = r?.script ?? '';
        }
        catch {
            text = '-- Failed to generate script\n';
        }
        context.scriptOverlay.classList.add('active');
        queryElement(context.topbar, '#dsgn-design-mode')?.classList.remove('active');
        queryElement(context.topbar, '#dsgn-design-mode')?.setAttribute('aria-selected', 'false');
        queryElement(context.topbar, '#dsgn-code-mode')?.classList.add('active');
        queryElement(context.topbar, '#dsgn-code-mode')?.setAttribute('aria-selected', 'true');
        const host = queryElement(context.scriptOverlay, '#dsgn-script-workbench-host');
        host.innerHTML = '';
        scriptEditor = await createScriptEditorWorkbench(host, {
            title: 'Script',
            authFetch: context._fetch,
            // The Portal has no file workspace (its catalog is folders/reports) and git
            // write-back is a separate roadmap item, so only schema + session are enabled.
            sidebar: { schema: true, session: true },
            runUrl: context.apiBase + '/api/designer/run',
            dataPreviewUrl: context.apiBase + '/api/designer/data-preview',
            dagUrl: context.apiBase + '/api/designer/dag',
            connectionRef: context.opts.connectionRef || null,
            documentUri: context.opts.documentUri || 'portal-designer',
            editor: {
                value: text,
                analyzeUrl: context.apiBase + '/api/designer/analyze',
                completeUrl: context.apiBase + '/api/designer/complete',
                authFetch: context._fetch,
                connectionRef: context.opts.connectionRef || null,
                documentUri: context.opts.documentUri || 'portal-designer',
                onCursorActivity: handleEditorCursorActivity,
            },
            onApply: async (script) => { await applyScriptText(script); },
            onClose: closeScript,
        });
    }
    function closeScript() {
        context.scriptOverlay.classList.remove('active');
        queryElement(context.topbar, '#dsgn-design-mode')?.classList.add('active');
        queryElement(context.topbar, '#dsgn-design-mode')?.setAttribute('aria-selected', 'true');
        queryElement(context.topbar, '#dsgn-code-mode')?.classList.remove('active');
        queryElement(context.topbar, '#dsgn-code-mode')?.setAttribute('aria-selected', 'false');
        scriptEditor?.dispose();
        scriptEditor = null;
        context.isSplitActive = false;
        context.root.classList.remove('split-screen');
        queryElement(context.topbar, '#dsgn-split-toggle')?.classList.remove('active');
        context.triggerChartResizes();
    }
    function setPreviewStatus(text, kind) {
        if (!context.previewStatusEl)
            return;
        context.previewStatusEl.textContent = text || '';
        const colors = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        /** @type {HTMLElement} */ (context.previewStatusEl).style.color = colors[kind] || colors.neutral;
    }
    async function refreshPreview() {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const currentScript = currentScriptText() || null;
            const gen = await context.apiJson('/api/designer/generate', 'POST', { designState: context.state, script: currentScript });
            const script = gen?.script ?? '';
            if (!script.trim()) {
                setPreviewStatus('Nothing to preview yet.', 'neutral');
                return;
            }
            const manifest = await context.apiJson('/api/designer/preview', 'POST', { script });
            if (!manifest)
                return;
            context._pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            /** @type {HTMLImageElement | HTMLIFrameElement | HTMLScriptElement | HTMLMediaElement} */ (context.previewFrame).src = context.previewUrl + (context.previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        }
        catch (e) {
            setPreviewStatus('Preview failed: ' + context.errorText(e), 'error');
        }
    }
    function openPreview() {
        context.previewOverlay.classList.add('active');
        refreshPreview();
    }
    function closePreview() {
        context.previewOverlay.classList.remove('active');
    }
    function invalidateScriptApply() {
        scriptApplySequence++;
    }
    async function applyScriptText(script) {
        const sequence = ++scriptApplySequence;
        try {
            const r = await context.apiJson('/api/designer/parse', 'POST', { script });
            if (sequence !== scriptApplySequence)
                return { applied: false, stale: true };
            if (r?.designState?.pages?.length) {
                context.setScriptDiagnosticBadge(null);
                Object.assign(context.state, r.designState);
                if (!context.state.datasets)
                    context.state.datasets = [];
                if (context.pageIdx >= context.state.pages.length) {
                    context.pageIdx = 0;
                }
                context.selVisualId = null;
                if (!context.isSplitActive) {
                    closeScript();
                }
                renderAllFromScript();
                return { applied: true, designState: r.designState };
            }
            else {
                context.setScriptDiagnosticBadge(r?.error || 'Script syntax error');
                if (!context.isSplitActive) {
                    feedback.notify(r?.error || 'Could not parse script.', { title: 'Script not parsed', tone: 'error' });
                }
                return { applied: false, error: r?.error || 'Script syntax error' };
            }
        }
        catch (e) {
            if (sequence !== scriptApplySequence)
                return { applied: false, stale: true };
            context.setScriptDiagnosticBadge(context.errorText(e));
            if (!context.isSplitActive) {
                feedback.notify(context.errorText(e), { title: 'Script not parsed', tone: 'error' });
            }
            return { applied: false, error: context.errorText(e) };
        }
    }
    return { selectVisualInEditor, currentScriptText, syncScriptFromGridDebounced, openScript, closeScript, refreshPreview, openPreview, closePreview, invalidateScriptApply, applyScriptText };
}
