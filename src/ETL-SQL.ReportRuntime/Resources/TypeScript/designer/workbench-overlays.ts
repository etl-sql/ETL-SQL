/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report preview, flow inspection, commands, and formatter controls.
 */

import { asHtml, asIframe, asInput, asSelect } from './workbench-context.js';

import { renderDag, type DagHandle } from './dag.js';
import { _feedback, escapeHtml } from './designer-util.js';
import type { ScriptEditorHandle } from './script-editor.js';
import type { CommandPaletteItem, FormatterConfig, ScriptWorkbenchOptions } from './workbench-context.js';

export interface WorkbenchOverlaysContext {
    _pendingManifest: any;
    readonly apply: () => Promise<void>;
    readonly cancelRun: () => void;
    readonly container: HTMLElement;
    readonly editor: ScriptEditorHandle;
    readonly editorHost: HTMLElement;
    readonly flowBody: HTMLElement;
    flowDagInstance: DagHandle | null;
    readonly flowOverlay: Element | null;
    readonly flowStatusEl: Element | null;
    readonly getDocumentUri: () => string;
    readonly metadataApiBase: () => string;
    readonly opts: ScriptWorkbenchOptions;
    readonly palette: HTMLElement;
    readonly paletteFilter: HTMLInputElement;
    readonly paletteList: HTMLElement;
    readonly previewFrame: Element | null;
    readonly previewOverlay: Element | null;
    readonly previewStatusEl: Element | null;
    readonly previewUrl: string;
    readonly root: HTMLElement;
    readonly run: (scope?: "script" | "selection", confirmDestructive?: boolean) => Promise<void>;
    readonly save: () => Promise<void>;
    readonly scheduleSidebarRefresh: () => void;
}

export function createWorkbenchOverlays(context: WorkbenchOverlaysContext) {

    function setPreviewStatus(text: string, kind: 'error' | 'pending' | 'neutral'): void {
        if (!context.previewStatusEl) return;
        context.previewStatusEl.textContent = text || '';
        const colors: Record<string, string> = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        asHtml(context.previewStatusEl).style.color = colors[kind] || colors.neutral;
    }

    async function refreshPreview(): Promise<void> {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const script = context.editor.getValue();
            if (!script.trim()) { setPreviewStatus('Nothing to preview yet.', 'neutral'); return; }
            const fetcher = context.opts.authFetch ?? ((url, init) => fetch(url, init));
            const res = await fetcher(context.opts.previewApiUrl!, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, connectionRef: context.opts.connectionRef || null }),
            });
            if (!res?.ok) throw new Error(await res.text());
            const manifest = await res.json();
            context._pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            if (context.previewFrame) {
                asIframe(context.previewFrame).src = context.previewUrl + (context.previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            }
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        } catch (e: any) {
            setPreviewStatus('Preview failed: ' + (e?.message || e), 'error');
        }
    }

    function openPreview(): void {
        if (!context.previewOverlay) return;
        context.previewOverlay.classList.add('active');
        refreshPreview();
    }

    function closePreview(): void {
        context.previewOverlay?.classList.remove('active');
    }

    function setFlowStatus(text: string, kind: 'error' | 'pending' | 'neutral'): void {
        if (!context.flowStatusEl) return;
        context.flowStatusEl.textContent = text || '';
        const colors: Record<string, string> = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        asHtml(context.flowStatusEl).style.color = colors[kind] || colors.neutral;
    }

    async function refreshFlow(): Promise<void> {
        if (!context.opts.dagUrl || !context.flowBody) return;
        setFlowStatus('Building flow...', 'pending');
        context.flowDagInstance?.dispose?.();
        context.flowDagInstance = null;
        context.flowBody.innerHTML = '<div class="etlsql-dag-empty">Building flow preview...</div>';
        try {
            const script = context.editor.getValue();
            if (!script.trim()) {
                context.flowBody.innerHTML = '<div class="etlsql-dag-empty">No script flow yet.</div>';
                setFlowStatus('Nothing to diagram.', 'neutral');
                return;
            }

            const fetcher = context.opts.authFetch ?? ((url, init) => fetch(url, init));
            const res = await fetcher(context.opts.dagUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: context.getDocumentUri() }),
            });
            if (!res?.ok) throw new Error(await res.text());
            const data = await res.json();
            if (data?.error || data?.parsed === false) {
                throw new Error(data.error || 'Script flow could not be parsed.');
            }
            const graph = data?.dag || data || {};
            context.flowDagInstance = renderDag(context.flowBody, {
                nodes: graph.nodes ?? graph.Nodes ?? [],
                edges: graph.edges ?? graph.Edges ?? [],
            }, {
                theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
                onNodeClick: (_nodeId: string | null, meta: any) => {
                    const line = meta?.line ?? meta?.Line;
                    if (line) context.editor.gotoLine?.(line);
                },
            });
            const nodeCount = (graph.nodes ?? graph.Nodes ?? []).length;
            const edgeCount = (graph.edges ?? graph.Edges ?? []).length;
            setFlowStatus(`${nodeCount} node${nodeCount === 1 ? '' : 's'}, ${edgeCount} edge${edgeCount === 1 ? '' : 's'}.`, 'neutral');
        } catch (e: any) {
            context.flowBody.innerHTML = `<div class="etlsql-dag-empty">Flow preview failed: ${escapeHtml(e?.message || e)}</div>`;
            setFlowStatus('Flow failed.', 'error');
        }
    }

    function openFlow(): void {
        if (!context.flowOverlay) return;
        context.flowOverlay.classList.add('active');
        refreshFlow();
    }

    function closeFlow(): void {
        context.flowOverlay?.classList.remove('active');
    }

    function commandItems(): CommandPaletteItem[] {
        return [
            { id: 'run', label: 'ETL-SQL: Run Script', enabled: Boolean(context.opts.runUrl || context.opts.onRun), action: () => context.run('script') },
            { id: 'run-selected', label: 'ETL-SQL: Run Selection or Current Statement', enabled: Boolean(context.opts.runUrl || context.opts.onRun), action: () => context.run('selection') },
            { id: 'cancel-run', label: 'ETL-SQL: Cancel Running Script', enabled: context.root.classList.contains('is-running'), action: context.cancelRun },
            { id: 'new-connection', label: 'ETL-SQL: New Connection Wizard...', enabled: true, action: openConnectionWizard },
            { id: 'flow', label: 'ETL-SQL: Preview Script Flow', enabled: Boolean(context.opts.dagUrl), action: openFlow },
            { id: 'preview', label: 'ETL-SQL: Preview Report', enabled: Boolean(context.opts.previewApiUrl), action: openPreview },
            { id: 'suggest', label: 'ETL-SQL: Trigger Suggestions (Ctrl-Space / Ctrl-.)', enabled: Boolean(context.editor.hasCompletion && context.editor.triggerCompletion), action: () => { context.editor.triggerCompletion(); } },
            { id: 'analyze', label: 'ETL-SQL: Analyze Script', enabled: typeof context.editor.analyze === 'function', action: () => context.editor.analyze() },
            { id: 'apply', label: 'ETL-SQL: Update Designer from Script', enabled: Boolean(context.opts.onApply), action: context.apply },
            { id: 'save', label: 'ETL-SQL: Save Script', enabled: Boolean(context.opts.onSave), action: context.save },
            { id: 'format', label: 'ETL-SQL: Format Document', enabled: Boolean(context.opts.onFormat), action: () => context.opts.onFormat?.(context.editor.getValue()) },
            { id: 'close', label: 'ETL-SQL: Close Editor', enabled: Boolean(context.opts.onClose), action: () => context.opts.onClose?.() },
        ].filter(c => c.enabled);
    }

    async function openConnectionWizard(): Promise<void> {
        try {
            const { createConnectionWizard } = await import('./connection-wizard.js');
            const fetcher = context.opts.authFetch ?? fetch;
            const apiBase = context.metadataApiBase();
            const scriptText = context.editor.getValue();
            const existingNames: string[] = [];
            for (const m of scriptText.matchAll(/\bCREATE\s+CONNECTION\s+([a-zA-Z0-9_#]+)/gi)) { if (m[1]) existingNames.push(m[1]); }
            for (const m of scriptText.matchAll(/\bCREATE\s+DATASET\s+([a-zA-Z0-9_#]+)/gi)) { if (m[1]) existingNames.push(m[1]); }

            createConnectionWizard({
                host: document.body,
                mode: 'script',
                existingNames,
                fetchSchemas: async () => {
                    try {
                        const res = await fetcher(`${apiBase}/api/connectors/schema`);
                        if (res.ok) {
                            const d = await res.json();
                            return Array.isArray(d) ? d : (d.schemas || []);
                        }
                    } catch (e) {
                        console.warn('Failed to fetch schemas', e);
                    }
                    return [];
                },
                onTest: async (req: any) => {
                    const res = await fetcher(`${apiBase}/api/connectors/test`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(req)
                    });
                    if (!res.ok) throw new Error(await res.text());
                    return await res.json();
                },
                onParseString: async (rawString: string, hint: string) => {
                    const res = await fetcher(`${apiBase}/api/connectors/parse-string`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ connectionString: rawString, hintProvider: hint })
                    });
                    if (!res.ok) throw new Error(await res.text());
                    return await res.json();
                },
                onInsert: (sql: string) => {
                    insertConnectionSql(sql);
                }
            });
        } catch (err: any) {
            _feedback?.notify?.('Failed to open Connection Wizard: ' + err.message, { title: 'Wizard Error', tone: 'error' });
        }
    }

    function insertConnectionSql(sql: string): void {
        const current = context.editor.getValue();
        if (!current.trim()) {
            context.editor.setValue(sql + '\n\n');
        } else {
            const matches = [...current.matchAll(/CREATE\s+CONNECTION\s+[\s\S]*?(?:;|\n\);?)/gi)];
            if (matches.length > 0) {
                const last = matches[matches.length - 1];
                const pos = (last.index ?? 0) + last[0].length;
                const updated = current.slice(0, pos) + '\n\n' + sql + current.slice(pos);
                context.editor.setValue(updated);
            } else {
                context.editor.setValue(sql + '\n\n' + current);
            }
        }
        context.editor.analyze?.();
        context.scheduleSidebarRefresh();
        _feedback?.notify?.('Connection inserted into script.', { title: 'Connection Created', tone: 'success' });
    }

    function renderPalette(): void {
        const filter = String(asInput(context.paletteFilter).value || '').toLowerCase();
        const commands = commandItems().filter(c => !filter || c.label.toLowerCase().includes(filter));
        context.paletteList.innerHTML = commands.length
            ? commands.map((c, i) => `<button type="button" data-command="${escapeHtml(c.id)}" class="${i === 0 ? 'active' : ''}">${escapeHtml(c.label)}</button>`).join('')
            : '<div class="etlsql-script-results-empty">No commands</div>';
        context.paletteList.querySelectorAll('[data-command]').forEach(button => {
            button.addEventListener('click', async () => {
                const cmd = commands.find(c => c.id === asHtml(button).dataset.command);
                closePalette();
                await cmd?.action();
            });
        });
    }

    function openPalette(): void {
        asHtml(context.palette).hidden = false;
        asInput(context.paletteFilter).value = '';
        renderPalette();
        asInput(context.paletteFilter).focus();
    }

    function closePalette(): void {
        asHtml(context.palette).hidden = true;
        const cmEl = context.editorHost.querySelector('.cm-editor');
        if (cmEl) asHtml(cmEl).focus();
    }

    async function formatScript(): Promise<void> {
        if (context.opts.onFormat) {
            await context.opts.onFormat(context.editor.getValue());
            return;
        }
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const docUri = context.getDocumentUri();
            const script = context.editor.getValue();
            const res = await fetcher('/api/format', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: docUri }),
            });
            if (res.ok) {
                const data = await res.json();
                if (data?.script) context.editor.setValue(data.script);
            }
        } catch (e) {
            console.warn('Format failed:', e);
        }
    }

    async function openFormatterSettingsModal(): Promise<void> {
        let modalEl = context.container.querySelector('#etlsql-formatter-modal');
        if (!modalEl) {
            modalEl = document.createElement('div');
            modalEl.id = 'etlsql-formatter-modal';
            modalEl.className = 'etlsql-formatter-drawer';
            context.container.appendChild(modalEl);
        }
        const modal = asHtml(modalEl);

        modal.innerHTML = `
            <div class="etlsql-formatter-header">
                <strong>⚙️ Formatter Settings</strong>
                <button type="button" class="etlsql-tool-btn" data-fmt-close title="Close">✕</button>
            </div>
            <div class="etlsql-formatter-body">
                <label class="etlsql-fmt-field">
                    <span>Keyword Casing</span>
                    <select id="fmt-casing" class="form-control">
                        <option value="upper">UPPERCASE (SELECT)</option>
                        <option value="lower">lowercase (select)</option>
                        <option value="pascal">PascalCase (Select)</option>
                        <option value="preserve">Preserve</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Indent Size</span>
                    <select id="fmt-indent" class="form-control">
                        <option value="2">2 spaces</option>
                        <option value="4">4 spaces</option>
                        <option value="8">8 spaces</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Comma Placement</span>
                    <select id="fmt-comma" class="form-control">
                        <option value="leading">Leading (,col)</option>
                        <option value="trailing">Trailing (col,)</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Line Width</span>
                    <input type="number" id="fmt-linewidth" class="form-control" min="40" max="300" value="100">
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-indentjoins"> Indent JOIN clauses
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-onnewline"> Put ON clause on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-casenewline"> Put CASE WHEN/THEN on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-breakwindow"> Breakout window functions
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-rightalign"> Right-align query keywords
                </label>
            </div>
            <div class="etlsql-formatter-footer">
                <button type="button" id="fmt-save-btn" class="btn btn-primary btn-sm">Save to .etlsql-formatter.json</button>
                <span id="fmt-status" class="etlsql-fmt-status"></span>
            </div>
        `;

        modal.style.display = 'flex';
        modal.querySelector('[data-fmt-close]')?.addEventListener('click', () => { modal.style.display = 'none'; });

        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const docUri = context.getDocumentUri();
            const res = await fetcher(`/api/formatter/config?documentUri=${encodeURIComponent(docUri)}`);
            if (res.ok) {
                const config: FormatterConfig = await res.json();
                if (config) {
                    if (config.keywordCasing) asSelect(modal.querySelector('#fmt-casing')).value = config.keywordCasing.toLowerCase();
                    if (config.indentSize) asSelect(modal.querySelector('#fmt-indent')).value = String(config.indentSize);
                    if (config.commaPlacement) asSelect(modal.querySelector('#fmt-comma')).value = config.commaPlacement.toLowerCase();
                    if (config.lineWidth) asInput(modal.querySelector('#fmt-linewidth')).value = String(config.lineWidth);
                    asInput(modal.querySelector('#fmt-indentjoins')).checked = Boolean(config.indentJoins);
                    asInput(modal.querySelector('#fmt-onnewline')).checked = Boolean(config.onClauseOnNewLine);
                    asInput(modal.querySelector('#fmt-casenewline')).checked = Boolean(config.caseWhenThenNewLine);
                    asInput(modal.querySelector('#fmt-breakwindow')).checked = Boolean(config.breakoutWindowFunctions);
                    asInput(modal.querySelector('#fmt-rightalign')).checked = Boolean(config.rightAlignKeywords);
                }
            }
        } catch (e) {
            console.warn('Failed to load formatter options:', e);
        }

        modal.querySelector('#fmt-save-btn')?.addEventListener('click', async () => {
            const statusEl = asHtml(modal.querySelector('#fmt-status'));
            statusEl.textContent = 'Saving...';
            const payload = {
                keywordCasing: asSelect(modal.querySelector('#fmt-casing')).value,
                indentSize: parseInt(asSelect(modal.querySelector('#fmt-indent')).value, 10),
                commaPlacement: asSelect(modal.querySelector('#fmt-comma')).value,
                lineWidth: parseInt(asInput(modal.querySelector('#fmt-linewidth')).value, 10) || 100,
                indentJoins: asInput(modal.querySelector('#fmt-indentjoins')).checked,
                onClauseOnNewLine: asInput(modal.querySelector('#fmt-onnewline')).checked,
                caseWhenThenNewLine: asInput(modal.querySelector('#fmt-casenewline')).checked,
                breakoutWindowFunctions: asInput(modal.querySelector('#fmt-breakwindow')).checked,
                rightAlignKeywords: asInput(modal.querySelector('#fmt-rightalign')).checked,
            };

            try {
                const fetcher = context.opts.authFetch ?? fetch;
                const res = await fetcher('/api/formatter/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload),
                });

                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                statusEl.textContent = '✓ Saved to .etlsql-formatter.json';
                setTimeout(() => { modal.style.display = 'none'; }, 1000);

                await formatScript();
            } catch (err: any) {
                statusEl.textContent = 'Error: ' + err.message;
            }
        });
    }

    return { refreshPreview, openPreview, closePreview, refreshFlow, openFlow, closeFlow, openConnectionWizard, renderPalette, openPalette, closePalette, formatScript, openFormatterSettingsModal };
}
