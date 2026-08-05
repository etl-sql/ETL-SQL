const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
}[c]));

function formatDate(value) {
  if (!value) return 'Unknown';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Unknown' : date.toLocaleString();
}

function renderStatus(item) {
  if (item.isReplayable) return '<span class="dq-status dq-status-ready">Replayable</span>';
  return '<span class="dq-status dq-status-blocked">Blocked</span>';
}

// A target whose rows this Portal cannot read. Older manifests predate the flag, so an absent
// value means "readable" — the endpoint is still the authority and will decline if it is not.
function rowsReadable(item) {
  return item.rowsReadable !== false;
}

function isEvidenceColumn(column) {
  return String(column || '').toLowerCase().startsWith('__dq_');
}

function rowId(row) {
  return row.__dq_row_id || row.__DQ_ROW_ID || '';
}

function renderManifestRow(item, replayingTarget, selectedTarget) {
  const columns = (item.inputColumns || []).slice(0, 6).map(esc).join(', ');
  const extra = (item.inputColumns || []).length > 6 ? ` +${item.inputColumns.length - 6}` : '';
  const isReplaying = replayingTarget === item.quarantineTarget;
  const replayDisabled = !item.isReplayable || isReplaying;
  const selected = selectedTarget === item.quarantineTarget;
  const readable = rowsReadable(item);
  return `<article class="dq-row">
    <div class="dq-row-main">
      <div class="dq-row-title">
        <strong>${esc(item.quarantineTarget)}</strong>
        ${renderStatus(item)}
      </div>
      <div class="dq-row-detail">
        Job ${esc(item.jobName)} · Section ${esc(item.sectionLabel || 'Unlabeled')} · Source ${esc(item.sourceTable)}
      </div>
      <div class="dq-row-detail">${columns ? `Columns ${columns}${extra}` : 'No captured columns recorded'}</div>
      ${item.nonReplayableReason ? `<div class="dq-row-warning">${esc(item.nonReplayableReason)}</div>` : ''}
      ${readable ? '' : `<div class="dq-row-warning">${esc(item.rowsUnavailableReason
        || 'Portal cannot read this target’s rows.')} Replay and trend still work; copy the
        review statement to read the rows where the connection exists.</div>`}
    </div>
    <div class="dq-row-side">
      <time>${esc(formatDate(item.updatedAtUtc))}</time>
      <code>${esc(item.replayStatement)}</code>
      <div class="dq-row-actions">
        <button class="btn btn-primary btn-xs" data-replay-target="${esc(item.quarantineTarget)}" data-job-name="${esc(item.jobName)}" type="button"${replayDisabled ? ' disabled' : ''}>${isReplaying ? 'Submitting' : 'Replay'}</button>
        ${readable
          ? `<button class="btn btn-outline btn-xs" data-review-target="${esc(item.quarantineTarget)}" data-job-name="${esc(item.jobName)}" type="button">${selected ? 'Rows Open' : 'Review Rows'}</button>`
          : `<span class="dq-status dq-status-blocked" title="${esc(item.rowsUnavailableReason || '')}">View only</span>
             <button class="btn btn-outline btn-xs" data-copy-review="${esc(item.reviewStatement || '')}" type="button">Copy Review SQL</button>`}
        <button class="btn btn-outline btn-xs" data-trend-job="${esc(item.jobName)}" type="button">Trend</button>
        <button class="btn btn-outline btn-xs" data-copy-replay="${esc(item.replayStatement)}" type="button">Copy</button>
      </div>
    </div>
  </article>`;
}

function formatRate(rate) {
  if (rate === null || rate === undefined) return '—';
  return `${(Number(rate) * 100).toFixed(2)}%`;
}

function renderTrendDelta(delta) {
  if (delta === null || delta === undefined) return '';
  const pct = Number(delta) * 100;
  // A rising quarantine rate means quality is degrading, so "up" is the bad direction here.
  if (Math.abs(pct) < 0.005) return '<span class="dq-trend-flat">no change vs. earlier runs</span>';
  const cls = pct > 0 ? 'dq-trend-worse' : 'dq-trend-better';
  const arrow = pct > 0 ? '▲' : '▼';
  const word = pct > 0 ? 'worse' : 'better';
  return `<span class="${cls}">${arrow} ${Math.abs(pct).toFixed(2)} pts ${word} than earlier runs</span>`;
}

function renderSparkline(runs) {
  // Oldest → newest, so the line reads left to right like every other trend chart.
  const ordered = runs.slice().reverse().filter(r => r.quarantineRate !== null && r.quarantineRate !== undefined);
  if (ordered.length < 2) return '';
  const values = ordered.map(r => Number(r.quarantineRate));
  const max = Math.max(...values, 0.0001);
  const bars = ordered.map(run => {
    const height = Math.max(2, Math.round((Number(run.quarantineRate) / max) * 100));
    const title = `${formatDate(run.endTime || run.startTime)} — ${formatRate(run.quarantineRate)} quarantined (${run.rowsQuarantined} of ${run.rowsProcessed})`;
    return `<span class="dq-spark-bar" style="height:${height}%" title="${esc(title)}"></span>`;
  }).join('');
  return `<div class="dq-spark" role="img" aria-label="Quarantine rate over the last ${ordered.length} runs">${bars}</div>`;
}

/**
 * Which rules are firing, with the target table, the action taken and the owner.
 *
 * Those three came back from the API already and were being dropped here, which mattered beyond
 * missing detail: two columns with the same name in different target tables rendered as two
 * identical-looking rows with different counts, and a steward had no way to tell which was which.
 *
 * A `countsOnly` row is from a run that predates structured capture and can never carry the three.
 * It is marked rather than left blank — an empty Owner cell otherwise reads as "nobody owns this",
 * which is a different and more alarming statement than "this run did not record it".
 */
export function renderTopFailures(failures) {
  if (!failures.length)
    return '<p class="library-subtitle">No per-rule failure counts recorded for these runs.</p>';

  const unavailable = '<td class="dq-unrecorded" title="Not recorded: this run predates structured rule capture.">—</td>';
  const rows = failures.map(f => `<tr${f.countsOnly ? ' class="dq-counts-only"' : ''}>
      ${f.countsOnly ? unavailable : `<td>${esc(f.targetTable || '—')}</td>`}
      <td>${esc(f.column)}</td>
      <td><code>${esc(f.rule)}</code></td>
      ${f.countsOnly ? unavailable : `<td>${esc(f.action || '—')}</td>`}
      ${f.countsOnly ? unavailable : `<td>${esc(f.owner || '—')}</td>`}
      <td>${Number(f.count).toLocaleString()}</td>
    </tr>`).join('');

  const legacy = failures.some(f => f.countsOnly)
    ? `<p class="library-subtitle" data-dq-legacy-note>Rows marked — come from runs recorded before
       per-rule capture; only the column, rule and count were kept for those.</p>`
    : '';

  return `<h4 class="dq-trend-subhead">Rules firing most</h4>
    <table class="dq-rows-table">
      <thead><tr><th>Target</th><th>Column</th><th>Rule</th><th>Action</th><th>Owner</th><th>Failures</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>${legacy}`;
}

function renderTrendPanel(state) {
  if (!state.trendJob) return '';
  const trend = state.trend;
  return `<div class="modal-overlay" style="display: flex; z-index: 1000;" role="dialog" aria-modal="true"
      aria-labelledby="dqTrendModalTitle">
    <div class="modal-card modal-xl" style="max-height: 90vh; display: flex; flex-direction: column;">
      <div class="modal-header" style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--portal-border-soft,#374151); padding-bottom: 16px;">
        <div>
          <span class="library-kicker">Quality trend</span>
          <h2 class="modal-title" id="dqTrendModalTitle" style="margin: 4px 0 0 0;">${esc(state.trendJob)}</h2>
          <p class="modal-subtitle" style="margin: 4px 0 0 0;">Quarantine and warn outcomes recorded on each completed run.</p>
        </div>
        <button class="btn btn-outline" id="dqTrendClose" type="button">Close</button>
      </div>
      <div class="modal-body" style="flex: 1; overflow: auto; padding-top: 16px;">
        ${state.trendError ? `<div class="error-msg">${esc(state.trendError)}</div>` : ''}
        ${state.rulesError ? `<div class="error-msg">Rule inventory unavailable: ${esc(state.rulesError)}</div>` : ''}
        ${state.trendLoading ? '<div class="loading-state"><span class="spinner"></span><span>Loading quality trend...</span></div>' :
          !trend || trend.runCount === 0 ? `<div class="empty-state empty-state-panel">
            <h2>No recorded runs</h2>
            <p>This job has no completed runs with data-quality metrics yet.</p>
          </div>` : `
          <div class="dq-trend-stats">
            <div class="dq-trend-stat">
              <span class="dq-trend-label">Latest quarantine rate</span>
              <strong>${formatRate(trend.latestQuarantineRate)}</strong>
              ${renderTrendDelta(trend.quarantineRateDelta)}
            </div>
            <div class="dq-trend-stat">
              <span class="dq-trend-label">Average over ${trend.runCount} run(s)</span>
              <strong>${formatRate(trend.averageQuarantineRate)}</strong>
            </div>
            <div class="dq-trend-stat">
              <span class="dq-trend-label">Rows quarantined / warned</span>
              <strong>${trend.totalRowsQuarantined.toLocaleString()} / ${trend.totalRowsWarned.toLocaleString()}</strong>
              <span class="dq-trend-flat">of ${trend.totalRowsProcessed.toLocaleString()} processed</span>
            </div>
          </div>
          ${renderSparkline(trend.runs || [])}
          <h4 class="dq-trend-subhead">Rules protecting columns</h4>
          ${(state.rules || []).length ? `<table class="dq-rows-table">
            <thead><tr><th>Target</th><th>Column</th><th>Tag</th><th>Rule</th><th>Action</th><th>Source</th></tr></thead>
            <tbody>${state.rules.map(rule => `<tr>
              <td>${esc(rule.targetTable)}</td><td>${esc(rule.targetColumn || '—')}</td>
              <td><code>${esc(rule.ruleTag)}</code></td><td><code>${esc(rule.rule)}</code></td>
              <td>${esc(rule.action)}</td><td>${esc(rule.sourceFile || '—')}:${esc(rule.line)}</td>
            </tr>`).join('')}</tbody>
          </table>` : '<p class="library-subtitle">No readable rule definitions were found for this job script.</p>'}
          ${renderTopFailures(trend.topRuleFailures || [])}
          <h4 class="dq-trend-subhead">Recent runs</h4>
          <table class="dq-rows-table">
            <thead><tr><th>Completed</th><th>Status</th><th>Processed</th><th>Quarantined</th><th>Warned</th><th>Rate</th></tr></thead>
            <tbody>${(trend.runs || []).map(run => `<tr>
              <td>${esc(formatDate(run.endTime || run.startTime))}</td>
              <td>${esc(run.status)}</td>
              <td>${run.rowsProcessed.toLocaleString()}</td>
              <td>${run.rowsQuarantined.toLocaleString()}</td>
              <td>${run.rowsWarned.toLocaleString()}</td>
              <td>${formatRate(run.quarantineRate)}</td>
            </tr>`).join('')}</tbody>
          </table>`}
      </div>
    </div>
  </div>`;
}

function renderRowsPanel(state) {
  const target = state.selectedItem;
  if (!target) return '';
  const response = state.rows;
  const columns = response?.columns || [];
  const rows = response?.rows || [];
  const sourceColumns = columns.filter(column => !isEvidenceColumn(column));
  const evidenceColumns = columns.filter(column => isEvidenceColumn(column));
  return `<div class="modal-overlay" style="display: flex; z-index: 1000;" role="dialog" aria-modal="true"
      aria-labelledby="dqRowsModalTitle">
    <div class="modal-card modal-xl" style="max-height: 90vh; display: flex; flex-direction: column;">
      <div class="modal-header" style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--portal-border-soft,#374151); padding-bottom: 16px;">
        <div>
          <span class="library-kicker">${esc(target.jobName)} · Section ${esc(target.sectionLabel || 'Unlabeled')}</span>
          <h2 class="modal-title" id="dqRowsModalTitle" style="margin: 4px 0 0 0;">${esc(target.quarantineTarget)}</h2>
          <p class="modal-subtitle" style="margin: 4px 0 0 0;">${rows.length} row${rows.length === 1 ? '' : 's'} loaded${response?.capped ? ' · capped' : ''}</p>
        </div>
        <div class="dq-row-actions" style="display: flex; gap: 8px; align-items: center;">
          <select id="dqRowsStatus" aria-label="Row status filter">
            ${['quarantined', 'released', 'replaying', 'discarded', 'replayed', 'all'].map(status =>
              `<option value="${status}"${state.rowStatus === status ? ' selected' : ''}>${status}</option>`).join('')}
          </select>
          <button class="btn btn-outline btn-xs" id="dqReloadRows" type="button">Reload</button>
          <button class="btn btn-outline btn-xs" id="dqCloseRows" type="button">Close</button>
        </div>
      </div>
      <div class="modal-body" style="flex: 1; overflow: auto; padding-top: 16px;">
        ${state.rowsError ? `<div class="error-msg">${esc(state.rowsError)}</div>` : ''}
        ${state.rowsLoading ? '<div class="loading-state"><span class="spinner"></span><span>Loading rows...</span></div>' :
          rows.length ? `<div class="dq-rows-table-wrap" style="overflow-x: auto;"><table class="dq-rows-table" style="width: 100%; border-collapse: collapse;">
            <thead><tr>
              <th style="text-align: left; padding: 8px;">Actions</th>
              ${sourceColumns.map(column => `<th style="text-align: left; padding: 8px;">${esc(column)}</th>`).join('')}
              ${evidenceColumns.map(column => `<th style="text-align: left; padding: 8px;">${esc(column)}</th>`).join('')}
            </tr></thead>
            <tbody>${rows.map(row => {
              const id = rowId(row);
              const edits = state.edits[id] || {};
              const isReplayClaim = row.__dq_status === 'replaying';
              return `<tr>
                <td class="dq-row-action-cell" style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); white-space: nowrap;">
                  <button class="btn btn-primary btn-xs" data-release-row="${esc(id)}" type="button"${!target.isReplayable || state.rowAction === id ? ' disabled' : ''}>${state.rowAction === id ? 'Saving' : isReplayClaim ? 'Return to released' : 'Save + Release'}</button>
                  <button class="btn btn-outline btn-xs" data-discard-row="${esc(id)}" data-disposition="${isReplayClaim ? 'replayed' : 'discarded'}" type="button"${state.rowAction === id ? ' disabled' : ''}>${isReplayClaim ? 'Mark replayed' : 'Discard'}</button>
                  <input class="dq-cell-input dq-note-input" data-note-row="${esc(id)}" placeholder="Reason (audited)" value="${esc(state.notes[id] ?? '')}" style="margin-left: 4px; padding: 2px 4px; font-size: 11px;">
                </td>
                ${sourceColumns.map(column => `<td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);"><input class="dq-cell-input" data-edit-row="${esc(id)}" data-edit-column="${esc(column)}" value="${esc(edits[column] ?? row[column] ?? '')}" style="padding: 4px 6px; font-size: 12px; width: 100%; min-width: 100px;"></td>`).join('')}
                ${evidenceColumns.map(column => `<td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);"><code>${esc(row[column] ?? '')}</code></td>`).join('')}
              </tr>`;
            }).join('')}</tbody>
          </table></div>` :
          state.rowsError ? '' :
          `<div class="empty-state empty-state-panel">
            <h2>No rows</h2>
            <p>No quarantine rows match the current status filter.</p>
          </div>`}
      </div>
    </div>
  </div>`;
}

const TERMINAL_JOB_STATUSES = new Set(['Completed', 'Failed', 'Cancelled']);
const TRACKED_JOBS_KEY = 'etlsql_dq_tracked_jobs';

function renderTrackedJobs(jobs) {
  if (!jobs.length) return '';
  return `<section class="dq-rows-panel dq-job-panel" aria-live="polite">
    <header class="dq-rows-header"><div><h3>Submitted work</h3><p>Replay and disposition jobs remain here until their durable execution reaches a terminal state.</p></div></header>
    <div class="dq-job-list">${jobs.map(job => {
      const terminal = TERMINAL_JOB_STATUSES.has(job.status);
      const failed = job.status === 'Failed' || job.status === 'Cancelled';
      return `<article class="dq-job-row">
        <span class="dq-status ${terminal && !failed ? 'dq-status-ready' : failed ? 'dq-status-blocked' : ''}">${esc(job.status)}</span>
        <div><strong>${esc(job.kind)}</strong><small>${esc(job.target)} · <code>${esc(job.jobId)}</code></small></div>
        <div><time>${esc(formatDate(job.completedAt || job.startedAt || job.createdAt))}</time>${job.error || job.trackingError ? `<small class="dq-row-warning">${esc(job.error || job.trackingError)}</small>` : ''}</div>
      </article>`;
    }).join('')}</div>
  </section>`;
}

export function createDataQualityQueue({ host, dataQualityApi, prepare }) {
  let storage = null;
  try { storage = typeof sessionStorage !== 'undefined' ? sessionStorage : null; } catch { }
  const pollTimers = new Map();
  let disposed = false;
  const state = {
    q: '',
    replayable: '',
    limit: 100,
    items: [],
    loading: false,
    error: null,
    replayingTarget: null,
    message: null,
    selectedItem: null,
    rowStatus: 'quarantined',
    rows: null,
    rowsLoading: false,
    rowsError: null,
    edits: {},
    notes: {},
    rowAction: null,
    trendJob: null,
    trend: null,
    rules: [],
    trendLoading: false,
    trendError: null,
    rulesError: null,
    trackedJobs: [],
    allJobs: []
  };

  function persistTrackedJobs() {
    try { storage?.setItem(TRACKED_JOBS_KEY, JSON.stringify(state.trackedJobs.slice(0, 20))); } catch { }
  }

  function restoreTrackedJobs() {
    try {
      const parsed = JSON.parse(storage?.getItem(TRACKED_JOBS_KEY) || '[]');
      state.trackedJobs = Array.isArray(parsed) ? parsed.filter(job => job?.jobId).slice(0, 20) : [];
    } catch { state.trackedJobs = []; }
    state.trackedJobs.filter(job => !TERMINAL_JOB_STATUSES.has(job.status)).forEach(job => pollJob(job.jobId));
  }

  function schedulePoll(jobId) {
    if (disposed || pollTimers.has(jobId)) return;
    pollTimers.set(jobId, setTimeout(() => {
      pollTimers.delete(jobId);
      pollJob(jobId);
    }, 1000));
  }

  async function pollJob(jobId) {
    if (disposed) return;
    const tracked = state.trackedJobs.find(job => job.jobId === jobId);
    if (!tracked || TERMINAL_JOB_STATUSES.has(tracked.status)) return;
    try {
      const result = await dataQualityApi.jobStatus(jobId);
      if (disposed) return;
      Object.assign(tracked, result, { trackingError: null });
      persistTrackedJobs();
      render();
      if (TERMINAL_JOB_STATUSES.has(tracked.status)) {
        state.message = `${tracked.kind} job ${jobId} ${tracked.status.toLowerCase()} for ${tracked.target}.`;
        if (tracked.kind === 'Disposition' && state.selectedItem) await loadRows(state.selectedItem);
        else await load();
      } else {
        schedulePoll(jobId);
      }
    } catch (err) {
      tracked.trackingError = err.message || 'Status temporarily unavailable.';
      render();
      schedulePoll(jobId);
    }
  }

  function trackJob(jobId, kind, target) {
    state.trackedJobs = state.trackedJobs.filter(job => job.jobId !== jobId);
    state.trackedJobs.unshift({ jobId, kind, target, status: 'Submitted', createdAt: new Date().toISOString() });
    persistTrackedJobs();
    render();
    pollJob(jobId);
  }

  async function loadTrend(jobName) {
    state.trendJob = jobName;
    state.trend = null;
    state.rules = [];
    state.trendError = null;
    state.rulesError = null;
    state.trendLoading = true;
    render();
    try {
      const [trend, rules] = await Promise.allSettled([
        dataQualityApi.qualityTrend({ jobName }),
        dataQualityApi.qualityRules(jobName)
      ]);
      if (trend.status === 'rejected') throw trend.reason;
      state.trend = trend.value;
      if (rules.status === 'fulfilled') state.rules = rules.value;
      else state.rulesError = rules.reason?.message || 'Unable to load rule inventory.';
    } catch (err) {
      state.trendError = err.message || 'Unable to load quality trend.';
    } finally {
      state.trendLoading = false;
      render();
    }
  }

  async function load() {
    state.loading = true;
    state.error = null;
    render();
    try {
      const promises = [
        dataQualityApi.quarantineQueue({
          q: state.q,
          replayable: state.replayable,
          limit: state.limit
        })
      ];
      if (!state.allJobs.length) {
        promises.push(dataQualityApi.qualityJobs());
      }
      const results = await Promise.all(promises);
      state.items = results[0];
      if (results[1]) {
        state.allJobs = results[1] || [];
      }
    } catch (err) {
      state.error = err.message || 'Unable to load quarantine queue.';
    } finally {
      state.loading = false;
      render();
    }
  }

  async function loadRows(item = state.selectedItem) {
    if (!item) return;
    state.selectedItem = item;
    state.rowsLoading = true;
    state.rowsError = null;
    state.edits = {};
    render();
    try {
      state.rows = await dataQualityApi.quarantineRows({
        quarantineTarget: item.quarantineTarget,
        jobName: item.jobName,
        status: state.rowStatus,
        limit: 50
      });
    } catch (err) {
      state.rows = null;
      state.rowsError = err.message || 'Unable to load quarantine rows.';
    } finally {
      state.rowsLoading = false;
      render();
    }
  }

  async function replay(item) {
    state.replayingTarget = item.quarantineTarget;
    state.error = null;
    state.message = null;
    render();
    try {
      const result = await dataQualityApi.replayQuarantine(item.quarantineTarget, item.jobName);
      state.message = `Replay job ${result.jobId} submitted; tracking durable execution status.`;
      trackJob(result.jobId, 'Replay', item.quarantineTarget);
    } catch (err) {
      state.error = err.message || 'Unable to submit quarantine replay.';
    } finally {
      state.replayingTarget = null;
      render();
    }
  }

  async function updateDisposition(row, disposition) {
    const id = rowId(row);
    if (!id) return;
    const item = state.selectedItem;
    state.rowAction = id;
    state.error = null;
    state.message = null;
    render();
    try {
      const changes = disposition === 'released' ? (state.edits[id] || {}) : null;
      const result = await dataQualityApi.updateQuarantineDisposition({
        quarantineTarget: item.quarantineTarget,
        jobName: item.jobName,
        rowIds: [id],
        disposition,
        changes,
        note: state.notes[id] || null
      });
      state.message = `Disposition job ${result.jobId} submitted; the row will refresh after terminal status.`;
      trackJob(result.jobId, 'Disposition', `${item.quarantineTarget} · row ${id}`);
    } catch (err) {
      state.error = err.message || 'Unable to submit quarantine disposition.';
    } finally {
      state.rowAction = null;
      render();
    }
  }

  function render() {
    prepare?.();
    const replayableCount = state.items.filter(item => item.isReplayable).length;
    const blockedCount = state.items.length - replayableCount;
    host.innerHTML = `<section class="dq-page">
      <div class="library-header">
        <div>
          <span class="library-kicker">Data Quality</span>
          <h2>Quarantine Queue</h2>
          <p class="library-subtitle">Replay manifests from orchestrator-hosted data-quality quarantines.</p>
        </div>
        <button class="btn btn-outline" id="dqRefreshBtn" type="button">Refresh</button>
      </div>
      <form class="dq-toolbar" id="dqQueueForm">
        <input id="dqQueueSearch" type="search" placeholder="Search job, target, source, or column"
          aria-label="Search the quarantine queue by job, target, source, or column" value="${esc(state.q)}">
        <select id="dqReplayableFilter" aria-label="Replayability filter">
          <option value=""${state.replayable === '' ? ' selected' : ''}>All targets</option>
          <option value="true"${state.replayable === 'true' ? ' selected' : ''}>Replayable</option>
          <option value="false"${state.replayable === 'false' ? ' selected' : ''}>Blocked</option>
        </select>
        <button class="btn btn-primary" type="submit">Apply</button>
      </form>
      <div class="dq-toolbar" style="margin-top: 10px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap;">
        <span style="font-size: 13px; font-weight: 600;">Job Quality Lookup:</span>
        <select id="dqJobLookupSelect" style="flex: 1; max-width: 300px; min-width: 200px; padding: 6px 10px; border-radius: 6px; border: 1px solid var(--portal-border,#374151); background:var(--portal-surface,#111827); color:inherit;">
          <option value="">Select a job to view rules & trend…</option>
          ${state.allJobs.map(j => `<option value="${esc(j.name)}" ${state.trendJob === j.name ? 'selected' : ''}>${esc(j.displayName || j.name)}</option>`).join('')}
        </select>
        <button class="btn btn-outline btn-xs" id="dqJobLookupBtn" type="button">View Trend & Rules</button>
      </div>
      <div class="dq-summary">
        <span>${state.items.length} targets</span>
        <span>${replayableCount} replayable</span>
        <span>${blockedCount} blocked</span>
      </div>
      ${state.message ? `<div class="dq-success">${esc(state.message)}</div>` : ''}
      ${state.error ? `<div class="error-msg">${esc(state.error)}</div>` : ''}
      ${state.loading ? '<div class="loading-state"><span class="spinner"></span><span>Loading quarantine queue...</span></div>' :
        state.items.length ? `<div class="dq-list">${state.items.map(item => renderManifestRow(item, state.replayingTarget, state.selectedItem?.quarantineTarget)).join('')}</div>` :
        `<div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>No quarantine manifests</h2>
          <p>No data-quality quarantine replay manifests match the current filters.</p>
        </div>`}
      ${renderTrackedJobs(state.trackedJobs)}
      ${renderTrendPanel(state)}
      ${renderRowsPanel(state)}
    </section>`;

    host.querySelectorAll('[data-trend-job]').forEach(btn => {
      btn.addEventListener('click', () => {
        const jobName = btn.dataset.trendJob;
        const select = host.querySelector('#dqJobLookupSelect');
        if (select) select.value = jobName;
        loadTrend(jobName);
      });
    });
    host.querySelector('#dqJobLookupSelect')?.addEventListener('change', e => {
      const jobName = e.target.value;
      if (jobName) {
        loadTrend(jobName);
      }
    });
    host.querySelector('#dqJobLookupBtn')?.addEventListener('click', () => {
      const select = host.querySelector('#dqJobLookupSelect');
      const jobName = select?.value;
      if (jobName) {
        loadTrend(jobName);
      }
    });
    host.querySelector('#dqTrendClose')?.addEventListener('click', () => {
      state.trendJob = null;
      state.trend = null;
      state.trendError = null;
      render();
    });

    host.querySelector('#dqQueueForm')?.addEventListener('submit', e => {
      e.preventDefault();
      state.q = host.querySelector('#dqQueueSearch')?.value?.trim() || '';
      state.replayable = host.querySelector('#dqReplayableFilter')?.value || '';
      load();
    });
    host.querySelector('#dqRefreshBtn')?.addEventListener('click', () => load());
    host.querySelectorAll('[data-replay-target]').forEach(btn => {
      btn.addEventListener('click', () => {
        const item = state.items.find(value =>
          value.quarantineTarget === btn.dataset.replayTarget
          && value.jobName === btn.dataset.jobName);
        if (item) replay(item);
      });
    });
    host.querySelectorAll('[data-review-target]').forEach(btn => {
      btn.addEventListener('click', () => {
        const item = state.items.find(value =>
          value.quarantineTarget === btn.dataset.reviewTarget
          && value.jobName === btn.dataset.jobName);
        if (item) loadRows(item);
      });
    });
    host.querySelector('#dqRowsStatus')?.addEventListener('change', e => {
      state.rowStatus = e.target.value;
      loadRows();
    });
    host.querySelector('#dqReloadRows')?.addEventListener('click', () => loadRows());
    host.querySelector('#dqCloseRows')?.addEventListener('click', () => {
      state.selectedItem = null;
      state.rows = null;
      state.rowsError = null;
      state.edits = {};
      render();
    });
    host.querySelectorAll('[data-edit-row]').forEach(input => {
      input.addEventListener('input', () => {
        const id = input.dataset.editRow;
        const column = input.dataset.editColumn;
        state.edits[id] ??= {};
        state.edits[id][column] = input.value;
      });
    });
    host.querySelectorAll('[data-note-row]').forEach(input => {
      input.addEventListener('input', () => {
        state.notes[input.dataset.noteRow] = input.value;
      });
    });
    host.querySelectorAll('[data-release-row]').forEach(btn => {
      btn.addEventListener('click', () => {
        const row = (state.rows?.rows || []).find(value => rowId(value) === btn.dataset.releaseRow);
        if (row) updateDisposition(row, 'released');
      });
    });
    host.querySelectorAll('[data-discard-row]').forEach(btn => {
      btn.addEventListener('click', () => {
        const row = (state.rows?.rows || []).find(value => rowId(value) === btn.dataset.discardRow);
        if (row) updateDisposition(row, btn.dataset.disposition || 'discarded');
      });
    });
    host.querySelectorAll('[data-copy-replay], [data-copy-review]').forEach(btn => {
      const label = btn.textContent;
      btn.addEventListener('click', async () => {
        await navigator.clipboard?.writeText(btn.dataset.copyReplay ?? btn.dataset.copyReview ?? '');
        btn.textContent = 'Copied';
        setTimeout(() => { btn.textContent = label; }, 1200);
      });
    });
  }

  return {
    show() { disposed = false; restoreTrackedJobs(); load(); },
    dispose() { disposed = true; pollTimers.forEach(timer => clearTimeout(timer)); pollTimers.clear(); host.innerHTML = ''; }
  };
}
