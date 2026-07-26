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

function renderRow(item, replayingTarget) {
  const columns = (item.inputColumns || []).slice(0, 6).map(esc).join(', ');
  const extra = (item.inputColumns || []).length > 6 ? ` +${item.inputColumns.length - 6}` : '';
  const isReplaying = replayingTarget === item.quarantineTarget;
  const replayDisabled = !item.isReplayable || isReplaying;
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
        <button class="btn btn-outline btn-xs" data-copy-replay="${esc(item.replayStatement)}" type="button">Copy</button>
      </div>
    </div>
  </article>`;
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
    message: null
  };

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
        state.items.length ? `<div class="dq-list">${state.items.map(item => renderRow(item, state.replayingTarget)).join('')}</div>` :
        `<div class="empty-state empty-state-panel">
          <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>No quarantine manifests</h2>
          <p>No data-quality quarantine replay manifests match the current filters.</p>
        </div>`}
    </section>`;

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
