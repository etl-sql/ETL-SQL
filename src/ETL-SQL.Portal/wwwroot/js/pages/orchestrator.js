/**
 * Page module for orchestrator.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { renderDag, createScriptEditor } from '../../designer/designer.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { renderTriageBoard, selectedJobNames } from '../triage-ui.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { accessPanelHtml, ownerLabel, unownedListHtml } from '../orchestrator-acl-ui.js';
import {
  escHtml, fmtDt, fmtTimeAgo,
  filterAndPaginateJobs,
  renderSchedulesTable,
  renderNotificationsTable,
  renderWatermarksTable,
  renderJobAuditTrail,
  renderCalendarTimeline
} from '../orchestrator-admin-ui.js';

renderPortalHeader();
installDialogAccessibility();

/**
 * Confirmation and error notices for this page.
 *
 * Thirty-four call sites on this page were already written against this name and it was never
 * defined anywhere — while the page's code lived inside an inline `<script type="module">` block
 * nothing parsed it, so every one of them threw a ReferenceError at runtime. Enabling a job,
 * killing a run, deleting a job, a schedule or a notification all completed on the server and then
 * said nothing; every `catch` re-threw on the way out and swallowed the original error with it.
 *
 * `ETLSQLFeedback` is the Portal's own toast surface (js/feedback.js, loaded as a classic script
 * before this module), so this is a thin adapter over it rather than a second implementation.
 *
 * @param {string} message
 * @param {boolean} [isError=false] Renders as an assertive error toast rather than an info one.
 */
function showToast(message, isError = false) {
    window.ETLSQLFeedback?.notify(message, isError ? { tone: 'error' } : {});
}

// ── Auth guard ─────────────────────────────────────────────────────────────────
if (!auth.isLoggedIn()) window.location.href = '/login.html';
applyPortalBranding();
initTheme();

let isAdmin = false;
let isManager = false;
try {
  const identity = getSessionIdentity(auth.getToken());
  renderSessionIdentity(identity, document.getElementById('topbarUser'));
  isAdmin = hasRole(identity, 'Admin');
  isManager = hasRole(identity, 'Admin', 'OrchestratorManager');
  const canOrch = hasRole(identity, 'Admin', 'OrchestratorManager', 'OrchestratorViewer');
  if (!canOrch) window.location.href = '/index.html';
  if (!isManager) {
    document.getElementById('newJobBtn')?.setAttribute('style', 'display:none');
    document.getElementById('newScheduleBtn')?.setAttribute('style', 'display:none');
    document.getElementById('newNotificationBtn')?.setAttribute('style', 'display:none');
  }
} catch { window.location.href = '/login.html'; }

applyNavigationSafely();

// ── API helpers ────────────────────────────────────────────────────────────────
const BASE = '/api/orchestrator';

async function apiFetch(url, opts = {}) {
  const token = auth.getToken();
  const headers = { ...(opts.headers || {}) };
  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (opts.body && typeof opts.body === 'object') {
    headers['Content-Type'] = 'application/json';
    opts = { ...opts, body: JSON.stringify(opts.body) };
  }
  return fetch(url, { ...opts, headers });
}

async function apiJson(url, opts = {}) {
  const res = await apiFetch(url, opts);
  if (res.status === 401) { auth.redirectToLogin(); return null; }
  if (!res.ok) throw new Error(await res.text());
  if (res.status === 204) return {};
  return res.json();
}

const api = {
  status:       () => apiJson(`${BASE}/status`),
  metrics:      () => apiJson(`${BASE}/metrics`),
  jobs:         () => apiJson(`${BASE}/jobs`),
  history:      (name, limit = 30) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/history?limit=${limit}`),
  resume:       (historyId) => apiFetch(`${BASE}/runs/${historyId}/resume`, { method: 'POST' }),
  create:       (body) => apiFetch(`${BASE}/jobs`, { method: 'POST', body }),
  update:       (name, body) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}`, { method: 'PUT', body }),
  delete:       (name) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  trigger:      (name, variables = {}) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}/trigger`, { method: 'POST', body: { variables } }),
  kill:         (name) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}/kill`, { method: 'POST' }),
  scripts:      () => apiJson(`${BASE}/scripts`),
  scriptContent:(path) => apiJson(`${BASE}/scripts/content?path=${encodeURIComponent(path)}`),
  bundles:      () => apiJson(`${BASE}/bundles`),
  bundleVersions:(name) => apiJson(`${BASE}/bundles/${encodeURIComponent(name)}/versions`),
  stop:         () => apiFetch(`${BASE}/service/stop`, { method: 'POST' }),
  dag:          (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/dag`),
  dependencies: (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/dependencies`),
  audit:        (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/audit`),
  triage:       (lookbackHours = 24) => apiJson(`${BASE}/triage?lookbackHours=${encodeURIComponent(lookbackHours)}`),
  triageRun:(runId) => apiJson(`${BASE}/triage/runs/${encodeURIComponent(runId)}`),
  rerun:        (jobNames) => apiFetch(`${BASE}/jobs/rerun`, { method: 'POST', body: { jobNames } }),

  // Schedules
  schedules:       (limit = 1000) => apiJson(`${BASE}/schedules?limit=${limit}`),
  scheduleCreate:  (body) => apiFetch(`${BASE}/schedules`, { method: 'POST', body }),
  scheduleUpdate:  (name, body) => apiFetch(`${BASE}/schedules/${encodeURIComponent(name)}`, { method: 'PUT', body }),
  scheduleDelete:  (name) => apiFetch(`${BASE}/schedules/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  jobSchedules:    (jobName) => apiJson(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules`),
  jobScheduleAttach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'POST' }),
  jobScheduleDetach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'DELETE' }),

  // Notifications
  notifications:      (limit = 1000) => apiJson(`${BASE}/notifications?limit=${limit}`),
  notificationCreate: (body) => apiFetch(`${BASE}/notifications`, { method: 'POST', body }),
  notificationUpdate: (name, body) => apiFetch(`${BASE}/notifications/${encodeURIComponent(name)}`, { method: 'PUT', body }),
  notificationDelete: (name) => apiFetch(`${BASE}/notifications/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  notificationDispatch:(name, body) => apiFetch(`${BASE}/notifications/${encodeURIComponent(name)}/dispatch`, { method: 'POST', body }),
  jobNotifications:   (jobName) => apiJson(`${BASE}/jobs/${encodeURIComponent(jobName)}/notifications`),
  jobNotificationAttach:(jobName, notificationName, trigger = 'Completion') => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/notifications/${encodeURIComponent(notificationName)}`, { method: 'POST', body: { trigger } }),
  jobNotificationDetach:(jobName, notificationName, trigger) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/notifications/${encodeURIComponent(notificationName)}${trigger ? `?trigger=${encodeURIComponent(trigger)}` : ''}`, { method: 'DELETE' }),

  // State & Watermarks
  jobStates:   (jobName) => apiJson(`${BASE}/jobs/${encodeURIComponent(jobName)}/state`),
  jobStateSet: (jobName, key, value) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/state/${encodeURIComponent(key)}`, { method: 'PUT', body: { value } }),
  jobStateDelete:(jobName, key) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/state/${encodeURIComponent(key)}`, { method: 'DELETE' }),

  // DQ & Stewardship
  dqStatus:   () => apiJson(`${BASE}/data-quality/status`),
  dqFailures: () => apiJson(`${BASE}/data-quality/failures`),
  stewardshipScore: () => apiJson(`${BASE}/stewardship/score`),
  stewardshipGaps:  () => apiJson(`${BASE}/stewardship/gaps`),

  // Grants & Ownership
  grants:      (kind, name) => apiFetch(`${BASE}/authorization/${encodeURIComponent(kind)}/${encodeURIComponent(name)}`),
  grantSet:    (kind, name, principalKind, principalId, permission) => apiFetch(
                 `${BASE}/authorization/${encodeURIComponent(kind)}/${encodeURIComponent(name)}/${encodeURIComponent(principalKind)}/${encodeURIComponent(principalId)}`,
                 { method: 'PUT', body: { permission } }),
  grantRevoke: (kind, name, principalKind, principalId) => apiFetch(
                 `${BASE}/authorization/${encodeURIComponent(kind)}/${encodeURIComponent(name)}/${encodeURIComponent(principalKind)}/${encodeURIComponent(principalId)}`,
                 { method: 'DELETE' }),
  setOwner:    (kind, name, principalKind, principalId) => apiFetch(
                 `${BASE}/authorization/${encodeURIComponent(kind)}/${encodeURIComponent(name)}/owner`,
                 { method: 'PUT', body: { principalKind, principalId } }),
  unowned:     () => apiFetch(`${BASE}/authorization/unowned`),
  adopt:       (principalKind, principalId, kind = null) => apiFetch(
                 `${BASE}/authorization/adopt`, { method: 'POST', body: { principalKind, principalId, kind } }),
};

// ── State ──────────────────────────────────────────────────────────────────────
let activeView = 'jobs'; // 'jobs' | 'schedules' | 'notifications'
let allJobs = [];
let allSchedules = [];
let allNotifications = [];
let selectedJob = null;
let ganttChart = null;
let sparklineChart = null;
let depGraphChart = null;
let timelineMode = 'gantt'; // 'gantt' | 'calendar'
let sparklineMetric = 'duration'; // 'duration' | 'rows'
let online = false;
let pollHandle = null;
let triageHandle = null;
let jobsFilter = 'all';
let jobsSearchTerm = '';
let jobsStatusFilter = 'all';
let jobsPage = 1;
let jobsPageSize = 25;
let metricJobNames = new Map();
let metricHistoryByJob = new Map();
let dagInstance  = null;
let dagJobName   = null;
let scriptEditor = null;
let scriptOriginalValue = '';
let lastSparklineEntries = [];
let unownedObjects = [];
let editingScheduleName = null;
let editingNotificationName = null;
let activeJobStates = [];

// ── View Switcher ──────────────────────────────────────────────────────────────
function setActiveView(view) {
  activeView = view;
  document.getElementById('orchNavJobs').classList.toggle('active', view === 'jobs');
  document.getElementById('orchNavSchedules').classList.toggle('active', view === 'schedules');
  document.getElementById('orchNavNotifications').classList.toggle('active', view === 'notifications');
  document.getElementById('orchNavJobs').setAttribute('aria-selected', view === 'jobs' ? 'true' : 'false');
  document.getElementById('orchNavSchedules').setAttribute('aria-selected', view === 'schedules' ? 'true' : 'false');
  document.getElementById('orchNavNotifications').setAttribute('aria-selected', view === 'notifications' ? 'true' : 'false');

  document.getElementById('viewJobsSection').style.display = view === 'jobs' ? '' : 'none';
  document.getElementById('viewSchedulesSection').style.display = view === 'schedules' ? '' : 'none';
  document.getElementById('viewNotificationsSection').style.display = view === 'notifications' ? '' : 'none';

  if (view === 'schedules') loadSchedules();
  if (view === 'notifications') loadNotifications();
}

document.getElementById('orchNavJobs').addEventListener('click', () => setActiveView('jobs'));
document.getElementById('orchNavSchedules').addEventListener('click', () => setActiveView('schedules'));
document.getElementById('orchNavNotifications').addEventListener('click', () => setActiveView('notifications'));

// ── Top Action Buttons ─────────────────────────────────────────────────────────
document.getElementById('logoutBtn')?.addEventListener('click', () => authApi.logout());
document.getElementById('refreshJobsBtn').addEventListener('click', () => loadJobs());
// The four metric chips are read-outs, not filters.
//
// They were wired to `showMetricJobs` and `clearJobsFilter`, neither of which is defined
// anywhere, so every click threw a ReferenceError and nothing happened. The feature cannot be
// finished here: two of the four counts come from the service's runtime metrics (how many jobs
// are running or queued *right now*) while this table lists job definitions, which carry no run
// state at all — `LastRun` and `NextRun` are the only temporal fields on them. Telling done-today
// from failed-today needs the per-job history endpoint, one call per job, which is why
// `failedToday` in loadJobs() has always been zero. Filtering the table by any of the four needs
// a server-side filter or a run-state field on the list; approximating it from what is here would
// put a plausible wrong answer in front of an operator.
//
// So they stop claiming to be buttons. A chip that reads its number and says why it does not
// filter is honest; one that throws on click is not.
for (const [id, why] of [
  ['activeFilterBtn', 'Jobs running now. Filtering the table by this needs run state the job list does not carry.'],
  ['queuedFilterBtn', 'Jobs queued now. Filtering the table by this needs run state the job list does not carry.'],
  ['completedTodayFilterBtn', 'Runs finished today. Filtering the table by this needs the per-job run history.'],
  ['failedTodayFilterBtn', 'Runs that failed today. Filtering the table by this needs the per-job run history.'],
]) {
  const chip = /** @type {HTMLButtonElement|null} */ (document.getElementById(id));
  if (!chip) continue;
  chip.disabled = true;
  chip.title = why;
}
document.getElementById('clearJobsFilterBtn')?.addEventListener('click', () => {
  jobsFilter = 'all';
  jobsPage = 1;
  renderCurrentJobsView();
});

document.getElementById('refreshSchedulesBtn')?.addEventListener('click', () => loadSchedules());
document.getElementById('refreshNotificationsBtn')?.addEventListener('click', () => loadNotifications());

// ── Search & Filter Listeners ──────────────────────────────────────────────────
const searchInput = document.getElementById('jobsSearchInput');
const searchClearBtn = document.getElementById('jobsSearchClearBtn');
const statusSelect = document.getElementById('jobsStatusSelect');
const pageSizeSelect = document.getElementById('jobsPageSizeSelect');

searchInput.addEventListener('input', () => {
  jobsSearchTerm = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (searchInput).value;
  searchClearBtn.style.display = jobsSearchTerm ? '' : 'none';
  jobsPage = 1;
  renderCurrentJobsView();
});

searchClearBtn.addEventListener('click', () => {
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (searchInput).value = '';
  jobsSearchTerm = '';
  searchClearBtn.style.display = 'none';
  jobsPage = 1;
  renderCurrentJobsView();
  searchInput.focus();
});

statusSelect.addEventListener('change', () => {
  jobsStatusFilter = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (statusSelect).value;
  jobsPage = 1;
  renderCurrentJobsView();
});

pageSizeSelect.addEventListener('change', () => {
  jobsPageSize = Number(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (pageSizeSelect).value) || 25;
  jobsPage = 1;
  renderCurrentJobsView();
});

document.getElementById('jobsPrevPageBtn').addEventListener('click', () => {
  if (jobsPage > 1) { jobsPage--; renderCurrentJobsView(); }
});

document.getElementById('jobsNextPageBtn').addEventListener('click', () => {
  jobsPage++;
  renderCurrentJobsView();
});

// ── Timeline Switcher ──────────────────────────────────────────────────────────
document.getElementById('timelineModeGanttBtn').addEventListener('click', () => {
  timelineMode = 'gantt';
  document.getElementById('timelineModeGanttBtn').classList.add('active');
  document.getElementById('timelineModeCalendarBtn').classList.remove('active');
  document.getElementById('timelineGanttWrap').style.display = '';
  document.getElementById('timelineCalendarWrap').style.display = 'none';
  document.getElementById('timelineKicker').textContent = "Today's schedule";
  document.getElementById('timelineTitle').textContent = "Job Timeline (24h)";
  if (ganttChart && allJobs.length) renderGantt(allJobs);
});

document.getElementById('timelineModeCalendarBtn').addEventListener('click', () => {
  timelineMode = 'calendar';
  document.getElementById('timelineModeCalendarBtn').classList.add('active');
  document.getElementById('timelineModeGanttBtn').classList.remove('active');
  document.getElementById('timelineGanttWrap').style.display = 'none';
  document.getElementById('timelineCalendarWrap').style.display = '';
  document.getElementById('timelineKicker').textContent = "Multi-Day Schedule";
  document.getElementById('timelineTitle').textContent = "7-Day Run Calendar";
  renderCalendarTimelineView();
});

function renderCalendarTimelineView() {
  const host = document.getElementById('timelineCalendarWrap');
  host.innerHTML = renderCalendarTimeline(allJobs, 7);
  host.querySelectorAll('.orch-calendar-item').forEach(item => {
    item.addEventListener('click', () => {
      const jobName = /** @type {HTMLElement} */ (item).dataset.job;
      const job = allJobs.find(j => jobValue(j, 'Name') === jobName);
      if (job) openDetail(job);
    });
  });
}

// ── Polling & Status ───────────────────────────────────────────────────────────
async function poll() {
  try {
    const [statusData, metricsData] = await Promise.all([api.status(), api.metrics()]);
    online = statusData?.online === true;
    updateOnlineState(online);
    if (online && metricsData?.metrics) updateMetrics(metricsData.metrics);
  } catch { online = false; updateOnlineState(false); }
}

function updateOnlineState(isOnline) {
  const dot   = document.getElementById('statusDot');
  const label = document.getElementById('statusLabel');
  const banner = document.getElementById('offlineBanner');
  const stopBtn = document.getElementById('stopBtn');
  const restartBtn = document.getElementById('restartBtn');
  dot.className   = `orch-status-dot ${isOnline ? 'online' : 'offline'}`;
  label.textContent = isOnline ? 'Online' : 'Offline';
  banner.style.display = isOnline ? 'none' : '';
  if (stopBtn) stopBtn.style.display = isOnline ? '' : 'none';
  if (restartBtn) restartBtn.style.display = isOnline ? '' : 'none';
}

function updateMetrics(m) {
  document.getElementById('activeCount').textContent = m.activeJobs ?? m.active_jobs ?? 0;
  document.getElementById('queuedCount').textContent = m.queuedJobs ?? m.queued_jobs ?? 0;
}

// ── Load Jobs & Render ─────────────────────────────────────────────────────────
function jobValue(job, prop) {
  if (!job) return undefined;
  return job[prop] ?? job[prop.charAt(0).toLowerCase() + prop.slice(1)];
}
function jobBool(job, prop) {
  const val = jobValue(job, prop);
  return val === true || val === 'true' || val === 1;
}

function fmtSchedule(job) {
  const interval = jobValue(job, 'Interval');
  const unit     = jobValue(job, 'Unit');
  const atTime   = jobValue(job, 'AtTime');
  if (!interval) return 'Manual';
  const u = (unit || 'HOUR').toLowerCase();
  const plural = interval === 1 ? u.replace(/s$/, '') : (u.endsWith('s') ? u : u + 's');
  let s = `Every ${interval} ${plural}`;
  if (atTime) s += ` at ${atTime}`;
  return s;
}

function statusBadge(isEnabled) {
  return isEnabled
    ? `<span class="badge badge-success">Active</span>`
    : `<span class="badge badge-neutral">Disabled</span>`;
}

function lastRunBadge(job) {
  return jobValue(job, 'LastRun') ? fmtDt(jobValue(job, 'LastRun')) : '<span style="color:var(--portal-muted)">Never</span>';
}

async function loadJobs() {
  try {
    const jobs = await api.jobs();
    allJobs = jobs || [];
    document.getElementById('jobsNavCount').textContent = allJobs.length;

    // Load today's history summary for stat chips
    const today = new Date();
    today.setHours(0,0,0,0);
    let doneToday = 0;
    let failedToday = 0;
    allJobs.forEach(j => {
      const lr = jobValue(j, 'LastRun');
      if (lr && new Date(lr) >= today) {
        doneToday++;
      }
    });
    document.getElementById('completedCount').textContent = String(doneToday);
    document.getElementById('failedCount').textContent = String(failedToday);

    renderCurrentJobsView();

    if (timelineMode === 'gantt') {
      renderGantt(allJobs);
    } else {
      renderCalendarTimelineView();
    }
  } catch (err) {
    document.getElementById('jobsTableWrap').innerHTML =
      `<div class="empty-state" style="color:var(--portal-danger)">Failed to load jobs: ${err.message}</div>`;
  }
}

function renderCurrentJobsView() {
  const wrap = document.getElementById('jobsTableWrap');
  const pagBar = document.getElementById('jobsPaginationBar');
  const info = document.getElementById('jobsPaginationInfo');
  const pageIndicator = document.getElementById('jobsPageIndicator');
  const prevBtn = document.getElementById('jobsPrevPageBtn');
  const nextBtn = document.getElementById('jobsNextPageBtn');

  // Filter and paginate
  const result = filterAndPaginateJobs(allJobs, {
    search: jobsSearchTerm,
    status: jobsStatusFilter,
    page: jobsPage,
    pageSize: jobsPageSize
  });

  renderJobsFilterHeader(result.total);

  if (result.total === 0) {
    wrap.innerHTML = `<div class="empty-state" style="padding:32px 0;text-align:center;color:var(--portal-muted)">
      No jobs match the search or filter criteria. Click <strong>+ New Job</strong> to create one.
    </div>`;
    pagBar.style.display = 'none';
    return;
  }

  pagBar.style.display = 'flex';
  info.textContent = `Showing ${result.startIdx}–${result.endIdx} of ${result.total} jobs`;
  pageIndicator.textContent = `${result.page} / ${result.totalPages}`;
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (prevBtn).disabled = result.page <= 1;
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (nextBtn).disabled = result.page >= result.totalPages;

  wrap.innerHTML = `
    <div style="overflow-x:auto;">
    <table class="data-table">
      <thead><tr>
        <th>Job Name</th>
        <th>Target / Sandbox</th>
        <th>Owner</th>
        <th>Schedule</th>
        <th>Status</th>
        <th>Last Run</th>
        <th>Next Run</th>
        <th>Actions</th>
      </tr></thead>
      <tbody>
      ${result.items.map(j => {
        const name = jobValue(j, 'Name');
        const disp = jobValue(j, 'DisplayName') || name;
        const targetKind = jobValue(j, 'JobType') || 'Script';
        const targetPath = jobValue(j, 'TargetPath') || '';
        const isBundle = targetKind === 'Bundle' || (targetPath && targetPath.startsWith('bundle://'));
        const optionsRaw = jobValue(j, 'Options');
        let sandbox = 'Default';
        if (optionsRaw) {
          try {
            const parsed = typeof optionsRaw === 'string' ? JSON.parse(optionsRaw) : optionsRaw;
            if (parsed.SandboxProfile) sandbox = parsed.SandboxProfile;
          } catch {}
        }

        return `
        <tr class="job-row${jobValue(selectedJob, 'Name') === name ? ' selected' : ''}" data-name="${escHtml(name)}">
          <td>
            <strong>${escHtml(disp)}</strong>
            ${disp !== name ? `<div style="font-size:.78em;color:var(--portal-muted);">${escHtml(name)}</div>` : ''}
          </td>
          <td>
            <div style="display:flex;align-items:center;gap:4px;flex-wrap:wrap;">
              <span class="badge" style="font-size:.75em;background:${isBundle ? 'var(--portal-accent-soft)' : 'var(--portal-surface-subtle)'};">
                ${isBundle ? '📦 Bundle' : '📜 Script'}
              </span>
              ${sandbox !== 'Default' ? `<span class="badge badge-accent" style="font-size:.72em;">${escHtml(sandbox)}</span>` : ''}
            </div>
            ${targetPath ? `<div style="font-size:.72em;color:var(--portal-muted);max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="${escHtml(targetPath)}">${escHtml(targetPath)}</div>` : ''}
          </td>
          <td class="orch-owner-cell" title="${escHtml(ownerLabel(jobValue(j, 'CreatedBy')))}">${escHtml(ownerLabel(jobValue(j, 'CreatedBy')))}</td>
          <td>${escHtml(fmtSchedule(j))}</td>
          <td>${statusBadge(jobBool(j, 'IsEnabled'))}</td>
          <td>${lastRunBadge(j)}</td>
          <td>${jobValue(j, 'NextRun') ? fmtDt(jobValue(j, 'NextRun')) : '—'}</td>
          <td>
            <div class="table-actions">
              <button class="btn btn-sm btn-outline" data-action="trigger" data-name="${escHtml(name)}" title="Run now">▶ Run</button>
              <button class="btn btn-sm btn-outline" data-action="toggle" data-name="${escHtml(name)}" data-enabled="${jobBool(j, 'IsEnabled')}">
                ${jobBool(j, 'IsEnabled') ? 'Disable' : 'Enable'}
              </button>
              <button class="btn btn-sm btn-outline" data-action="kill" data-name="${escHtml(name)}" title="Cancel running instance">Kill</button>
              <button class="btn btn-sm btn-outline btn-danger-outline" data-action="delete" data-name="${escHtml(name)}">Delete</button>
            </div>
          </td>
        </tr>`;
      }).join('')}
      </tbody>
    </table>
    </div>`;

  wrap.querySelectorAll('tr.job-row').forEach(row => {
    row.addEventListener('click', e => {
      if (/** @type {Element} */ (e.target).closest('button')) return;
      const name = /** @type {HTMLElement} */ (row).dataset.name;
      const job  = allJobs.find(j => jobValue(j, 'Name') === name);
      if (job) openDetail(job);
    });
  });

  wrap.querySelectorAll('[data-action]').forEach(btn => {
    btn.addEventListener('click', e => { e.stopPropagation(); handleAction(btn); });
  });
}

function renderJobsFilterHeader(visibleCount) {
  const actions = document.getElementById('jobsFilterActions');
  const label = document.getElementById('jobsFilterLabel');
  const kicker = document.getElementById('jobsSectionKicker');
  const title = document.getElementById('jobsSectionTitle');

  if (jobsFilter !== 'all') {
    const labels = {
      active: 'Active',
      queued: 'Queued',
      completedToday: 'Done today',
      failedToday: 'Failed today'
    };
    kicker.textContent = labels[jobsFilter] ?? 'Filtered jobs';
    title.textContent = `${visibleCount} Scheduled ${visibleCount === 1 ? 'Job' : 'Jobs'}`;
    label.textContent = `${labels[jobsFilter] ?? 'Metric'} filter`;
    actions.style.display = '';
  } else {
    kicker.textContent = 'All jobs';
    title.textContent = 'Scheduled Jobs';
    label.textContent = '';
    actions.style.display = 'none';
  }
}

async function handleAction(btn) {
  const name   = btn.dataset.name;
  const action = btn.dataset.action;
  btn.disabled = true;
  try {
    if (action === 'trigger') {
      openRunModal(name);
    } else if (action === 'toggle') {
      const enable = btn.dataset.enabled === 'true' ? false : true;
      const job = allJobs.find(j => jobValue(j, 'Name') === name);
      await api.update(name, { IsEnabled: enable });
      showToast(`Job '${name}' ${enable ? 'enabled' : 'disabled'}.`);
      await loadJobs();
      if (selectedJob && jobValue(selectedJob, 'Name') === name) openDetail(job);
    } else if (action === 'kill') {
      await api.kill(name);
      showToast(`Kill signal sent to '${name}'.`);
    } else if (action === 'delete') {
      if (!await ETLSQLFeedback.confirm(`Delete job '${name}'?`, { title: 'Delete job and history', impact: 'All execution history for this job will be removed. This cannot be undone.', confirmLabel: 'Delete job', danger: true, auditAction: 'orchestrator.job.delete' })) return;
      await api.delete(name);
      showToast(`Job '${name}' deleted.`);
      if (selectedJob && jobValue(selectedJob, 'Name') === name) closeDetail();
      await loadJobs();
    }
  } catch (err) {
    showToast(`Error: ${err.message}`, true);
  } finally {
    btn.disabled = false;
  }
}

// ── Schedules Catalog View ────────────────────────────────────────────────────
async function loadSchedules() {
  const wrap = document.getElementById('schedulesTableWrap');
  try {
    const schedules = await api.schedules();
    allSchedules = schedules || [];
    document.getElementById('schedulesNavCount').textContent = allSchedules.length;

    // Count linked jobs
    const linkedMap = {};
    wrap.innerHTML = renderSchedulesTable(allSchedules, linkedMap);

    wrap.querySelectorAll('[data-action="edit-schedule"]').forEach(btn => {
      btn.addEventListener('click', () => openEditScheduleModal(/** @type {HTMLElement} */ (btn).dataset.name));
    });
    wrap.querySelectorAll('[data-action="toggle-schedule"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const name = /** @type {HTMLElement} */ (btn).dataset.name;
        const enable = /** @type {HTMLElement} */ (btn).dataset.enabled !== 'true';
        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
        try {
          await api.scheduleUpdate(name, { IsEnabled: enable });
          showToast(`Schedule '${name}' ${enable ? 'enabled' : 'disabled'}.`);
          await loadSchedules();
        } catch (err) { showToast(`Error: ${err.message}`, true); }
        finally { /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false; }
      });
    });
    wrap.querySelectorAll('[data-action="delete-schedule"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const name = /** @type {HTMLElement} */ (btn).dataset.name;
        if (!await ETLSQLFeedback.confirm(`Delete schedule '${name}'?`, { title: 'Delete schedule', impact: 'This schedule will be permanently deleted.', confirmLabel: 'Delete schedule', danger: true })) return;
        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
        try {
          const res = await api.scheduleDelete(name);
          if (!res.ok) {
            const body = await res.json().catch(() => null);
            throw new Error(body?.error || 'Failed to delete schedule.');
          }
          showToast(`Schedule '${name}' deleted.`);
          await loadSchedules();
        } catch (err) { showToast(`Error: ${err.message}`, true); }
        finally { /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false; }
      });
    });
  } catch (err) {
    wrap.innerHTML = `<div class="empty-state" style="color:var(--portal-danger)">Failed to load schedules: ${err.message}</div>`;
  }
}

// ── Notifications Catalog View ────────────────────────────────────────────────
async function loadNotifications() {
  const wrap = document.getElementById('notificationsTableWrap');
  try {
    const notifications = await api.notifications();
    allNotifications = notifications || [];
    document.getElementById('notificationsNavCount').textContent = allNotifications.length;

    wrap.innerHTML = renderNotificationsTable(allNotifications, {});

    wrap.querySelectorAll('[data-action="dispatch-notification"]').forEach(btn => {
      btn.addEventListener('click', () => openDispatchModal(/** @type {HTMLElement} */ (btn).dataset.name));
    });
    wrap.querySelectorAll('[data-action="edit-notification"]').forEach(btn => {
      btn.addEventListener('click', () => openEditNotificationModal(/** @type {HTMLElement} */ (btn).dataset.name));
    });
    wrap.querySelectorAll('[data-action="delete-notification"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const name = /** @type {HTMLElement} */ (btn).dataset.name;
        if (!await ETLSQLFeedback.confirm(`Delete notification endpoint '${name}'?`, { title: 'Delete notification', impact: 'This notification config will be permanently deleted.', confirmLabel: 'Delete notification', danger: true })) return;
        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
        try {
          const res = await api.notificationDelete(name);
          if (!res.ok) {
            const body = await res.json().catch(() => null);
            throw new Error(body?.error || 'Failed to delete notification.');
          }
          showToast(`Notification '${name}' deleted.`);
          await loadNotifications();
        } catch (err) { showToast(`Error: ${err.message}`, true); }
        finally { /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false; }
      });
    });
  } catch (err) {
    wrap.innerHTML = `<div class="empty-state" style="color:var(--portal-danger)">Failed to load notifications: ${err.message}</div>`;
  }
}

// ── Gantt Chart ───────────────────────────────────────────────────────────────
function renderGantt(jobs) {
  if (!ganttChart) {
    const el = document.getElementById('ganttChart');
    ganttChart = /** @type {{on: Function, setOption: Function, resize: Function, dispose: Function}} */ (
      nativeCharts.init(el));
    ganttChart.on('click', params => {
      if (params.componentType === 'series') {
        const name = params.data?.[3];
        const job  = allJobs.find(j => jobValue(j, 'Name') === name);
        if (job) openDetail(job);
      }
    });
  }

  const today = new Date();
  const startOfDay = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 0, 0, 0).getTime();
  const endOfDay   = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 23, 59, 59).getTime();
  const MIN_BAR_MS = 5 * 60 * 1000;

  const jobNames = jobs.map(j => jobValue(j, 'Name'));
  const barData  = [];

  const style = getComputedStyle(document.body);
  const accentColor = style.getPropertyValue('--portal-accent').trim() || '#2563eb';
  const mutedColor = style.getPropertyValue('--portal-muted').trim() || '#7a8798';
  const textColor = style.getPropertyValue('--portal-text').trim() || '#172033';
  const borderSoftColor = style.getPropertyValue('--portal-border-soft').trim() || '#e8edf4';
  const borderColor = style.getPropertyValue('--portal-border').trim() || '#d9e0ea';
  const fontFam = style.getPropertyValue('--portal-font').trim() || '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

  jobs.forEach((job, idx) => {
    const start = jobValue(job, 'NextRun') ? new Date(jobValue(job, 'NextRun')).getTime() : null;
    if (!start || start < startOfDay || start > endOfDay) return;

    const end   = start + MIN_BAR_MS;
    const color = jobBool(job, 'IsEnabled') ? accentColor : mutedColor;
    barData.push([idx, start, end, jobValue(job, 'Name'), color]);
  });

  const option = {
    textStyle: { fontFamily: fontFam, color: textColor },
    tooltip: {
      formatter: params => {
        const [, s, , name] = params.data;
        const job = allJobs.find(j => jobValue(j, 'Name') === name);
        if (!job) return name;
        return `<strong>${escHtml(name)}</strong><br/>${escHtml(fmtSchedule(job))}<br/>Next: ${fmtDt(jobValue(job, 'NextRun'))}`;
      }
    },
    grid: { left: 160, right: 20, top: 10, bottom: 30 },
    xAxis: {
      type: 'time',
      min: startOfDay,
      max: endOfDay,
      axisLabel: { color: textColor, formatter: v => new Date(v).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) },
      axisLine: { lineStyle: { color: borderColor } },
      splitLine: { lineStyle: { color: borderSoftColor } }
    },
    yAxis: {
      type: 'category',
      data: jobNames,
      axisLabel: { fontSize: 12, color: textColor },
      axisLine: { lineStyle: { color: borderColor } },
      splitLine: { lineStyle: { color: borderSoftColor } }
    },
    series: [{
      type: 'custom',
      renderItem(params, api) {
        const idx   = api.value(0);
        const start = api.coord([api.value(1), idx]);
        const end   = api.coord([api.value(2), idx]);
        const h     = api.size([0, 1])[1] * 0.55;
        return {
          type: 'rect',
          shape: { x: start[0], y: start[1] - h / 2, width: Math.max(end[0] - start[0], 4), height: h },
          style: { fill: api.value(4), opacity: 0.85 }
        };
      },
      data: barData
    }]
  };

  ganttChart.setOption(option, true);
}

// ── Detail Panel Sub-Tabs & Open / Close ──────────────────────────────────────
const detailTabs = ['details', 'flow', 'deps', 'quality', 'audit'];
let activeDetailTab = 'details';

function setDetailTab(tab) {
  activeDetailTab = tab;
  document.getElementById('detailTabDetails').classList.toggle('active', tab === 'details');
  document.getElementById('detailTabFlow').classList.toggle('active', tab === 'flow');
  document.getElementById('detailTabDeps').classList.toggle('active', tab === 'deps');
  document.getElementById('detailTabQuality').classList.toggle('active', tab === 'quality');
  document.getElementById('detailTabAudit').classList.toggle('active', tab === 'audit');

  document.getElementById('detailPanelDetails').style.display = tab === 'details' ? '' : 'none';
  document.getElementById('detailPanelFlow').style.display = tab === 'flow' ? '' : 'none';
  document.getElementById('detailPanelDeps').style.display = tab === 'deps' ? '' : 'none';
  document.getElementById('detailPanelQuality').style.display = tab === 'quality' ? '' : 'none';
  document.getElementById('detailPanelAudit').style.display = tab === 'audit' ? '' : 'none';

  if (!selectedJob) return;
  const name = jobValue(selectedJob, 'Name');

  if (tab === 'flow') {
    if (dagJobName !== name) {
      dagJobName = name;
      api.dag(name).then(dagData => {
        const container = document.getElementById('jobDagContainer');
        dagInstance = renderDag(container, dagData, { height: 360 });
      }).catch(err => {
        document.getElementById('jobDagContainer').innerHTML =
          `<div class="empty-state" style="color:var(--portal-danger)">Could not load DAG: ${err.message}</div>`;
      });
    }
  } else if (tab === 'deps') {
    loadDependencyChain(name);
  } else if (tab === 'quality') {
    loadDataQualitySummary(name);
  } else if (tab === 'audit') {
    loadAuditTrail(name);
  }
}

document.getElementById('detailTabDetails').addEventListener('click', () => setDetailTab('details'));
document.getElementById('detailTabFlow').addEventListener('click', () => setDetailTab('flow'));
document.getElementById('detailTabDeps').addEventListener('click', () => setDetailTab('deps'));
document.getElementById('detailTabQuality').addEventListener('click', () => setDetailTab('quality'));
document.getElementById('detailTabAudit').addEventListener('click', () => setDetailTab('audit'));

document.getElementById('closeDetailBtn').addEventListener('click', closeDetail);

function closeDetail() {
  selectedJob = null;
  document.getElementById('detailPanel').classList.remove('open');
  document.querySelectorAll('tr.job-row.selected').forEach(r => r.classList.remove('selected'));
}

async function openDetail(job) {
  selectedJob = job;
  const name = jobValue(job, 'Name');
  const disp = jobValue(job, 'DisplayName') || name;
  const desc = jobValue(job, 'Description');
  const targetKind = jobValue(job, 'JobType') || 'Script';
  const targetPath = jobValue(job, 'TargetPath') || '';
  const isBundle = targetKind === 'Bundle' || (targetPath && targetPath.startsWith('bundle://'));
  const version = jobValue(job, 'Version') || 1;
  const owner = ownerLabel(jobValue(job, 'CreatedBy'));

  let sandbox = 'Default';
  const optionsRaw = jobValue(job, 'Options');
  if (optionsRaw) {
    try {
      const parsed = typeof optionsRaw === 'string' ? JSON.parse(optionsRaw) : optionsRaw;
      if (parsed.SandboxProfile) sandbox = parsed.SandboxProfile;
    } catch {}
  }

  document.getElementById('detailJobName').textContent = name;
  document.getElementById('detailJobDisplayName').textContent = disp !== name ? disp : '';
  document.getElementById('metaTargetKind').textContent = isBundle ? 'Bundle' : 'Script';
  document.getElementById('metaSandboxProfile').textContent = sandbox;
  document.getElementById('metaVersion').textContent = `v${version}`;
  document.getElementById('metaOwner').textContent = owner;

  const descSec = document.getElementById('detailDescSection');
  if (desc) {
    descSec.style.display = '';
    document.getElementById('detailDescription').textContent = desc;
  } else {
    descSec.style.display = 'none';
  }

  document.getElementById('detailSchedule').textContent = fmtSchedule(job);
  document.getElementById('detailLastRun').textContent  = jobValue(job, 'LastRun') ? fmtDt(jobValue(job, 'LastRun')) : 'Never';
  document.getElementById('detailNextRun').textContent  = jobValue(job, 'NextRun') ? fmtDt(jobValue(job, 'NextRun')) : '—';

  // Highlight row
  document.querySelectorAll('tr.job-row').forEach(r => {
    r.classList.toggle('selected', /** @type {HTMLElement} */ (r).dataset.name === name);
  });

  document.getElementById('detailPanel').classList.add('open');

  // Load sub-components
  loadJobLinkedSchedules(name);
  loadJobLinkedNotifications(name);
  loadJobWatermarks(name);
  loadScriptEditor(job);
  loadAccessPanel(job);
  loadHistory(name);

  // If on non-details tab, refresh it
  if (activeDetailTab !== 'details') {
    setDetailTab(activeDetailTab);
  }
}

// ── Linked Schedules in Details ───────────────────────────────────────────────
async function loadJobLinkedSchedules(jobName) {
  const host = document.getElementById('detailLinkedSchedulesWrap');
  host.innerHTML = '<span class="spinner" style="width:14px;height:14px;"></span> <small>Loading schedules…</small>';
  try {
    const links = await api.jobSchedules(jobName);
    if (!links || !links.length) {
      host.innerHTML = '<div style="font-size:.8em;color:var(--portal-muted);">No shared schedules attached.</div>';
      return;
    }
    host.innerHTML = `
      <div style="display:flex;flex-direction:column;gap:4px;">
        ${links.map(l => {
          const schedName = l.ScheduleName ?? l.scheduleName;
          const next = l.NextRun ?? l.nextRun;
          return `
            <div style="display:flex;justify-content:space-between;align-items:center;padding:4px 8px;background:var(--portal-surface-subtle);border-radius:4px;border:1px solid var(--portal-border-soft);font-size:.8em;">
              <div>
                <strong>🗓 ${escHtml(schedName)}</strong>
                ${next ? `<span style="color:var(--portal-muted);margin-left:6px;">Next: ${fmtDt(next)}</span>` : ''}
              </div>
              <button class="btn btn-sm btn-outline btn-danger-outline" data-detach-schedule="${escHtml(schedName)}" style="padding:2px 6px;font-size:.75em;">Detach</button>
            </div>`;
        }).join('')}
      </div>`;

    host.querySelectorAll('[data-detach-schedule]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const schedName = /** @type {HTMLElement} */ (btn).dataset.detachSchedule;
        if (!confirm(`Detach schedule '${schedName}' from job '${jobName}'?`)) return;
        try {
          await api.jobScheduleDetach(jobName, schedName);
          showToast(`Schedule detached.`);
          loadJobLinkedSchedules(jobName);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    });
  } catch (err) {
    host.innerHTML = `<span style="font-size:.8em;color:var(--portal-danger);">Failed to load schedules: ${err.message}</span>`;
  }
}

// ── Linked Notifications in Details ───────────────────────────────────────────
async function loadJobLinkedNotifications(jobName) {
  const host = document.getElementById('detailLinkedNotificationsWrap');
  host.innerHTML = '<span class="spinner" style="width:14px;height:14px;"></span> <small>Loading notifications…</small>';
  try {
    const links = await api.jobNotifications(jobName);
    if (!links || !links.length) {
      host.innerHTML = '<div style="font-size:.8em;color:var(--portal-muted);">No notification endpoints attached.</div>';
      return;
    }
    host.innerHTML = `
      <div style="display:flex;flex-direction:column;gap:4px;">
        ${links.map(l => {
          const notifName = l.NotificationName ?? l.notificationName;
          const trigger = l.Trigger ?? l.trigger ?? 'Completion';
          return `
            <div style="display:flex;justify-content:space-between;align-items:center;padding:4px 8px;background:var(--portal-surface-subtle);border-radius:4px;border:1px solid var(--portal-border-soft);font-size:.8em;">
              <div>
                <strong>🔔 ${escHtml(notifName)}</strong>
                <span class="badge" style="margin-left:6px;font-size:.72em;">${escHtml(trigger)}</span>
              </div>
              <button class="btn btn-sm btn-outline btn-danger-outline" data-detach-notif="${escHtml(notifName)}" data-trigger="${escHtml(trigger)}" style="padding:2px 6px;font-size:.75em;">Detach</button>
            </div>`;
        }).join('')}
      </div>`;

    host.querySelectorAll('[data-detach-notif]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const notifName = /** @type {HTMLElement} */ (btn).dataset.detachNotif;
        const trigger = /** @type {HTMLElement} */ (btn).dataset.trigger;
        if (!confirm(`Detach notification '${notifName}' from job '${jobName}'?`)) return;
        try {
          await api.jobNotificationDetach(jobName, notifName, trigger);
          showToast(`Notification detached.`);
          loadJobLinkedNotifications(jobName);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    });
  } catch (err) {
    host.innerHTML = `<span style="font-size:.8em;color:var(--portal-danger);">Failed to load notifications: ${err.message}</span>`;
  }
}

// ── Watermarks in Details ─────────────────────────────────────────────────────
async function loadJobWatermarks(jobName) {
  const host = document.getElementById('watermarksWrap');
  try {
    activeJobStates = await api.jobStates(jobName) || [];
    host.innerHTML = renderWatermarksTable(activeJobStates, jobName);

    document.getElementById('addStateBtn')?.addEventListener('click', () => {
      openEditStateModal(jobName, '', '');
    });

    host.querySelectorAll('[data-state-action="edit"]').forEach(btn => {
      btn.addEventListener('click', () => {
        openEditStateModal(/** @type {HTMLElement} */ (btn).dataset.job, /** @type {HTMLElement} */ (btn).dataset.key, /** @type {HTMLElement} */ (btn).dataset.val);
      });
    });

    host.querySelectorAll('[data-state-action="reset"]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const key = /** @type {HTMLElement} */ (btn).dataset.key;
        if (!confirm(`Reset and clear watermark key '${key}'? This will trigger a full backfill on next run.`)) return;
        try {
          await api.jobStateDelete(jobName, key);
          showToast(`Watermark '${key}' cleared.`);
          loadJobWatermarks(jobName);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    });
  } catch (err) {
    host.innerHTML = `<div style="font-size:.8em;color:var(--portal-danger);">Failed to load watermarks: ${err.message}</div>`;
  }
}

// ── Sparkline Trend ───────────────────────────────────────────────────────────
document.getElementById('sparklineMetricDurationBtn').addEventListener('click', () => {
  sparklineMetric = 'duration';
  document.getElementById('sparklineMetricDurationBtn').classList.add('active');
  document.getElementById('sparklineMetricRowsBtn').classList.remove('active');
  document.getElementById('sparklineTitle').textContent = 'Duration trend (last 30 runs, seconds)';
  renderSparkline(lastSparklineEntries);
});

document.getElementById('sparklineMetricRowsBtn').addEventListener('click', () => {
  sparklineMetric = 'rows';
  document.getElementById('sparklineMetricRowsBtn').classList.add('active');
  document.getElementById('sparklineMetricDurationBtn').classList.remove('active');
  document.getElementById('sparklineTitle').textContent = 'Rows processed trend (last 30 runs)';
  renderSparkline(lastSparklineEntries);
});

function renderSparkline(entries) {
  lastSparklineEntries = entries || [];
  const el = document.getElementById('sparklineChart');
  if (!sparklineChart) sparklineChart = nativeCharts.init(el);

  const style = getComputedStyle(document.body);
  const accentColor = style.getPropertyValue('--portal-accent').trim() || '#2563eb';
  const borderSoftColor = style.getPropertyValue('--portal-border-soft').trim() || '#e8edf4';

  const data = (lastSparklineEntries.slice().reverse()).map(h => {
    if (sparklineMetric === 'rows') {
      return h.RowsProcessed ?? h.rowsProcessed ?? 0;
    }
    const st = new Date(h.StartTime ?? h.startTime);
    const et = h.EndTime || h.endTime ? new Date(h.EndTime || h.endTime) : null;
    return et ? Math.round((et.getTime() - st.getTime()) / 1000) : 0;
  });

  sparklineChart.setOption({
    grid: { left: 8, right: 8, top: 8, bottom: 8 },
    xAxis: { type: 'category', show: false, data: data.map((_, i) => i) },
    yAxis: { type: 'value', show: false },
    tooltip: {
      trigger: 'axis',
      formatter: params => {
        const val = params[0]?.value ?? 0;
        return sparklineMetric === 'rows'
          ? `<strong>${Number(val).toLocaleString()}</strong> rows processed`
          : `<strong>${val}s</strong> duration`;
      }
    },
    series: [{
      type: 'line',
      data,
      smooth: true,
      symbol: 'none',
      lineStyle: { color: accentColor, width: 2 },
      areaStyle: { color: accentColor, opacity: 0.15 }
    }]
  }, true);
}

// ── Cross-Job Dependency View ─────────────────────────────────────────────────
async function loadDependencyChain(jobName) {
  const container = document.getElementById('dependencyGraphContainer');
  const detailsWrap = document.getElementById('dependencyDetailsWrap');
  container.innerHTML = '<span class="spinner"></span>';
  try {
    const chain = await api.dependencies(jobName);
    if (!depGraphChart) depGraphChart = nativeCharts.init(container);

    const nodes = (chain.Nodes || chain.nodes || []).map(n => {
      const isCur = n.IsCurrent ?? n.isCurrent;
      return {
        name: n.Name ?? n.name,
        value: n.DisplayName ?? n.displayName ?? n.Name ?? n.name,
        symbolSize: isCur ? 44 : 32,
        itemStyle: {
          color: isCur ? '#2563eb' : (n.IsEnabled ? '#10b981' : '#94a3b8')
        },
        label: { show: true, position: 'bottom', fontSize: 11 }
      };
    });

    const edges = (chain.Edges || chain.edges || []).map(e => ({
      source: e.From ?? e.from,
      target: e.To ?? e.to,
      label: { show: !!(e.Detail || e.detail), formatter: e.Detail || e.detail, fontSize: 9 }
    }));

    depGraphChart.setOption({
      tooltip: {
        formatter: params => `<strong>${escHtml(params.data.name || params.data.source + ' → ' + params.data.target)}</strong>`
      },
      series: [{
        type: 'graph',
        layout: 'force',
        data: nodes,
        links: edges,
        roam: true,
        edgeSymbol: ['none', 'arrow'],
        edgeSymbolSize: 6,
        force: { repulsion: 240, edgeLength: 90 },
        lineStyle: { color: '#94a3b8', width: 2, curveness: 0.1 }
      }]
    }, true);

    const upstreams = chain.Upstream || chain.upstream || [];
    const downstreams = chain.Downstream || chain.downstream || [];

    detailsWrap.innerHTML = `
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;font-size:.8em;">
        <div style="background:var(--portal-surface-subtle);padding:8px 10px;border-radius:6px;border:1px solid var(--portal-border-soft);">
          <div style="font-weight:700;color:var(--portal-muted);margin-bottom:4px;">▲ Upstream Providers (${upstreams.length})</div>
          ${upstreams.length ? upstreams.map(u => `<div>• <strong>${escHtml(u)}</strong></div>`).join('') : '<span style="color:var(--portal-muted)">No direct upstream job dependencies</span>'}
        </div>
        <div style="background:var(--portal-surface-subtle);padding:8px 10px;border-radius:6px;border:1px solid var(--portal-border-soft);">
          <div style="font-weight:700;color:var(--portal-muted);margin-bottom:4px;">▼ Downstream Consumers (${downstreams.length})</div>
          ${downstreams.length ? downstreams.map(d => `<div>• <strong>${escHtml(d)}</strong></div>`).join('') : '<span style="color:var(--portal-muted)">No downstream jobs depend on this output</span>'}
        </div>
      </div>`;
  } catch (err) {
    container.innerHTML = `<div class="empty-state" style="color:var(--portal-danger)">Could not load dependency chain: ${err.message}</div>`;
  }
}

// ── Data Quality Summary in Details ───────────────────────────────────────────
async function loadDataQualitySummary(jobName) {
  const host = document.getElementById('qualityDetailsWrap');
  host.innerHTML = '<span class="spinner"></span> <span>Loading data quality metrics…</span>';
  try {
    const [failures, statuses, stewardScore] = await Promise.all([
      api.dqFailures().catch(() => []),
      api.dqStatus().catch(() => []),
      api.stewardshipScore().catch(() => null)
    ]);

    const myFailures = (failures || []).filter(f => (f.JobName || f.jobName || '').toLowerCase() === jobName.toLowerCase());
    const myStatus = (statuses || []).find(s => (s.JobName || s.jobName || '').toLowerCase() === jobName.toLowerCase());

    host.innerHTML = `
      <div style="display:flex;flex-direction:column;gap:12px;">
        <div class="orch-meta-grid">
          <div class="orch-meta-card">
            <div class="orch-meta-label">Total Rows Quarantined</div>
            <div class="orch-meta-value" style="color:var(--portal-danger);">${Number(myStatus?.RowsQuarantined || myStatus?.rowsQuarantined || 0).toLocaleString()}</div>
          </div>
          <div class="orch-meta-card">
            <div class="orch-meta-label">Total Rows Warned</div>
            <div class="orch-meta-value" style="color:#d97706;">${Number(myStatus?.RowsWarned || myStatus?.rowsWarned || 0).toLocaleString()}</div>
          </div>
          <div class="orch-meta-card">
            <div class="orch-meta-label">Stewardship Coverage</div>
            <div class="orch-meta-value">${stewardScore?.CompletenessPercentage ? `${Math.round(stewardScore.CompletenessPercentage)}%` : 'Active'}</div>
          </div>
        </div>

        <div>
          <h4 style="margin:0 0 6px;font-size:0.88em;">Recent Rule Violations</h4>
          ${myFailures.length ? `
            <table class="data-table data-table-sm" style="font-size:0.82em;">
              <thead><tr><th>Rule / Column</th><th>Failure Count</th><th>Last Seen</th></tr></thead>
              <tbody>
                ${myFailures.map(f => `
                  <tr>
                    <td><code>${escHtml(f.RuleName || f.ruleName || f.ColumnName || f.columnName || 'Rule')}</code></td>
                    <td><span class="badge badge-danger">${f.FailureCount || f.failureCount || 0}</span></td>
                    <td>${fmtDt(f.LastOccurredAt || f.lastOccurredAt)}</td>
                  </tr>`).join('')}
              </tbody>
            </table>`
          : '<div class="empty-state" style="padding:16px 0;text-align:center;color:var(--portal-muted);font-size:.82em;">No data quality rule violations recorded for this job.</div>'}
        </div>
      </div>`;
  } catch (err) {
    host.innerHTML = `<div style="font-size:.82em;color:var(--portal-danger);">Failed to load data quality metrics: ${err.message}</div>`;
  }
}

// ── Change Log (Audit Trail) in Details ───────────────────────────────────────
async function loadAuditTrail(jobName) {
  const host = document.getElementById('auditLogWrap');
  host.innerHTML = '<span class="spinner"></span> <span>Loading audit trail…</span>';
  try {
    const entries = await api.audit(jobName);
    host.innerHTML = renderJobAuditTrail(entries);
  } catch (err) {
    host.innerHTML = `<div style="font-size:.82em;color:var(--portal-danger);">Failed to load audit trail: ${err.message}</div>`;
  }
}

// ── Script Editor ─────────────────────────────────────────────────────────────
function loadScriptEditor(job) {
  const editorEl = document.getElementById('detailScriptEditor');
  const saveBtn  = document.getElementById('saveScriptBtn');
  const scriptText = jobValue(job, 'Script') || '';
  scriptOriginalValue = scriptText;

  editorEl.innerHTML = '';
  scriptEditor = createScriptEditor(editorEl, {
    value: scriptText,
    onChange: val => {
      saveBtn.style.display = val !== scriptOriginalValue ? '' : 'none';
    }
  });

  saveBtn.onclick = async () => {
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveBtn).disabled = true;
    try {
      const val = scriptEditor.getValue();
      await api.update(jobValue(job, 'Name'), { ScriptText: val });
      scriptOriginalValue = val;
      saveBtn.style.display = 'none';
      showToast('Script saved.');
      await loadJobs();
    } catch (err) {
      showToast(`Error: ${err.message}`, true);
    } finally {
      /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (saveBtn).disabled = false;
    }
  };
}

// ── Access / Ownership & Grants ───────────────────────────────────────────────
async function loadAccessPanel(job) {
  const host = document.getElementById('accessPanelWrap');
  const name = jobValue(job, 'Name');
  try {
    const res = await api.grants('Job', name);
    if (!res.ok) {
      host.innerHTML = `<div class="orch-acl-error">Failed to load grants.</div>`;
      return;
    }
    const grants = await res.json();
    host.innerHTML = accessPanelHtml(jobValue(job, 'CreatedBy'), grants, isAdmin);

    host.querySelectorAll('[data-grant-revoke]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const [kind, id] = /** @type {HTMLElement} */ (btn).dataset.grantRevoke.split(':');
        try {
          await api.grantRevoke('Job', name, kind, id);
          loadAccessPanel(job);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    });

    const addBtn = host.querySelector('#orchAddGrantBtn');
    if (addBtn) {
      addBtn.addEventListener('click', async () => {
        const pKind = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (host.querySelector('#orchGrantPrincipalKind')).value;
        const pId   = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (host.querySelector('#orchGrantPrincipalId')).value.trim();
        const perm  = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (host.querySelector('#orchGrantPermission')).value;
        if (!pId) return;
        try {
          await api.grantSet('Job', name, pKind, pId, perm);
          loadAccessPanel(job);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    }

    const setOwnerBtn = host.querySelector('#orchSetOwnerBtn');
    if (setOwnerBtn) {
      setOwnerBtn.addEventListener('click', async () => {
        const oKind = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (host.querySelector('#orchOwnerKind')).value;
        const oKey  = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (host.querySelector('#orchOwnerKey')).value.trim();
        if (!oKey) return;
        try {
          await api.setOwner('Job', name, oKind, oKey);
          showToast('Owner updated.');
          loadJobs();
          loadAccessPanel(job);
        } catch (err) { showToast(`Error: ${err.message}`, true); }
      });
    }
  } catch (err) {
    host.innerHTML = `<div class="orch-acl-error">${err.message}</div>`;
  }
}

// ── Run History ───────────────────────────────────────────────────────────────
async function loadHistory(jobName) {
  const host = document.getElementById('historyTableWrap');
  try {
    const history = await api.history(jobName, 30);
    renderSparkline(history);

    if (!history || !history.length) {
      host.innerHTML = '<div style="font-size:.82em;color:var(--portal-muted);padding:8px 0;">No execution history recorded yet.</div>';
      return;
    }

    host.innerHTML = `
      <div style="overflow-x:auto;">
      <table class="data-table data-table-sm" style="font-size:.82em;">
        <thead><tr>
          <th>Run ID</th><th>Status</th><th>Started</th><th>Duration</th><th>Rows</th><th>Actions</th>
        </tr></thead>
        <tbody>
        ${history.map(h => {
          const id = h.Id ?? h.id;
          const status = h.Status ?? h.status;
          const start = fmtDt(h.StartTime ?? h.startTime);
          const st = new Date(h.StartTime ?? h.startTime);
          const et = h.EndTime || h.endTime ? new Date(h.EndTime || h.endTime) : null;
          const dur = et ? `${Math.round((et.getTime() - st.getTime()) / 1000)}s` : '—';
          const rows = Number(h.RowsProcessed ?? h.rowsProcessed ?? 0).toLocaleString();
          const isSuccess = status === 'Success' || status === 'Completed';
          const canResume = h.HasResumeSession ?? h.hasResumeSession;

          return `
            <tr>
              <td>#${id}</td>
              <td><span class="badge ${isSuccess ? 'badge-success' : (status === 'Running' ? 'badge-accent' : 'badge-danger')}">${escHtml(status)}</span></td>
              <td>${start}</td>
              <td>${dur}</td>
              <td>${rows}</td>
              <td>
                ${canResume ? `<button class="btn btn-sm btn-outline" data-resume-id="${id}" title="Resume from named checkpoint">Resume</button>` : '—'}
              </td>
            </tr>`;
        }).join('')}
        </tbody>
      </table>
      </div>`;

    host.querySelectorAll('[data-resume-id]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const hid = /** @type {HTMLElement} */ (btn).dataset.resumeId;
        /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
        try {
          const res = await api.resume(hid);
          if (res.ok) {
            showToast(`Run #${hid} queued to resume.`);
            loadHistory(jobName);
          } else {
            const body = await res.json().catch(() => null);
            throw new Error(body?.error || 'Resume failed.');
          }
        } catch (err) { showToast(`Error: ${err.message}`, true); }
        finally { /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false; }
      });
    });
  } catch (err) {
    host.innerHTML = `<div style="font-size:.82em;color:var(--portal-danger);">Failed to load history: ${err.message}</div>`;
  }
}

// ── Modals wiring ──────────────────────────────────────────────────────────────

// Create Job Modal
document.getElementById('newJobBtn').addEventListener('click', async () => {
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-display-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-description')).value = '';
  document.getElementById('cj-error').textContent = '';
  document.getElementById('createJobModal').style.display = 'flex';

  // Load scripts and schedules and bundles
  const [scriptsRes, bundlesRes, schedulesRes] = await Promise.all([
    api.scripts().catch(() => null),
    api.bundles().catch(() => null),
    api.schedules().catch(() => null)
  ]);

  const scriptSelect = document.getElementById('cj-script-select');
  scriptSelect.innerHTML = '<option value="">— Select from server —</option>';
  (scriptsRes?.files || scriptsRes?.Files || []).forEach(f => {
    const opt = document.createElement('option');
    opt.value = f;
    opt.textContent = f;
    scriptSelect.appendChild(opt);
  });

  const bundleSelect = document.getElementById('cj-bundle-select');
  bundleSelect.innerHTML = '<option value="">— Select published bundle —</option>';
  (bundlesRes || []).forEach(b => {
    const opt = document.createElement('option');
    opt.value = `bundle://${b.BundleName || b.bundleName}/latest`;
    opt.textContent = `${b.BundleName || b.bundleName} (v${b.LatestVersion || b.latestVersion || 1})`;
    bundleSelect.appendChild(opt);
  });

  const schedSelect = document.getElementById('cj-shared-schedule-select');
  schedSelect.innerHTML = '<option value="">— Select shared schedule —</option>';
  (schedulesRes || []).forEach(s => {
    const opt = document.createElement('option');
    opt.value = s.Name || s.name;
    opt.textContent = `${s.DisplayName || s.displayName || s.Name || s.name} (${s.Cron || s.cron})`;
    schedSelect.appendChild(opt);
  });
});

document.getElementById('cj-target-kind').addEventListener('change', e => {
  const isBundle = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (e.target).value === 'Bundle';
  document.getElementById('cj-script-target-row').style.display = isBundle ? 'none' : '';
  document.getElementById('cj-bundle-target-row').style.display = isBundle ? '' : 'none';
});

document.getElementById('cj-sched-mode-inline').addEventListener('change', () => {
  document.getElementById('cj-inline-sched-wrap').style.display = '';
  document.getElementById('cj-shared-sched-wrap').style.display = 'none';
});

document.getElementById('cj-sched-mode-shared').addEventListener('change', () => {
  document.getElementById('cj-inline-sched-wrap').style.display = 'none';
  document.getElementById('cj-shared-sched-wrap').style.display = '';
});

document.getElementById('closeCreateBtn').addEventListener('click', () => {
  document.getElementById('createJobModal').style.display = 'none';
});
document.getElementById('cj-cancelBtn').addEventListener('click', () => {
  document.getElementById('createJobModal').style.display = 'none';
});

document.getElementById('cj-saveBtn').addEventListener('click', async () => {
  const btn = document.getElementById('cj-saveBtn');
  const err = document.getElementById('cj-error');
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-name')).value.trim();
  const disp = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-display-name')).value.trim();
  const desc = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-description')).value.trim();
  const targetKind = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-target-kind')).value;
  const sandbox = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-sandbox-profile')).value;
  const isBundle = targetKind === 'Bundle';
  const targetPath = isBundle
    ? (/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-bundle-uri')).value.trim() || /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-bundle-select')).value)
    : (/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-script-path')).value.trim() || /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-script-select')).value);

  const schedMode = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.querySelector('input[name="cj-sched-mode"]:checked'))?.value || 'inline';
  const interval = Number(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-interval')).value) || 1;
  const unit = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-unit')).value;
  const atTime = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-at-time')).value || null;
  const retries = Number(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-retries')).value) || 0;
  const delay = Number(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-retry-delay')).value) || 30;
  const hashPolicy = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-hash-policy')).value;

  if (!name) { err.textContent = 'Job identifier is required.'; return; }
  if (!targetPath) { err.textContent = 'Target path or bundle is required.'; return; }

  const options = sandbox !== 'Default' ? { SandboxProfile: sandbox } : null;

  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
  err.textContent = '';
  try {
    const payload = {
      Name: name,
      DisplayName: disp || name,
      Description: desc || null,
      JobType: targetKind,
      TargetPath: targetPath,
      Interval: interval,
      Unit: unit,
      AtTime: atTime,
      MaxRetries: retries,
      RetryDelaySeconds: delay,
      HashPolicy: hashPolicy,
      Options: options,
      Mode: 'Create'
    };

    const res = await api.create(payload);
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      throw new Error(body?.error || 'Failed to create job.');
    }

    // If shared schedule mode selected, attach it
    if (schedMode === 'shared') {
      const schedName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cj-shared-schedule-select')).value;
      if (schedName) {
        await api.jobScheduleAttach(name, schedName).catch(() => {});
      }
    }

    document.getElementById('createJobModal').style.display = 'none';
    showToast(`Job '${name}' created.`);
    await loadJobs();
  } catch (ex) {
    err.textContent = ex.message;
  } finally {
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false;
  }
});

// Schedule Modal
document.getElementById('newScheduleBtn').addEventListener('click', () => {
  editingScheduleName = null;
  document.getElementById('scheduleModalTitle').textContent = 'Create Schedule';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-name')).value = '';
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-name')).disabled = false;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-display-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-description')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-cron')).value = '0 0 * * *';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-timezone')).value = 'UTC';
  /** @type {HTMLInputElement} */ (document.getElementById('cs-enabled')).checked = true;
  document.getElementById('cs-error').textContent = '';
  document.getElementById('scheduleModal').style.display = 'flex';
});

function openEditScheduleModal(name) {
  const sched = allSchedules.find(s => (s.Name || s.name) === name);
  if (!sched) return;
  editingScheduleName = name;
  document.getElementById('scheduleModalTitle').textContent = `Edit Schedule '${name}'`;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-name')).value = name;
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-name')).disabled = true;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-display-name')).value = sched.DisplayName || sched.displayName || name;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-description')).value = sched.Description || sched.description || '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-cron')).value = sched.Cron || sched.cron || '0 0 * * *';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-timezone')).value = sched.TimeZone || sched.timeZone || 'UTC';
  /** @type {HTMLInputElement} */ (document.getElementById('cs-enabled')).checked = sched.IsEnabled ?? sched.isEnabled ?? true;
  document.getElementById('cs-error').textContent = '';
  document.getElementById('scheduleModal').style.display = 'flex';
}

document.getElementById('closeScheduleModalBtn').addEventListener('click', () => {
  document.getElementById('scheduleModal').style.display = 'none';
});
document.getElementById('cs-cancelBtn').addEventListener('click', () => {
  document.getElementById('scheduleModal').style.display = 'none';
});

document.getElementById('cs-saveBtn').addEventListener('click', async () => {
  const btn = document.getElementById('cs-saveBtn');
  const err = document.getElementById('cs-error');
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-name')).value.trim();
  const disp = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-display-name')).value.trim();
  const desc = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-description')).value.trim();
  const cron = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-cron')).value.trim();
  const tz = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cs-timezone')).value;
  const enabled = /** @type {HTMLInputElement} */ (document.getElementById('cs-enabled')).checked;

  if (!name) { err.textContent = 'Name is required.'; return; }
  if (!cron) { err.textContent = 'Cron expression is required.'; return; }

  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
  err.textContent = '';
  try {
    if (editingScheduleName) {
      await api.scheduleUpdate(editingScheduleName, {
        DisplayName: disp || name,
        Description: desc || null,
        Cron: cron,
        TimeZone: tz,
        IsEnabled: enabled
      });
      showToast(`Schedule '${editingScheduleName}' updated.`);
    } else {
      const res = await api.scheduleCreate({
        Name: name,
        DisplayName: disp || name,
        Description: desc || null,
        Cron: cron,
        TimeZone: tz,
        IsEnabled: enabled
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.error || 'Failed to create schedule.');
      }
      showToast(`Schedule '${name}' created.`);
    }
    document.getElementById('scheduleModal').style.display = 'none';
    loadSchedules();
  } catch (ex) {
    err.textContent = ex.message;
  } finally {
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false;
  }
});

// Notification Modal
document.getElementById('newNotificationBtn').addEventListener('click', () => {
  editingNotificationName = null;
  document.getElementById('notificationModalTitle').textContent = 'Create Notification Endpoint';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-name')).value = '';
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-name')).disabled = false;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-display-name')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-description')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-connection')).value = '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-recipient')).value = '';
  /** @type {HTMLInputElement} */ (document.getElementById('cn-enabled')).checked = true;
  document.getElementById('cn-error').textContent = '';
  document.getElementById('notificationModal').style.display = 'flex';
});

function openEditNotificationModal(name) {
  const notif = allNotifications.find(n => (n.Name || n.name) === name);
  if (!notif) return;
  editingNotificationName = name;
  document.getElementById('notificationModalTitle').textContent = `Edit Notification '${name}'`;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-name')).value = name;
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-name')).disabled = true;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-display-name')).value = notif.DisplayName || notif.displayName || name;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-description')).value = notif.Description || notif.description || '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-connection')).value = notif.ConnectionName || notif.connectionName || '';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-recipient')).value = notif.Recipient || notif.recipient || '';
  /** @type {HTMLInputElement} */ (document.getElementById('cn-enabled')).checked = notif.IsEnabled ?? notif.isEnabled ?? true;
  document.getElementById('cn-error').textContent = '';
  document.getElementById('notificationModal').style.display = 'flex';
}

document.getElementById('closeNotificationModalBtn').addEventListener('click', () => {
  document.getElementById('notificationModal').style.display = 'none';
});
document.getElementById('cn-cancelBtn').addEventListener('click', () => {
  document.getElementById('notificationModal').style.display = 'none';
});

document.getElementById('cn-saveBtn').addEventListener('click', async () => {
  const btn = document.getElementById('cn-saveBtn');
  const err = document.getElementById('cn-error');
  const name = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-name')).value.trim();
  const disp = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-display-name')).value.trim();
  const desc = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-description')).value.trim();
  const conn = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-connection')).value.trim();
  const recip = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('cn-recipient')).value.trim();
  const enabled = /** @type {HTMLInputElement} */ (document.getElementById('cn-enabled')).checked;

  if (!name) { err.textContent = 'Name is required.'; return; }
  if (!conn) { err.textContent = 'Connection name is required.'; return; }

  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
  err.textContent = '';
  try {
    if (editingNotificationName) {
      await api.notificationUpdate(editingNotificationName, {
        DisplayName: disp || name,
        Description: desc || null,
        ConnectionName: conn,
        Recipient: recip || null,
        IsEnabled: enabled
      });
      showToast(`Notification '${editingNotificationName}' updated.`);
    } else {
      const res = await api.notificationCreate({
        Name: name,
        DisplayName: disp || name,
        Description: desc || null,
        ConnectionName: conn,
        Recipient: recip || null,
        IsEnabled: enabled
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.error || 'Failed to create notification.');
      }
      showToast(`Notification '${name}' created.`);
    }
    document.getElementById('notificationModal').style.display = 'none';
    loadNotifications();
  } catch (ex) {
    err.textContent = ex.message;
  } finally {
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false;
  }
});

// Test Dispatch Modal
let dispatchTargetName = null;
function openDispatchModal(name) {
  dispatchTargetName = name;
  document.getElementById('dispatchModalTitle').textContent = `Test Dispatch to '${name}'`;
  document.getElementById('disp-error').textContent = '';
  document.getElementById('dispatchModal').style.display = 'flex';
}

document.getElementById('closeDispatchModalBtn').addEventListener('click', () => {
  document.getElementById('dispatchModal').style.display = 'none';
});
document.getElementById('disp-cancelBtn').addEventListener('click', () => {
  document.getElementById('dispatchModal').style.display = 'none';
});

document.getElementById('disp-sendBtn').addEventListener('click', async () => {
  const btn = document.getElementById('disp-sendBtn');
  const err = document.getElementById('disp-error');
  const title = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('disp-title')).value.trim();
  const text = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('disp-text')).value.trim();
  const sourceKind = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('disp-source-kind')).value;
  const trigger = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('disp-trigger')).value;
  const override = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('disp-recipient-override')).value.trim() || null;

  if (!title || !text) { err.textContent = 'Title and text are required.'; return; }

  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
  err.textContent = '';
  try {
    const res = await api.notificationDispatch(dispatchTargetName, {
      SourceKind: sourceKind,
      Title: title,
      Text: text,
      Trigger: trigger,
      RecipientOverride: override
    });
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      throw new Error(body?.error || 'Dispatch refused.');
    }
    showToast(`Test notification sent to '${dispatchTargetName}'.`);
    document.getElementById('dispatchModal').style.display = 'none';
  } catch (ex) {
    err.textContent = ex.message;
  } finally {
    /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false;
  }
});

// Attach Schedule Modal
document.getElementById('attachScheduleBtn').addEventListener('click', async () => {
  if (!selectedJob) return;
  const sel = document.getElementById('as-select');
  sel.innerHTML = '<option value="">— Loading schedules —</option>';
  document.getElementById('as-error').textContent = '';
  document.getElementById('attachScheduleModal').style.display = 'flex';

  const schedules = await api.schedules().catch(() => []);
  sel.innerHTML = '<option value="">— Select a schedule —</option>';
  (schedules || []).forEach(s => {
    const opt = document.createElement('option');
    opt.value = s.Name || s.name;
    opt.textContent = `${s.DisplayName || s.displayName || s.Name || s.name} (${s.Cron || s.cron})`;
    sel.appendChild(opt);
  });
});

document.getElementById('as-submitBtn').addEventListener('click', async () => {
  if (!selectedJob) return;
  const jobName = jobValue(selectedJob, 'Name');
  const schedName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('as-select')).value;
  const err = document.getElementById('as-error');
  if (!schedName) { err.textContent = 'Please choose a schedule.'; return; }

  try {
    await api.jobScheduleAttach(jobName, schedName);
    showToast(`Schedule '${schedName}' attached to '${jobName}'.`);
    document.getElementById('attachScheduleModal').style.display = 'none';
    loadJobLinkedSchedules(jobName);
  } catch (ex) {
    err.textContent = ex.message;
  }
});

// Attach Notification Modal
document.getElementById('attachNotificationBtn').addEventListener('click', async () => {
  if (!selectedJob) return;
  const sel = document.getElementById('an-select');
  sel.innerHTML = '<option value="">— Loading notifications —</option>';
  document.getElementById('an-error').textContent = '';
  document.getElementById('attachNotificationModal').style.display = 'flex';

  const notifs = await api.notifications().catch(() => []);
  sel.innerHTML = '<option value="">— Select a notification —</option>';
  (notifs || []).forEach(n => {
    const opt = document.createElement('option');
    opt.value = n.Name || n.name;
    opt.textContent = `${n.DisplayName || n.displayName || n.Name || n.name} (${n.ConnectionName || n.connectionName})`;
    sel.appendChild(opt);
  });
});

document.getElementById('an-submitBtn').addEventListener('click', async () => {
  if (!selectedJob) return;
  const jobName = jobValue(selectedJob, 'Name');
  const notifName = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('an-select')).value;
  const trigger = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('an-trigger')).value;
  const err = document.getElementById('an-error');
  if (!notifName) { err.textContent = 'Please choose a notification.'; return; }

  try {
    await api.jobNotificationAttach(jobName, notifName, trigger);
    showToast(`Notification '${notifName}' attached.`);
    document.getElementById('attachNotificationModal').style.display = 'none';
    loadJobLinkedNotifications(jobName);
  } catch (ex) {
    err.textContent = ex.message;
  }
});

// Edit Watermark State Modal
let stateJobName = null;
function openEditStateModal(jobName, key = '', val = '') {
  stateJobName = jobName;
  document.getElementById('editStateTitle').textContent = key ? `Set Watermark '${key}'` : 'Add Watermark Key';
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('es-key')).value = key;
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('es-key')).disabled = !!key;
  /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('es-val')).value = val === '<null>' ? '' : val;
  document.getElementById('es-error').textContent = '';
  document.getElementById('editStateModal').style.display = 'flex';
}

document.getElementById('es-submitBtn').addEventListener('click', async () => {
  const key = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('es-key')).value.trim();
  const val = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('es-val')).value.trim();
  const err = document.getElementById('es-error');
  if (!key) { err.textContent = 'Key is required.'; return; }

  try {
    await api.jobStateSet(stateJobName, key, val);
    showToast(`Watermark '${key}' saved.`);
    document.getElementById('editStateModal').style.display = 'none';
    loadJobWatermarks(stateJobName);
  } catch (ex) {
    err.textContent = ex.message;
  }
});

// ── Triage Setup ───────────────────────────────────────────────────────────────
const triageState = { expanded: new Set(), selected: new Set(), openRuns: new Set(), details: new Map() };
let triageBoardData = null;

async function loadTriage() {
  const host = document.getElementById('triageBoard');
  const lookback = Number(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (document.getElementById('triageLookback')).value) || 24;
  try {
    triageBoardData = await api.triage(lookback);
    if (!triageBoardData) return;
    host.innerHTML = renderTriageBoard(triageBoardData, triageState);
  } catch (err) {
    host.innerHTML = `<div class="orch-offline-banner">
      <strong>Triage is unavailable.</strong> ${String(err.message || err).slice(0, 300)}
      <button class="btn btn-sm btn-outline" id="triageRetryBtn" type="button">Retry</button>
    </div>`;
    document.getElementById('triageRetryBtn')?.addEventListener('click', () => loadTriage());
  }
}

document.getElementById('triageRefreshBtn').addEventListener('click', () => loadTriage());
document.getElementById('triageLookback').addEventListener('change', () => {
  triageState.expanded.clear();
  triageState.selected.clear();
  triageState.openRuns.clear();
  triageState.details.clear();
  loadTriage();
});

document.getElementById('triageBoard').addEventListener('click', async event => {
  const toggle = /** @type {Element} */ (event.target).closest('.triage-incident-toggle');
  if (toggle) {
    const index = Number(/** @type {HTMLElement} */ (toggle).dataset.incident);
    triageState.expanded.has(index) ? triageState.expanded.delete(index) : triageState.expanded.add(index);
    document.getElementById('triageBoard').innerHTML = renderTriageBoard(triageBoardData, triageState);
    return;
  }
  const evidence = /** @type {Element} */ (event.target).closest('.triage-run-evidence');
  if (evidence) {
    const runId = Number(/** @type {HTMLElement} */ (evidence).dataset.run);
    if (triageState.openRuns.has(runId)) {
      triageState.openRuns.delete(runId);
      document.getElementById('triageBoard').innerHTML = renderTriageBoard(triageBoardData, triageState);
      return;
    }
    triageState.openRuns.add(runId);
    if (!triageState.details.has(runId)) {
      triageState.details.set(runId, { status: 'loading' });
      document.getElementById('triageBoard').innerHTML = renderTriageBoard(triageBoardData, triageState);
      try {
        const detail = await api.triageRun(runId);
        if (detail) triageState.details.set(runId, detail);
      } catch (err) {
        triageState.details.set(runId, { status: 'error', message: String(err.message || err).slice(0, 300) });
      }
    }
    document.getElementById('triageBoard').innerHTML = renderTriageBoard(triageBoardData, triageState);
    return;
  }
  if (/** @type {Element} */ (event.target).closest('.triage-rerun-selected')) {
    rerunJobs(selectedJobNames(triageBoardData, triageState.selected));
    return;
  }
  const one = /** @type {Element} */ (event.target).closest('.triage-rerun-one');
  if (one) rerunJobs([/** @type {HTMLElement} */ (one).dataset.job]);
});

document.getElementById('triageBoard').addEventListener('change', event => {
  const check = /** @type {Element} */ (event.target).closest('.triage-incident-check');
  if (!check) return;
  const index = Number(/** @type {HTMLElement} */ (check).dataset.incident);
  /** @type {HTMLInputElement} */ (check).checked ? triageState.selected.add(index) : triageState.selected.delete(index);
  document.getElementById('triageBoard').innerHTML = renderTriageBoard(triageBoardData, triageState);
});

async function rerunJobs(jobNames) {
  if (!jobNames.length) return;
  if (jobNames.length === 1) { openRunModal(jobNames[0]); return; }
  if (!confirm(`Re-run ${jobNames.length} jobs?`)) return;
  try {
    const res = await api.rerun(jobNames);
    const body = await res.json().catch(() => null);
    if (!res.ok) { alert(body?.error || 'Re-run failed.'); return; }
    triageState.selected.clear();
    await loadTriage();
    await loadJobs();
  } catch (err) { alert(`Re-run failed: ${err.message || err}`); }
}

// ── One-run overrides ─────────────────────────────────────────────────────────
let runJobName = null;
function openRunModal(name) {
  runJobName = name;
  document.getElementById('runJobTitle').textContent = `Run ${name}`;
  document.getElementById('runOverrideRows').innerHTML = '';
  document.getElementById('runJobError').textContent = '';
  document.getElementById('runJobModal').style.display = 'flex';
}
function closeRunModal() {
  document.getElementById('runJobModal').style.display = 'none';
  runJobName = null;
}
document.getElementById('runJobCloseBtn').addEventListener('click', closeRunModal);
document.getElementById('runJobCancelBtn').addEventListener('click', closeRunModal);

document.getElementById('runAddOverrideBtn').addEventListener('click', () => {
  const row = document.createElement('div');
  row.className = 'run-override-row';
  row.innerHTML = `
    <div class="form-group"><label>Variable<input class="run-override-name" type="text" placeholder="@start_date" autocomplete="off"></label></div>
    <div class="form-group"><label>Value<input class="run-override-value" type="text" placeholder="2026-08-01" autocomplete="off"></label></div>
    <button class="btn-icon run-override-remove" type="button" title="Remove">✕</button>`;
  row.querySelector('.run-override-remove').addEventListener('click', () => row.remove());
  document.getElementById('runOverrideRows').appendChild(row);
});

document.getElementById('runJobSubmitBtn').addEventListener('click', async () => {
  const btn = document.getElementById('runJobSubmitBtn');
  const err = document.getElementById('runJobError');
  const variables = {};
  for (const row of document.querySelectorAll('.run-override-row')) {
    let n = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (row.querySelector('.run-override-name')).value.trim();
    const v = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (row.querySelector('.run-override-value')).value;
    if (!n && !v) continue;
    if (!n.startsWith('@')) n = '@' + n;
    variables[n] = v;
  }
  /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = true;
  err.textContent = '';
  try {
    const res = await api.trigger(runJobName, variables);
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      throw new Error(body?.error || 'Trigger failed.');
    }
    closeRunModal();
    showToast(`Job '${runJobName}' triggered.`);
    loadJobs();
  } catch (ex) { err.textContent = ex.message; }
  finally { /** @type {HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (btn).disabled = false; }
});

// ── Initial Start ─────────────────────────────────────────────────────────────
//
// `?job=<name>` opens that job's detail once the list has loaded. Studio writes a scheduled job into
// a script and then sends the author here; landing on a list of every job in the workspace and
// asking them to find the one they just made is the unexplained application switch this link exists
// to avoid. A name that matches nothing simply leaves the list as it is — the job may not have been
// run into existence yet, and an error about it would be wrong as often as it was right.
async function openRequestedJob() {
  const requested = new URLSearchParams(location.search).get('job');
  if (!requested) return;
  const job = (allJobs || []).find(item => jobValue(item, 'Name') === requested);
  if (job) await openDetail(job);
}

poll();
loadTriage();
loadJobs().then(openRequestedJob);
pollHandle = setInterval(poll, 5000);
triageHandle = setInterval(loadTriage, 15000);
