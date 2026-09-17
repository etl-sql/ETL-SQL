/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * run-results.js — split out of designer.js, TODO.md §2.
 * Script run results: trace normalization, the results panel, and CSV/XLSX export.
 */

import { escapeHtml } from './designer-util.js';
import { renderCompactDag, updateDagLines } from './dag.js';

export function redactSecrets(text?: string | null): string;
export function redactSecrets<T>(text: T): T;
export function redactSecrets(text: unknown): unknown {
    if (!text || typeof text !== 'string') return text;
    return text
        .replace(/\b(USE\s+PASSWORD|PASSWORD|PWD|SECRET_KEY|SECRETKEY|APIKEY|API_KEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|CLIENT_SECRET|CLIENTSECRET|CREDENTIAL|PRIVATEKEY|PRIVATE_KEY|ACCESS_KEY|ACCESSKEY|ACCOUNT_KEY|ACCOUNTKEY|SAS_TOKEN|PASSPHRASE|KEY_FILE)\s*=\s*(['"]?)[^'"\s,;)]*\2/gi, '$1 = $2********$2')
        .replace(/\bUSE\s+PASSWORD\s+(?!PROMPT\b)(['"])[^'"\s;]+\1/gi, 'USE PASSWORD $1********$1')
        .replace(/\b(ENC|DPAPI-M|DPAPI|MACHINE|SECRET|CAPABILITY|SHARED):[A-Za-z0-9+/=_:.-]+/gi, '$1:********')
        .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]+/gi, 'Bearer ********');
}

export interface RunTraceEvent {
    type: string;
    [key: string]: unknown;
}

export interface RunDiagnosticQuickFix {
    title: string;
    [key: string]: unknown;
}

export interface RunDiagnosticGuidance {
    summary: string;
    action: string;
    anchor?: string;
    anchorKind?: string;
    docPath?: string;
    quickFix?: RunDiagnosticQuickFix;
}

export interface RunDiagnostic {
    code?: string;
    line?: number;
    startLine?: number;
    startColumn?: number;
    message?: string;
    severity?: string | number;
    source?: string;
    guidance?: RunDiagnosticGuidance;
    [key: string]: unknown;
}

export interface RunPipelineNode {
    id?: string;
    name?: string;
    status?: string;
    rowsProcessed?: number;
    durationMs?: number;
    isParallelBlock?: boolean;
    children?: RunPipelineNode[];
    [key: string]: unknown;
}

export interface RunPerformanceStatement {
    type?: string;
    totalMs?: number;
    [key: string]: unknown;
}

export interface RunPerformanceMetrics {
    executionMs?: number;
    rowsProcessed?: number;
    memoryMb?: number;
    statements?: RunPerformanceStatement[];
    [key: string]: unknown;
}

export interface RunPerformancePayload {
    metrics?: RunPerformanceMetrics;
    [key: string]: unknown;
}

export interface RunResultContext {
    kind?: string;
    label?: string;
    source?: string;
    elapsedMs?: number;
    capped?: boolean;
    byteCapped?: boolean;
    [key: string]: unknown;
}

export interface RunResultSet {
    columns: string[];
    rows: Record<string, unknown>[];
    context?: RunResultContext | null;
}

export interface LineageHopLike {
    targetColumn?: string;
    TargetColumn?: string;
    targetTable?: string;
    TargetTable?: string;
    sourceTables?: string;
    SourceTables?: string;
    sourceColumns?: string;
    SourceColumns?: string;
    transformationKind?: string;
    TransformationKind?: string;
    description?: string;
    Description?: string;
    [key: string]: unknown;
}

export interface RunResultPayload {
    trace?: RunTraceEvent[];
    success?: boolean;
    rows?: Record<string, unknown>[];
    columns?: string[];
    elapsedMs?: number;
    message?: string;
    messages?: (string | { text?: string; message?: string })[];
    diagnostics?: RunDiagnostic[];
    pipeline?: RunPipelineNode[];
    lineage?: unknown[];
    [key: string]: unknown;
}

export function normalizeRunTrace(result?: RunResultPayload | null, script?: string): RunTraceEvent[] {
    if (Array.isArray(result?.trace)) return result.trace;
    const isSuccess = result?.success !== false;
    const rows = Array.isArray(result?.rows) ? result.rows : [];
    const columns = Array.isArray(result?.columns) ? result.columns : [];
    const elapsedMs = Number.isFinite(result?.elapsedMs) ? (result?.elapsedMs as number) : 0;
    const message = redactSecrets(result?.message || (rows.length ? `Returned ${rows.length} rows.` : 'No rows returned.'));

    // Annotated, not inferred: TypeScript would otherwise take the union of these three literals
    // as the element type and report every later `push` of a different event shape as an error.
    /** @type {Array<{type: string, [key: string]: *}>} */
    const trace: RunTraceEvent[] = [
        { type: 'clear', resetHistory: true },
        { type: 'status', status: isSuccess ? 'running' : 'failed' },
        { type: 'message', level: 'sys', text: 'Designer run started.' }
    ];

    if (Array.isArray(result?.messages)) {
        result.messages.forEach(m => {
            const raw = typeof m === 'string' ? m : (m.text || m.message || '');
            trace.push({ type: 'message', level: 'info', text: redactSecrets(raw) });
        });
    }

    if (Array.isArray(result?.diagnostics)) {
        result.diagnostics.forEach(d => {
            const rawMsg = redactSecrets(d.message || '');
            trace.push({ type: 'message', level: d.severity?.toString().toLowerCase() === 'error' ? 'error' : 'warn', text: `[${d.code || 'Error'}] Line ${d.line || 0}: ${rawMsg}` });
        });
    }

    // Prefer the engine's real execution tree (ExecutionResult.ExecutionTree snapshot);
    // fall back to a single summary node for hosts that don't return one yet.
    const pipeline = Array.isArray(result?.pipeline) && result.pipeline.length
        ? result.pipeline
        : [{ id: '1', name: 'Execute script', status: isSuccess ? 'Completed' : 'Failed', rowsProcessed: rows.length, durationMs: elapsedMs, isParallelBlock: false, children: [] }];
    trace.push({ type: 'progress', data: pipeline });

    if (Array.isArray(result?.lineage)) {
        trace.push({ type: 'lineage', data: result.lineage });
    }

    if (isSuccess) {
        trace.push({ type: 'message', level: rows.length ? 'info' : 'warn', text: message });
        trace.push({ type: 'message', level: 'sys', text: redactSecrets(String(script || '').trim().replace(/\s+/g, ' ')).slice(0, 180) });
        trace.push({ type: 'results', columns, rows });
        trace.push({ type: 'performance', metrics: {
            executionMs: elapsedMs,
            rowsProcessed: rows.length,
            memoryMb: 0,
            statements: [{ type: 'SELECT', totalMs: elapsedMs }],
        } });
        trace.push({ type: 'done', exitCode: 0 });
    } else {
        trace.push({ type: 'message', level: 'error', text: message });
        trace.push({ type: 'done', exitCode: 1 });
    }
    return trace;
}

function toXlsxXml(columns: readonly string[], rows: readonly Record<string, unknown>[]): string {
    let xml = `<?xml version="1.0"?><?mso-application progid="Excel.Sheet"?><Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet" xmlns:html="http://www.w3.org/TR/REC-html40"><Worksheet ss:Name="Sheet1"><Table>`;
    // Header Row
    xml += '<Row>';
    columns.forEach(c => {
        xml += `<Cell><ss:Data ss:Type="String">${escapeHtml(c)}</ss:Data></Cell>`;
    });
    xml += '</Row>';
    // Data Rows
    rows.forEach(r => {
        xml += '<Row>';
        columns.forEach(c => {
            const val = r[c] == null ? '' : String(r[c]);
            const type = typeof r[c] === 'number' ? 'Number' : 'String';
            xml += `<Cell><ss:Data ss:Type="${type}">${escapeHtml(val)}</ss:Data></Cell>`;
        });
        xml += '</Row>';
    });
    xml += '</Table></Worksheet></Workbook>';
    return xml;
}

export interface ScriptResultsPanelOptions {
    onNavigate?: ((line: number, column: number) => void) | null;
}

export interface ScriptResultsPanel {
    replay(trace?: readonly RunTraceEvent[] | null): void;
    setDiagnostics(list?: readonly RunDiagnostic[] | null): void;
    startElapsed(): void;
    stopElapsed(): void;
    clear(): void;
    setNavigate(handler?: ((line: number, column: number) => void) | null): void;
    setApplyFix(handler?: ((fix: unknown) => void) | null): void;
    dispose(): void;
}

export function createScriptResultsPanel(container: HTMLElement, { onNavigate = null }: ScriptResultsPanelOptions = {}): ScriptResultsPanel {
    let navigate = onNavigate;
    let applyFix: ((fix: unknown) => void) | null = null;
    let messages: RunTraceEvent[] = [];
    let progress: RunPipelineNode[][] = [];
    let resultSets: RunResultSet[] = [];
    let diagnostics: RunDiagnostic[] = [];
    let performance: RunPerformancePayload | null = null;
    let activeTab = 'results';
    let status = 'idle';
    let resultFilter = '';

    container.className = 'etlsql-script-results';
    container.innerHTML = `
        <div class="etlsql-script-results-tabs">
            <button type="button" data-tab="results">Results</button>
            <button type="button" data-tab="messages">Messages</button>
            <button type="button" data-tab="pipeline">Pipeline</button>
            <button type="button" data-tab="performance">Performance</button>
            <span class="etlsql-script-results-tools" data-result-tools>
                <input type="search" data-result-filter placeholder="Filter results" autocomplete="off">
                <button type="button" data-export="csv">CSV</button>
                <button type="button" data-export="xlsx">Excel</button>
                <button type="button" data-export="json">JSON</button>
            </span>
            <span class="etlsql-script-results-status" data-status>Idle</span>
        </div>
        <div class="etlsql-script-results-body" data-body></div>`;

    const body = container.querySelector('[data-body]') as HTMLElement;
    const statusEl = container.querySelector('[data-status]') as HTMLElement | null;
    const filterEl = container.querySelector('[data-result-filter]') as HTMLInputElement | null;
    const toolsEl = container.querySelector('[data-result-tools]') as HTMLElement | null;

    // A diagnostic that names a line but cannot take you there makes the reader do the lookup by
    // hand. Hosts supply onNavigate; without one the entries stay inert rather than pretending.
    function onDiagnosticActivate(event: Event) {
        // A repair button sits inside the diagnostic row, so it has to be handled before the jump:
        // otherwise clicking "Close the quote" would scroll to the line and change nothing.
        const target = event.target as HTMLElement | null;
        const fixTarget = target?.closest?.('[data-quick-fix]') as HTMLElement | null;
        if (fixTarget) {
            if (event.type === 'keydown' && (event as KeyboardEvent).key !== 'Enter' && (event as KeyboardEvent).key !== ' ') return;
            event.preventDefault();
            event.stopPropagation();
            try {
                applyFix?.(JSON.parse(fixTarget.dataset.quickFix || ''));
            } catch {
                // A malformed payload is a bug here, not something the author can act on. Leaving
                // the row alone is the honest outcome; the diagnostic it explains is still on screen.
            }
            return;
        }
        const jumpTarget = target?.closest?.('[data-jump-line]') as HTMLElement | null;
        if (!jumpTarget) return;
        if (event.type === 'keydown' && (event as KeyboardEvent).key !== 'Enter' && (event as KeyboardEvent).key !== ' ') return;
        event.preventDefault();
        navigate?.(Number(jumpTarget.dataset.jumpLine) || 1, Number(jumpTarget.dataset.jumpColumn) || 1);
    }
    container.addEventListener('click', onDiagnosticActivate);
    container.addEventListener('keydown', onDiagnosticActivate);

    function setTab(tab: string) {
        activeTab = tab;
        render();
        if (tab === 'pipeline') {
            setTimeout(() => {
                const dagCont = body.querySelector('.etlsql-compact-dag');
                if (dagCont) updateDagLines(dagCont);
            }, 50);
        }
    }

    function escape(value: unknown): string {
        return escapeHtml(value);
    }

    let activeLineageColumn: string | null = null;
    let lineageData: LineageHopLike[] = [];

    function renderLineageBar() {
        if (!activeLineageColumn) return '';
        // A column name appears once per hop (m.Users.UserID -> #staging.UserID -> RESULTSET.UserID).
        // The grid shows the final result set, so prefer that entry; a plain find() would report
        // the first intermediate hop instead of the lineage of the column actually clicked.
        const columnMatches = lineageData.filter(e =>
            String(e.targetColumn || e.TargetColumn || '').toLowerCase() === String(activeLineageColumn).toLowerCase()
        );
        const match = columnMatches.find(e =>
            String(e.targetTable || e.TargetTable || '').toUpperCase() === 'RESULTSET'
        ) ?? columnMatches[0];
        let pathStr: string;
        if (match) {
            const srcT = match.sourceTables || match.SourceTables || 'source';
            const srcC = match.sourceColumns || match.SourceColumns || activeLineageColumn;
            const tgtT = match.targetTable || match.TargetTable || 'result';
            const kind = match.transformationKind || match.TransformationKind ? ` [${match.transformationKind || match.TransformationKind}]` : '';
            const desc = match.description || match.Description ? ` — ${match.description || match.Description}` : '';
            pathStr = `${escape(srcT)}.${escape(srcC)} ➔ ${escape(tgtT)}.${escape(activeLineageColumn)}${escape(kind)}${escape(desc)}`;
        } else {
            // Say so rather than drawing a plausible-looking path. A guessed
            // source.db ➔ #staging ➔ result chain reads as recorded lineage and would be
            // trusted as such — the whole point of the panel is that it reflects the run.
            pathStr = `<em>no lineage recorded for ${escape(activeLineageColumn)}</em>`;
        }
        return `
            <div class="etlsql-lineage-bar" style="background:var(--portal-accent-soft, rgba(88,166,255,0.15)); border:1px solid var(--portal-border, #30363d); padding:4px 10px; font-size:11px; display:flex; align-items:center; justify-content:space-between; margin-bottom:6px; border-radius:4px;">
                <span>📍 <strong>Lineage:</strong> ${pathStr}</span>
                <button type="button" data-close-lineage style="background:none; border:none; color:var(--portal-text-muted, #9da7b1); cursor:pointer; font-size:11px; font-weight:bold;">✕</button>
            </div>`;
    }

    function renderResults() {
        const latest = resultSets[resultSets.length - 1];
        if (!latest) return '<div class="etlsql-script-results-empty">No results yet.</div>';
        const columns = Array.isArray(latest.columns) ? latest.columns : [];
        const rows = Array.isArray(latest.rows) ? latest.rows : [];
        if (!columns.length) return '<div class="etlsql-script-results-empty">No result grid.</div>';
        const filteredRows = filterRows(rows, columns, resultFilter);
        // Bounded so an uncapped producer cannot hang the panel; the label says when it truncated.
        const { visible, label: count } = resultRenderWindow(filteredRows, rows.length, !!resultFilter);
        const head = columns.map(c => `<th data-column="${escape(c)}" style="cursor:pointer;" title="Click for column lineage">${escape(c)}</th>`).join('');
        const dataRows = visible.map(row => `<tr>${columns.map(c => `<td data-column="${escape(c)}" style="cursor:pointer;" title="Click for cell lineage">${escape(formatResultCell(row?.[c]))}</td>`).join('')}</tr>`).join('');
        const context = latest.context;
        const contextBar = context ? `<div class="etlsql-result-context">
            <span class="etlsql-result-context-badge" data-kind="${escape(context.kind || 'run')}">${escape(context.label || 'Run result')}</span>
            <strong>${escape(context.source || '')}</strong>
            <span>${Number(context.elapsedMs || 0).toLocaleString()} ms</span>
            ${(context.capped || context.byteCapped) ? '<span class="etlsql-result-context-limit">bounded preview</span>' : ''}
        </div>` : '';
        return `${renderLineageBar()}${contextBar}<div class="etlsql-script-results-count">${escape(count)}</div><table><thead><tr>${head}</tr></thead><tbody>${dataRows || `<tr><td colspan="${columns.length}">No rows</td></tr>`}</tbody></table>`;
    }

    function diagnosticLevel(d: RunDiagnostic): string {
        const severity = String(d?.severity ?? '').toLowerCase();
        return (severity.includes('error') || d?.severity === 0) ? 'error' : 'warn';
    }

    /**
     * The beginner-facing half of a diagnostic: what went wrong in a sentence, what to do about it,
     * the card it belongs to, a reference page, and — where exactly one repair is correct — a button
     * that makes it.
     *
     * The parser's own message stays on the row above. It is the precise statement of the failure
     * and an experienced author reads it first; the translation is an addition to it, never a
     * replacement, because a message that paraphrases away the detail is worse for the person who
     * did understand the original.
     */
    function renderGuidanceBlock(d: RunDiagnostic): string {
        const guidance = d?.guidance;
        if (!guidance) return '';
        const anchor = guidance.anchor
            ? `<span class="etlsql-script-guidance-anchor">${escape(`${(guidance.anchorKind || 'object').toLowerCase()} ${guidance.anchor}`)}</span>`
            : '';
        const fix = guidance.quickFix
            ? `<button type="button" class="etlsql-script-guidance-fix" data-quick-fix="${escape(JSON.stringify(guidance.quickFix))}">${escape(guidance.quickFix.title)}</button>`
            : '';
        const doc = guidance.docPath
            ? `<a class="etlsql-script-guidance-doc" href="${escape(guidance.docPath)}" target="_blank" rel="noreferrer">Reference</a>`
            : '';
        return `<div class="etlsql-script-guidance">
                <strong>${escape(guidance.summary)}</strong>${anchor}
                <span>${escape(guidance.action)}</span>
                <div class="etlsql-script-guidance-actions">${fix}${doc}</div>
            </div>`;
    }

    function renderDiagnosticsBlock() {
        if (!diagnostics.length) return '';
        const rows = diagnostics.map(d => {
            // Analyzer positions are 0-based; the editor gutter shows them 1-based.
            const line = (Number.isFinite(d.startLine) ? (d.startLine as number) : 0) + 1;
            const column = (Number.isFinite(d.startColumn) ? (d.startColumn as number) : 0) + 1;
            return `<div class="etlsql-script-message etlsql-script-message-jump" role="button" tabindex="0" data-level="${diagnosticLevel(d)}" data-jump-line="${line}" data-jump-column="${column}" title="Go to line ${line}"><span>${escape(d.code || d.source || 'lint')}</span>${escape(`${line}:${column}  ${d.message || ''}`)}</div>${renderGuidanceBlock(d)}`;
        }).join('');
        return `<div class="etlsql-script-message-group"><div class="etlsql-script-message-group-title">Diagnostics</div>${rows}</div>`;
    }

    function renderMessages() {
        if (!messages.length && !diagnostics.length) return '<div class="etlsql-script-results-empty">No messages yet.</div>';
        const runMessages = messages.length
            ? `<div class="etlsql-script-message-list">${messages.map(m => `<div class="etlsql-script-message" data-level="${escape(m.level || 'info')}"><span>${escape(m.level || 'info')}</span>${escape(m.text || '')}</div>`).join('')}</div>`
            : '';
        return `${renderDiagnosticsBlock()}${runMessages}`;
    }

    function renderPipelineRows(nodes?: readonly RunPipelineNode[] | null, depth = 0): string {
        return (nodes || []).map(node => `
            <tr>
                <td style="padding-left:${8 + depth * 18}px">${escape(node.name || node.id || 'Step')}</td>
                <td>${escape(node.status || '')}</td>
                <td>${Number(node.rowsProcessed || 0).toLocaleString()}</td>
                <td>${Number(node.durationMs || 0).toLocaleString()} ms</td>
            </tr>${renderPipelineRows(node.children, depth + 1)}`).join('');
    }

    function renderPipeline() {
        if (!progress.length) return '<div class="etlsql-script-results-empty">No pipeline events yet.</div>';
        const latest = progress[progress.length - 1] || [];
        const dagHtml = renderCompactDag(latest);
        const tableHtml = `<table><thead><tr><th>Step</th><th>Status</th><th>Rows</th><th>Duration</th></tr></thead><tbody>${renderPipelineRows(latest)}</tbody></table>`;
        return `
            <div class="etlsql-pipeline-view" style="display:flex; flex-direction:column; height:100%; overflow:hidden;">
                ${dagHtml}
                <div class="etlsql-pipeline-table-container" style="flex:1; overflow:auto; padding-top:10px;">
                    ${tableHtml}
                </div>
            </div>
        `;
    }

    function renderPerformance() {
        const metrics = performance?.metrics || (performance as RunPerformanceMetrics | null);
        if (!metrics) return '<div class="etlsql-script-results-empty">No performance metrics yet.</div>';
        const statements = Array.isArray(metrics.statements) ? metrics.statements : [];
        return `
            <div class="etlsql-script-perf-summary">
                <div><strong>${Number(metrics.executionMs || 0).toLocaleString()} ms</strong><span>Execution</span></div>
                <div><strong>${Number(metrics.rowsProcessed || 0).toLocaleString()}</strong><span>Rows</span></div>
                <div><strong>${Number(metrics.memoryMb || 0).toLocaleString()} MB</strong><span>Memory</span></div>
            </div>
            <table><thead><tr><th>Statement</th><th>Total</th></tr></thead><tbody>${statements.map(s => `<tr><td>${escape(s.type || 'Statement')}</td><td>${Number(s.totalMs || 0).toLocaleString()} ms</td></tr>`).join('')}</tbody></table>`;
    }

    // Elapsed time ticks next to the status while a run is in flight, so a long run looks
    // busy rather than hung.
    let elapsedTimer: ReturnType<typeof setInterval> | null = null;
    let elapsedStart = 0;

    function formatElapsed(ms: number): string {
        const seconds = ms / 1000;
        return seconds < 10 ? `${seconds.toFixed(1)}s` : `${Math.round(seconds)}s`;
    }

    function paintStatus() {
        if (!statusEl) return;
        statusEl.textContent = elapsedTimer
            ? `${status} · ${formatElapsed(Date.now() - elapsedStart)}`
            : status;
    }

    function renderMessagesTabLabel() {
        const tab = container.querySelector<HTMLElement>('[data-tab="messages"]');
        if (!tab) return;
        const errors = diagnostics.filter(d => diagnosticLevel(d) === 'error').length;
        tab.textContent = diagnostics.length ? `Messages (${diagnostics.length})` : 'Messages';
        tab.dataset.badge = errors ? 'error' : (diagnostics.length ? 'warn' : '');
    }

    function render() {
        container.querySelectorAll<HTMLElement>('[data-tab]').forEach(btn => btn.classList.toggle('active', btn.dataset.tab === activeTab));
        renderMessagesTabLabel();
        paintStatus();
        if (toolsEl) toolsEl.hidden = activeTab !== 'results';
        if (activeTab === 'messages') body.innerHTML = renderMessages();
        else if (activeTab === 'pipeline') body.innerHTML = renderPipeline();
        else if (activeTab === 'performance') body.innerHTML = renderPerformance();
        else body.innerHTML = renderResults();
    }

    function clear() {
        messages = [];
        progress = [];
        resultSets = [];
        performance = null;
        resultFilter = '';
        if (filterEl) filterEl.value = '';
        status = 'Idle';
        render();
    }

    function latestResults() {
        const latest = resultSets[resultSets.length - 1];
        const columns = Array.isArray(latest?.columns) ? latest.columns : [];
        const rows = Array.isArray(latest?.rows) ? latest.rows : [];
        return { columns, rows: filterRows(rows, columns, resultFilter) };
    }

    function exportResults(format?: string) {
        const { columns, rows } = latestResults();
        if (!columns.length) return;
        let text: string;
        let mime: string;
        let ext: string;

        if (format === 'json') {
            text = JSON.stringify(rows, null, 2);
            mime = 'application/json';
            ext = 'json';
        } else if (format === 'xlsx') {
            text = toXlsxXml(columns, rows);
            mime = 'application/vnd.ms-excel';
            ext = 'xls';
        } else {
            text = toCsv(columns, rows);
            mime = 'text/csv';
            ext = 'csv';
        }

        const blob = new Blob([text], { type: `${mime};charset=utf-8` });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `etl-sql-results.${ext}`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }

    function post(message?: RunTraceEvent | null) {
        switch (message?.type) {
            case 'clear':
                clear();
                break;
            case 'status':
                status = (message.status as string) || status;
                // Auto switch tabs during running state
                if (status === 'running') {
                    activeTab = 'pipeline';
                }
                break;
            case 'message':
                messages.push(message);
                break;
            case 'progress':
                progress.push(Array.isArray(message.data) ? (message.data as RunPipelineNode[]) : []);
                break;
            case 'lineage':
                lineageData = Array.isArray(message.data) ? (message.data as LineageHopLike[]) : [];
                break;
            case 'results':
                resultSets.push({
                    columns: (message.columns as string[]) || [],
                    rows: (message.rows as Record<string, unknown>[]) || [],
                    context: (message.context as RunResultContext) || null
                });
                // Focus results tab on success
                activeTab = 'results';
                break;
            case 'performance':
                performance = message as RunPerformancePayload;
                break;
            case 'done':
                status = (message.status as string) ?? (message.exitCode === 0 ? 'Complete' : 'Failed');
                // Switch to Messages if execution failed
                if (message.exitCode !== 0) {
                    activeTab = 'messages';
                }
                break;
            default:
                break;
        }
        render();
        if (activeTab === 'pipeline') {
            setTimeout(() => {
                const dagCont = body.querySelector('.etlsql-compact-dag');
                if (dagCont) updateDagLines(dagCont);
            }, 50);
        }
    }

    // Delegated: the results grid is re-rendered on every trace message, so binding a listener
    // per cell would re-attach hundreds of them per run.
    body.addEventListener('click', (event: MouseEvent) => {
        const target = event.target as HTMLElement | null;
        if (target?.closest?.('[data-close-lineage]')) {
            activeLineageColumn = null;
            render();
            return;
        }
        const cell = target?.closest?.('[data-column]') as HTMLElement | null;
        if (!cell) return;
        activeLineageColumn = cell.dataset.column || null;
        render();
    });

    container.querySelectorAll<HTMLElement>('[data-tab]').forEach(btn => btn.addEventListener('click', () => setTab(btn.dataset.tab || '')));
    filterEl?.addEventListener('input', () => {
        resultFilter = filterEl.value || '';
        render();
    });
    container.querySelectorAll<HTMLElement>('[data-export]').forEach(btn => btn.addEventListener('click', () => exportResults(btn.dataset.export)));

    // Window resize handler for SVG updating
    const onResize = () => {
        if (activeTab === 'pipeline') {
            const dagCont = body.querySelector('.etlsql-compact-dag');
            if (dagCont) updateDagLines(dagCont);
        }
    };
    window.addEventListener('resize', onResize);

    clear();
    return {
        replay(trace?: readonly RunTraceEvent[] | null) {
            for (const message of (Array.isArray(trace) ? trace : [])) post(message);
        },
        // Linter/parser diagnostics belong to the buffer, not to a run, so they are
        // held separately from run messages and survive clear().
        setDiagnostics(list?: readonly RunDiagnostic[] | null) {
            diagnostics = Array.isArray(list) ? (list as RunDiagnostic[]) : [];
            render();
        },
        startElapsed() {
            elapsedStart = Date.now();
            if (elapsedTimer !== null) clearInterval(elapsedTimer);
            elapsedTimer = setInterval(paintStatus, 100);
            paintStatus();
        },
        stopElapsed() {
            if (elapsedTimer !== null) clearInterval(elapsedTimer);
            elapsedTimer = null;
            paintStatus();
        },
        clear,
        /** Sets the jump-to-line handler used by clickable diagnostics. */
        setNavigate(handler?: ((line: number, column: number) => void) | null) {
            navigate = typeof handler === 'function' ? handler : null;
        },
        /**
         * Sets the handler a quick fix is applied through. The panel does not own the buffer, so
         * without one the repair buttons stay inert rather than pretending to work — the same rule
         * the jump handler already follows.
         */
        setApplyFix(handler?: ((fix: unknown) => void) | null) {
            applyFix = typeof handler === 'function' ? handler : null;
        },
        dispose() {
            if (elapsedTimer !== null) clearInterval(elapsedTimer);
            elapsedTimer = null;
            window.removeEventListener('resize', onResize);
            container.removeEventListener('click', onDiagnosticActivate);
            container.removeEventListener('keydown', onDiagnosticActivate);
            container.replaceChildren();
        },
    };
}

/**
 * Rows the grid will build DOM for in one pass.
 *
 * Not every producer bounds its result set: the Workstation and Portal run paths cap at 100/1000,
 * but the VS Code REPL streams whatever the CLI evaluated, so `SELECT * FROM big_table` arrives
 * whole. Rendering that as a single HTML string hangs the panel. Export is unaffected because it
 * reads the filtered rows directly rather than what was drawn.
 */
export const MAX_RENDERED_ROWS = 5000;

export interface ResultRenderWindow {
    visible: Record<string, unknown>[];
    truncated: boolean;
    label: string;
}

/**
 * Splits filtered rows into what to draw and what to say about it. Pure so the cap is testable
 * without a DOM — the point is that a truncated grid says so rather than quietly showing less.
 */
export function resultRenderWindow(
    filteredRows?: readonly Record<string, unknown>[] | null,
    totalRows?: number | null,
    isFiltered?: boolean | null,
    cap: number = MAX_RENDERED_ROWS
): ResultRenderWindow {
    const filtered = Array.isArray(filteredRows) ? filteredRows : [];
    const total = Number.isFinite(totalRows) ? (totalRows as number) : filtered.length;
    const truncated = filtered.length > cap;
    const visible = truncated ? filtered.slice(0, cap) : [...filtered];

    const plural = (n: number) => `${n.toLocaleString()} row${n === 1 ? '' : 's'}`;
    let label: string;
    if (truncated) {
        label = isFiltered
            ? `showing first ${plural(visible.length)} of ${filtered.length.toLocaleString()} matched (${plural(total)} total)`
            : `showing first ${plural(visible.length)} of ${plural(total)}`;
    } else {
        label = isFiltered ? `${filtered.length.toLocaleString()} of ${plural(total)}` : plural(total);
    }

    return { visible, truncated, label };
}

// Exported for scripts/test-result-grid-ui.mjs. These carry the result grid's behaviour — what the
// filter box matches, how a value becomes display text, what CSV export writes, and how many rows
// are drawn — and are pure, so they are testable without a DOM. The rendering around them is not.
export function filterRows(
    rows: readonly Record<string, unknown>[],
    columns: readonly string[],
    filter?: string | null
): Record<string, unknown>[] {
    const term = String(filter || '').trim().toLowerCase();
    if (!term) return rows as Record<string, unknown>[];
    return rows.filter(row => columns.some(c => formatResultCell(row?.[c]).toLowerCase().includes(term)));
}

export function toCsv(columns: readonly string[], rows: readonly Record<string, unknown>[]): string {
    const escapeCsv = (value: unknown) => {
        const text = formatResultCell(value);
        return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    };
    return [
        columns.map(escapeCsv).join(','),
        ...rows.map(row => columns.map(c => escapeCsv(row?.[c])).join(',')),
    ].join('\r\n');
}

export function formatResultCell(value: unknown): string {
    if (value == null) return '';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
}

export interface DataPreviewSourceLike {
    sourceKind?: string;
    [key: string]: unknown;
}

export interface DataPreviewPayload {
    script: string | null;
    documentUri: string;
    [key: string]: unknown;
}

export function buildDataPreviewPayload(
    source?: DataPreviewSourceLike | null,
    script?: string | null,
    documentUri?: string | null
): DataPreviewPayload {
    const kind = source?.sourceKind;
    return {
        ...source,
        // A governed source preview never sends the editor buffer: the server builds the SELECT
        // after ACL-scoped schema validation. Temp preview needs the buffer only to recreate the
        // read-only prefix that materializes the chosen #table.
        script: kind === 'temp' ? String(script || '') : null,
        documentUri: documentUri || 'portal-designer',
    };
}

export function editLeaseRetryDelay(expiresAt: string | number | Date, now: number = Date.now()): number {
    const expiry = new Date(expiresAt).valueOf();
    if (!Number.isFinite(expiry)) return 30_000;
    return Math.min(60_000, Math.max(5_000, expiry - now + 1_000));
}
