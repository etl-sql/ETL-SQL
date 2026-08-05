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

function renderRunRow(run) {
  const dq = run.dataQualityFailures
    ? `<span class="badge badge-warning" title="Per-rule failure counts">${esc(run.dataQualityFailures)}</span>`
    : '';
  // A script that changed between the last good run and this one is the first thing to suspect,
  // so the mismatch travels with the run rather than hiding in a detail panel.
  const drift = run.hashMatched === false
    ? '<span class="badge badge-stale" title="Script differs from the registered hash">script changed</span>'
    : '';
  return `
    <tr>
      <td>${esc(run.jobName)}</td>
      <td><span class="badge badge-error">${esc(run.status)}</span></td>
      <td>${fmtTime(run.startTime)}</td>
      <td>${fmtDuration(run.startTime, run.endTime)}</td>
      <td>${Number(run.rowsProcessed ?? 0).toLocaleString()}</td>
      <td>${dq}${drift}</td>
    </tr>`;
}

/**
 * One incident: the shared error, who it hit, and the runs behind it. `expanded` controls the run
 * table, `selected` the re-run checkbox — both are owned by the host page so this stays pure.
 */
export function renderIncident(incident, { expanded = false, selected = false, index = 0 } = {}) {
  const jobs = incident.jobNames || [];
  const shown = jobs.slice(0, 6).map(j => `<span class="badge badge-neutral">${esc(j)}</span>`).join(' ');
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
        </button>
        <span class="triage-incident-when">${span}</span>
      </div>
      <div class="triage-incident-jobs">${shown}${more}</div>
      ${expanded ? `
      <div class="triage-incident-runs">
        <table class="data-table">
          <thead><tr><th>Job</th><th>Status</th><th>Started</th><th>Duration</th><th>Rows</th><th>Quality</th></tr></thead>
          <tbody>${(incident.runs || []).map(renderRunRow).join('')}</tbody>
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
      <td>
        <button class="btn-link triage-rerun-one" type="button" data-job="${esc(m.jobName)}">Run now</button>
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
          <button class="btn-secondary triage-rerun-selected" type="button" ${selected.size === 0 ? 'disabled' : ''}>
            Re-run selected${selected.size > 0 ? ` (${selected.size})` : ''}
          </button>
        </div>
        ${incidents.map((incident, i) => renderIncident(incident, {
          expanded: expanded.has(i),
          selected: selected.has(i),
          index: i
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
