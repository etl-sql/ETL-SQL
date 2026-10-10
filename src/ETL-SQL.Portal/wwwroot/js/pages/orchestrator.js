/**
 * Page module for orchestrator.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi, apiFetch as portalApiFetch } from '../api.js';
import { createJobActionController, createJobRunDialog, jobVersion, requireMutationStatus } from '../orchestrator-job-actions.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { renderDag, createScriptEditor } from '../../designer/designer.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createTriageController } from '../triage-controller.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { accessPanelHtml, ownerLabel } from '../orchestrator-acl-ui.js';
import { activityQuery } from '../orchestrator-activity.js';
import { createScheduledJobActivityCoordinator } from '../orchestrator-activity-coordinator.js';
import { renderScheduledJobsTable } from '../orchestrator-job-table.js';
import { createNotificationCatalog, createScheduleCatalog } from '../orchestrator-catalogs.js';
import { readCatalogJson } from '../orchestrator-catalog-responses.js';
import {
  escHtml, fmtDt,
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
try {
  const identity = getSessionIdentity(auth.getToken());
  renderSessionIdentity(identity, document.getElementById('topbarUser'));
  isAdmin = hasRole(identity, 'Admin');
  const isManager = hasRole(identity, 'Admin', 'OrchestratorManager');
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
  const response = await portalApiFetch(url, opts);
  if (!response.ok && opts.method && opts.method !== 'GET') await requireMutationStatus(response, 200);
  return response;
}

async function apiJson(url, opts = {}) {
  const res = await apiFetch(url, opts);
  if (res.status === 401) { auth.redirectToLogin(); return null; }
  if (!res.ok) throw new Error(await res.text());
  if (res.status === 204) return {};
  return res.json();
}

/** A catalog read: a refused or unanswered request is an error, never an empty list. */
async function catalogJson(url, signal) {
  const response = await portalApiFetch(url, { signal, cache: 'no-store' });
  if (response.status === 401) { auth.redirectToLogin(); throw new Error('Your session has expired.'); }
  return readCatalogJson(response);
}

const api = {
  status:       (signal) => apiJson(`${BASE}/status`, { signal }),
  activity:     (request, signal) => apiJson(`${BASE}/jobs/activity?${activityQuery(request)}`, { signal, cache: 'no-store' }),
  history:      (name, limit = 30) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/history?limit=${limit}`),
  resume:       (historyId) => apiFetch(`${BASE}/runs/${historyId}/resume`, { method: 'POST' }),
  create:       (body) => apiFetch(`${BASE}/jobs`, { method: 'POST', body }),
  update:       (name, body, version, signal) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}`, { method: 'PUT', body, signal, headers: { 'If-Match': `"${jobVersion(version)}"` } }),
  delete:       (name, version, signal) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}`, { method: 'DELETE', signal, headers: { 'If-Match': `"${jobVersion(version)}"` } }),
  trigger:      (name, variables = {}, signal) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}/trigger`, { method: 'POST', body: { variables }, signal }),
  kill:         (name, signal) => apiFetch(`${BASE}/jobs/${encodeURIComponent(name)}/kill`, { method: 'POST', signal }),
  scripts:      () => apiJson(`${BASE}/scripts`),
  scriptContent:(path) => apiJson(`${BASE}/scripts/content?path=${encodeURIComponent(path)}`),
  bundles:      () => apiJson(`${BASE}/bundles`),
  bundleVersions:(name) => apiJson(`${BASE}/bundles/${encodeURIComponent(name)}/versions`),
  stop:         () => apiFetch(`${BASE}/service/stop`, { method: 'POST' }),
  dag:          (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/dag`),
  dependencies: (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/dependencies`),
  audit:        (name) => apiJson(`${BASE}/jobs/${encodeURIComponent(name)}/audit`),
  triage:       (lookbackHours = 24, signal) => apiJson(`${BASE}/triage?lookbackHours=${encodeURIComponent(lookbackHours)}`, { signal, cache: 'no-store' }),
  triageRun:(runId, signal) => apiJson(`${BASE}/triage/runs/${encodeURIComponent(runId)}`, { signal, cache: 'no-store' }),
  rerun:        (jobNames, signal) => apiFetch(`${BASE}/jobs/rerun`, { method: 'POST', body: { jobNames }, signal }),

  // Schedules
  schedules:       (limit = 1000) => apiJson(`${BASE}/schedules?limit=${limit}`),
  jobSchedules:    (jobName) => apiJson(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules`),
  jobScheduleAttach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'POST' }),
  jobScheduleDetach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'DELETE' }),

  // Notifications
  notifications:      (limit = 1000) => apiJson(`${BASE}/notifications?limit=${limit}`),
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
let allJobs = [];
let selectedJob = null;
let ganttChart = null;
let sparklineChart = null;
let depGraphChart = null;
let timelineMode = 'gantt'; // 'gantt' | 'calendar'
let sparklineMetric = 'duration'; // 'duration' | 'rows'
let dagJobName   = null;
let scriptEditor = null;
let scriptOriginalValue = '';
let lastSparklineEntries = [];
let activeJobStates = [];
let visibleJobs = [];
const activityCoordinator = createScheduledJobActivityCoordinator({
  view: {
    root: document,
    fetchPage: api.activity,
    renderPage: renderCurrentJobsView,
    clearPage: () => { visibleJobs = []; }
  },
  status: api.status,
  onlineChanged: updateOnlineState,
  renderCatalog: (jobs) => {
    allJobs = jobs;
    document.getElementById('jobsNavCount').textContent = String(jobs.length);
    if (timelineMode === 'gantt') renderGantt(jobs);
    else renderCalendarTimelineView();
  },
  clearCatalog: () => {
    allJobs = [];
    document.getElementById('jobsNavCount').textContent = '—';
    if (ganttChart) renderGantt([]);
    document.getElementById('timelineCalendarWrap').replaceChildren();
  },
  catalogError: error => showToast(`Failed to load the job timeline: ${error.message}`, true)
});
function findJob(name) {
  return visibleJobs.find(job => jobValue(job, 'Name') === name)
    ?? allJobs.find(job => jobValue(job, 'Name') === name);
}

// ── View Switcher ──────────────────────────────────────────────────────────────
function setActiveView(view) {
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
  await activityCoordinator.poll();
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

async function loadJobs() {
  await activityCoordinator.refresh();
}

/** @param {ScheduledJobActivityPage} page */
function renderCurrentJobsView(page) {
  const wrap = document.getElementById('jobsTableWrap');
  const focused = document.activeElement instanceof HTMLElement && wrap.contains(document.activeElement)
    ? document.activeElement : null;
  const focusedAction = focused?.dataset.action;
  const focusedName = focused?.dataset.name;
  visibleJobs = page.items.map(item => item.job);
  wrap.innerHTML = renderScheduledJobsTable(visibleJobs, jobValue(selectedJob, 'Id'));
  if (focusedAction && focusedName) {
    const replacement = wrap.querySelector(`[data-action="${CSS.escape(focusedAction)}"][data-name="${CSS.escape(focusedName)}"]`);
    if (replacement instanceof HTMLElement) replacement.focus();
    else document.getElementById('refreshJobsBtn').focus();
  }

  wrap.querySelectorAll('tr.job-row').forEach(row => {
    row.addEventListener('click', e => {
      if (/** @type {Element} */ (e.target).closest('button')) return;
      const name = /** @type {HTMLElement} */ (row).dataset.name;
      const job  = findJob(name);
      if (job) openDetail(job);
    });
  });

  wrap.querySelectorAll('[data-action]').forEach(btn => {
    btn.addEventListener('click', e => { e.stopPropagation(); handleAction(btn); });
  });
}

const jobActions = createJobActionController({
  findTarget(name) {
    const job = findJob(name);
    return job ? { name, version: jobValue(job, 'Version') } : null;
  },
  setEnabled: (target, enabled, signal) => api.update(target.name, { IsEnabled: enabled }, target.version, signal),
  deleteJob: (target, signal) => api.delete(target.name, target.version, signal),
  kill: (target, signal) => api.kill(target.name, signal),
  confirmDelete: name => ETLSQLFeedback.confirm(`Delete job '${name}'?`, { title: 'Delete job and history', impact: 'All execution history for this job will be removed. This cannot be undone.', confirmLabel: 'Delete job', danger: true, auditAction: 'orchestrator.job.delete' }),
  async changed(action, target, signal) {
    if (action === 'delete' && jobValue(selectedJob, 'Name') === target.name) closeDetail();
    await loadJobs();
    if (signal.aborted) return;
    if (action === 'toggle' && jobValue(selectedJob, 'Name') === target.name) {
      const current = findJob(target.name);
      if (current) await openDetail(current);
    }
  },
  notify: showToast
});
function handleAction(btn) {
  if (btn.dataset.action === 'trigger') openRunModal(btn.dataset.name);
  else void jobActions.run(/** @type {HTMLButtonElement} */ (btn));
}

// ── Schedule and Notification Catalogs ────────────────────────────────────────
const catalogHost = {
  root: document,
  notify: showToast,
  confirm: (message, options) => ETLSQLFeedback.confirm(message, options)
};
const scheduleCatalog = createScheduleCatalog(catalogHost, {
  list: signal => catalogJson(`${BASE}/schedules?limit=1000`, signal),
  create: (body, signal) => portalApiFetch(`${BASE}/schedules`, { method: 'POST', body, signal }),
  update: (name, body, signal) => portalApiFetch(`${BASE}/schedules/${encodeURIComponent(name)}`, { method: 'PUT', body, signal }),
  remove: (name, signal) => portalApiFetch(`${BASE}/schedules/${encodeURIComponent(name)}`, { method: 'DELETE', signal })
});
const notificationCatalog = createNotificationCatalog(catalogHost, {
  list: signal => catalogJson(`${BASE}/notifications?limit=1000`, signal),
  create: (body, signal) => portalApiFetch(`${BASE}/notifications`, { method: 'POST', body, signal }),
  update: (name, body, signal) => portalApiFetch(`${BASE}/notifications/${encodeURIComponent(name)}`, { method: 'PUT', body, signal }),
  remove: (name, signal) => portalApiFetch(`${BASE}/notifications/${encodeURIComponent(name)}`, { method: 'DELETE', signal }),
  dispatch: (name, body, signal) => portalApiFetch(`${BASE}/notifications/${encodeURIComponent(name)}/dispatch`, { method: 'POST', body, signal })
});
function loadSchedules() { void scheduleCatalog.load(); }
function loadNotifications() { void notificationCatalog.load(); }

// ── Gantt Chart ───────────────────────────────────────────────────────────────
function renderGantt(jobs) {
  if (!ganttChart) {
    const el = document.getElementById('ganttChart');
    ganttChart = /** @type {{on: Function, setOption: Function, resize: Function, dispose: Function}} */ (
      nativeCharts.init(el));
    ganttChart.on('click', params => {
      if (params.componentType === 'series') {
        const name = params.data?.[3];
        const job  = findJob(name);
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
        const [, , , name] = params.data;
        const job = findJob(name);
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
        renderDag(container, dagData, { height: 360 });
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
    } catch {
      // Options is free-form JSON; unparseable means no sandbox profile, so 'Default' stands.
    }
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
        if (!await ETLSQLFeedback.confirm(`Detach schedule '${schedName}' from job '${jobName}'?`, { title: 'Detach schedule', impact: 'The job keeps its definition; it just stops running on this schedule.', confirmLabel: 'Detach schedule', danger: true, auditAction: 'orchestrator.job.schedule.detach' })) return;
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
        if (!await ETLSQLFeedback.confirm(`Detach notification '${notifName}' from job '${jobName}'?`, { title: 'Detach notification', impact: 'Nobody will be notified for this trigger until it is attached again.', confirmLabel: 'Detach notification', danger: true, auditAction: 'orchestrator.job.notification.detach' })) return;
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
        if (!await ETLSQLFeedback.confirm(`Reset and clear watermark key '${key}'?`, { title: 'Clear watermark', impact: 'The next run will backfill from the beginning.', confirmLabel: 'Clear watermark', danger: true, auditAction: 'orchestrator.job.watermark.reset' })) return;
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
      await requireMutationStatus(await api.update(jobValue(job, 'Name'), { ScriptText: val }, jobValue(job, 'Version')), 204);
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

          // Recovery needs three facts, and the reader needs to be told which one is missing. A
          // dash in this cell says only "no", and an operator staring at a failed run wants to know
          // whether it cannot resume because the run succeeded, or because nothing was checkpointed.
          const checkpoint = h.CheckpointLabel ?? h.checkpointLabel ?? null;
          const eligibleStatus = status === 'Failed' || status === 'Cancelled';
          const hasSession = Boolean(h.HasResumeSession ?? h.hasResumeSession);
          const canResume = eligibleStatus && hasSession && Boolean(checkpoint);
          const unavailableReason = eligibleStatus
            ? 'No resumable checkpoint: this run was not persistent or never reached a top-level label.'
            : 'Only failed or cancelled runs can resume.';

          return `
            <tr>
              <td>#${id}</td>
              <td><span class="badge ${isSuccess ? 'badge-success' : (status === 'Running' ? 'badge-accent' : 'badge-danger')}">${escHtml(status)}</span></td>
              <td>${start}</td>
              <td>${dur}</td>
              <td>${rows}</td>
              <td>
                ${canResume
                  ? `<button class="btn btn-sm btn-outline history-resume-btn" type="button" data-resume-id="${id}" data-resume-label="${escHtml(checkpoint)}" title="Resume from named checkpoint">Resume · ${escHtml(checkpoint)}</button>`
                  : `<button class="btn btn-sm btn-outline history-resume-btn" type="button" disabled>Unavailable</button><span class="history-resume-reason">${escHtml(unavailableReason)}</span>`}
              </td>
            </tr>`;
        }).join('')}
        </tbody>
      </table>
      </div>`;

    host.querySelectorAll('[data-resume-id]').forEach(btn => {
      btn.addEventListener('click', async () => {
        const hid = /** @type {HTMLElement} */ (btn).dataset.resumeId;
        const label = /** @type {HTMLElement} */ (btn).dataset.resumeLabel;
        // Resuming re-runs everything the checkpoint did not cover, which for a partially applied
        // load is the difference between a recovery and a duplicate. Say so before doing it.
        if (!await ETLSQLFeedback.confirm(`Resume run #${hid} from checkpoint '${label}'?`, {
          title: 'Resume from named checkpoint',
          impact: 'Work after that label may run again.',
          confirmLabel: 'Resume run',
          auditAction: 'orchestrator.run.resume'
        })) return;
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
const triageController = createTriageController({
  root: document.getElementById('triageBoard'),
  lookback: /** @type {HTMLSelectElement} */ (document.getElementById('triageLookback')),
  refreshButton: document.getElementById('triageRefreshBtn'),
  pollMs: 15000,
  api: {
    board: (hours, signal) => api.triage(hours, signal),
    detail: (id, signal) => api.triageRun(id, signal),
    async rerun(names, signal) {
      const response = await api.rerun(names, signal);
      if (response.status === 401) auth.redirectToLogin();
      if (!response.ok) throw new Error('Re-run request did not return a successful response.');
      return response.json();
    }
  },
  runOne: name => openRunModal(name),
  confirmRerun: names => ETLSQLFeedback.confirm(`Re-run ${names.length} jobs?`,
    { title: 'Re-run selected jobs', confirmLabel: 'Re-run jobs', auditAction: 'orchestrator.job.rerun' }),
  notify: (message, failed) => ETLSQLFeedback.notify(message,
    { title: 'Re-run selected jobs', tone: failed ? 'error' : 'success' }),
  onTriggered: () => loadJobs()
});

// ── One-run overrides ───────────────────────────────────────
const runDialog = createJobRunDialog({ root: document,
  trigger: api.trigger, notify: showToast, refresh: loadJobs });
function openRunModal(name) { runDialog.open(name); }

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
triageController.reload();
// Both catalogs load with the page so their navigation counts are read, not assumed to be zero.
loadSchedules();
loadNotifications();
loadJobs().then(openRequestedJob);
const statusTimer = setInterval(poll, 5000);
window.addEventListener('pagehide', () => {
  clearInterval(statusTimer);
  jobActions.dispose();
  runDialog.dispose();
  triageController.dispose();
  scheduleCatalog.dispose();
  notificationCatalog.dispose();
  activityCoordinator.dispose();
}, { once: true });
window.addEventListener('pageshow', event => {
  if (event.persisted) window.location.reload();
});
