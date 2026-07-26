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
    </div>
    <div class="dq-row-side">
      <time>${esc(formatDate(item.updatedAtUtc))}</time>
      <code>${esc(item.replayStatement)}</code>
      <div class="dq-row-actions">
        <button class="btn btn-primary btn-xs" data-replay-target="${esc(item.quarantineTarget)}" data-job-name="${esc(item.jobName)}" type="button"${replayDisabled ? ' disabled' : ''}>${isReplaying ? 'Submitting' : 'Replay'}</button>
        <button class="btn btn-outline btn-xs" data-review-target="${esc(item.quarantineTarget)}" data-job-name="${esc(item.jobName)}" type="button">${selected ? 'Rows Open' : 'Review Rows'}</button>
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

function renderTrendPanel(state) {
  if (!state.trendJob) return '';
  const trend = state.trend;
  return `<section class="dq-rows-panel" id="dqTrendPanel">
    <header class="dq-rows-header">
      <div>
        <h3>Quality trend — ${esc(state.trendJob)}</h3>
        <p class="library-subtitle">Quarantine and warn outcomes recorded on each completed run.</p>
      </div>
      <button class="btn btn-outline btn-xs" id="dqTrendClose" type="button">Close</button>
    </header>
    ${state.trendError ? `<div class="error-msg">${esc(state.trendError)}</div>` : ''}
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
      ${(trend.topRuleFailures || []).length ? `
        <h4 class="dq-trend-subhead">Rules firing most</h4>
        <table class="dq-rows-table">
          <thead><tr><th>Column</th><th>Rule</th><th>Failures</th></tr></thead>
          <tbody>${trend.topRuleFailures.map(f => `<tr>
            <td>${esc(f.column)}</td><td><code>${esc(f.rule)}</code></td><td>${f.count.toLocaleString()}</td>
          </tr>`).join('')}</tbody>
        </table>` : '<p class="library-subtitle">No per-rule failure counts recorded for these runs.</p>'}
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
  </section>`;
}

function renderRowsPanel(state) {
  const target = state.selectedItem;
  if (!target) return '';
  const response = state.rows;
  const columns = response?.columns || [];
  const rows = response?.rows || [];
  const sourceColumns = columns.filter(column => !isEvidenceColumn(column));
  const evidenceColumns = columns.filter(column => isEvidenceColumn(column));
  return `<section class="dq-rows-panel">
    <div class="dq-rows-header">
      <div>
        <h3>${esc(target.quarantineTarget)}</h3>
        <p>${rows.length} row${rows.length === 1 ? '' : 's'} loaded${response?.capped ? ' · capped' : ''}</p>
      </div>
      <div class="dq-row-actions">
        <select id="dqRowsStatus" aria-label="Row status filter">
          ${['quarantined', 'released', 'discarded', 'replayed', 'all'].map(status =>
            `<option value="${status}"${state.rowStatus === status ? ' selected' : ''}>${status}</option>`).join('')}
        </select>
        <button class="btn btn-outline btn-xs" id="dqReloadRows" type="button">Reload</button>
        <button class="btn btn-outline btn-xs" id="dqCloseRows" type="button">Close</button>
      </div>
    </div>
    ${state.rowsError ? `<div class="error-msg">${esc(state.rowsError)}</div>` : ''}
    ${state.rowsLoading ? '<div class="loading-state"><span class="spinner"></span><span>Loading rows...</span></div>' :
      rows.length ? `<div class="dq-rows-table-wrap"><table class="dq-rows-table">
        <thead><tr>
          <th>Actions</th>
          ${sourceColumns.map(column => `<th>${esc(column)}</th>`).join('')}
          ${evidenceColumns.map(column => `<th>${esc(column)}</th>`).join('')}
        </tr></thead>
        <tbody>${rows.map(row => {
          const id = rowId(row);
          const edits = state.edits[id] || {};
          return `<tr>
            <td class="dq-row-action-cell">
              <button class="btn btn-primary btn-xs" data-release-row="${esc(id)}" type="button"${!target.isReplayable || state.rowAction === id ? ' disabled' : ''}>${state.rowAction === id ? 'Saving' : 'Save + Release'}</button>
              <button class="btn btn-outline btn-xs" data-discard-row="${esc(id)}" type="button"${state.rowAction === id ? ' disabled' : ''}>Discard</button>
              <input class="dq-cell-input dq-note-input" data-note-row="${esc(id)}" placeholder="Reason (audited)" value="${esc(state.notes[id] ?? '')}">
            </td>
            ${sourceColumns.map(column => `<td><input class="dq-cell-input" data-edit-row="${esc(id)}" data-edit-column="${esc(column)}" value="${esc(edits[column] ?? row[column] ?? '')}"></td>`).join('')}
            ${evidenceColumns.map(column => `<td><code>${esc(row[column] ?? '')}</code></td>`).join('')}
          </tr>`;
        }).join('')}</tbody>
      </table></div>` :
      // Only an actual empty result is "no rows". When the read failed we have no idea whether
      // rows exist, and telling a steward nothing matched would read as an all-clear.
      state.rowsError ? '' :
      `<div class="empty-state empty-state-panel">
        <h2>No rows</h2>
        <p>No quarantine rows match the current status filter.</p>
      </div>`}
  </section>`;
}

export function createDataQualityQueue({ host, dataQualityApi, prepare }) {
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
    trendLoading: false,
    trendError: null
  };

  async function loadTrend(jobName) {
    state.trendJob = jobName;
    state.trend = null;
    state.trendError = null;
    state.trendLoading = true;
    render();
    try {
      state.trend = await dataQualityApi.qualityTrend({ jobName });
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
      state.items = await dataQualityApi.quarantineQueue({
        q: state.q,
        replayable: state.replayable,
        limit: state.limit
      });
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
      state.message = `Replay job ${result.jobId} submitted for ${item.quarantineTarget}.`;
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
      state.message = `Disposition job ${result.jobId} submitted for ${id}.`;
      await loadRows(item);
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
        <input id="dqQueueSearch" type="search" placeholder="Search job, target, source, or column" value="${esc(state.q)}">
        <select id="dqReplayableFilter" aria-label="Replayability filter">
          <option value=""${state.replayable === '' ? ' selected' : ''}>All targets</option>
          <option value="true"${state.replayable === 'true' ? ' selected' : ''}>Replayable</option>
          <option value="false"${state.replayable === 'false' ? ' selected' : ''}>Blocked</option>
        </select>
        <button class="btn btn-primary" type="submit">Apply</button>
      </form>
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
      ${renderTrendPanel(state)}
      ${renderRowsPanel(state)}
    </section>`;

    host.querySelectorAll('[data-trend-job]').forEach(btn => {
      btn.addEventListener('click', () => loadTrend(btn.dataset.trendJob));
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
        if (row) updateDisposition(row, 'discarded');
      });
    });
    host.querySelectorAll('[data-copy-replay]').forEach(btn => {
      btn.addEventListener('click', async () => {
        await navigator.clipboard?.writeText(btn.dataset.copyReplay || '');
        btn.textContent = 'Copied';
        setTimeout(() => { btn.textContent = 'Copy'; }, 1200);
      });
    });
  }

  return {
    show() { load(); },
    dispose() { host.innerHTML = ''; }
  };
}
