/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/triage-ui.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { esc, fmtDateTime, fmtTime, fmtDuration, fmtMetricDuration, fmtBytes, formatOverdue } from './triage-format.js';
import { isObject } from './http-error.js';
import { readTriageBoard, readTriageRunDetail } from './triage-responses.js';
export { formatOverdue } from './triage-format.js';
export function renderSummary(board) {
    const chip = (value, label, cls) => `
    <div class="orch-stat-chip ${cls}">
      <span class="orch-stat-value">${Number(value ?? 0).toLocaleString()}</span>
      <span class="orch-stat-label">${esc(label)}</span>
    </div>`;
    // Missed sits beside failed rather than below it: a job that never started is not a lesser
    // problem than one that started and broke, and it is the one an all-green failure list hides.
    return `
    <div class="orch-stats-bar triage-summary">
      ${chip(board.failureCount, 'Failed', 'danger')}
      ${chip(board.incidentCount, 'Incidents', 'danger')}
      ${chip(board.missedCount, 'Missed', 'danger')}
      ${chip(board.runningCount, 'Running', 'running')}
    </div>`;
}
/**
 * Deep link into the lineage catalog's impact view for one job. This is the answer SSISDB
 * structurally cannot give: a failed load is not just a red row, it is a set of tables downstream
 * consumers are about to read as if they were fresh.
 *
 * Parameters ride in the query string, not the hash — the governance hash router lowercases the
 * whole route and would corrupt a case-sensitive name.
 */
export function impactUrl(jobName) {
    const params = new URLSearchParams({
        impactKind: 'job',
        impactName: String(jobName ?? ''),
        impactDirection: 'downstream',
    });
    return `/index.html?${params.toString()}#governance/impact`;
}
export function quarantineUrl(jobName, targetTable) {
    const params = new URLSearchParams();
    if (jobName)
        params.set('jobName', String(jobName));
    if (targetTable)
        params.set('q', String(targetTable));
    return `/index.html?${params.toString()}#governance/quarantine`;
}
export function renderRunEvidence(detailState, expected) {
    if (isObject(detailState) && detailState.status === 'loading') {
        return '<div class="triage-evidence-state" role="status">Loading durable run evidence…</div>';
    }
    if (isObject(detailState) && detailState.status === 'error') {
        return `<div class="triage-evidence-state danger" role="status"><strong>Evidence unavailable.</strong> ${esc(detailState.message)}</div>`;
    }
    if (detailState == null)
        return '<div class="triage-evidence-state">Open a run to load its durable evidence.</div>';
    let detail;
    try {
        detail = readTriageRunDetail(detailState, expected);
    }
    catch {
        return '<div class="triage-evidence-state danger" role="status"><strong>Evidence unavailable.</strong> The response is incomplete or does not match this run.</div>';
    }
    const run = detail.run;
    const integrity = run.hashMatched === false
        ? { badge: 'badge-stale', label: 'MISMATCH', text: 'The executed script differs from its registered hash.' }
        : run.hashMatched === true
            ? { badge: 'badge-ok', label: 'MATCHED', text: 'The executed script matches its registered hash.' }
            : { badge: 'badge-neutral', label: 'NOT PINNED', text: 'No registered hash was available for comparison.' };
    const quality = detail.qualityFailures;
    const qualityBody = quality.length > 0
        ? `<table class="data-table triage-evidence-table">
         <thead><tr><th>Target / column</th><th>Rule</th><th>Action</th><th>Failed</th><th>Owner</th></tr></thead>
         <tbody>${quality.map(q => {
            const isQuarantine = String(q.action || '').toUpperCase() === 'QUARANTINE';
            const link = isQuarantine
                ? ` <a class="triage-impact-link" href="${esc(quarantineUrl(run.jobName, q.targetTable))}" title="Review quarantined rows for ${esc(q.targetTable || '')}" style="margin-left:6px;font-size:0.82em;">Review rows →</a>`
                : '';
            return `<tr>
             <td>${esc(q.targetTable || '#temp')} / ${esc(q.columnName)}</td>
             <td><code>${esc(q.rule)}</code></td>
             <td><span class="badge badge-warning">${esc(q.action)}</span>${link}</td>
             <td>${Number(q.failureCount || 0).toLocaleString()}</td>
             <td>${esc(q.owner || '—')}</td>
           </tr>`;
        }).join('')}</tbody>
       </table>`
        : '<p class="triage-evidence-empty">No normalized quality-rule failures were recorded.</p>';
    const statements = detail.statements;
    const statementBody = statements.length > 0
        ? `<table class="data-table triage-evidence-table">
         <thead><tr><th>#</th><th>Normalized statement</th><th>Duration</th><th>Rows</th><th>Wait</th><th>Spill</th></tr></thead>
         <tbody>${statements.map((s, i) => `<tr class="${s.failed ? 'triage-statement-failed' : ''}">
           <td>${i + 1}</td>
           <td><code>${esc(s.statement)}</code>${s.failed ? ' <span class="badge badge-error">failed</span>' : ''}</td>
           <td>${fmtMetricDuration(s.duration_ms)}</td>
           <td>${Number(s.rows_processed || 0).toLocaleString()}</td>
           <td>${fmtMetricDuration((Number(s.queue_wait_ms) || 0) + (Number(s.lock_wait_ms) || 0))}</td>
           <td>${fmtBytes(s.spilled_bytes)}</td>
         </tr>`).join('')}</tbody>
       </table>`
        : '<p class="triage-evidence-empty">No statement timeline was retained for this run.</p>';
    return `<div class="triage-evidence-grid">
    <section class="triage-evidence-rail integrity">
      <h4>Script integrity</h4>
      <p><span class="badge ${integrity.badge}">${integrity.label}</span> ${esc(integrity.text)}</p>
      <dl><dt>Runtime hash</dt><dd><code>${esc(run.scriptHashAtRunTime || 'not recorded')}</code></dd></dl>
    </section>
    <section class="triage-evidence-rail quality"><h4>Quality failures (${quality.length})</h4>${qualityBody}</section>
    <section class="triage-evidence-rail timeline"><h4>Statement timeline (${statements.length})</h4>${statementBody}</section>
  </div>`;
}
/** Render one validated run and its independently loaded evidence. */
function renderRunRow(run, { evidenceOpen = false, evidence, pending = false } = {}) {
    const qLink = (run.rowsQuarantined > 0 || run.dataQualityFailures)
        ? `<a class="triage-impact-link" href="${esc(quarantineUrl(run.jobName))}" title="Review quarantined rows in Quarantine Queue">Quarantine →</a>`
        : '';
    const dq = run.dataQualityFailures
        ? `<span class="badge badge-warning" title="Per-rule failure counts">${esc(run.dataQualityFailures)}</span>`
        : '';
    // A script that changed between the last good run and this one is the first thing to suspect,
    // so the mismatch travels with the run rather than hiding in a detail panel.
    const drift = run.hashMatched === false
        ? '<span class="badge badge-stale" title="Script differs from the registered hash">script changed</span>'
        : '';
    const row = `
    <tr>
      <td>${esc(run.jobName)}</td>
      <td><span class="badge badge-error">${esc(run.status)}</span></td>
      <td>${fmtTime(run.startTime)}</td>
      <td>${fmtDuration(run.startTime, run.endTime)}</td>
      <td>${Number(run.rowsProcessed ?? 0).toLocaleString()}</td>
      <td><div style="display:flex;flex-direction:column;gap:3px;align-items:flex-start;">${dq}${qLink}${drift}</div></td>
      <td>
        <a class="triage-impact-link" href="${esc(impactUrl(run.jobName))}"
           title="What is downstream of this job">Impact →</a>
      </td>
      <td class="triage-actions-cell">
        <button class="btn btn-outline btn-xs triage-rerun-one" type="button" data-job="${esc(run.jobName)}"
                title="Run '${esc(run.jobName)}' individually" ${pending ? 'disabled' : ''}>Run now</button>
        <button class="btn btn-outline btn-xs triage-run-evidence" type="button" data-run="${Number(run.id)}"
                aria-expanded="${evidenceOpen ? 'true' : 'false'}">${evidenceOpen ? 'Close evidence' : 'Evidence'}</button>
      </td>
    </tr>`;
    return evidenceOpen
        ? `${row}<tr class="triage-evidence-row"><td colspan="8">${renderRunEvidence(evidence, run)}</td></tr>`
        : row;
}
/**
 * One incident: the shared error, who it hit, and the runs behind it. `expanded` controls the run
 * table, `selected` the re-run checkbox — both are owned by the host page so this stays pure.
 */
export function renderIncident(incident, { expanded = false, selected = false, index = 0, openRuns = new Set(), details = new Map(), pending = false } = {}) {
    const jobs = incident.jobNames || [];
    const shown = jobs.slice(0, 6).map(j => `
    <span class="triage-job-chip">
      <span>${esc(j)}</span>
      <button class="triage-chip-play triage-rerun-one" type="button" data-job="${esc(j)}"
              title="Run '${esc(j)}' individually" aria-label="Run '${esc(j)}' individually" ${pending ? 'disabled' : ''}>▶</button>
    </span>`).join(' ');
    const more = jobs.length > 6 ? ` <span class="triage-more">+${jobs.length - 6} more</span>` : '';
    const span = incident.firstSeen === incident.lastSeen
        ? fmtTime(incident.lastSeen)
        : `${fmtTime(incident.firstSeen)} – ${fmtTime(incident.lastSeen)}`;
    return `
    <article class="triage-incident${expanded ? ' expanded' : ''}" data-incident="${index}">
      <div class="triage-incident-head">
        <label class="triage-select">
          <input type="checkbox" class="triage-incident-check" data-incident="${index}"
                 ${selected ? 'checked' : ''} data-signature="${esc(incident.signature)}" aria-label="Select ${esc(jobs.length)} job(s) for re-run">
        </label>
        <button class="triage-incident-toggle" type="button" data-incident="${index}"
                data-signature="${esc(incident.signature)}" aria-expanded="${expanded ? 'true' : 'false'}">
          <span class="triage-incident-count badge badge-error">${Number(incident.failureCount) || 0}×</span>
          <span class="triage-incident-error">${esc(incident.sampleError)}</span>
          <span class="triage-incident-chevron" aria-hidden="true">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="9 18 15 12 9 6"></polyline>
            </svg>
          </span>
        </button>
        <span class="triage-incident-when">${span}</span>
      </div>
      ${!expanded ? `<div class="triage-incident-jobs">${shown}${more}</div>` : ''}
      ${expanded ? `
      <div class="triage-incident-runs">
        <table class="data-table">
          <thead><tr><th>Job</th><th>Status</th><th>Started</th><th>Duration</th><th>Rows</th><th>Quality</th><th>Downstream</th><th>Run</th></tr></thead>
          <tbody>${(incident.runs || []).map(run => renderRunRow(run, {
        evidenceOpen: openRuns.has(Number(run.id)),
        evidence: details.get(Number(run.id)), pending
    })).join('')}</tbody>
        </table>
      </div>` : ''}
    </article>`;
}
export function renderMissed(missed, pending = false) {
    if (!missed || missed.length === 0)
        return '';
    const rows = missed.map(m => `
    <tr>
      <td>${esc(m.displayName || m.jobName)}</td>
      <td>${fmtDateTime(m.dueAt)}</td>
      <td><span class="badge badge-warning">${formatOverdue(m.overdueMinutes)} late</span></td>
      <td>${m.lastRun ? fmtDateTime(m.lastRun) : 'never'}</td>
      <td class="triage-actions-cell">
        <button class="btn btn-outline btn-xs triage-rerun-one" type="button" data-job="${esc(m.jobName)}" ${pending ? 'disabled' : ''}>Run now</button>
        <a class="triage-impact-link" href="${esc(impactUrl(m.jobName))}"
           title="What is downstream of this job">Impact →</a>
      </td>
    </tr>`).join('');
    return `
    <section class="triage-section">
      <h3>Missed runs (${missed.length})</h3>
      <p class="triage-hint">
        Enabled jobs whose scheduled time passed without the scheduler claiming them. A missed run
        writes no history row, so it never appears in a failure list.
      </p>
      <table class="data-table">
        <thead><tr><th>Job</th><th>Due</th><th>Overdue</th><th>Last run</th><th></th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </section>`;
}
function renderRunning(running) {
    if (!running || running.length === 0)
        return '';
    const rows = running.map(r => `
    <tr>
      <td>${esc(r.jobName)}</td>
      <td><span class="badge badge-running">RUNNING</span></td>
      <td>${fmtDateTime(r.startTime)}</td>
      <td>${fmtDuration(r.startTime, new Date().toISOString())}</td>
    </tr>`).join('');
    return `
    <section class="triage-section">
      <h3>In flight (${running.length})</h3>
      <table class="data-table">
        <thead><tr><th>Job</th><th>Status</th><th>Started</th><th>Elapsed</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </section>`;
}
/**
 * The whole board. `state` carries only view concerns — which incidents are expanded and which are
 * selected — so re-rendering after a poll never loses the operator's place.
 */
export function renderTriageBoard(value, state = {}) {
    let board;
    try {
        board = readTriageBoard(value);
    }
    catch {
        return '<div class="triage-empty" role="status">Triage data is unavailable.</div>';
    }
    const expanded = state.expanded instanceof Set ? state.expanded : new Set();
    const selected = state.selected instanceof Set ? state.selected : new Set();
    const openRuns = state.openRuns instanceof Set ? state.openRuns : new Set();
    const details = state.details instanceof Map ? state.details : new Map();
    const incidents = board.incidents || [];
    const truncated = board.truncated
        ? `<div class="orch-offline-banner triage-truncated">
         Showing the most recent runs only — the history read hit its row cap, so these counts are a
         floor, not a total.
       </div>`
        : '';
    const quiet = incidents.length === 0 && (board.missed || []).length === 0;
    const body = quiet
        ? `<div class="triage-empty">
         <strong>Nothing to triage.</strong>
         No failures and no missed runs in the last ${esc(board.lookbackHours)} hours.
       </div>`
        : `
      ${incidents.length > 0 ? `
      <section class="triage-section">
        <div class="triage-section-head">
          <h3>Failures (${Number(board.failureCount) || 0} across ${incidents.length} incident${incidents.length === 1 ? '' : 's'})</h3>
          <button class="btn ${selected.size > 0 ? 'btn-primary' : 'btn-outline'} btn-sm triage-rerun-selected" type="button" ${selected.size === 0 || state.pending ? 'disabled' : ''}>
            Re-run selected${selected.size > 0 ? ` (${selected.size})` : ''}
          </button>
        </div>
        ${incidents.map((incident, i) => renderIncident(incident, {
            expanded: expanded.has(i),
            selected: selected.has(i),
            index: i,
            openRuns,
            details, pending: state.pending
        })).join('')}
      </section>` : ''}
      ${renderMissed(board.missed, state.pending)}`;
    return `
    ${renderSummary(board)}
    ${truncated}
    <div class="triage-meta">
      Last ${esc(board.lookbackHours)}h · generated ${fmtDateTime(board.generatedAt)}
    </div>
    ${body}
    ${renderRunning(board.running)}`;
}
/** Job names behind the selected incidents, de-duplicated — what a bulk re-run actually submits. */
export function selectedJobNames(value, selected) {
    let board;
    try {
        board = readTriageBoard(value);
    }
    catch {
        return [];
    }
    const names = new Map();
    for (const index of selected) {
        for (const job of board.incidents[index]?.jobNames ?? []) {
            if (!names.has(job.toUpperCase()))
                names.set(job.toUpperCase(), job);
        }
    }
    return [...names.values()];
}
