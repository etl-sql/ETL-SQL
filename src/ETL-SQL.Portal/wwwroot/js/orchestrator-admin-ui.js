/**
 * Orchestrator Admin UI rendering components and helpers.
 * Covers Schedules catalog, Notifications catalog, Watermark state,
 * Definition Change Log (Audit), Cross-Job Dependency View, Data Quality & Stewardship metrics,
 * Table Search/Filter/Pagination, and Multi-Day Calendar View.
 */

export function escHtml(str) {
  if (str === null || str === undefined) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

export function fmtDt(isoStr) {
  if (!isoStr) return '—';
  try {
    const d = new Date(isoStr);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleString([], {
      month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  } catch { return '—'; }
}

export function fmtTimeAgo(isoStr) {
  if (!isoStr) return '—';
  try {
    const d = new Date(isoStr);
    if (isNaN(d.getTime())) return '—';
    const sec = Math.floor((Date.now() - d.getTime()) / 1000);
    if (sec < 60) return `${sec}s ago`;
    const min = Math.floor(sec / 60);
    if (min < 60) return `${min}m ago`;
    const hr = Math.floor(min / 60);
    if (hr < 24) return `${hr}h ago`;
    const day = Math.floor(hr / 24);
    return `${day}d ago`;
  } catch { return '—'; }
}

// ── Search, Filter & Pagination ───────────────────────────────────────────────

export function filterAndPaginateJobs(jobs, { search = '', status = 'all', page = 1, pageSize = 25 }) {
  const query = search.trim().toLowerCase();
  let filtered = (jobs || []).filter(j => {
    // Status filter
    const isEnabled = j.IsEnabled ?? j.isEnabled ?? true;
    if (status === 'enabled' && !isEnabled) return false;
    if (status === 'disabled' && isEnabled) return false;

    // Search query
    if (!query) return true;
    const name = (j.Name ?? j.name ?? '').toLowerCase();
    const disp = (j.DisplayName ?? j.displayName ?? '').toLowerCase();
    const desc = (j.Description ?? j.description ?? '').toLowerCase();
    const target = (j.TargetPath ?? j.targetPath ?? j.Script ?? j.script ?? '').toLowerCase();
    const sched = `${j.Interval ?? j.interval ?? ''} ${j.Unit ?? j.unit ?? ''} ${j.AtTime ?? j.atTime ?? ''}`.toLowerCase();
    const owner = (j.CreatedBy ?? j.createdBy ?? '').toLowerCase();

    return name.includes(query) || disp.includes(query) || desc.includes(query) ||
      target.includes(query) || sched.includes(query) || owner.includes(query);
  });

  const total = filtered.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const currentPage = Math.min(Math.max(1, page), totalPages);
  const startIdx = (currentPage - 1) * pageSize;
  const paged = filtered.slice(startIdx, startIdx + pageSize);

  return {
    items: paged,
    total,
    page: currentPage,
    totalPages,
    pageSize,
    startIdx: total === 0 ? 0 : startIdx + 1,
    endIdx: Math.min(startIdx + pageSize, total)
  };
}

// ── Schedules Catalog Table ───────────────────────────────────────────────────

export function renderSchedulesTable(schedules, linkedCounts = {}) {
  if (!schedules || !schedules.length) {
    return `<div class="empty-state" style="padding:32px 0;text-align:center;color:var(--portal-muted)">
      No shared schedules defined yet. Click <strong>+ New Schedule</strong> to create one.
    </div>`;
  }

  return `
    <div style="overflow-x:auto;">
    <table class="data-table">
      <thead><tr>
        <th>Schedule Name</th>
        <th>Cron Expression</th>
        <th>Timezone</th>
        <th>Status</th>
        <th>Linked Jobs</th>
        <th>Owner / Attribution</th>
        <th>Actions</th>
      </tr></thead>
      <tbody>
      ${schedules.map(s => {
        const name = s.Name ?? s.name;
        const disp = s.DisplayName ?? s.displayName ?? name;
        const cron = s.Cron ?? s.cron;
        const tz = s.TimeZone ?? s.timeZone ?? 'UTC';
        const isEnabled = s.IsEnabled ?? s.isEnabled ?? true;
        const linkedCount = linkedCounts[name] || 0;
        const owner = s.CreatedBy ?? s.createdBy ?? '—';
        const version = s.Version ?? s.version ?? 1;

        return `
          <tr data-schedule-name="${escHtml(name)}">
            <td>
              <strong>${escHtml(disp)}</strong>
              ${disp !== name ? `<div style="font-size:.78em;color:var(--portal-muted)">${escHtml(name)}</div>` : ''}
            </td>
            <td><code>${escHtml(cron)}</code></td>
            <td><span class="badge" style="font-size:.78em;">${escHtml(tz)}</span></td>
            <td>
              <span class="badge ${isEnabled ? 'badge-success' : 'badge-neutral'}">
                ${isEnabled ? 'Active' : 'Disabled'}
              </span>
            </td>
            <td>
              <span class="badge" style="font-size:.82em;background:var(--portal-surface-subtle);">
                ${linkedCount} job${linkedCount === 1 ? '' : 's'}
              </span>
            </td>
            <td style="font-size:.82em;color:var(--portal-muted)">
              <div>${escHtml(owner)}</div>
              <div style="font-size:.75em;">v${version}</div>
            </td>
            <td>
              <div class="table-actions">
                <button class="btn btn-sm btn-outline" data-action="edit-schedule" data-name="${escHtml(name)}">Edit</button>
                <button class="btn btn-sm btn-outline" data-action="toggle-schedule" data-name="${escHtml(name)}" data-enabled="${isEnabled}">
                  ${isEnabled ? 'Disable' : 'Enable'}
                </button>
                <button class="btn btn-sm btn-outline btn-danger-outline" data-action="delete-schedule" data-name="${escHtml(name)}">Delete</button>
              </div>
            </td>
          </tr>`;
      }).join('')}
      </tbody>
    </table>
    </div>`;
}

// ── Notifications Catalog Table ───────────────────────────────────────────────

export function renderNotificationsTable(notifications, linkedCounts = {}) {
  if (!notifications || !notifications.length) {
    return `<div class="empty-state" style="padding:32px 0;text-align:center;color:var(--portal-muted)">
      No notifications configured yet. Click <strong>+ New Notification</strong> to configure delivery channels.
    </div>`;
  }

  return `
    <div style="overflow-x:auto;">
    <table class="data-table">
      <thead><tr>
        <th>Notification Name</th>
        <th>Connection Alias</th>
        <th>Recipient</th>
        <th>Status</th>
        <th>Linked Jobs</th>
        <th>Attribution</th>
        <th>Actions</th>
      </tr></thead>
      <tbody>
      ${notifications.map(n => {
        const name = n.Name ?? n.name;
        const disp = n.DisplayName ?? n.displayName ?? name;
        const conn = n.ConnectionName ?? n.connectionName;
        const recipient = n.Recipient ?? n.recipient ?? '—';
        const isEnabled = n.IsEnabled ?? n.isEnabled ?? true;
        const linkedCount = linkedCounts[name] || 0;
        const owner = n.CreatedBy ?? n.createdBy ?? '—';

        return `
          <tr data-notification-name="${escHtml(name)}">
            <td>
              <strong>${escHtml(disp)}</strong>
              ${disp !== name ? `<div style="font-size:.78em;color:var(--portal-muted)">${escHtml(name)}</div>` : ''}
            </td>
            <td><code>${escHtml(conn)}</code></td>
            <td>${escHtml(recipient)}</td>
            <td>
              <span class="badge ${isEnabled ? 'badge-success' : 'badge-neutral'}">
                ${isEnabled ? 'Active' : 'Disabled'}
              </span>
            </td>
            <td>
              <span class="badge" style="font-size:.82em;background:var(--portal-surface-subtle);">
                ${linkedCount} job${linkedCount === 1 ? '' : 's'}
              </span>
            </td>
            <td style="font-size:.82em;color:var(--portal-muted)">${escHtml(owner)}</td>
            <td>
              <div class="table-actions">
                <button class="btn btn-sm btn-outline" data-action="dispatch-notification" data-name="${escHtml(name)}" title="Test notification dispatch">Test</button>
                <button class="btn btn-sm btn-outline" data-action="edit-notification" data-name="${escHtml(name)}">Edit</button>
                <button class="btn btn-sm btn-outline btn-danger-outline" data-action="delete-notification" data-name="${escHtml(name)}">Delete</button>
              </div>
            </td>
          </tr>`;
      }).join('')}
      </tbody>
    </table>
    </div>`;
}

// ── Watermarks & Incremental State ────────────────────────────────────────────

export function renderWatermarksTable(states, jobName) {
  if (!states || !states.length) {
    return `<div class="empty-state" style="padding:16px 0;text-align:center;color:var(--portal-muted);font-size:.85em;">
      No high-water marks or incremental state keys recorded for this job.
      <div style="margin-top:8px;">
        <button class="btn btn-sm btn-outline" id="addStateBtn" data-job="${escHtml(jobName)}">+ Set Watermark Key</button>
      </div>
    </div>`;
  }

  return `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;">
      <span style="font-size:.82em;color:var(--portal-muted)">${states.length} recorded state key${states.length === 1 ? '' : 's'}</span>
      <button class="btn btn-sm btn-outline" id="addStateBtn" data-job="${escHtml(jobName)}">+ Add Key</button>
    </div>
    <div style="overflow-x:auto;">
    <table class="data-table data-table-sm" style="font-size:.82em;">
      <thead><tr>
        <th>State Key</th>
        <th>Current Value</th>
        <th>Last Updated</th>
        <th>Actions</th>
      </tr></thead>
      <tbody>
      ${states.map(s => {
        const key = s.StateKey ?? s.stateKey ?? s.Key ?? s.key;
        const val = s.StateValue ?? s.stateValue ?? s.Value ?? s.value ?? '<null>';
        const updated = s.UpdatedAt ?? s.updatedAt;

        return `
          <tr>
            <td><code>${escHtml(key)}</code></td>
            <td style="max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="${escHtml(val)}">
              <code>${escHtml(val)}</code>
            </td>
            <td>${fmtDt(updated)}</td>
            <td>
              <div class="table-actions">
                <button class="btn btn-sm btn-outline" data-state-action="edit" data-job="${escHtml(jobName)}" data-key="${escHtml(key)}" data-val="${escHtml(val)}">Edit</button>
                <button class="btn btn-sm btn-outline btn-danger-outline" data-state-action="reset" data-job="${escHtml(jobName)}" data-key="${escHtml(key)}" title="Clear key for full backfill">Reset</button>
              </div>
            </td>
          </tr>`;
      }).join('')}
      </tbody>
    </table>
    </div>`;
}

// ── Definition Change Log (Audit Trail) ───────────────────────────────────────

export function renderJobAuditTrail(auditEntries) {
  if (!auditEntries || !auditEntries.length) {
    return `<div class="empty-state" style="padding:24px 0;text-align:center;color:var(--portal-muted);font-size:.85em;">
      No audit records found for this job.
    </div>`;
  }

  return `
    <div class="orch-audit-timeline" style="display:flex;flex-direction:column;gap:12px;padding:8px 0;">
      ${auditEntries.map(entry => {
        const action = entry.Action ?? entry.action ?? 'Action';
        const timestamp = entry.Timestamp ?? entry.timestamp ?? entry.OccurredAt ?? entry.occurredAt;
        const actor = entry.ActorId ?? entry.actorId ?? (entry.UserId ? `User #${entry.UserId}` : 'System');
        const detail = entry.Detail ?? entry.detail ?? '';
        const cap = entry.StudioCapability ?? entry.studioCapability;

        return `
          <div class="orch-audit-card" style="padding:10px 14px;background:var(--portal-surface-subtle);border-radius:6px;border:1px solid var(--portal-border-soft);font-size:.82em;">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;">
              <span class="badge badge-accent" style="font-weight:700;">${escHtml(action)}</span>
              <span style="color:var(--portal-muted);font-size:.78em;">${fmtDt(timestamp)} (${fmtTimeAgo(timestamp)})</span>
            </div>
            <div style="color:var(--portal-text);margin-bottom:2px;">
              By: <strong>${escHtml(actor)}</strong>
              ${cap ? `<span class="badge" style="margin-left:6px;font-size:.75em;">${escHtml(cap)}</span>` : ''}
            </div>
            ${detail ? `<div style="color:var(--portal-muted);font-size:.78em;font-family:var(--portal-mono);word-break:break-all;margin-top:4px;">${escHtml(detail)}</div>` : ''}
          </div>`;
      }).join('')}
    </div>`;
}

// ── Multi-Day Calendar View ───────────────────────────────────────────────────

export function renderCalendarTimeline(jobs, daysCount = 7) {
  const now = new Date();
  const startDay = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);

  const days = [];
  for (let i = 0; i < daysCount; i++) {
    const dayDate = new Date(startDay.getTime() + i * 24 * 60 * 60 * 1000);
    days.push({
      date: dayDate,
      label: dayDate.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' }),
      isToday: i === 0
    });
  }

  return `
    <div class="orch-calendar-grid" style="display:grid;grid-template-columns:repeat(${daysCount}, 1fr);gap:8px;padding:12px 16px;background:var(--portal-surface-subtle);border-radius:8px;border:1px solid var(--portal-border-soft);">
      ${days.map(d => `
        <div class="orch-calendar-day" style="background:var(--portal-surface);padding:8px;border-radius:6px;border:1px solid ${d.isToday ? 'var(--portal-accent)' : 'var(--portal-border-soft)'};min-height:160px;">
          <div style="font-weight:700;font-size:.78em;color:${d.isToday ? 'var(--portal-accent)' : 'var(--portal-text)'};margin-bottom:8px;border-bottom:1px solid var(--portal-border-soft);padding-bottom:4px;">
            ${escHtml(d.label)} ${d.isToday ? '<span class="badge badge-accent" style="font-size:.7em;margin-left:4px;">Today</span>' : ''}
          </div>
          <div class="orch-calendar-events" style="display:flex;flex-direction:column;gap:6px;">
            ${(jobs || []).filter(j => {
              const nextRun = j.NextRun ?? j.nextRun;
              if (!nextRun) return false;
              const dt = new Date(nextRun);
              return dt.getDate() === d.date.getDate() && dt.getMonth() === d.date.getMonth() && dt.getFullYear() === d.date.getFullYear();
            }).map(j => {
              const name = j.Name ?? j.name;
              const nextRun = new Date(j.NextRun ?? j.nextRun);
              const timeStr = nextRun.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
              const isEnabled = j.IsEnabled ?? j.isEnabled ?? true;

              return `
                <div class="orch-calendar-item" data-job="${escHtml(name)}" style="padding:4px 6px;background:${isEnabled ? 'var(--portal-surface-subtle)' : 'var(--portal-border-soft)'};border-left:3px solid ${isEnabled ? 'var(--portal-accent)' : 'var(--portal-muted)'};border-radius:3px;font-size:.75em;cursor:pointer;" title="${escHtml(name)} at ${timeStr}">
                  <div style="font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${escHtml(name)}</div>
                  <div style="color:var(--portal-muted);font-size:.72em;">${timeStr}</div>
                </div>`;
            }).join('') || '<span style="color:var(--portal-muted);font-size:.72em;">No runs scheduled</span>'}
          </div>
        </div>`).join('')}
    </div>`;
}
