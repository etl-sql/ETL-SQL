/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Engine scope, query plans, and diagnostic quick fixes.
 */

import { _escapeHtml, _feedback, _readErrorText, _studioIcon, errorMessage, queryElement } from './studio-context.js';

import type { StudioDomElement, StudioDynamic, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';

export interface StudioEnginePanelContext {
    readonly activeScriptText: () => any;
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly designerApiJson: <T = StudioDynamic>(path: string, body: unknown) => Promise<T>;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly offerUndo: (label: string, change: { document?: any; before?: string; after?: string; }) => void;
    readonly renderTabs: () => void;
    readonly sidebarContent: StudioDomElement;
    readonly sidebarTitle: StudioDomElement;
    readonly state: StudioRuntimeState;
}

export function createStudioEnginePanel(hostContext: StudioEnginePanelContext) {

    // ── Engine state and visual EXPLAIN ───────────────────────────────────────
    // Two questions an author asks of a statement they are looking at, answered in one place: what
    // can this see from here, and what would the engine do with it.
    //
    // "What can it see" is the Phase 2 scope model asked with a caret instead of a task label — the
    // same positional rule, because it is the same question. What it must never do is list every
    // name in the file: a `#temp` created below the cursor does not exist yet, and offering it is
    // wrong only at run time, which is the most expensive place to find out.
    //
    // "What would the engine do" is the engine's own EXPLAIN, not a second planner written here. The
    // plan is asked for through the ordinary run route, so it passes the same policy, the same
    // limits, and the same audit as any other execution — a design surface must not become a second
    // door into the engine. That also means the statements above the cursor really do run: they are
    // what builds the `#temp` tables the plan reads, and the panel says so before the author asks
    // rather than after.

    /** Reads the row shape EXPLAIN returns, whatever case the host serialised the columns in. */
    function planCell(row: StudioDynamic, columns: StudioDynamic, name: string) {
        const index = columns.findIndex((column: StudioDynamic) => String(column).toLowerCase() === name.toLowerCase());
        return index < 0 ? '' : String(row?.[index] ?? '');
    }

    function planOperatorMarkup(row: StudioDynamic, columns: StudioDynamic) {
        const operation = planCell(row, columns, 'Operation');
        const details = planCell(row, columns, 'Details');
        const mode = planCell(row, columns, 'Mode').toUpperCase();
        const cost = planCell(row, columns, 'Cost');
        const estimated = planCell(row, columns, 'Est. Rows');
        const spillBytes = Number(planCell(row, columns, 'Spill Bytes') || 0);
        const notes = planCell(row, columns, 'Plan Notes');

        const badges = [];
        // BLOCKING is the one an author acts on: it is where the query stops streaming and starts
        // holding rows, which is where memory and spill come from.
        if (mode) badges.push(`<span class="etlsql-studio-plan-badge is-${mode === 'BLOCKING' ? 'blocking' : 'streaming'}">${_escapeHtml(mode.toLowerCase())}</span>`);
        if (/pushdown/i.test(details)) badges.push('<span class="etlsql-studio-plan-badge is-pushdown">pushed to source</span>');
        if (/index/i.test(operation)) badges.push('<span class="etlsql-studio-plan-badge is-index">index</span>');
        if (spillBytes > 0) badges.push(`<span class="etlsql-studio-plan-badge is-spill">spilled ${_escapeHtml(String(spillBytes))} bytes</span>`);

        const facts = [
            cost ? `cost ${cost}` : '',
            estimated && estimated !== '--' ? `~${estimated} rows` : '',
        ].filter(Boolean).join(' · ');

        return `<li class="etlsql-studio-plan-step">
                <div class="etlsql-studio-plan-op"><strong>${_escapeHtml(operation)}</strong>${badges.join('')}</div>
                ${details ? `<code>${_escapeHtml(details)}</code>` : ''}
                <span class="etlsql-studio-plan-facts">${_escapeHtml(facts)}${notes ? ` · ${_escapeHtml(notes)}` : ''}</span>
            </li>`;
    }

    function scopeListMarkup(scope: StudioDynamic) {
        const variables = scope?.variables || [];
        const temps = scope?.tempTables || [];
        if (!variables.length && !temps.length) {
            return '<div class="etlsql-studio-empty-compact">Nothing is in scope above the cursor yet.</div>';
        }
        const variableRows = variables.map((variable: StudioDynamic) => `<li><code>${_escapeHtml(variable.name)}</code><span>${
            _escapeHtml([variable.type, variable.value].filter(Boolean).join(' = ') || variable.origin)}</span></li>`).join('');
        const tempRows = temps.map((temp: StudioDynamic) => `<li><code>${_escapeHtml(temp.name)}</code><span>${
            _escapeHtml(temp.columns?.length ? temp.columns.map((column: StudioDynamic) => column.name).join(', ') : temp.origin)}</span></li>`).join('');
        return `${variables.length ? `<div class="etlsql-sidebar-section-header"><span>Variables</span></div><ul class="etlsql-studio-scope-list">${variableRows}</ul>` : ''}
            ${temps.length ? `<div class="etlsql-sidebar-section-header"><span>#temp tables</span></div><ul class="etlsql-studio-scope-list">${tempRows}</ul>` : ''}`;
    }

    /** 1-based caret line, or 1 when the editor cannot say. */
    function cursorLine() {
        const editor = hostContext.state.editorInstance;
        const reported = editor?.getCursorLine?.() ?? editor?.getCursor?.()?.line ?? null;
        const line = Number(reported);
        return Number.isFinite(line) && line > 0 ? Math.floor(line) : 1;
    }

    async function renderEnginePanel() {
        hostContext.sidebarTitle.textContent = 'Engine';
        const doc = hostContext.getActiveDoc();
        if (!doc) {
            hostContext.sidebarContent.innerHTML = '<div class="etlsql-studio-empty-guidance"><strong>Open a script</strong><span>Engine state is read from the script you are editing.</span></div>';
            return;
        }

        const line = cursorLine();
        hostContext.sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading the script…</div>';

        let scope;
        try {
            scope = await hostContext.designerApiJson(STUDIO_ROUTES.pipelineScope, { script: hostContext.activeScriptText(), line });
        } catch (error) {
            hostContext.sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Engine state could not be read</strong><p>${_escapeHtml(errorMessage(error) || String(error))}</p></div>`;
            return;
        }
        if (hostContext.state.activeActivity !== 'engine' || hostContext.getActiveDoc() !== doc) return;

        hostContext.state.enginePlanScope = scope?.resolved ? scope : null;
        const statement = scope?.resolved ? String(scope.statementText || '').trim() : '';
        const hasPrefix = Boolean(String(scope?.prefixScript || '').trim());
        const prefixEffects: Array<{ taskId: string; action: string; target: string; line: number }> =
            Array.isArray(scope?.prefixEffects) ? scope.prefixEffects : [];
        const hasMutatingPrefix = prefixEffects.length > 0;
        const effectSummary = prefixEffects
            .map(e => `${_escapeHtml(e.action)} ${_escapeHtml(e.target || '')} (line ${e.line})`)
            .join(', ');

        hostContext.sidebarContent.innerHTML = `
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>In scope here</strong><span>Line ${line} · what this statement can read</span></div></div>
                ${scope?.resolved
                    ? scopeListMarkup(scope)
                    : `<div class="etlsql-studio-empty-compact">${_escapeHtml(scope?.error || 'The script does not parse yet.')}</div>`}
            </section>
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>Query plan</strong><span>The engine's own EXPLAIN</span></div></div>
                ${statement
                    ? `<code class="etlsql-studio-plan-target">${_escapeHtml(statement.length > 220 ? statement.slice(0, 220) + '…' : statement)}</code>
                       ${hasMutatingPrefix ? `
                       <div class="etlsql-studio-capability-state" style="border-left: 3px solid var(--portal-danger, #da3633); margin-block: 8px;" role="alert">
                           <strong>Mutating prefix detected</strong>
                           <p>The statements above line ${line} modify persistent data: ${effectSummary}.</p>
                           <label style="display: flex; align-items: center; gap: 6px; font-size: 0.75rem; margin-top: 6px; cursor: pointer;">
                               <input type="checkbox" data-allow-mutating-prefix>
                               <span>Allow executing mutating prefix statements</span>
                           </label>
                       </div>` : ''}
                       <button type="button" class="etlsql-studio-btn is-primary" data-explain-statement>Explain this statement</button>
                       <p class="etlsql-studio-outline-note">${hasPrefix
                            ? 'The statements above the cursor run first, because they build the #temp tables the plan reads. EXPLAIN itself does not run the statement it explains.'
                            : 'EXPLAIN builds the plan without running the statement.'}</p>`
                    : '<div class="etlsql-studio-empty-compact">Put the cursor in a query to plan it.</div>'}
                <div data-plan-host></div>
            </section>
            <button type="button" class="etlsql-studio-btn" data-engine-refresh>${_studioIcon('run', 13)} Refresh from cursor</button>`;

        queryElement(hostContext.sidebarContent, '[data-engine-refresh]')?.addEventListener('click', () => void renderEnginePanel());
        queryElement(hostContext.sidebarContent, '[data-explain-statement]')?.addEventListener('click', () => void explainStatementAtCursor());
    }

    async function explainStatementAtCursor() {
        const doc = hostContext.getActiveDoc();
        const scope = hostContext.state.enginePlanScope;
        const host = queryElement(hostContext.sidebarContent, '[data-plan-host]');
        if (!doc || !scope || !host) return;

        const prefixEffects: Array<{ taskId: string; action: string; target: string; line: number }> =
            Array.isArray(scope.prefixEffects) ? scope.prefixEffects : [];
        const hasMutatingPrefix = prefixEffects.length > 0;
        const allowMutating = queryElement<HTMLInputElement>(hostContext.sidebarContent, '[data-allow-mutating-prefix]')?.checked;

        if (hasMutatingPrefix && !allowMutating) {
            const effectSummary = prefixEffects
                .map(e => `${e.action} ${e.target || ''} (line ${e.line})`)
                .join(', ');
            host.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Refused: Mutating prefix statements</strong><p>Statements above the cursor modify persistent data (${_escapeHtml(effectSummary)}). Check "Allow executing mutating prefix statements" to confirm explicit intent before planning.</p></div>`;
            return;
        }

        const statement = String(scope.statementText || '').trim().replace(/;\s*$/, '');
        if (!statement) return;
        const slice = [String(scope.prefixScript || '').trim(), `EXPLAIN ${statement};`]
            .filter(Boolean)
            .join('\n\n');

        host.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Asking the engine for a plan…</div>';
        let response;
        try {
            response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.run, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: hostContext.activeScriptText(), selection: slice }),
            });
        } catch (error) {
            host.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>No plan</strong><p>${_escapeHtml(errorMessage(error) || String(error))}</p></div>`;
            return;
        }

        if (!response.ok) {
            // The host refused, and the reason is the product: an interactive run has limits, and an
            // author who cannot see why one applied will conclude the button is broken.
            const reason = await _readErrorText(response);
            host.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>The engine did not plan this</strong><p>${_escapeHtml(reason)}</p></div>`;
            return;
        }

        const data = await response.json();
        const columns = (data.columns || []).map((column: StudioDynamic) => (typeof column === 'string' ? column : column?.name || ''));
        const rows = data.rows || [];
        if (!rows.length) {
            host.innerHTML = '<div class="etlsql-studio-empty-compact">The engine returned no plan for this statement.</div>';
            return;
        }

        const blocking = rows.filter((row: StudioDynamic) => planCell(row, columns, 'Mode').toUpperCase() === 'BLOCKING').length;
        const spilled = rows.some((row: StudioDynamic) => Number(planCell(row, columns, 'Spill Bytes') || 0) > 0);
        host.innerHTML = `
            <div class="etlsql-studio-plan-summary">
                <span>${rows.length} operator${rows.length === 1 ? '' : 's'} · ${blocking} blocking</span>
                <small>${spilled
                    ? 'An operator spilled to disk. That is where the time is going.'
                    : 'A blocking operator holds rows in memory before it can produce any; a streaming one does not.'}</small>
            </div>
            <ol class="etlsql-studio-plan">${rows.map((row: StudioDynamic) => planOperatorMarkup(row, columns)).join('')}</ol>`;
    }

    /**
     * Applies a diagnostic's one-click repair to the buffer.
     *
     * Applied as a ranged edit through the editor's own transaction, like every other GUI write in
     * Studio, which is what makes the undo offer work: the editor's history already holds the exact
     * inverse. A repair that rewrote the whole document would undo as a whole-document restore and
     * take back whatever the author typed after it.
     *
     * The positions are the diagnostic contract's — zero-based line and column — and are clamped to
     * the buffer as it is now rather than as it was when the diagnostic was produced. An author who
     * has kept typing gets a refusal, not an edit at a stale offset.
     */
    function applyDiagnosticQuickFix(fix: StudioDynamic) {
        if (hostContext.state.isEditorDegraded) {
            _feedback.notify('Quick fixes are disabled in degraded textarea mode.', { title: 'Action Unavailable', tone: 'warning' });
            return;
        }
        const editor = hostContext.state.editorInstance as StudioDynamic;
        const doc = hostContext.getActiveDoc();
        if (!editor || !doc || !fix) return;

        const text = editor.getValue();
        const lines = text.split('\n');
        const lineIndex = Number(fix.startLine);
        if (!Number.isInteger(lineIndex) || lineIndex < 0 || lineIndex >= lines.length
            || Number(fix.endLine) !== lineIndex) {
            _feedback.notify(
                'The script changed after this suggestion was made, so applying it here would edit the wrong place. Analyze again for a fresh one.',
                { title: 'Nothing changed', tone: 'warning' });
            return;
        }

        const line = lines[lineIndex];
        const start = Math.max(0, Math.min(line.length, Number(fix.startColumn) || 0));
        const end = Math.max(start, Math.min(line.length, Number(fix.endColumn) || start));
        const before = text;
        lines[lineIndex] = line.slice(0, start) + String(fix.replacement ?? '') + line.slice(end);
        const after = lines.join('\n');
        if (after === before) return;

        const changed = editor.replaceAll?.(after) ?? editor.setValue?.(after);
        if (changed) editor.revealRange?.(changed.from, changed.to);
        doc.content = after;
        doc.isDirty = true;
        doc.contentRevision = ((doc.contentRevision as number) || 0) + 1;
        hostContext.renderTabs();
        hostContext.offerUndo(fix.title || 'Quick fix', { document: doc, before, after });
        editor.analyze?.();
    }

    return { renderEnginePanel, applyDiagnosticQuickFix };
}
