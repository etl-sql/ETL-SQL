/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/pages/orchestrator.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { auth, authApi, apiFetch } from '../api.js';
import { asError, isObject } from '../http-error.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { renderDag, createScriptEditor } from '../../designer/designer.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { createTriageController } from '../triage-controller.js';
import { activityQuery } from '../orchestrator-activity.js';
import { createScheduledJobActivityCoordinator } from '../orchestrator-activity-coordinator.js';
import { renderScheduledJobsTable } from '../orchestrator-job-table.js';
import { createJobActionController, createJobRunDialog, jobVersion, requireMutationStatus } from '../orchestrator-job-actions.js';
import { createNotificationCatalog, createScheduleCatalog } from '../orchestrator-catalogs.js';
import { readCatalogJson } from '../orchestrator-catalog-responses.js';
import { createJobDetail } from '../orchestrator-job-detail.js';
import { createJobDialog } from '../orchestrator-create-job.js';
import { createJobTimeline } from '../orchestrator-timeline.js';
import { createUnownedDialog } from '../orchestrator-unowned.js';
const VIEWS = ['jobs', 'schedules', 'notifications'];
const job = (name) => `/api/orchestrator/jobs/${encodeURIComponent(name)}`;
function mountOrchestratorPage() {
    renderPortalHeader();
    installDialogAccessibility();
    // Each exit returns: nothing below may run, or call the API, for someone who should not be here.
    if (!auth.isLoggedIn()) {
        window.location.href = '/login.html';
        return;
    }
    let isAdmin, isManager;
    try {
        const identity = getSessionIdentity(auth.getToken());
        renderSessionIdentity(identity, document.getElementById('topbarUser'));
        if (!hasRole(identity, 'Admin', 'OrchestratorManager', 'OrchestratorViewer')) {
            window.location.href = '/index.html';
            return;
        }
        isAdmin = hasRole(identity, 'Admin');
        isManager = hasRole(identity, 'Admin', 'OrchestratorManager');
    }
    catch {
        window.location.href = '/login.html';
        return;
    }
    void applyPortalBranding();
    initTheme();
    applyNavigationSafely();
    const feedback = window.ETLSQLFeedback, charts = window.nativeCharts;
    if (!feedback || !charts)
        throw new Error('The Orchestrator page could not load its notification or chart support.');
    const lifetime = new AbortController();
    const box = (id) => {
        const found = document.getElementById(id);
        if (!found)
            throw new TypeError(`Orchestrator page element is missing: ${id}`);
        return found;
    };
    const notify = (message, failed = false) => { feedback.notify(message, failed ? { tone: 'error' } : {}); };
    // Named apart from the browser's built-in confirmation prompt, which this page never uses.
    const ask = (message, options) => feedback.confirm(message, options);
    const sessionExpired = () => auth.redirectToLogin();
    /** A JSON read: a refused or unanswered request is an error, never an empty result. */
    async function readJson(url, signal) {
        const response = await apiFetch(url, { signal, cache: 'no-store' });
        if (response.status === 401) {
            sessionExpired();
            throw new Error('Your session has expired.');
        }
        return readCatalogJson(response);
    }
    for (const id of ['newJobBtn', 'newScheduleBtn', 'newNotificationBtn'])
        box(id).style.display = isManager ? '' : 'none';
    // ── Jobs: list, actions, timeline, detail ──────────────────────────────────
    let catalogJobs = [], visibleJobs = [];
    const findJob = (name) => visibleJobs.find(item => item.name === name) ?? catalogJobs.find(item => item.name === name) ?? null;
    const jobsWrap = box('jobsTableWrap');
    const detail = createJobDetail({
        root: document, isAdmin, fetch: apiFetch, notify, confirm: ask, charts, createEditor: createScriptEditor,
        renderDag: (container, dag) => {
            if (!isObject(dag) || !Array.isArray(dag.nodes) || !Array.isArray(dag.edges))
                throw new TypeError('Invalid job flow response.');
            renderDag(container, dag);
        },
        reloadJob: async (name) => { await activity.refresh(); return findJob(name); },
        selectionChanged: name => {
            for (const row of jobsWrap.querySelectorAll('tr.job-row'))
                row.classList.toggle('selected', name !== null && row.dataset.name === name);
        },
        sessionExpired
    });
    const timeline = createJobTimeline({ root: document, open: detail.open });
    function renderJobs(page) {
        const focused = document.activeElement instanceof HTMLElement && jobsWrap.contains(document.activeElement)
            ? document.activeElement.dataset : null;
        visibleJobs = page.items.map(item => item.job);
        const open = findOpenJob();
        // The open panel follows the job as each poll reports it, so a later save sends the current version.
        if (open)
            detail.refresh(open);
        jobsWrap.innerHTML = renderScheduledJobsTable(visibleJobs, open?.id);
        if (!focused?.action || !focused.name)
            return;
        const replacement = Array.from(jobsWrap.querySelectorAll('[data-action][data-name]'))
            .find(button => button.dataset.action === focused.action && button.dataset.name === focused.name);
        (replacement ?? box('refreshJobsBtn')).focus();
    }
    const findOpenJob = () => detail.selectedName ? visibleJobs.find(item => item.name === detail.selectedName) ?? null : null;
    const activity = createScheduledJobActivityCoordinator({
        view: {
            root: document,
            fetchPage: (request, signal) => readJson(`/api/orchestrator/jobs/activity?${activityQuery(request)}`, signal),
            renderPage: renderJobs,
            clearPage: () => { visibleJobs = []; }
        },
        status: signal => readJson('/api/orchestrator/status', signal),
        onlineChanged: online => {
            box('statusDot').className = `orch-status-dot ${online ? 'online' : 'offline'}`;
            box('statusLabel').textContent = online ? 'Online' : 'Offline';
            box('offlineBanner').style.display = online ? 'none' : '';
            // Stopping the service is an administrator's action; nobody else is offered it.
            box('stopBtn').style.display = online && isAdmin ? '' : 'none';
            // Ownership is an administrator's concern too, and needs the service to answer.
            box('unownedBtn').style.display = online && isAdmin ? '' : 'none';
        },
        renderCatalog: jobs => { catalogJobs = jobs; box('jobsNavCount').textContent = String(jobs.length); timeline.render(jobs); },
        clearCatalog: () => { catalogJobs = []; box('jobsNavCount').textContent = '—'; timeline.clear(); },
        catalogError: error => notify(`Failed to load the job timeline: ${error.message}`, true)
    });
    const jobActions = createJobActionController({
        findTarget: name => { const found = findJob(name); return found ? { name, version: found.version } : null; },
        setEnabled: (target, enabled, signal) => apiFetch(job(target.name), { method: 'PUT', body: { isEnabled: enabled }, signal, headers: { 'If-Match': `"${jobVersion(target.version)}"` } }),
        deleteJob: (target, signal) => apiFetch(job(target.name), { method: 'DELETE', signal, headers: { 'If-Match': `"${jobVersion(target.version)}"` } }),
        kill: (target, signal) => apiFetch(`${job(target.name)}/kill`, { method: 'POST', signal }),
        confirmDelete: name => ask(`Delete job '${name}'?`, { title: 'Delete job and history',
            impact: 'All execution history for this job will be removed. This cannot be undone.', confirmLabel: 'Delete job',
            danger: true, auditAction: 'orchestrator.job.delete' }),
        async changed(action, target, signal) {
            if (action === 'delete' && detail.selectedName === target.name)
                detail.close();
            await activity.refresh();
            if (signal.aborted)
                return;
            const current = action === 'toggle' && detail.selectedName === target.name ? findJob(target.name) : null;
            if (current)
                detail.refresh(current);
        },
        notify
    });
    const runDialog = createJobRunDialog({ root: document, notify, refresh: () => activity.refresh(),
        trigger: (name, variables, signal) => apiFetch(`${job(name)}/trigger`, { method: 'POST', body: { variables }, signal }) });
    jobsWrap.addEventListener('click', event => {
        if (!(event.target instanceof Element))
            return;
        const button = event.target.closest('button[data-action]');
        if (button) {
            if (button.dataset.action === 'trigger' && button.dataset.name)
                runDialog.open(button.dataset.name);
            else
                void jobActions.run(button);
            return;
        }
        if (event.target.closest('button'))
            return;
        const selected = findJob(event.target.closest('tr.job-row')?.dataset.name);
        if (selected)
            detail.open(selected);
    }, { signal: lifetime.signal });
    box('refreshJobsBtn').addEventListener('click', () => { void activity.refresh(); }, { signal: lifetime.signal });
    const createDialog = createJobDialog({ root: document, fetch: apiFetch, notify, sessionExpired, created: () => activity.refresh() });
    const unowned = createUnownedDialog({ root: document, fetch: apiFetch, notify, confirm: ask, sessionExpired,
        changed: async () => {
            await activity.refresh();
            // The open panel shows an owner and grants that may just have changed.
            const open = findOpenJob();
            if (open)
                detail.open(open);
        } });
    // ── Schedule and notification catalogs ─────────────────────────────────────
    const catalogHost = { root: document, notify, confirm: ask };
    const schedules = createScheduleCatalog(catalogHost, {
        list: signal => readJson('/api/orchestrator/schedules?limit=1000', signal),
        create: (body, signal) => apiFetch('/api/orchestrator/schedules', { method: 'POST', body, signal }),
        update: (name, body, signal) => apiFetch(`/api/orchestrator/schedules/${encodeURIComponent(name)}`, { method: 'PUT', body, signal }),
        remove: (name, signal) => apiFetch(`/api/orchestrator/schedules/${encodeURIComponent(name)}`, { method: 'DELETE', signal })
    });
    const notifications = createNotificationCatalog(catalogHost, {
        list: signal => readJson('/api/orchestrator/notifications?limit=1000', signal),
        create: (body, signal) => apiFetch('/api/orchestrator/notifications', { method: 'POST', body, signal }),
        update: (name, body, signal) => apiFetch(`/api/orchestrator/notifications/${encodeURIComponent(name)}`, { method: 'PUT', body, signal }),
        remove: (name, signal) => apiFetch(`/api/orchestrator/notifications/${encodeURIComponent(name)}`, { method: 'DELETE', signal }),
        dispatch: (name, body, signal) => apiFetch(`/api/orchestrator/notifications/${encodeURIComponent(name)}/dispatch`, { method: 'POST', body, signal })
    });
    function showView(view) {
        for (const name of VIEWS) {
            const suffix = name.charAt(0).toUpperCase() + name.slice(1);
            box(`orchNav${suffix}`).classList.toggle('active', name === view);
            box(`orchNav${suffix}`).setAttribute('aria-selected', name === view ? 'true' : 'false');
            box(`view${suffix}Section`).style.display = name === view ? '' : 'none';
        }
        if (view === 'schedules')
            void schedules.load();
        if (view === 'notifications')
            void notifications.load();
    }
    for (const name of VIEWS)
        box(`orchNav${name.charAt(0).toUpperCase() + name.slice(1)}`).addEventListener('click', () => showView(name), { signal: lifetime.signal });
    // ── Triage ────────────────────────────────────────────────────────────────
    const lookback = box('triageLookback');
    const triage = createTriageController({
        root: box('triageBoard'), refreshButton: box('triageRefreshBtn'), pollMs: 15000,
        ...(lookback instanceof HTMLSelectElement ? { lookback } : {}),
        api: {
            board: (hours, signal) => readJson(`/api/orchestrator/triage?lookbackHours=${encodeURIComponent(hours)}`, signal),
            detail: (id, signal) => readJson(`/api/orchestrator/triage/runs/${encodeURIComponent(id)}`, signal),
            async rerun(jobNames, signal) {
                const response = await apiFetch('/api/orchestrator/jobs/rerun', { method: 'POST', body: { jobNames }, signal });
                if (response.status === 401)
                    sessionExpired();
                if (!response.ok)
                    throw new Error('Re-run request did not return a successful response.');
                return response.json();
            }
        },
        runOne: name => runDialog.open(name),
        confirmRerun: names => ask(`Re-run ${names.length} jobs?`, { title: 'Re-run selected jobs', confirmLabel: 'Re-run jobs', auditAction: 'orchestrator.job.rerun' }),
        notify: (message, failed) => { feedback.notify(message, { title: 'Re-run selected jobs', tone: failed ? 'error' : 'success' }); },
        onTriggered: () => activity.refresh()
    });
    // ── Service controls ──────────────────────────────────────────────────────
    const stop = box('stopBtn');
    let stopping = false;
    stop.addEventListener('click', async () => {
        if (stopping || !isAdmin)
            return;
        if (!await ask('Stop the Orchestrator service?', { title: 'Stop the Orchestrator service',
            impact: 'Scheduled jobs do not run while the service is stopped. It restarts on its own only when the host supervises it.',
            confirmLabel: 'Stop service', danger: true, auditAction: 'orchestrator.service.stop' }))
            return;
        if (lifetime.signal.aborted)
            return;
        stopping = true;
        try {
            await requireMutationStatus(await apiFetch('/api/orchestrator/service/stop', { method: 'POST', signal: lifetime.signal }), 200);
            if (lifetime.signal.aborted)
                return;
            notify('Stop signal sent to the Orchestrator service.');
            await activity.poll();
        }
        catch (failure) {
            if (!lifetime.signal.aborted)
                notify(`Error: ${asError(failure).message}`, true);
        }
        finally {
            stopping = false;
        }
    }, { signal: lifetime.signal });
    box('logoutBtn').addEventListener('click', () => { void authApi.logout(); }, { signal: lifetime.signal });
    // ── Start ─────────────────────────────────────────────────────────────────
    // `?job=<name>` opens that job once the list has loaded. Studio writes a scheduled job into a
    // script and sends the author here; a name that matches nothing leaves the list as it is, since
    // the job may not have been run into existence yet.
    async function start() {
        void activity.poll();
        void triage.reload();
        // Both catalogs load with the page so their navigation counts are read, not assumed to be zero.
        void schedules.load();
        void notifications.load();
        if (isAdmin)
            void unowned.refreshCount();
        await activity.refresh();
        const requested = lifetime.signal.aborted ? null : findJob(new URLSearchParams(location.search).get('job'));
        if (requested)
            detail.open(requested);
    }
    const statusTimer = setInterval(() => { void activity.poll(); }, 5000);
    window.addEventListener('pagehide', () => {
        clearInterval(statusTimer);
        lifetime.abort();
        for (const owner of [jobActions, runDialog, createDialog, unowned, triage, schedules, notifications, detail, timeline, activity])
            owner.dispose();
    }, { once: true });
    // A page restored from the back/forward cache has disposed owners; load it again instead.
    window.addEventListener('pageshow', event => { if (event.persisted)
        window.location.reload(); });
    void start();
}
mountOrchestratorPage();
