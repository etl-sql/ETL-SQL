/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Guided source edits, syntax feedback, and document-scoped undo.
 */

import { _escapeHtml, _feedback, _readErrorText, analyzeSyntaxDiff, queryElement, queryElements } from './studio-context.js';
import type { DesignerStateLike, DesignerVisualLike } from './studio-sql-mutations.js';

import { markdownToTooltipHtml } from './script-editor.js';
import type { StudioDomElement, StudioRuntimeDocument, StudioRuntimeState, SyntaxBridgeContext } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';

export interface StudioSyntaxBridgeContext {
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly canonicalDesignerMutation: <T = unknown>(label: string, mutate: (designState: DesignerStateLike) => Promise<T> | T) => Promise<T | null>;
    readonly findDesignerVisual: (designState: DesignerStateLike, visualId: string | null | undefined) => DesignerVisualLike | null;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly hasDataSample: () => boolean;
    readonly inspector: StudioDomElement;
    readonly setActivity: (activity: string) => void;
    readonly setProjection: (mode: string) => void;
    readonly state: StudioRuntimeState;
    readonly switchDoc: (docId: string) => Promise<void>;
    readonly syntaxBridgeBtn: HTMLButtonElement;
    readonly syntaxBridgePanel: HTMLDivElement;
    readonly uniqueVisualName: (designState: DesignerStateLike, baseName: string) => string;
    readonly visualSourceBinding: () => { dataset: string | null; options: Record<string, string>; };
}

export function createStudioSyntaxBridge(hostContext: StudioSyntaxBridgeContext) {
    let dismissUndoOffer: (() => void) | null = null;

    function performUndoFor(target: any, before: string, after: string, label: string) {
        if (hostContext.getActiveDoc() !== target) {
            _feedback.notify(
                `Undo applies to ${target.name}. Open that document again and use its own undo.`,
                { title: 'Nothing undone', tone: 'warning' });
            return;
        }
        if (hostContext.state.editorInstance?.getValue?.() !== after) {
            _feedback.notify(
                'The script changed again after this edit, so undoing it here would take back the wrong change. '
                + 'The editor\'s own undo still steps back through every change in order.',
                { title: 'Nothing undone', tone: 'warning' });
            return;
        }
        hostContext.state.editorInstance?.undo?.();
        if (hostContext.state.editorInstance?.getValue?.() !== before) {
            _feedback.notify(
                'The editor undid a different change than expected, so the script is not back where it started. '
                + 'Check the script before saving.',
                { title: 'Undo Incomplete', tone: 'error' });
            return;
        }
        _feedback.notify(`${label} was undone.`, { title: 'Script restored', tone: 'info' });
    }

    function resolveCurrentEditorContext(): SyntaxBridgeContext | null {
        const doc = hostContext.getActiveDoc();
        if (!doc) return null;
        if (hostContext.state.lastSyntaxBridgeContext && hostContext.state.lastSyntaxBridgeContext.document === doc) {
            return hostContext.state.lastSyntaxBridgeContext;
        }
        const script = hostContext.state.editorInstance?.getValue?.() || doc.content || '';
        const currentStatement = String(hostContext.state.editorInstance?.getCurrentStatement?.() || '').trim();
        const token = currentStatement.split(/[\s(;]+/)[0]?.toUpperCase() || 'VISUAL';
        return {
            document: doc,
            label: `Inspect ${token}`,
            before: script,
            after: script,
            diff: {
                from: 0,
                to: Math.min(script.length, 80),
                insertedText: currentStatement,
                replacedText: '',
                constructName: token,
                keyword: token,
                explanation: `Canonical syntax documentation for ${token}.`,
                relatedKeywords: ['VISUAL', 'LAYOUT', 'MAPPINGS', 'OPTIONS'],
            },
        };
    }

    async function openSyntaxBridge(context?: SyntaxBridgeContext | null) {
        const ctx = context || hostContext.state.lastSyntaxBridgeContext || resolveCurrentEditorContext();
        if (!ctx?.document) return;
        if (hostContext.getActiveDoc() !== ctx.document) {
            await hostContext.switchDoc(ctx.document.id);
        }
        const doc = hostContext.getActiveDoc();
        if (!doc) return;

        if (doc.projection === 'canvas') {
            hostContext.setProjection('split');
        }

        hostContext.state.editorInstance?.revealRange?.(ctx.diff.from, ctx.diff.to, true);
        void renderSyntaxBridge(ctx);
    }

    async function renderSyntaxBridge(ctx: SyntaxBridgeContext, selectedKeyword = ctx.diff.keyword) {
        if (!hostContext.syntaxBridgePanel) return;
        hostContext.syntaxBridgePanel.style.display = 'flex';
        hostContext.syntaxBridgeBtn?.classList.add('is-active');

        ctx.cachedHelp ||= {};

        const snippet = (ctx.diff.insertedText || ctx.diff.replacedText || '').trim();
        const keywords = ctx.diff.relatedKeywords || [ctx.diff.keyword];

        hostContext.syntaxBridgePanel.innerHTML = `
            <div class="etlsql-syntax-bridge-header">
                <div class="etlsql-syntax-bridge-title">
                    <span>Syntax Bridge: ${_escapeHtml(ctx.diff.constructName)}</span>
                    <span class="etlsql-syntax-badge">${_escapeHtml(selectedKeyword)}</span>
                </div>
                <button type="button" class="etlsql-syntax-bridge-close" data-syntax-close title="Close Syntax Helper" aria-label="Close Syntax Helper">×</button>
            </div>
            <p class="etlsql-syntax-bridge-desc">${_escapeHtml(ctx.diff.explanation)} Try a hand edit in the script; click <strong>Syntax Helper</strong> in the toolbar above to reopen this at any time.</p>
            ${snippet ? `
            <div class="etlsql-syntax-snippet-box">
                <pre><code>${_escapeHtml(snippet)}</code></pre>
            </div>` : ''}
            <div class="etlsql-syntax-keywords">
                <span>Canonical help topics:</span>
                ${keywords.map(kw => `
                    <button type="button" class="etlsql-syntax-kw-chip${kw === selectedKeyword ? ' is-active' : ''}" data-syntax-kw="${_escapeHtml(kw)}">${_escapeHtml(kw)}</button>
                `).join('')}
            </div>
            <div class="etlsql-syntax-help-content" data-syntax-help-body>
                <em>Loading canonical reference for ${_escapeHtml(selectedKeyword)}...</em>
            </div>
            <div class="etlsql-syntax-actions">
                <button type="button" class="etlsql-studio-btn is-primary" data-syntax-undo>Undo this edit</button>
                <button type="button" class="etlsql-studio-btn" data-syntax-dismiss>Close helper</button>
            </div>
        `;

        queryElement(hostContext.syntaxBridgePanel, '[data-syntax-close]')?.addEventListener('click', closeSyntaxBridge);
        queryElement(hostContext.syntaxBridgePanel, '[data-syntax-dismiss]')?.addEventListener('click', closeSyntaxBridge);
        queryElement(hostContext.syntaxBridgePanel, '[data-syntax-undo]')?.addEventListener('click', () => {
            closeSyntaxBridge();
            performUndoFor(ctx.document, ctx.before, ctx.after, ctx.label);
        });

        queryElements(hostContext.syntaxBridgePanel, '[data-syntax-kw]').forEach(chip => {
            chip.addEventListener('click', () => {
                const kw = chip.getAttribute('data-syntax-kw');
                if (kw) void renderSyntaxBridge(ctx, kw);
            });
        });

        const helpContainer = queryElement(hostContext.syntaxBridgePanel, '[data-syntax-help-body]');
        if (!helpContainer) return;

        if (ctx.cachedHelp[selectedKeyword]) {
            helpContainer.innerHTML = markdownToTooltipHtml(ctx.cachedHelp[selectedKeyword]);
            return;
        }

        try {
            const res = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.hover, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ word: selectedKeyword }),
            });
            if (res.ok) {
                const data = await res.json();
                if (data?.markdown) {
                    ctx.cachedHelp[selectedKeyword] = data.markdown;
                    helpContainer.innerHTML = markdownToTooltipHtml(data.markdown);
                    return;
                }
            }
        } catch (error) {
            if (typeof console !== 'undefined') console.debug?.('Hover help lookup failed', error);
        }

        const fallback = `# ${selectedKeyword}\n\n${ctx.diff.explanation}\n\n\`\`\`sql\n${snippet || selectedKeyword}\n\`\`\``;
        helpContainer.innerHTML = markdownToTooltipHtml(fallback);
    }

    function closeSyntaxBridge() {
        if (!hostContext.syntaxBridgePanel) return;
        hostContext.syntaxBridgePanel.style.display = 'none';
        hostContext.syntaxBridgeBtn?.classList.remove('is-active');
    }

    function offerUndo(label: string, { document: target, before, after }: { document?: any; before?: string; after?: string }) {
        if (!target || typeof before !== 'string' || typeof after !== 'string' || before === after) return;
        if (typeof hostContext.state.editorInstance?.undo !== 'function') return;
        if (hostContext.getActiveDoc() !== target) return;

        const diff = analyzeSyntaxDiff(label, before, after);
        const bridgeContext: SyntaxBridgeContext = {
            document: target,
            label,
            before,
            after,
            diff,
        };
        hostContext.state.lastSyntaxBridgeContext = bridgeContext;
        if (hostContext.syntaxBridgeBtn) {
            hostContext.syntaxBridgeBtn.style.display = 'inline-flex';
            hostContext.syntaxBridgeBtn.title = `Syntax Helper: ${diff.constructName} (${label})`;
        }

        // One offer at a time. Ticking through a filter's values writes once per click, and a column
        // of stacked offers would bury the panel being worked in — while only the newest of them
        // could be taken anyway, since each write invalidates the one before it.
        dismissUndoOffer?.();
        dismissUndoOffer = _feedback.notify(`Studio wrote this into ${target.name}.`, {
            title: label,
            tone: 'success',
            actions: [
                {
                    label: 'Show what changed',
                    primary: true,
                    onSelect: () => {
                        void openSyntaxBridge(bridgeContext);
                    },
                },
                {
                    label: 'Undo',
                    onSelect: () => {
                        performUndoFor(target, before, after, label);
                    },
                },
            ],
        });
    }

    /**
     * The authoring module's only network path. Route strings come from the tables, never literals,
     * and a failure surfaces the server's own message rather than a status code.
     */
    async function authoringRequest(route: string, { method = 'POST', body = null, query = null, fallbackError = null, accept = null }: { method?: string; body?: any; query?: Record<string, any> | null; fallbackError?: string | null; accept?: string | null } = {}) {
        const search = query
            ? '?' + Object.entries(query)
                .filter(([, value]) => value != null)
                .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
                .join('&')
            : '';
        const headers: Record<string, string> = { ...(body === null ? {} : { 'Content-Type': 'application/json' }), ...(accept ? { Accept: accept } : {}) };
        const response = await hostContext.authFetch(hostContext.apiBase + route + search, body === null
            ? { method, headers }
            : { method, headers, body: JSON.stringify(body) });
        if (!response) throw new Error('The Studio session ended during the request.');
        // A failure is JSON even when the caller asked for bytes, so the server's own reason is read
        // the same way either way rather than surfacing as an unreadable blob.
        if (!response.ok) throw new Error(await _readErrorText(response) || fallbackError || `The request failed (${response.status}).`);
        return accept && accept !== 'application/json' ? response.blob() : response.json();
    }

    async function addVisualToCanvas(type: string) {
        const uType = (type || 'BAR').toUpperCase();
        if (!hostContext.hasDataSample()) {
            hostContext.setActivity('catalog');
            _feedback.notify('Choose a connection and table before adding a visual.', { title: 'Data required', tone: 'info' });
            return;
        }
        const binding = hostContext.visualSourceBinding();
        const addedName = await hostContext.canonicalDesignerMutation(`Add ${uType} visual`, (designState: any) => {
            const page = designState.pages[0];
            page.visuals ||= [];
            const name = hostContext.uniqueVisualName(designState, `${uType.toLowerCase()}_visual`);
            const maxRow = page.visuals.reduce((max: number, visual: any) => Math.max(max, (visual.gridRow || 0) + (visual.gridRowSpan || 1) - 1), 0);
            page.visuals.push({
                id: `studio_${Date.now().toString(36)}`,
                name,
                type: uType,
                gridCol: 1,
                gridRow: maxRow + 1,
                gridColSpan: uType === 'CARD' ? 3 : uType === 'TABLE' ? 12 : 6,
                gridRowSpan: uType === 'CARD' ? 2 : uType === 'TABLE' ? 5 : 4,
                title: `New ${uType} Visual`,
                dataset: binding.dataset,
                mappings: {},
                options: { ...binding.options }
            });
            return name;
        });
        if (addedName) _feedback.notify(`Added ${uType} visual to canvas.`, { title: 'Visual Added', tone: 'success' });
        return addedName;
    }

    async function duplicateVisual(visualId: string) {
        const duplicateName = await hostContext.canonicalDesignerMutation('Duplicate visual', (designState: any) => {
            const source = hostContext.findDesignerVisual(designState, visualId);
            if (!source) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            const page = designState.pages.find((item: any) => item.visuals?.includes(source));
            const name = hostContext.uniqueVisualName(designState, `${source.name}_copy`);
            page.visuals.push({
                ...structuredClone(source),
                id: `studio_${Date.now().toString(36)}`,
                name,
                gridRow: ((Number(source.gridRow) || 1) + (Number(source.gridRowSpan) || 1))
            });
            return name;
        });
        if (duplicateName) _feedback.notify(`Duplicated visual ${visualId}.`, { title: 'Visual Duplicated', tone: 'success' });
        return duplicateName;
    }

    // Programmatic API: no confirmation here. The interactive Delete-key path lives in the designer
    // canvas and confirms there — putting a modal in this function would block any caller that is
    // not a human, which is every automated one.
    async function deleteVisual(visualId: string) {
        const deleted = await hostContext.canonicalDesignerMutation('Delete visual', (designState: any) => {
            const visual = hostContext.findDesignerVisual(designState, visualId);
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            for (const page of designState.pages) page.visuals = (page.visuals || []).filter((item: any) => item !== visual);
            return true;
        });
        if (deleted && hostContext.state.selectedVisualId === visualId) {
            hostContext.state.selectedVisualId = null;
            hostContext.inspector.style.display = 'none';
        }
        if (deleted) _feedback.notify(`Deleted visual ${visualId}.`, { title: 'Visual Deleted', tone: 'info' });
        return deleted;
    }

    function surgicalPatchVisualOption(visualId: string, optionKey: string, optionValue: string) {
        return hostContext.canonicalDesignerMutation('Update visual option', (designState: any) => {
            const visual = hostContext.findDesignerVisual(designState, visualId) as any;
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            if (optionKey.toUpperCase() === 'TITLE') visual.title = optionValue;
            else {
                visual.options ||= {};
                visual.options[optionKey.toUpperCase()] = optionValue;
            }
            return true;
        });
    }

    function surgicalPatchVisualMapping(visualId: string, mapKey: string, mapValue: string) {
        return hostContext.canonicalDesignerMutation('Update visual mapping', (designState: any) => {
            const visual = hostContext.findDesignerVisual(designState, visualId) as any;
            if (!visual) throw new Error(`Visual ${visualId} was not found in the parsed document.`);
            visual.mappings ||= {};
            if (mapValue?.trim()) visual.mappings[mapKey.toUpperCase()] = mapValue.trim();
            else delete visual.mappings[mapKey.toUpperCase()];
            return true;
        });
    }

    return { openSyntaxBridge, closeSyntaxBridge, offerUndo, authoringRequest, addVisualToCanvas, duplicateVisual, deleteVisual, surgicalPatchVisualOption, surgicalPatchVisualMapping };
}
