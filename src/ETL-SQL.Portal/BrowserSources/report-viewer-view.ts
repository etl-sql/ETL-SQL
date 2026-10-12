import { isObject } from './http-error.js';
import { escapeHtml as esc, formatBuiltAt, timeAgo } from './report-format.js';

/** Response readers and markup for the report viewer. Pure, so they can be checked without a browser. */
export type ViewerReport = Pick<ReportDto, 'id' | 'name' | 'description' | 'hasSnapshot'> & {
    /** The report's rows depend on who is looking: it is shown live and never has a snapshot. */
    perViewer: boolean;
};
export interface ViewerSnapshot { builtAt: string; isStale: boolean }
export type RunState = { status: 'Running' } | { status: 'Completed' } | { status: 'Failed' | 'Cancelled'; reason: string };

export function readViewerReport(value: unknown, id: number): ViewerReport {
    if (!isObject(value) || value.id !== id || typeof value.name !== 'string' || !value.name
        || (value.description != null && typeof value.description !== 'string') || typeof value.hasSnapshot !== 'boolean')
        throw new TypeError('The report response is unavailable or incomplete.');
    return { id, name: value.name, description: value.description, hasSnapshot: value.hasSnapshot, perViewer: value.perViewer === true };
}

export function readViewerSnapshot(value: unknown): ViewerSnapshot {
    if (!isObject(value) || typeof value.builtAt !== 'string' || !Number.isFinite(Date.parse(value.builtAt))
        || (value.isStale != null && typeof value.isStale !== 'boolean'))
        throw new TypeError('The snapshot response is unavailable or incomplete.');
    return { builtAt: value.builtAt, isStale: value.isStale === true };
}

export function readAcceptedRun(value: unknown): string {
    if (!isObject(value) || typeof value.jobId !== 'string' || !value.jobId)
        throw new TypeError('The run was not confirmed: the server did not return a job.');
    return value.jobId;
}

/** A status this page does not know is still "running": only the three terminal states end the wait. */
export function readRunState(value: unknown, jobId: string): RunState {
    if (!isObject(value) || value.jobId !== jobId || typeof value.status !== 'string' || !value.status
        || (value.error != null && typeof value.error !== 'string'))
        throw new TypeError('The run status response is unavailable or incomplete.');
    if (value.status === 'Completed') return { status: 'Completed' };
    if (value.status === 'Failed') return { status: 'Failed', reason: `Report execution failed: ${value.error || 'unknown error'}` };
    if (value.status === 'Cancelled') return { status: 'Cancelled', reason: value.error || 'Report execution was cancelled.' };
    return { status: 'Running' };
}

function frame(kicker: string, report: Pick<ViewerReport, 'name'>, summary: string, meta: string, actions: string, body: string, banner = ''): string {
    return `<div class="report-viewer">
      <div class="viewer-commandbar">
        <button class="btn btn-ghost btn-sm" type="button" id="backBtn" data-viewer-action="back">Back</button>
        <div class="viewer-header-info"><span class="library-kicker">${kicker}</span>
          <h2>${esc(report.name)}</h2><p>${esc(summary)}</p>${meta}</div>
        <div class="viewer-actions">${actions}</div>
      </div>${banner}${body}</div>`;
}
const action = (name: string, label: string, primary = false) =>
    `<button class="btn ${primary ? 'btn-primary' : 'btn-outline'} btn-sm" type="button" id="${name}Btn" data-viewer-action="${name}">${label}</button>`;
const design = (canDesign: boolean) => canDesign ? action('design', 'Design') : '';

/** A report whose rows depend on the reader: shown live, with nothing built or stored to report on. */
export function renderLiveView(report: ViewerReport, canDesign: boolean): string {
    return frame('Report', report, report.description || 'No description provided.',
        '<div class="viewer-meta"><span class="viewer-meta-pill viewer-meta-ready" title="This report shows each reader their own rows. Nothing is stored.">Live · your rows</span></div>',
        action('refresh', 'Refresh', true) + design(canDesign) + action('structure', 'Structure') + action('dependencies', 'Dependencies')
            + action('history', 'History') + action('export', 'Export') + action('sub', 'Subscribe'),
        '<div class="viewer-body viewer-frame-shell" id="reportFrame"><div class="loading-state"><span class="spinner"></span><span>Rendering report…</span></div></div>');
}

export function renderSnapshotView(report: ViewerReport, snapshot: ViewerSnapshot, canDesign: boolean): string {
    const built = formatBuiltAt(snapshot.builtAt), age = timeAgo(snapshot.builtAt);
    return frame('Report', report, report.description || 'No description provided.',
        `<div class="viewer-meta"><span class="viewer-meta-pill">Built ${esc(age)}</span><span class="viewer-meta-pill">${esc(built)}</span>
          ${snapshot.isStale ? '<span class="viewer-meta-pill viewer-meta-warning">Stale</span>' : '<span class="viewer-meta-pill viewer-meta-ready">Ready</span>'}</div>`,
        action('refresh', 'Refresh', true) + design(canDesign) + action('structure', 'Structure') + action('dependencies', 'Dependencies')
            + action('history', 'History') + action('export', 'Export') + action('sub', 'Subscribe'),
        '<div class="viewer-body viewer-frame-shell" id="reportFrame"><div class="loading-state"><span class="spinner"></span><span>Rendering report…</span></div></div>',
        snapshot.isStale ? `<div class="status-banner status-banner-warning"><span class="status-dot" aria-hidden="true"></span>
          <span><b>Needs refresh.</b> This snapshot was built ${esc(age)} (${esc(built)}).</span>
          <button class="btn btn-sm btn-outline" type="button" id="staleRefreshBtn" data-viewer-action="refresh">Refresh Now</button></div>` : '');
}

export function renderFirstRunView(report: ViewerReport, hasParameters: boolean, preflightError: string | null, canDesign: boolean): string {
    return frame('Report', report, report.description || 'No snapshot is available yet.', '',
        `<button class="btn btn-primary btn-sm" type="button" id="execBtn" data-viewer-action="run"${preflightError ? ' disabled' : ''}>Run Report</button>`
            + design(canDesign) + '<button class="btn btn-outline btn-sm" type="button" disabled title="Run the report before subscribing">Subscribe</button>',
        `<div class="viewer-body viewer-body-empty"><div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>${hasParameters ? 'Set parameters, then run' : 'Ready for first run'}</h2>
          <p>One run generates the first snapshot. Export and subscriptions become available after it completes.</p>
          ${preflightError ? `<div class="status-banner status-banner-danger" role="alert"><span>Parameter preflight failed: ${esc(preflightError)}</span></div>` : ''}
          <div id="runParameterFields" class="param-grid"></div><div id="runParameterError" class="error-msg" role="alert"></div>
        </div></div>`);
}

export function renderRunningView(report: Pick<ViewerReport, 'name' | 'description'>): string {
    return frame('Running', report, report.description || 'The report is running. You can leave this view and return when it completes.', '',
        '<button class="btn btn-primary btn-sm" type="button" disabled>Running</button><button class="btn btn-outline btn-sm" type="button" disabled>Export</button>'
            + '<button class="btn btn-outline btn-sm" type="button" disabled>Subscribe</button>',
        `<div class="viewer-body viewer-body-empty"><div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>Waiting for results</h2><p>The viewer will refresh automatically when the run finishes.</p></div></div>`,
        `<div class="status-banner status-banner-info" id="pollBanner" role="status"><span class="spinner"></span>
          <span><b>Running report.</b> Building a fresh snapshot and preserving your current page.</span></div>`);
}

export function renderRunFailureView(report: Pick<ViewerReport, 'name' | 'hasSnapshot'>, message: string): string {
    return frame('Run failed', report, message, '',
        action('retryRun', 'Retry', true) + (report.hasSnapshot ? action('sub', 'Subscribe')
            : '<button class="btn btn-outline btn-sm" type="button" disabled title="A successful snapshot is required">Subscribe</button>'),
        `<div class="viewer-body viewer-body-empty"><div class="empty-state empty-state-panel empty-state-error" role="alert">
          <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
          <h2>Could not build snapshot</h2><p>${esc(message)}</p></div></div>`,
        `<div class="status-banner status-banner-danger"><span class="status-dot" aria-hidden="true"></span>
          <span><b>Run did not complete.</b> Review the message above, then retry when ready.</span></div>`);
}

function historyTable(title: string, className: string, headers: string[], rows: string): string {
    return `<div class="dependency-section history-section"><h4>${esc(title)}</h4>
      ${rows ? `<div class="history-table-scroll"><table class="dependency-table history-table ${className}">
        <thead><tr>${headers.map(header => `<th>${esc(header)}</th>`).join('')}</tr></thead><tbody>${rows}</tbody></table></div>`
        : '<p class="text-muted">No data available.</p>'}</div>`;
}

export function renderReportHistory(value: unknown): string {
    const rows = (list: unknown) => Array.isArray(list) && list.every(isObject) ? list : null;
    const snapshots = isObject(value) ? rows(value.snapshots ?? []) : null, changes = isObject(value) ? rows(value.changes ?? []) : null;
    if (!isObject(value) || typeof value.scriptChanged !== 'boolean' || !snapshots || !changes)
        throw new TypeError('The report history response is unavailable or incomplete.');
    const report = isObject(value.report) ? value.report : {};
    return `<div class="dependency-summary"><span>${esc(report.folderPath || '')} / ${esc(report.name || 'Report')}</span>
        ${value.scriptChanged ? '<span class="viewer-meta-pill viewer-meta-warning">Script changed</span>'
            : '<span class="viewer-meta-pill viewer-meta-ready">Pinned hash current</span>'}</div>
      <div class="history-hash-grid"><div><span>Published hash</span><code>${esc(value.publishedScriptHash || 'Not recorded')}</code></div>
        <div><span>Current hash</span><code>${esc(value.currentScriptHash || 'Unavailable')}</code></div></div>
      ${historyTable('Snapshots', 'snapshot-history-table', ['Built', 'User', 'Hash', 'Runtime Hash'], snapshots.map(item => `<tr>
        <td>${esc(formatBuiltAt(item.builtAt as string))}</td><td>${esc(item.builtBy)}</td>
        <td>${esc(item.hashMatched == null ? 'Unknown' : item.hashMatched ? 'Matched' : 'Changed')}</td>
        <td class="history-code-cell"><code>${esc(item.scriptHashAtRunTime || 'Not recorded')}</code></td></tr>`).join(''))}
      ${historyTable('Changes', 'change-history-table', ['Time', 'Action', 'User', 'Detail'], changes.map(item => `<tr>
        <td>${esc(formatBuiltAt(item.timestamp as string))}</td><td>${esc(item.action)}</td><td>${esc(item.userId ?? '')}</td>
        <td class="history-detail-cell">${esc(item.detail || '')}</td></tr>`).join(''))}`;
}
