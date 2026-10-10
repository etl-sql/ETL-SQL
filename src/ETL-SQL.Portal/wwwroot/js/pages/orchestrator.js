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
import { createJobDetail } from '../orchestrator-job-detail.js';
import { activityQuery } from '../orchestrator-activity.js';
import { createScheduledJobActivityCoordinator } from '../orchestrator-activity-coordinator.js';
import { renderScheduledJobsTable } from '../orchestrator-job-table.js';
import { createNotificationCatalog, createScheduleCatalog } from '../orchestrator-catalogs.js';
import { readCatalogJson } from '../orchestrator-catalog-responses.js';
import {
  escHtml, fmtDt,
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
  triage:       (lookbackHours = 24, signal) => apiJson(`${BASE}/triage?lookbackHours=${encodeURIComponent(lookbackHours)}`, { signal, cache: 'no-store' }),
  triageRun:(runId, signal) => apiJson(`${BASE}/triage/runs/${encodeURIComponent(runId)}`, { signal, cache: 'no-store' }),
  rerun:        (jobNames, signal) => apiFetch(`${BASE}/jobs/rerun`, { method: 'POST', body: { jobNames }, signal }),

  // Schedules
  schedules:       (limit = 1000) => apiJson(`${BASE}/schedules?limit=${limit}`),
  jobScheduleAttach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'POST' }),
  jobScheduleDetach:(jobName, scheduleName) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/schedules/${encodeURIComponent(scheduleName)}`, { method: 'DELETE' }),

  // Notifications
  notifications:      (limit = 1000) => apiJson(`${BASE}/notifications?limit=${limit}`),
  jobNotificationAttach:(jobName, notificationName, trigger = 'Completion') => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/notifications/${encodeURIComponent(notificationName)}`, { method: 'POST', body: { trigger } }),
  jobNotificationDetach:(jobName, notificationName, trigger) => apiFetch(`${BASE}/jobs/${encodeURIComponent(jobName)}/notifications/${encodeURIComponent(notificationName)}${trigger ? `?trigger=${encodeURIComponent(trigger)}` : ''}`, { method: 'DELETE' }),

};

// ── State ──────────────────────────────────────────────────────────────────────
let allJobs = [];
let ganttChart = null;
let timelineMode = 'gantt'; // 'gantt' | 'calendar'
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
  const openJob = detail.selectedName ? visibleJobs.find(job => job.name === detail.selectedName) : null;
  // The open panel follows the job as each poll reports it, so a later save sends the current version.
  if (openJob) detail.refresh(openJob);
  wrap.innerHTML = renderScheduledJobsTable(visibleJobs, openJob?.id);
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
    if (action === 'delete' && detail.selectedName === target.name) closeDetail();
    await loadJobs();
    if (signal.aborted) return;
    if (action === 'toggle' && detail.selectedName === target.name) {
      const current = findJob(target.name);
      if (current) detail.refresh(current);
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

// ── Job Detail Panel ──────────────────────────────────────────────────────────
const detail = createJobDetail({
  root: document,
  isAdmin,
  fetch: portalApiFetch,
  notify: showToast,
  confirm: (message, options) => ETLSQLFeedback.confirm(message, options),
  charts: nativeCharts,
  renderDag,
  createEditor: createScriptEditor,
  reloadJob: async name => { await loadJobs(); return findJob(name) ?? null; },
  selectionChanged: name => {
    document.querySelectorAll('tr.job-row').forEach(row => {
      row.classList.toggle('selected', name !== null && /** @type {HTMLElement} */ (row).dataset.name === name);
    });
  },
  sessionExpired: () => auth.redirectToLogin()
});
function openDetail(job) { detail.open(job); }
function closeDetail() { detail.close(); }

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
  if (job) openDetail(job);
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
  detail.dispose();
  ganttChart?.dispose();
  scheduleCatalog.dispose();
  notificationCatalog.dispose();
  activityCoordinator.dispose();
}, { once: true });
window.addEventListener('pageshow', event => {
  if (event.persisted) window.location.reload();
});
