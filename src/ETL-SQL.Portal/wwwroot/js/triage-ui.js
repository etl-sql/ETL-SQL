// Rendering for the operations triage board. Pure functions over the /api/orchestrator/triage
// payload: they take data and return HTML, and never fetch or bind events. Event wiring lives with
// the host page, which is what lets the whole surface be driven from fixtures in the UI sandbox.

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c]));
}

function fmtDateTime(value) {
  if (!value) return '—';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString();
}

function fmtTime(value) {
  if (!value) return '—';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleTimeString();
}

/**
 * Renders a duration the way an operator reads one: minutes while that is still meaningful, then
 * hours and days. "1,462 minutes overdue" is technically precise and practically unreadable.
 */
export function formatOverdue(minutes) {
  const m = Math.max(0, Math.round(Number(minutes) || 0));
  if (m < 60) return `${m} min`;
  const hours = m / 60;
  if (hours < 48) return `${hours.toFixed(hours < 10 ? 1 : 0)} h`;
  return `${(hours / 24).toFixed(1)} d`;
}

function fmtDuration(start, end) {
  if (!start || !end) return '—';
  const seconds = (new Date(end) - new Date(start)) / 1000;
  if (!Number.isFinite(seconds) || seconds < 0) return '—';
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
  return `${(seconds / 3600).toFixed(1)}h`;
}

function fmtMetricDuration(milliseconds) {
  const ms = Math.max(0, Number(milliseconds) || 0);
  if (ms < 1000) return `${Math.round(ms)} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(ms < 10_000 ? 1 : 0)} s`;
  return `${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1000)}s`;
}

function fmtBytes(value) {
  let bytes = Math.max(0, Number(value) || 0);
  if (bytes === 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let unit = 0;
  while (bytes >= 1024 && unit < units.length - 1) { bytes /= 1024; unit += 1; }
  return `${bytes.toFixed(unit === 0 || bytes >= 10 ? 0 : 1)} ${units[unit]}`;
}

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

export function renderRunEvidence(detailState) {
  if (detailState?.status === 'loading') {
    return '<div class="triage-evidence-state">Loading durable run evidence…</div>';
  }
  if (detailState?.status === 'error') {
    return `<div class="triage-evidence-state danger"><strong>Evidence unavailable.</strong> ${esc(detailState.message)}</div>`;
  }
  if (!detailState?.run) {
    return '<div class="triage-evidence-state">Open a run to load its durable evidence.</div>';
  }

  const run = detailState.run;
  const integrity = run.hashMatched === false
    ? { badge: 'badge-stale', label: 'MISMATCH', text: 'The executed script differs from its registered hash.' }
    : run.hashMatched === true
      ? { badge: 'badge-ok', label: 'MATCHED', text: 'The executed script matches its registered hash.' }
      : { badge: 'badge-neutral', label: 'NOT PINNED', text: 'No registered hash was available for comparison.' };

  const quality = detailState.qualityFailures || [];
  const qualityBody = quality.length > 0
    ? `<table class="data-table triage-evidence-table">
         <thead><tr><th>Target / column</th><th>Rule</th><th>Action</th><th>Failed</th><th>Owner</th></tr></thead>
         <tbody>${quality.map(q => `<tr>
           <td>${esc(q.targetTable || '#temp')} / ${esc(q.columnName)}</td>
           <td><code>${esc(q.rule)}</code></td>
           <td><span class="badge badge-warning">${esc(q.action)}</span></td>
           <td>${Number(q.failureCount || 0).toLocaleString()}</td>
           <td>${esc(q.owner || '—')}</td>
         </tr>`).join('')}</tbody>
       </table>`
    : '<p class="triage-evidence-empty">No normalized quality-rule failures were recorded.</p>';

  const statements = detailState.statements || [];
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

function renderRunRow(run, { evidenceOpen = false, evidence } = {}) {
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
      <td>${dq}${drift}</td>
      <td>
        <a class="triage-impact-link" href="${esc(impactUrl(run.jobName))}"
           title="What is downstream of this job">Impact →</a>
      </td>
      <td class="triage-actions-cell">
        <button class="btn btn-outline btn-xs triage-rerun-one" type="button" data-job="${esc(run.jobName)}"
                title="Run '${esc(run.jobName)}' individually">Run now</button>
        <button class="btn btn-outline btn-xs triage-run-evidence" type="button" data-run="${Number(run.id)}"
                aria-expanded="${evidenceOpen ? 'true' : 'false'}">${evidenceOpen ? 'Close evidence' : 'Evidence'}</button>
      </td>
    </tr>`;
  return evidenceOpen
    ? `${row}<tr class="triage-evidence-row"><td colspan="8">${renderRunEvidence(evidence)}</td></tr>`
    : row;
}

/**
 * One incident: the shared error, who it hit, and the runs behind it. `expanded` controls the run
 * table, `selected` the re-run checkbox — both are owned by the host page so this stays pure.
 */
export function renderIncident(incident, {
  expanded = false, selected = false, index = 0, openRuns = new Set(), details = new Map()
} = {}) {
  const jobs = incident.jobNames || [];
  const shown = jobs.slice(0, 6).map(j => `
    <span class="triage-job-chip">
      <span>${esc(j)}</span>
      <button class="triage-chip-play triage-rerun-one" type="button" data-job="${esc(j)}"
              title="Run '${esc(j)}' individually" aria-label="Run '${esc(j)}' individually">▶</button>
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
                 ${selected ? 'checked' : ''} aria-label="Select ${esc(jobs.length)} job(s) for re-run">
        </label>
        <button class="triage-incident-toggle" type="button" data-incident="${index}"
                aria-expanded="${expanded ? 'true' : 'false'}">
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
            evidence: details.get(Number(run.id))
          })).join('')}</tbody>
        </table>
      </div>` : ''}
    </article>`;
}

export function renderMissed(missed) {
  if (!missed || missed.length === 0) return '';
  const rows = missed.map(m => `
    <tr>
      <td>${esc(m.displayName || m.jobName)}</td>
      <td>${fmtDateTime(m.dueAt)}</td>
      <td><span class="badge badge-warning">${formatOverdue(m.overdueMinutes)} late</span></td>
      <td>${m.lastRun ? fmtDateTime(m.lastRun) : 'never'}</td>
      <td class="triage-actions-cell">
        <button class="btn btn-outline btn-xs triage-rerun-one" type="button" data-job="${esc(m.jobName)}">Run now</button>
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
  if (!running || running.length === 0) return '';
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
export function renderTriageBoard(board, state = {}) {
  if (!board) {
    return '<div class="triage-empty">Triage data is unavailable.</div>';
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
          <button class="btn ${selected.size > 0 ? 'btn-primary' : 'btn-outline'} btn-sm triage-rerun-selected" type="button" ${selected.size === 0 ? 'disabled' : ''}>
            Re-run selected${selected.size > 0 ? ` (${selected.size})` : ''}
          </button>
        </div>
        ${incidents.map((incident, i) => renderIncident(incident, {
          expanded: expanded.has(i),
          selected: selected.has(i),
          index: i,
          openRuns,
          details
        })).join('')}
      </section>` : ''}
      ${renderMissed(board.missed)}
      ${renderRunning(board.running)}`;

  return `
    ${renderSummary(board)}
    ${truncated}
    <div class="triage-meta">
      Last ${esc(board.lookbackHours)}h · generated ${fmtDateTime(board.generatedAt)}
    </div>
    ${body}`;
}

/** Job names behind the selected incidents, de-duplicated — what a bulk re-run actually submits. */
export function selectedJobNames(board, selected) {
  const names = new Set();
  for (const index of selected || []) {
    const incident = (board.incidents || [])[index];
    for (const name of incident?.jobNames || []) names.add(name);
  }
  return [...names];
}
