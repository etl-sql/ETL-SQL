// Governance dashboard for the ETL-SQL Portal.
//
// Every value shown here comes from `/api/governance/*`. There is no demo data and no local
// workflow state: a finding a steward ignored has to still be ignored after a refresh, on someone
// else's screen, and in an audit six months later. State that lives in a tab satisfies none of
// those, and — worse — looks identical to state that does.
//
// The four states below are rendered honestly and separately, because collapsing them is how a
// dashboard lies:
//   loading      — we do not know yet, so we claim nothing
//   unauthorized — you are not permitted to see this (403), not "there is nothing here"
//   failed       — we asked and could not find out; never a fabricated stand-in
//   empty        — we asked, we know, and the answer is genuinely nothing
//
// A fifth distinction matters just as much: "never scanned" is not "no findings". A KPI tile
// showing zero cannot tell those apart on its own, so the scan banner says which one it is.
export function createGovernancePortal(opts = {}) {
  const {
    host,
    governanceApi,
    dataQualityApi,
    prepare = () => {},
    notify = (msg, o) => window.ETLSQLFeedback?.notify(msg, o),
    confirm = (msg, o) => window.ETLSQLFeedback?.confirm(msg, o),
  } = opts;

  const state = {
    mode: 'all',            // 'mine' | 'all'
    tab: 'overview',        // overview | workqueue | exceptions | badges | glossary | settings
    searchFilter: '',
    badgeFilter: 'all',
    categoryFilter: 'all',
    // load: 'idle' | 'loading' | 'ready' | 'unauthorized' | 'failed'
    load: 'idle',
    error: null,
    dashboard: null,
    findings: [],
    categories: [],
    glossary: [],
    settings: null,
    editingTerm: null,
    editingCategory: null,
    pendingFindingId: null,
    activeDqTrendJob: null,
    activeDqTrend: null,
    activeDqRules: [],
    activeDqLoading: false,
    dqJobs: [],
    dqTrends: {},
    dqLoading: false,
    allRules: [],
    quarantineQueue: [],
    allRulesLoaded: false,
    dqSearchFilter: '',
  };

  const esc = s => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/>/g, '&gt;').replace(/"/g, '&quot;');

  const isForbidden = err => err?.status === 403 || err?.status === 401;

  /** Runs a mutation and reports the outcome; never leaves the UI showing a change that failed. */
  async function mutate(action, { success, failure, auditAction }) {
    try {
      const result = await action();
      notify(success, { title: 'Governance', tone: 'success', auditAction });
      // Re-read and redraw. Reloading without redrawing leaves the steward looking at the state
      // before their change — which reads as the change having failed.
      await load();
      await render();
      return result;
    } catch (err) {
      notify(
        isForbidden(err)
          ? 'Your role does not permit this governance change.'
          : `${failure} ${err?.message || ''}`.trim(),
        { title: 'Governance', tone: 'warning' });
      return null;
    }
  }

  async function load() {
    state.load = 'loading';
    state.error = null;
    try {
      const scope = state.mode === 'mine' ? 'mine' : 'all';
      const [dashboard, findings, categories, glossary, settings] = await Promise.all([
        governanceApi.dashboard({ scope }),
        governanceApi.findings({ limit: 500 }),
        governanceApi.categories(),
        governanceApi.glossary(),
        governanceApi.settings(),
      ]);
      state.dashboard = dashboard;
      state.findings = Array.isArray(findings) ? findings : [];
      state.categories = Array.isArray(categories) ? categories : [];
      state.glossary = Array.isArray(glossary) ? glossary : [];
      state.settings = settings;

      if (dataQualityApi) {
        state.dqLoading = true;
        try {
          const [jobs, rules, queue] = await Promise.all([
            dataQualityApi.qualityJobs(),
            dataQualityApi.qualityRulesAll(),
            dataQualityApi.quarantineQueue()
          ]);
          state.dqJobs = Array.isArray(jobs) ? jobs : [];
          state.allRules = Array.isArray(rules) ? rules : [];
          state.quarantineQueue = Array.isArray(queue) ? queue : [];
          state.allRulesLoaded = true;

          const trendPromises = state.dqJobs.slice(0, 5).map(async (job) => {
            try {
              const trend = await dataQualityApi.qualityTrend({ jobName: job.name });
              state.dqTrends[job.name] = trend;
            } catch (trendErr) {
              console.warn(`Failed to load trend for job ${job.name}:`, trendErr);
            }
          });
          await Promise.all(trendPromises);
        } catch (dqErr) {
          console.warn('Failed to load data quality jobs for governance dashboard:', dqErr);
        } finally {
          state.dqLoading = false;
        }
      }

      state.load = 'ready';
    } catch (err) {
      // No fallback dataset. Showing invented assets when the API is unreachable would put
      // fictional governance evidence in front of a steward with nothing marking it as fiction.
      state.dashboard = null;
      state.findings = [];
      state.load = isForbidden(err) ? 'unauthorized' : 'failed';
      state.error = err?.message || 'The governance API could not be reached.';
    }
  }

  const stateBanner = () => {
    if (state.load === 'loading') {
      return `<div class="gov-state gov-state-loading" data-gov-state="loading">
        <span class="gov-spinner"></span> Loading governance data…</div>`;
    }
    if (state.load === 'unauthorized') {
      return `<div class="gov-state gov-state-denied" data-gov-state="unauthorized">
        <h2 class="gov-state-title">You do not have access to governance data.</h2>
        <p>Viewing the estate's governance posture needs the GovernanceViewer, DataSteward, or
        GovernanceManager role. This is not an empty estate — it is a view you cannot see.</p></div>`;
    }
    if (state.load === 'failed') {
      return `<div class="gov-state gov-state-error" data-gov-state="failed">
        <h2 class="gov-state-title">Governance data is unavailable.</h2>
        <p>${esc(state.error)}</p>
        <p class="gov-state-note">Nothing is shown in place of the real posture. Retry once the
        service is reachable.</p>
        <button class="btn btn-outline btn-xs" id="btnGovRetry" type="button">Retry</button></div>`;
    }
    return '';
  };

  const scanBanner = () => {
    const scan = state.dashboard?.lastScan;
    if (!scan) {
      // The distinction the whole surface depends on.
      return `<div class="gov-state gov-state-unscanned" data-gov-state="never-scanned">
        <h2 class="gov-state-title">This estate has never been scanned.</h2>
        <p>The tiles below show no findings because none have been computed — not because none
        exist. Run a scan to establish the current posture.</p></div>`;
    }
    if (scan.status === 'failed') {
      return `<div class="gov-state gov-state-error" data-gov-state="scan-failed">
        <h2 class="gov-state-title">The last scan failed.</h2>
        <p>${esc(scan.error || 'No error detail was recorded.')}</p>
        <p class="gov-state-note">Findings below are from before that scan and may be stale.</p></div>`;
    }
    return `<div class="gov-scanline" data-gov-state="scanned">Last scan
      ${esc(new Date(scan.startedAtUtc).toLocaleString())} · ${esc(scan.assetsScanned)} assets ·
      ${esc(scan.findingsOpened)} opened · ${esc(scan.findingsResolved)} resolved ·
      ${esc(scan.findingsReopened)} reopened</div>`;
  };

  const emptyRow = (colspan, message) =>
    `<tr><td colspan="${colspan}" class="gov-empty" data-gov-state="empty">${esc(message)}</td></tr>`;

  const filteredAssets = () => {
    const assets = state.dashboard?.assets || [];
    const q = state.searchFilter.toLowerCase();
    return assets.filter(a => {
      const matchesSearch = !q || a.assetKey.toLowerCase().includes(q)
        || (a.scriptPath || '').toLowerCase().includes(q);
      const matchesBadge = state.badgeFilter === 'all'
        || (a.automaticBadges || []).includes(state.badgeFilter)
        || (a.assignedBadges || []).includes(state.badgeFilter);
      return matchesSearch && matchesBadge;
    });
  };

  const suppressed = () => state.findings.filter(
    f => f.status === 'ignored' || f.status === 'accepted-risk');

  const renderOverview = () => {
    const s = state.dashboard?.summary;
    if (!s) return '';
    const pct = s.totalAssets ? Math.round((s.governedAssets / s.totalAssets) * 100) : 0;

    let dqContent = '';
    if (dataQualityApi && state.dqJobs.length > 0) {
      const rowsHtml = state.dqJobs.map(job => {
        const trend = state.dqTrends[job.name];
        const status = trend?.runs?.[0]?.status || 'Unknown';
        const processed = trend?.totalRowsProcessed ?? 0;
        const quarantined = trend?.totalRowsQuarantined ?? 0;
        const warned = trend?.totalRowsWarned ?? 0;
        
        let rateHtml = '—';
        if (trend && trend.latestQuarantineRate !== null && trend.latestQuarantineRate !== undefined) {
          const rateVal = (Number(trend.latestQuarantineRate) * 100).toFixed(2);
          const isBad = Number(trend.latestQuarantineRate) > 0.05; // >5% failure rate
          rateHtml = `<span style="font-weight: 600; color: ${isBad ? 'var(--portal-error, #f87171)' : 'var(--portal-success, #34d399)'}">${rateVal}%</span>`;
        }

        const badgeClass = status === 'SUCCESS' || status === 'Completed' ? 'gov-badge-auto' : status === 'Failed' ? 'gov-badge-assigned' : 'gov-badge-muted';
        
        return `
          <tr>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151);">
              <div class="gov-asset-path" style="font-weight: 600;">${esc(job.displayName || job.name)}</div>
              <div class="gov-asset-meta" style="font-size: 11px; color: var(--portal-muted, #9ca3af); margin-top: 2px;">${esc(job.description || 'No description available')}</div>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle;">
              <span class="gov-badge ${badgeClass}" style="padding: 2px 6px; border-radius: 4px; font-size: 11px;">${esc(status)}</span>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; vertical-align: middle;">${Number(processed).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; font-weight: 500; vertical-align: middle;">${Number(quarantined).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; vertical-align: middle;">${Number(warned).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; font-weight: 600; vertical-align: middle;">${rateHtml}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: center; vertical-align: middle;">
              <button class="btn btn-outline btn-xs" data-view-dq-trend="${esc(job.name)}" type="button">Rules & Trend</button>
            </td>
          </tr>
        `;
      }).join('');

      dqContent = `
        <div class="gov-card" style="margin-top: 24px; padding: 20px; background: var(--portal-surface, #1e293b); border: 1px solid var(--portal-border, #334155); border-radius: 8px; box-sizing: border-box;">
          <div style="margin-bottom: 16px;">
            <h3 style="margin: 0; font-size: 16px; font-weight: 600;">Data Quality Operations</h3>
            <p class="library-subtitle" style="margin: 4px 0 0 0; font-size: 13px; color: var(--portal-muted, #9ca3af);">Persisted per-run data-quality outcomes, failure rates, and active rules coverage from orchestrator jobs.</p>
          </div>
          <div class="gov-table-wrap" style="overflow-x: auto; width: 100%;">
            <table class="gov-table" style="width: 100%; border-collapse: collapse; display: table;">
              <thead>
                <tr>
                  <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Job / Description</th>
                  <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Latest Status</th>
                  <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Processed Rows</th>
                  <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Quarantined</th>
                  <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Warned</th>
                  <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Failure Rate</th>
                  <th style="text-align: center; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 120px;">Actions</th>
                </tr>
              </thead>
              <tbody>
                ${rowsHtml}
              </tbody>
            </table>
          </div>
        </div>
      `;
    }

    return `
      ${scanBanner()}
      <div class="gov-kpis" role="list" aria-label="Governance summary">
        ${kpi('Governed assets', `${s.governedAssets}/${s.totalAssets}`, `${pct}% at or above ${s.targetScore}`, 'ok')}
        ${kpi('Below threshold', s.belowThreshold, 'Need follow-up', s.belowThreshold ? 'warn' : 'ok')}
        ${kpi('Open findings', s.openFindings, 'Awaiting a steward', s.openFindings ? 'warn' : 'ok')}
        ${kpi('Accepted risk', s.acceptedRisks, 'Suppressed with a reason', s.acceptedRisks ? 'risk' : 'ok')}
        ${kpi('Ignored', s.ignoredFindings, 'Declared false positives', 'muted')}
      </div>
      ${dqContent}`;
  };

  // Each tile is one list item carrying its whole meaning in an accessible name. Rendered as three
  // sibling divs, the five tiles collapsed into a single run of text -- a screen reader read
  // "0/0 Governed assets 0% at or above 80 0 Below threshold..." with no number attached to any
  // label. The visible text is hidden from the tree precisely because the name already says it.
  const kpi = (label, value, sub, tone) => `
    <div class="gov-kpi gov-kpi-${tone}" role="listitem"
      aria-label="${esc(label)}: ${esc(value)}. ${esc(sub)}">
      <div class="gov-kpi-value" aria-hidden="true">${esc(value)}</div>
      <div class="gov-kpi-label" aria-hidden="true">${esc(label)}</div>
      <div class="gov-kpi-sub" aria-hidden="true">${esc(sub)}</div>
    </div>`;

  const renderWorkqueue = () => {
    const assets = filteredAssets();
    const rows = assets.length
      ? assets.map(a => {
        const open = (a.findings || []).filter(
          f => f.status === 'open' || f.status === 'reopened');
        return `
          <tr>
            <td>
              <div class="gov-asset-path">${esc(a.assetKey)}</div>
              <div class="gov-asset-meta">Steward: ${esc(a.steward || 'Unassigned')} ·
                Owner: ${esc(a.owner || 'Unassigned')} · Domain: ${esc(a.domain || 'Unassigned')}</div>
              <div class="gov-badges">
                ${(a.automaticBadges || []).map(b => `<span class="gov-badge gov-badge-auto">${esc(b)}</span>`).join('')}
                ${(a.assignedBadges || []).map(b => `<span class="gov-badge gov-badge-assigned">${esc(b)}
                  <button class="remove-badge-btn" type="button" data-remove-badge-asset="${esc(a.assetKey)}"
                    data-remove-badge-name="${esc(b)}" title="Remove badge">×</button></span>`).join('')}
              </div>
            </td>
            <td class="gov-score-cell">
              <span class="gov-score ${a.governed ? 'score-high' : 'score-low'}">${esc(a.score)}</span>
            </td>
            <td>
              ${a.deductions?.length
            ? `<ul class="gov-deductions">${a.deductions.map(d =>
              `<li><code>${esc(d.ruleKey)}</code> −${esc(d.points)}: ${esc(d.reason)}</li>`).join('')}</ul>`
            : '<span class="gov-muted">No deductions</span>'}
            </td>
            <td class="gov-actions">
              <button class="btn btn-primary btn-xs" type="button"
                data-review-asset="${esc(a.assetKey)}" data-asset-version="${esc(a.assetVersion)}">Mark reviewed</button>
              ${open.length
            ? `<button class="btn btn-outline btn-xs" type="button" data-accept-risk="${esc(open[0].id)}"
                   data-asset-version="${esc(a.assetVersion)}">Accept risk</button>
                 <button class="btn btn-outline btn-xs" type="button" data-ignore-finding="${esc(open[0].id)}"
                   data-asset-version="${esc(a.assetVersion)}">Ignore</button>`
            : ''}
              <select class="gov-badge-select" data-assign-badge-to="${esc(a.assetKey)}"
                data-asset-version="${esc(a.assetVersion)}">
                <option value="">Assign badge…</option>
                ${STEWARD_BADGES.filter(b => !(a.assignedBadges || []).includes(b))
            .map(b => `<option value="${esc(b)}">${esc(b)}</option>`).join('')}
              </select>
            </td>
          </tr>`;
      }).join('')
      : emptyRow(4, state.dashboard?.lastScan
        ? 'No assets match the current filters.'
        : 'No assets yet. Run a scan to compute the estate posture.');

    return `
      ${scanBanner()}
      <table class="gov-table">
        <thead><tr><th>Asset</th><th>Score</th><th>Why</th><th>Actions</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>`;
  };

  const renderExceptions = () => {
    const items = suppressed().filter(f =>
      (state.categoryFilter === 'all' || f.decisions?.[0]?.categoryValue === state.categoryFilter)
      && (!state.searchFilter || f.assetKey.toLowerCase().includes(state.searchFilter.toLowerCase())));

    const rows = items.length
      ? items.map(f => {
        const d = f.decisions?.[0];
        return `
          <tr>
            <td><div class="gov-asset-path">${esc(f.assetKey)}</div>
              <div class="gov-asset-meta"><code>${esc(f.ruleKey)}</code></div></td>
            <td><span class="gov-badge gov-badge-${f.status === 'accepted-risk' ? 'risk' : 'noise'}">${esc(f.status)}</span></td>
            <td>${esc(d?.reason || '')}</td>
            <td>${esc(d?.decidedBy || 'unknown')}<br>
              <span class="gov-muted">${esc(new Date(f.lastSeenUtc).toLocaleDateString())}</span></td>
            <td>${f.suppressedUntilUtc
            ? esc(new Date(f.suppressedUntilUtc).toLocaleDateString())
            : '<span class="gov-muted">No expiry</span>'}</td>
            <td><button class="btn btn-outline btn-xs" type="button" data-reopen-finding="${esc(f.id)}">Reopen</button></td>
          </tr>`;
      }).join('')
      : emptyRow(6, 'No suppressed findings. Ignored and accepted-risk decisions appear here with their reasons.');

    return `<table class="gov-table">
      <thead><tr><th>Asset</th><th>Disposition</th><th>Reason</th><th>Decided by</th><th>Expires</th><th></th></tr></thead>
      <tbody>${rows}</tbody></table>`;
  };

  const renderGlossary = () => {
    const q = state.searchFilter.toLowerCase();
    const terms = state.glossary.filter(t => !q
      || t.term.toLowerCase().includes(q) || (t.aliases || '').toLowerCase().includes(q));
    const rows = terms.length
      ? terms.map(t => `
        <tr>
          <td><b>${esc(t.term)}</b><div class="gov-asset-meta">${esc(t.dataType)}</div></td>
          <td>${esc(t.aliases)}</td>
          <td>${esc(t.description)}</td>
          <td><code>${esc(t.formula || 'N/A')}</code></td>
          <td>${esc(t.steward || 'Unassigned')}</td>
          <td>
            <button class="btn btn-outline btn-xs" type="button" data-edit-term="${esc(t.term)}">Edit</button>
            <button class="btn btn-outline btn-xs" type="button" data-delete-term="${esc(t.term)}">Delete</button>
          </td>
        </tr>`).join('')
      : emptyRow(6, 'No glossary terms defined. Glossary checks stay off until terms exist and the check is enabled.');

    return `
      <div class="gov-toolbar">
        <button class="btn btn-primary btn-xs" id="btnAddNewTerm" type="button">Define term</button>
        ${state.settings && !state.settings.enableGlossaryCheck
        ? '<span class="gov-muted">Glossary checks are disabled — terms here do not affect scores.</span>'
        : ''}
      </div>
      <table class="gov-table">
        <thead><tr><th>Term</th><th>Aliases</th><th>Definition</th><th>Calculation</th><th>Steward</th><th></th></tr></thead>
        <tbody>${rows}</tbody></table>`;
  };

  const renderQuality = () => {
    if (!dataQualityApi) {
      return `<div class="empty-state empty-state-panel">
        <h2>Data Quality Unavailable</h2>
        <p>The data quality module is not configured or enabled on this server.</p>
      </div>`;
    }

    const totalJobs = state.dqJobs.length;
    const totalRules = state.allRules.length;
    const activeQuarantines = state.quarantineQueue.length;
    
    let avgQuarantineRate = 0;
    let runCount = 0;
    state.dqJobs.forEach(job => {
      const trend = state.dqTrends[job.name];
      if (trend && trend.averageQuarantineRate !== null && trend.averageQuarantineRate !== undefined) {
        avgQuarantineRate += Number(trend.averageQuarantineRate);
        runCount++;
      }
    });
    const avgFailureRate = runCount > 0 ? (avgQuarantineRate / runCount * 100).toFixed(2) + '%' : '0.00%';

    const jobsHtml = state.dqJobs
      .filter(job => {
        const q = state.dqSearchFilter.toLowerCase();
        return !q || job.name.toLowerCase().includes(q) || (job.displayName || '').toLowerCase().includes(q);
      })
      .map(job => {
        const trend = state.dqTrends[job.name];
        const status = trend?.runs?.[0]?.status || 'Unknown';
        const processed = trend?.totalRowsProcessed ?? 0;
        const quarantined = trend?.totalRowsQuarantined ?? 0;
        const warned = trend?.totalRowsWarned ?? 0;
        
        let rateHtml = '—';
        if (trend && trend.latestQuarantineRate !== null && trend.latestQuarantineRate !== undefined) {
          const rateVal = (Number(trend.latestQuarantineRate) * 100).toFixed(2);
          const isBad = Number(trend.latestQuarantineRate) > 0.05;
          rateHtml = `<span style="font-weight: 600; color: ${isBad ? 'var(--portal-error, #f87171)' : 'var(--portal-success, #34d399)'}">${rateVal}%</span>`;
        }

        const badgeClass = status === 'SUCCESS' || status === 'Completed' ? 'gov-badge-auto' : status === 'Failed' ? 'gov-badge-assigned' : 'gov-badge-muted';
        
        return `
          <tr>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151);">
              <div class="gov-asset-path" style="font-weight: 600;">${esc(job.displayName || job.name)}</div>
              <div class="gov-asset-meta" style="font-size: 11px; color: var(--portal-muted, #9ca3af); margin-top: 2px;">${esc(job.description || 'No description available')}</div>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle;">
              <span class="gov-badge ${badgeClass}" style="padding: 2px 6px; border-radius: 4px; font-size: 11px;">${esc(status)}</span>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; vertical-align: middle;">${Number(processed).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; font-weight: 500; vertical-align: middle;">${Number(quarantined).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; vertical-align: middle;">${Number(warned).toLocaleString()}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: right; font-weight: 600; vertical-align: middle;">${rateHtml}</td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); text-align: center; vertical-align: middle;">
              <button class="btn btn-outline btn-xs" data-view-dq-trend="${esc(job.name)}" type="button">Rules & Trend</button>
            </td>
          </tr>
        `;
      }).join('');

    const rulesHtml = state.allRules
      .filter(rule => {
        const q = (state.dqSearchFilter || '').toLowerCase();
        return !q 
          || (rule.targetTable || '').toLowerCase().includes(q) 
          || (rule.targetColumn || '').toLowerCase().includes(q)
          || (rule.rule || '').toLowerCase().includes(q)
          || (rule.jobName || '').toLowerCase().includes(q);
      })
      .map(rule => {
        const action = rule.action || '';
        return `
          <tr>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); font-weight: 600;">
              ${esc(rule.jobName || 'Unassigned')}
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151);">
              <div><code>${esc(rule.targetTable || '—')}</code></div>
              <div style="font-size: 11px; color: var(--portal-muted, #9ca3af); margin-top: 2px;">Column: <code>${esc(rule.targetColumn || '—')}</code></div>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle;">
              <span class="gov-badge gov-badge-auto">${esc(rule.ruleClause || '—')}</span>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle;">
              <code>${esc(rule.rule || '—')}</code>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle;">
              <span class="gov-badge ${action.includes('QUARANTINE') ? 'gov-badge-risk' : 'gov-badge-noise'}">${esc(action || '—')}</span>
            </td>
            <td style="padding: 10px 8px; border-bottom: 1px solid var(--portal-border-soft, #374151); vertical-align: middle; color: var(--portal-muted, #9ca3af); font-size: 11px;">
              ${esc(rule.sourceFile || '—')}:${esc(rule.line || '0')}
            </td>
          </tr>
        `;
      }).join('');

    return `
      <div class="gov-kpis" role="list" aria-label="Data Quality summary">
        ${kpi('Jobs Monitored', totalJobs, 'Configured orchestrator pipelines', 'ok')}
        ${kpi('Rules Coverage', totalRules, 'Active validation guardrails', 'ok')}
        ${kpi('Active Quarantines', activeQuarantines, 'Targets awaiting stewardship', activeQuarantines ? 'warn' : 'ok')}
        ${kpi('Avg Failure Rate', avgFailureRate, 'Mean failure rate across runs', 'muted')}
      </div>
      
      <div class="gov-filters" style="margin-top: 16px;">
        <input type="search" id="govDqSearch" placeholder="Filter jobs or rules by job name, column, or rule pattern..."
          aria-label="Filter data quality jobs or rules"
          value="${esc(state.dqSearchFilter)}">
      </div>

      <div class="gov-card" style="margin-top: 16px; padding: 20px; background: var(--portal-surface, #1e293b); border: 1px solid var(--portal-border, #334155); border-radius: 8px;">
        <h3 style="margin: 0 0 16px 0; font-size: 16px; font-weight: 600;">Protected Pipelines (Jobs)</h3>
        <div class="gov-table-wrap" style="overflow-x: auto; width: 100%;">
          <table class="gov-table" style="width: 100%; border-collapse: collapse; display: table;">
            <thead>
              <tr>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Job / Description</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Latest Status</th>
                <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Processed Rows</th>
                <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Quarantined</th>
                <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Warned</th>
                <th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Failure Rate</th>
                <th style="text-align: center; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 120px;">Actions</th>
              </tr>
            </thead>
            <tbody>
              ${jobsHtml || emptyRow(7, 'No matching jobs found.')}
            </tbody>
          </table>
        </div>
      </div>

      <div class="gov-card" style="margin-top: 24px; padding: 20px; background: var(--portal-surface, #1e293b); border: 1px solid var(--portal-border, #334155); border-radius: 8px;">
        <h3 style="margin: 0 0 16px 0; font-size: 16px; font-weight: 600;">Validation Rule Definitions</h3>
        <div class="gov-table-wrap" style="overflow-x: auto; width: 100%;">
          <table class="gov-table" style="width: 100%; border-collapse: collapse; display: table;">
            <thead>
              <tr>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 150px;">Job</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 220px;">Protected Target</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 100px;">Clause</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Rule Expression</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600; width: 120px;">Action</th>
                <th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151); color: var(--portal-muted,#9ca3af); font-weight: 600;">Source File</th>
              </tr>
            </thead>
            <tbody>
              ${rulesHtml || emptyRow(6, 'No matching rules found.')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  };

  const renderSettings = () => {
    const s = state.settings;
    if (!s) return '';
    const check = (id, key, label, deductId, deductKey) => `
      <div class="gov-setting">
        <label><input type="checkbox" id="${id}" ${s[key] ? 'checked' : ''}> ${esc(label)}</label>
        <input type="number" id="${deductId}" min="0" max="100" value="${esc(s[deductKey])}" ${s[key] ? '' : 'disabled'}>
      </div>`;

    const categoryRows = state.categories.length
      ? state.categories.map(c => `
        <tr>
          <td><b>${esc(c.label)}</b><div class="gov-asset-meta"><code>${esc(c.value)}</code></div></td>
          <td>${esc(c.color)}</td>
          <td>${c.expiryDays ? `${esc(c.expiryDays)} days` : '<span class="gov-muted">No expiry</span>'}</td>
          <td>${c.disabled ? '<span class="gov-muted">Disabled</span>' : 'Active'}</td>
          <td>
            <button class="btn btn-outline btn-xs" type="button" data-edit-cat="${esc(c.value)}">Edit</button>
            ${c.disabled ? '' : `<button class="btn btn-outline btn-xs" type="button" data-disable-cat="${esc(c.value)}">Disable</button>`}
          </td>
        </tr>`).join('')
      : emptyRow(5, 'No suppression categories defined. Stewards can still record a reason without one.');

    return `
      <div class="gov-settings-grid">
        <section>
          <h3>Scoring</h3>
          <label class="setting-label">Target score <span><b>${esc(s.targetScore)}</b>/100</span></label>
          <input type="range" id="settingsTargetScore" min="0" max="100" value="${esc(s.targetScore)}">
          ${check('chkEnableMeta', 'enableMetadataCheck', 'Required metadata', 'settingsDeductMeta', 'deductMetadata')}
          ${check('chkEnablePII', 'enableProtectedDataCheck', 'Protected data classification', 'settingsDeductPII', 'deductProtectedData')}
          ${check('chkEnableGlossary', 'enableGlossaryCheck', 'Glossary coverage', 'settingsDeductGlossary', 'deductGlossary')}
          ${check('chkEnableStale', 'enableStalenessCheck', 'Staleness', 'settingsDeductStale', 'deductStaleness')}
          <label class="setting-label">Stale after
            <input type="number" id="settingsStaleDays" min="1" value="${esc(s.staleAfterDays)}"> days</label>
          <label class="setting-label">Policy level
            <select id="settingsPolicyLevel">
              ${['visible', 'suggestion', 'scored', 'certification-gate'].map(l =>
      `<option value="${l}" ${s.policyLevel === l ? 'selected' : ''}>${l}</option>`).join('')}
            </select></label>
          <button class="btn btn-primary btn-xs" id="btnSaveScoringSettings" type="button">Save scoring</button>
        </section>
        <section>
          <h3>Suppression categories</h3>
          <button class="btn btn-primary btn-xs" id="btnAddNewCategory" type="button">Define category</button>
          <table class="gov-table">
            <thead><tr><th>Category</th><th>Colour</th><th>Expiry</th><th>Status</th><th></th></tr></thead>
            <tbody>${categoryRows}</tbody></table>
        </section>
      </div>`;
  };

  const STEWARD_BADGES = ['Reviewed', 'Trusted', 'Certified'];

  const TABS = [
    ['overview', 'Overview'],
    ['workqueue', 'Workqueue'],
    ['exceptions', 'Exceptions'],
    ['glossary', 'Glossary'],
    ['settings', 'Settings'],
  ];

  const render = async () => {
    prepare(state.tab);
    if (state.load === 'idle') await load();

    const body = state.load !== 'ready' ? stateBanner() : ({
      overview: renderOverview,
      workqueue: renderWorkqueue,
      exceptions: renderExceptions,
      glossary: renderGlossary,
      quality: renderQuality,
      settings: renderSettings,
    }[state.tab] || renderOverview)();

    host.innerHTML = `<div class="gov-container">
      <style>.gov-container{font-family:var(--portal-font,system-ui,sans-serif);color:var(--portal-text,#f9fafb);width:100%;display:flex;flex-direction:column;gap:16px;box-sizing:border-box}
.gov-header{display:flex;justify-content:space-between;align-items:center;gap:16px;border-bottom:1px solid var(--portal-border,#374151);padding-bottom:16px;flex-wrap:wrap}
.gov-header-title h1{margin:0;font-size:22px;font-weight:700}
.gov-header-title p{margin:4px 0 0;color:var(--portal-muted,#9ca3af);font-size:13px}
.gov-header-actions{display:flex;gap:8px;align-items:center}
.gov-tabs{display:flex;gap:4px;flex-wrap:wrap;border-bottom:1px solid var(--portal-border,#374151)}
.gov-tab{background:none;border:none;border-bottom:2px solid transparent;color:var(--portal-muted,#9ca3af);padding:8px 14px;font-size:13px;cursor:pointer}
.gov-tab.active{color:var(--portal-text,#f9fafb);border-bottom-color:var(--portal-accent,#3b82f6)}
.gov-filters{display:flex;gap:8px;flex-wrap:wrap}
.gov-filters input[type=search]{flex:1;min-width:220px;padding:6px 10px;border-radius:6px;border:1px solid var(--portal-border,#374151);background:var(--portal-surface,#111827);color:inherit}
.gov-body{display:flex;flex-direction:column;gap:16px}

/* The four honest states. Each is visually distinct so "denied", "broken", and "nothing here"
   can never be mistaken for one another at a glance. */
.gov-state{border-radius:8px;padding:14px 16px;font-size:13px;line-height:1.5;border:1px solid}
.gov-state p{margin:6px 0 0}
.gov-state-title{margin:0;font-size:14px;font-weight:700}
.gov-state-note{color:var(--portal-muted,#9ca3af);font-size:12px}
.gov-state-loading{border-color:var(--portal-border,#374151);color:var(--portal-muted,#9ca3af);display:flex;align-items:center;gap:10px}
.gov-state-denied{border-color:#7c3aed;background:rgba(124,58,237,.12)}
.gov-state-error{border-color:#dc2626;background:rgba(220,38,38,.12)}
.gov-state-unscanned{border-color:#d97706;background:rgba(217,119,6,.12)}
.gov-spinner{width:14px;height:14px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;display:inline-block;animation:gov-spin .7s linear infinite}
@keyframes gov-spin{to{transform:rotate(360deg)}}
@media (prefers-reduced-motion:reduce){.gov-spinner{animation:none}}
.gov-scanline{font-size:12px;color:var(--portal-muted,#9ca3af)}

.gov-kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px}
.gov-kpi{border:1px solid var(--portal-border,#374151);border-radius:8px;padding:14px;background:var(--portal-surface,#111827)}
.gov-kpi-value{font-size:26px;font-weight:700;line-height:1}
.gov-kpi-label{margin-top:6px;font-size:13px}
.gov-kpi-sub{margin-top:2px;font-size:11px;color:var(--portal-muted,#9ca3af)}
.gov-kpi-warn .gov-kpi-value{color:#f59e0b}
.gov-kpi-risk .gov-kpi-value{color:#ef4444}
.gov-kpi-ok .gov-kpi-value{color:#10b981}
.gov-kpi-muted .gov-kpi-value{color:var(--portal-muted,#9ca3af)}

.gov-table{width:100%;border-collapse:collapse;font-size:13px;display:block;overflow-x:auto}
.gov-table thead th{text-align:left;padding:8px;border-bottom:1px solid var(--portal-border,#374151);color:var(--portal-muted,#9ca3af);font-weight:600;white-space:nowrap}
.gov-table tbody td{padding:10px 8px;border-bottom:1px solid var(--portal-border,#374151);vertical-align:top}
.gov-empty{text-align:center;color:var(--portal-muted,#9ca3af);padding:24px 8px}
.gov-muted{color:var(--portal-muted,#9ca3af)}
.gov-asset-path{font-weight:600;word-break:break-all}
.gov-asset-meta{font-size:11px;color:var(--portal-muted,#9ca3af);margin-top:2px}
.gov-deductions{margin:0;padding-left:16px}
.gov-deductions li{margin-bottom:3px}
.gov-deductions code{font-size:11px}
.gov-score{font-size:18px;font-weight:700}
.score-high{color:#10b981}
.score-low{color:#ef4444}
.gov-score-cell{text-align:center}
.gov-actions{display:flex;flex-direction:column;gap:6px;min-width:150px}
.gov-badges{display:flex;flex-wrap:wrap;gap:4px;margin-top:6px}
.gov-badge{font-size:10px;border-radius:999px;padding:2px 8px;border:1px solid var(--portal-border,#374151);display:inline-flex;align-items:center;gap:4px}
.gov-badge-auto{color:var(--portal-muted,#9ca3af)}
.gov-badge-assigned{color:#60a5fa;border-color:#60a5fa}
.gov-badge-risk{color:#ef4444;border-color:#ef4444}
.gov-badge-noise{color:#f59e0b;border-color:#f59e0b}
.remove-badge-btn{background:none;border:none;color:inherit;cursor:pointer;padding:0;font-size:12px;line-height:1}
.gov-badge-select{font-size:11px;padding:3px 6px;border-radius:6px;border:1px solid var(--portal-border,#374151);background:var(--portal-surface,#111827);color:inherit}

.gov-toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;font-size:12px}
.gov-settings-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:24px}
.gov-settings-grid h3{margin:0 0 10px;font-size:14px}
.gov-setting{display:flex;justify-content:space-between;align-items:center;gap:10px;margin-bottom:8px;font-size:13px}
.gov-setting input[type=number],.setting-label input[type=number],.setting-label select{width:80px;padding:4px 6px;border-radius:6px;border:1px solid var(--portal-border,#374151);background:var(--portal-surface,#111827);color:inherit}
.setting-label{display:flex;justify-content:space-between;align-items:center;gap:10px;font-size:13px;margin:10px 0}
.gov-settings-grid input[type=range]{width:100%}

.gov-modal-backdrop{display:none;position:fixed;inset:0;background:rgba(0,0,0,.6);z-index:60;align-items:center;justify-content:center;padding:16px}
.gov-modal-backdrop.open{display:flex}
.gov-modal{background:var(--portal-surface,#111827);border:1px solid var(--portal-border,#374151);border-radius:10px;padding:20px;width:min(460px,100%);max-height:90vh;overflow-y:auto}
.gov-modal h3{margin:0 0 14px;font-size:16px}
.gov-modal label{display:flex;flex-direction:column;gap:4px;font-size:12px;color:var(--portal-muted,#9ca3af);margin-bottom:10px}
.gov-modal input,.gov-modal select,.gov-modal textarea{padding:6px 8px;border-radius:6px;border:1px solid var(--portal-border,#374151);background:var(--portal-bg,#0b1220);color:var(--portal-text,#f9fafb);font:inherit}
.gov-modal-note{font-size:11px;color:var(--portal-muted,#9ca3af);margin:0 0 14px}
.gov-modal-actions{display:flex;justify-content:flex-end;gap:8px}</style>
      <div class="gov-header">
        <div class="gov-header-title">
          <h1>Governance</h1>
          <p>Estate posture, steward workqueue, and the decisions behind both.</p>
        </div>
        <div class="gov-header-actions">
          <select id="govScope">
            <option value="all" ${state.mode === 'all' ? 'selected' : ''}>All governance work</option>
            <option value="mine" ${state.mode === 'mine' ? 'selected' : ''}>My steward work</option>
          </select>
          <button class="btn btn-primary btn-xs" id="btnRunScan" type="button">Run scan</button>
        </div>
      </div>
      ${state.tab !== 'overview' && state.tab !== 'settings' && state.tab !== 'quality' ? `
      <div class="gov-filters">
        <input type="search" id="govSearch" placeholder="Filter by asset or script path"
          aria-label="Filter governance assets by asset key or script path"
          value="${esc(state.searchFilter)}">
      </div>` : ''}
      <div class="gov-body">${body}</div>
      ${modals()}
      ${renderDqTrendModal()}
    </div>`;

    bind();
  };

  const renderDqTrendModal = () => {
    if (!state.activeDqTrendJob) return '';
    const jobName = state.activeDqTrendJob;
    const trend = state.activeDqTrend;
    const rules = state.activeDqRules || [];

    const formatRate = (rate) => {
      if (rate === null || rate === undefined) return '—';
      return `${(Number(rate) * 100).toFixed(2)}%`;
    };

    const formatDate = (value) => {
      if (!value) return 'Unknown';
      const date = new Date(value);
      return Number.isNaN(date.getTime()) ? 'Unknown' : date.toLocaleString();
    };

    const renderTrendDelta = (delta) => {
      if (delta === null || delta === undefined) return '';
      const pct = Number(delta) * 100;
      if (Math.abs(pct) < 0.005) return '<span class="dq-trend-flat" style="font-size: 12px; color: var(--portal-muted, #9ca3af);">no change vs. earlier runs</span>';
      const cls = pct > 0 ? 'dq-trend-worse' : 'dq-trend-better';
      const color = pct > 0 ? 'var(--portal-error, #f87171)' : 'var(--portal-success, #34d399)';
      const arrow = pct > 0 ? '▲' : '▼';
      const word = pct > 0 ? 'worse' : 'better';
      return `<span class="${cls}" style="font-size: 12px; color: ${color}; margin-left: 4px;">${arrow} ${Math.abs(pct).toFixed(2)} pts ${word} than earlier runs</span>`;
    };

    const renderSparkline = (runs) => {
      const ordered = (runs || []).slice().reverse().filter(r => r && r.quarantineRate !== null && r.quarantineRate !== undefined);
      if (ordered.length < 2) return '';
      const values = ordered.map(r => Number(r.quarantineRate));
      const max = Math.max(...values, 0.0001);
      const bars = ordered.map(run => {
        const height = Math.max(2, Math.round((Number(run.quarantineRate) / max) * 100));
        const title = `${formatDate(run.endTime || run.startTime)} — ${formatRate(run.quarantineRate)} quarantined (${run.rowsQuarantined ?? 0} of ${run.rowsProcessed ?? 0})`;
        return `<span class="dq-spark-bar" style="height:${height}%; display: inline-block; width: 6px; margin-right: 2px; background: var(--portal-accent, #3b82f6); border-radius: 1px;" title="${esc(title)}"></span>`;
      }).join('');
      return `<div class="dq-spark" role="img" aria-label="Quarantine rate over the last ${ordered.length} runs" style="height: 40px; display: flex; align-items: flex-end; margin: 16px 0; background: var(--portal-bg, #0b1220); padding: 4px; border-radius: 4px; border: 1px solid var(--portal-border, #374151);">${bars}</div>`;
    };

    return `
      <div class="modal-overlay" style="display: flex; z-index: 10000;" role="dialog" aria-modal="true"
          aria-labelledby="govTrendModalTitle">
        <div class="modal-card modal-xl" style="max-height: 90vh; display: flex; flex-direction: column; background: var(--portal-surface, #111827); color: var(--portal-text, #f9fafb);">
          <div class="modal-header" style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--portal-border-soft,#374151); padding-bottom: 16px;">
            <div>
              <span class="library-kicker">Data Quality Rules & Trend</span>
              <h2 class="modal-title" id="govTrendModalTitle" style="margin: 4px 0 0 0;">${esc(jobName)}</h2>
              <p class="modal-subtitle" style="margin: 4px 0 0 0;">Rules coverage, metrics on failure rates, and execution outcomes.</p>
            </div>
            <button class="btn btn-outline" id="govDqTrendCloseBtn" type="button">Close</button>
          </div>
          <div class="modal-body" style="flex: 1; overflow: auto; padding-top: 16px;">
            ${state.activeDqLoading ? '<div class="loading-state"><span class="spinner"></span><span>Loading trend and rules...</span></div>' :
              !trend || trend.runCount === 0 ? `<div class="empty-state empty-state-panel">
                <h2>No recorded runs</h2>
                <p>This job has no completed runs with data-quality metrics yet.</p>
              </div>` : `
              <div class="dq-trend-stats" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; margin-bottom: 20px;">
                <div class="dq-trend-stat" style="padding: 12px; background: var(--portal-bg, #0b1220); border-radius: 6px; border: 1px solid var(--portal-border, #374151);">
                  <span class="dq-trend-label" style="display: block; font-size: 12px; color: var(--portal-muted, #9ca3af); margin-bottom: 4px;">Latest quarantine rate</span>
                  <strong style="font-size: 20px;">${formatRate(trend.latestQuarantineRate)}</strong>
                  ${renderTrendDelta(trend.quarantineRateDelta)}
                </div>
                <div class="dq-trend-stat" style="padding: 12px; background: var(--portal-bg, #0b1220); border-radius: 6px; border: 1px solid var(--portal-border, #374151);">
                  <span class="dq-trend-label" style="display: block; font-size: 12px; color: var(--portal-muted, #9ca3af); margin-bottom: 4px;">Average over ${trend.runCount} run(s)</span>
                  <strong style="font-size: 20px;">${formatRate(trend.averageQuarantineRate)}</strong>
                </div>
                <div class="dq-trend-stat" style="padding: 12px; background: var(--portal-bg, #0b1220); border-radius: 6px; border: 1px solid var(--portal-border, #374151);">
                  <span class="dq-trend-label" style="display: block; font-size: 12px; color: var(--portal-muted, #9ca3af); margin-bottom: 4px;">Rows quarantined / warned</span>
                  <strong style="font-size: 20px;">${Number(trend.totalRowsQuarantined ?? 0).toLocaleString()} / ${Number(trend.totalRowsWarned ?? 0).toLocaleString()}</strong>
                  <span class="dq-trend-flat" style="display: block; font-size: 12px; color: var(--portal-muted, #9ca3af); margin-top: 4px;">of ${Number(trend.totalRowsProcessed ?? 0).toLocaleString()} processed</span>
                </div>
              </div>
              ${renderSparkline(trend.runs || [])}
              
              <h4 style="margin: 20px 0 10px 0; font-size: 15px; font-weight: 600; border-bottom: 1px solid var(--portal-border,#374151); padding-bottom: 6px;">Rules protecting columns</h4>
              ${rules.length ? `<table class="dq-rows-table" style="width: 100%; border-collapse: collapse; margin-bottom: 20px;">
                <thead><tr><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Target Table</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Column</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Clause</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Rule</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Action</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Source</th></tr></thead>
                <tbody>${rules.map(rule => `<tr>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(rule.targetTable || '—')}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(rule.targetColumn || '—')}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);"><code>${esc(rule.ruleClause || '—')}</code></td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);"><code>${esc(rule.rule || '—')}</code></td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(rule.action || '—')}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(rule.sourceFile || '—')}:${esc(rule.line || '0')}</td>
                </tr>`).join('')}</tbody>
              </table>` : '<p class="library-subtitle" style="color: var(--portal-muted, #9ca3af);">No readable rule definitions were found for this job script.</p>'}
              
              ${(trend.topRuleFailures || []).length ? `
                <h4 style="margin: 20px 0 10px 0; font-size: 15px; font-weight: 600; border-bottom: 1px solid var(--portal-border,#374151); padding-bottom: 6px;">Rules firing most</h4>
                <table class="dq-rows-table" style="width: 100%; border-collapse: collapse; margin-bottom: 20px;">
                  <thead><tr><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Column</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Rule</th><th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Failures</th></tr></thead>
                  <tbody>${trend.topRuleFailures.map(f => `<tr>
                    <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(f.column || '—')}</td>
                    <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);"><code>${esc(f.rule || '—')}</code></td>
                    <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); text-align: right;">${Number(f.count ?? 0).toLocaleString()}</td>
                  </tr>`).join('')}</tbody>
                </table>` : '<p class="library-subtitle" style="color: var(--portal-muted, #9ca3af);">No per-rule failure counts recorded for these runs.</p>'}
              
              <h4 style="margin: 20px 0 10px 0; font-size: 15px; font-weight: 600; border-bottom: 1px solid var(--portal-border,#374151); padding-bottom: 6px;">Recent runs</h4>
              <table class="dq-rows-table" style="width: 100%; border-collapse: collapse;">
                <thead><tr><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Completed</th><th style="text-align: left; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Status</th><th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Processed</th><th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Quarantined</th><th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Warned</th><th style="text-align: right; padding: 8px; border-bottom: 1px solid var(--portal-border,#374151);">Rate</th></tr></thead>
                <tbody>${(trend.runs || []).map(run => `<tr>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(formatDate(run.endTime || run.startTime))}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151);">${esc(run.status || '—')}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); text-align: right;">${Number(run.rowsProcessed ?? 0).toLocaleString()}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); text-align: right;">${Number(run.rowsQuarantined ?? 0).toLocaleString()}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); text-align: right;">${Number(run.rowsWarned ?? 0).toLocaleString()}</td>
                  <td style="padding: 8px; border-bottom: 1px solid var(--portal-border-soft,#374151); text-align: right;">${formatRate(run.quarantineRate)}</td>
                </tr>`).join('')}</tbody>
              </table>`}
          </div>
        </div>
      </div>
    `;
  };

  const modals = () => `
    <div class="gov-modal-backdrop" id="decisionModalBackdrop" role="dialog" aria-modal="true"
      aria-labelledby="decisionModalTitle" aria-hidden="true">
      <div class="gov-modal">
        <h3 id="decisionModalTitle">Accept risk</h3>
        <label>Asset<input id="decisionAsset" readonly></label>
        <label>Category<select id="decisionCategory"></select></label>
        <label>Justification<textarea id="decisionReason" rows="3"
          placeholder="Why this does not require remediation"></textarea></label>
        <p class="gov-modal-note">Recorded against this asset version. If the asset changes, the
          finding reopens — a suppression does not carry forward onto content nobody reviewed.</p>
        <div class="gov-modal-actions">
          <button class="btn btn-outline btn-xs" id="btnCancelDecision" type="button">Cancel</button>
          <button class="btn btn-primary btn-xs" id="btnConfirmDecision" type="button">Confirm</button>
        </div>
      </div>
    </div>
    <div class="gov-modal-backdrop" id="glossaryModalBackdrop" role="dialog" aria-modal="true"
      aria-labelledby="glossaryModalTitle" aria-hidden="true">
      <div class="gov-modal">
        <h3 id="glossaryModalTitle">Define term</h3>
        <label>Term<input id="glossaryTerm"></label>
        <label>Data type<input id="glossaryType" placeholder="DECIMAL(18,2)"></label>
        <label>Aliases<input id="glossaryAliases" placeholder="rev, gross_sales"></label>
        <label>Approved calculation<input id="glossaryFormula" placeholder="SUM(sales_amount)"></label>
        <label>Definition<textarea id="glossaryDesc" rows="3"></textarea></label>
        <div class="gov-modal-actions">
          <button class="btn btn-outline btn-xs" id="btnCancelGlossaryModal" type="button">Cancel</button>
          <button class="btn btn-primary btn-xs" id="btnConfirmGlossaryModal" type="button">Save</button>
        </div>
      </div>
    </div>
    <div class="gov-modal-backdrop" id="categoryModalBackdrop" role="dialog" aria-modal="true"
      aria-labelledby="categoryModalTitle" aria-hidden="true">
      <div class="gov-modal">
        <h3 id="categoryModalTitle">Define category</h3>
        <label>Label<input id="catLabel"></label>
        <label>Value<input id="catValue" placeholder="false-positive"></label>
        <label>Colour<select id="catColor">
          <option value="risk">Red (risk escalation)</option>
          <option value="false-positive">Green (compliance exclude)</option>
          <option value="noise">Yellow (muted noise)</option>
        </select></label>
        <label>Expiry (days, blank for none)<input id="catExpiry" type="number" min="1"></label>
        <div class="gov-modal-actions">
          <button class="btn btn-outline btn-xs" id="btnCancelCategoryModal" type="button">Cancel</button>
          <button class="btn btn-primary btn-xs" id="btnConfirmCategoryModal" type="button">Save</button>
        </div>
      </div>
    </div>`;

  // Opening a dialog moves focus into it and traps Tab; closing returns focus to whatever opened
  // it. Without the return, keyboard focus lands back at the top of the document and the user has
  // to tab through the whole page to get back to where they were — which is why "it works with a
  // mouse" is not evidence a dialog is usable.
  let dialogReturnFocus = null;
  let trapHandler = null;

  const focusable = dialog => [...dialog.querySelectorAll(
    'button, [href], input:not([type=hidden]), select, textarea, [tabindex]:not([tabindex="-1"])')]
    .filter(el => !el.disabled && el.offsetParent !== null);

  function openDialog(dialog) {
    dialogReturnFocus = document.activeElement;
    dialog.classList.add('open');
    dialog.removeAttribute('aria-hidden');

    const items = focusable(dialog);
    items[0]?.focus();

    trapHandler = event => {
      if (event.key === 'Escape') { closeDialog(dialog); return; }
      if (event.key !== 'Tab') return;
      const current = focusable(dialog);
      if (current.length === 0) return;
      const first = current[0];
      const last = current[current.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    dialog.addEventListener('keydown', trapHandler);
  }

  function closeDialog(dialog) {
    if (trapHandler) { dialog.removeEventListener('keydown', trapHandler); trapHandler = null; }
    dialog.classList.remove('open');
    dialog.setAttribute('aria-hidden', 'true');
    if (dialogReturnFocus?.isConnected) dialogReturnFocus.focus();
    dialogReturnFocus = null;
  }

  // Listeners are bound rather than inlined: the Portal CSP sets script-src-attr 'none', so an
  // inline onclick silently does nothing.
  function bind() {
    const on = (sel, evt, fn) => host.querySelector(sel)?.addEventListener(evt, fn);
    const each = (sel, evt, fn) => host.querySelectorAll(sel).forEach(el => el.addEventListener(evt, fn));

    each('[data-view-dq-trend]', 'click', async e => {
      const jobName = e.currentTarget.getAttribute('data-view-dq-trend');
      if (jobName) {
        state.activeDqTrendJob = jobName;
        state.activeDqTrend = null;
        state.activeDqRules = [];
        state.activeDqLoading = true;
        render(); // render loading modal
        try {
          const [trend, rules] = await Promise.allSettled([
            dataQualityApi.qualityTrend({ jobName }),
            dataQualityApi.qualityRules(jobName)
          ]);
          if (trend.status === 'fulfilled') state.activeDqTrend = trend.value;
          if (rules.status === 'fulfilled') state.activeDqRules = rules.value;
        } catch (err) {
          console.error(err);
        } finally {
          state.activeDqLoading = false;
          render(); // render completed modal
        }
      }
    });

    on('#govDqTrendCloseBtn', 'click', () => {
      state.activeDqTrendJob = null;
      state.activeDqTrend = null;
      state.activeDqRules = [];
      render();
    });

    on('#govDqSearch', 'input', e => {
      state.dqSearchFilter = e.target.value;
      const active = host.querySelector('#govDqSearch');
      const caret = active?.selectionStart;
      render().then(() => {
        const next = host.querySelector('#govDqSearch');
        if (next) { next.focus(); next.setSelectionRange(caret, caret); }
      });
    });

    host.querySelectorAll('[data-gov-tab]').forEach(btn => btn.addEventListener('click', () => {
      state.tab = btn.getAttribute('data-gov-tab');
      render();
    }));
    on('#btnGovRetry', 'click', async () => { state.load = 'idle'; await render(); });
    on('#govScope', 'change', async e => {
      state.mode = e.target.value;
      state.load = 'idle';
      await render();
    });
    on('#govSearch', 'input', e => {
      state.searchFilter = e.target.value;
      const active = host.querySelector('#govSearch');
      const caret = active?.selectionStart;
      render().then(() => {
        const next = host.querySelector('#govSearch');
        if (next) { next.focus(); next.setSelectionRange(caret, caret); }
      });
    });

    on('#btnRunScan', 'click', () => mutate(() => governanceApi.scan(), {
      success: 'Governance scan completed.',
      failure: 'The scan could not be run.',
      auditAction: 'governance.scan',
    }));

    each('[data-review-asset]', 'click', e => {
      const btn = e.currentTarget;
      mutate(() => governanceApi.reviewAsset({
        assetKey: btn.getAttribute('data-review-asset'),
        assetVersion: btn.getAttribute('data-asset-version'),
      }), {
        success: 'Asset marked reviewed at its current version.',
        failure: 'The review could not be recorded.',
        auditAction: 'governance.asset.review',
      });
    });

    each('[data-assign-badge-to]', 'change', e => {
      const select = e.currentTarget;
      const badge = select.value;
      if (!badge) return;
      mutate(() => governanceApi.assignBadge({
        assetKey: select.getAttribute('data-assign-badge-to'),
        badge,
        assetVersion: select.getAttribute('data-asset-version'),
      }), {
        success: `Badge "${badge}" assigned.`,
        failure: 'The badge could not be assigned.',
        auditAction: 'governance.badge.assign',
      });
    });

    each('.remove-badge-btn', 'click', e => {
      e.stopPropagation();
      const btn = e.currentTarget;
      mutate(() => governanceApi.removeBadge({
        assetKey: btn.getAttribute('data-remove-badge-asset'),
        badge: btn.getAttribute('data-remove-badge-name'),
      }), {
        success: 'Badge removed.',
        failure: 'The badge could not be removed.',
        auditAction: 'governance.badge.remove',
      });
    });

    each('[data-reopen-finding]', 'click', e => {
      const id = Number(e.currentTarget.getAttribute('data-reopen-finding'));
      mutate(() => governanceApi.decideFinding(id, {
        decision: 'reopen',
        reason: 'Reopened by steward from the exceptions queue.',
        assetVersion: null,
      }), {
        success: 'Finding reopened.',
        failure: 'The finding could not be reopened.',
        auditAction: 'governance.finding.reopen',
      });
    });

    const decisionModal = host.querySelector('#decisionModalBackdrop');
    const openDecision = (id, version, decision, assetLabel) => {
      state.pendingFindingId = id;
      state.pendingDecision = decision;
      state.pendingVersion = version;
      host.querySelector('#decisionModalTitle').textContent =
        decision === 'ignore' ? 'Ignore as false positive' : 'Accept risk';
      host.querySelector('#decisionAsset').value = assetLabel;
      host.querySelector('#decisionReason').value = '';
      host.querySelector('#decisionCategory').innerHTML =
        ['<option value="">No category</option>']
          .concat(state.categories.filter(c => !c.disabled)
            .map(c => `<option value="${esc(c.value)}">${esc(c.label)}</option>`))
          .join('');
      openDialog(decisionModal);
    };

    each('[data-accept-risk]', 'click', e => {
      const btn = e.currentTarget;
      const finding = state.findings.find(f => f.id === Number(btn.getAttribute('data-accept-risk')));
      openDecision(Number(btn.getAttribute('data-accept-risk')),
        btn.getAttribute('data-asset-version'), 'accept-risk', finding?.assetKey || '');
    });
    each('[data-ignore-finding]', 'click', e => {
      const btn = e.currentTarget;
      const finding = state.findings.find(f => f.id === Number(btn.getAttribute('data-ignore-finding')));
      openDecision(Number(btn.getAttribute('data-ignore-finding')),
        btn.getAttribute('data-asset-version'), 'ignore', finding?.assetKey || '');
    });

    on('#btnCancelDecision', 'click', () => decisionModal.classList.remove('open'));
    on('#btnConfirmDecision', 'click', async () => {
      const reason = host.querySelector('#decisionReason').value.trim();
      if (!reason) {
        notify('Enter a justification. The decision has to be reviewable later.',
          { title: 'Justification required', tone: 'warning' });
        return;
      }
      closeDialog(decisionModal);
      await mutate(() => governanceApi.decideFinding(state.pendingFindingId, {
        decision: state.pendingDecision,
        categoryValue: host.querySelector('#decisionCategory').value || null,
        reason,
        assetVersion: state.pendingVersion,
      }), {
        success: 'Decision recorded.',
        failure: 'The decision could not be recorded.',
        auditAction: `governance.finding.${state.pendingDecision}`,
      });
    });

    // ── glossary ──
    const gModal = host.querySelector('#glossaryModalBackdrop');
    on('#btnAddNewTerm', 'click', () => {
      state.editingTerm = null;
      host.querySelector('#glossaryModalTitle').textContent = 'Define term';
      ['#glossaryTerm', '#glossaryType', '#glossaryAliases', '#glossaryFormula', '#glossaryDesc']
        .forEach(sel => { host.querySelector(sel).value = ''; });
      host.querySelector('#glossaryTerm').readOnly = false;
      openDialog(gModal);
    });
    on('#btnCancelGlossaryModal', 'click', () => gModal.classList.remove('open'));
    each('[data-edit-term]', 'click', e => {
      const term = state.glossary.find(t => t.term === e.currentTarget.getAttribute('data-edit-term'));
      if (!term) return;
      state.editingTerm = term.term;
      host.querySelector('#glossaryModalTitle').textContent = 'Edit term';
      host.querySelector('#glossaryTerm').value = term.term;
      host.querySelector('#glossaryTerm').readOnly = true;
      host.querySelector('#glossaryType').value = term.dataType;
      host.querySelector('#glossaryAliases').value = term.aliases;
      host.querySelector('#glossaryFormula').value = term.formula || '';
      host.querySelector('#glossaryDesc').value = term.description;
      openDialog(gModal);
    });
    on('#btnConfirmGlossaryModal', 'click', async () => {
      const payload = {
        term: host.querySelector('#glossaryTerm').value.trim(),
        dataType: host.querySelector('#glossaryType').value.trim(),
        aliases: host.querySelector('#glossaryAliases').value.trim(),
        formula: host.querySelector('#glossaryFormula').value.trim() || null,
        description: host.querySelector('#glossaryDesc').value.trim(),
        disabled: false,
      };
      if (!payload.term || !payload.dataType || !payload.aliases || !payload.description) {
        notify('Term, type, aliases, and definition are required.',
          { title: 'Complete required fields', tone: 'warning' });
        return;
      }
      closeDialog(gModal);
      await mutate(() => governanceApi.saveGlossaryTerm(payload), {
        success: 'Glossary term saved.',
        failure: 'The term could not be saved.',
        auditAction: 'governance.glossary.save',
      });
    });
    each('[data-delete-term]', 'click', async e => {
      const term = e.currentTarget.getAttribute('data-delete-term');
      const ok = await confirm(`Delete glossary term "${term}"?`, {
        title: 'Delete glossary term',
        impact: 'Metadata rules that reference this term may stop matching.',
        confirmLabel: 'Delete term',
        danger: true,
        auditAction: 'governance.glossary.delete',
      });
      if (!ok) return;
      await mutate(() => governanceApi.deleteGlossaryTerm(term), {
        success: 'Glossary term deleted.',
        failure: 'The term could not be deleted.',
        auditAction: 'governance.glossary.delete',
      });
    });

    // ── settings ──
    on('#settingsTargetScore', 'input', e => {
      const label = host.querySelector('.setting-label span b');
      if (label) label.textContent = e.target.value;
    });
    [['#chkEnableMeta', '#settingsDeductMeta'], ['#chkEnablePII', '#settingsDeductPII'],
    ['#chkEnableGlossary', '#settingsDeductGlossary'], ['#chkEnableStale', '#settingsDeductStale']]
      .forEach(([chk, num]) => on(chk, 'change', e => {
        const input = host.querySelector(num);
        if (input) input.disabled = !e.target.checked;
      }));

    on('#btnSaveScoringSettings', 'click', async () => {
      const num = (sel, fallback) => {
        const parsed = parseInt(host.querySelector(sel)?.value, 10);
        return Number.isFinite(parsed) ? parsed : fallback;
      };
      await mutate(() => governanceApi.saveSettings({
        targetScore: num('#settingsTargetScore', state.settings.targetScore),
        enableMetadataCheck: host.querySelector('#chkEnableMeta').checked,
        enableProtectedDataCheck: host.querySelector('#chkEnablePII').checked,
        enableGlossaryCheck: host.querySelector('#chkEnableGlossary').checked,
        enableStalenessCheck: host.querySelector('#chkEnableStale').checked,
        deductMetadata: num('#settingsDeductMeta', 5),
        deductProtectedData: num('#settingsDeductPII', 10),
        deductGlossary: num('#settingsDeductGlossary', 5),
        deductStaleness: num('#settingsDeductStale', 15),
        staleAfterDays: num('#settingsStaleDays', 30),
        policyLevel: host.querySelector('#settingsPolicyLevel').value,
      }), {
        success: 'Scoring configuration saved.',
        failure: 'The scoring configuration could not be saved.',
        auditAction: 'governance.scoring.update',
      });
    });

    const cModal = host.querySelector('#categoryModalBackdrop');
    on('#btnAddNewCategory', 'click', () => {
      state.editingCategory = null;
      host.querySelector('#categoryModalTitle').textContent = 'Define category';
      host.querySelector('#catLabel').value = '';
      host.querySelector('#catValue').value = '';
      host.querySelector('#catValue').readOnly = false;
      host.querySelector('#catColor').value = 'noise';
      host.querySelector('#catExpiry').value = '';
      openDialog(cModal);
    });
    on('#btnCancelCategoryModal', 'click', () => cModal.classList.remove('open'));
    each('[data-edit-cat]', 'click', e => {
      const cat = state.categories.find(c => c.value === e.currentTarget.getAttribute('data-edit-cat'));
      if (!cat) return;
      state.editingCategory = cat.value;
      host.querySelector('#categoryModalTitle').textContent = 'Edit category';
      host.querySelector('#catLabel').value = cat.label;
      host.querySelector('#catValue').value = cat.value;
      host.querySelector('#catValue').readOnly = true;
      host.querySelector('#catColor').value = cat.color;
      host.querySelector('#catExpiry').value = cat.expiryDays ?? '';
      openDialog(cModal);
    });
    on('#btnConfirmCategoryModal', 'click', async () => {
      const label = host.querySelector('#catLabel').value.trim();
      const value = host.querySelector('#catValue').value.trim();
      if (!label || !value) {
        notify('Enter both a category label and value.',
          { title: 'Complete required fields', tone: 'warning' });
        return;
      }
      const expiry = parseInt(host.querySelector('#catExpiry').value, 10);
      closeDialog(cModal);
      await mutate(() => governanceApi.saveCategory({
        value,
        label,
        color: host.querySelector('#catColor').value,
        expiryDays: Number.isFinite(expiry) && expiry > 0 ? expiry : null,
        disabled: false,
      }), {
        success: 'Category saved.',
        failure: 'The category could not be saved.',
        auditAction: 'governance.category.save',
      });
    });
    each('[data-disable-cat]', 'click', async e => {
      const value = e.currentTarget.getAttribute('data-disable-cat');
      // Disable, never delete: historical suppressions cite this value, and removing it would
      // leave them pointing at a reason nobody can look up.
      const ok = await confirm(`Disable bypass category "${value}"?`, {
        title: 'Disable category',
        impact: 'Stewards can no longer choose it. Existing decisions keep citing it.',
        confirmLabel: 'Disable',
        danger: true,
        auditAction: 'governance.category.disable',
      });
      if (!ok) return;
      await mutate(() => governanceApi.disableCategory(value), {
        success: 'Category disabled.',
        failure: 'The category could not be disabled.',
        auditAction: 'governance.category.disable',
      });
    });
  }

  return {
    render,
    setTab(tabName) {
      state.tab = tabName;
      return render();
    },
    reload() {
      state.load = 'idle';
      return render();
    },
    dispose() { },
    state,
  };
}
