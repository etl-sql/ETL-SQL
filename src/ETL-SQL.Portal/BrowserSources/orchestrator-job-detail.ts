import { asError } from './http-error.js';
import type { ApiRequestOptions } from './portal-http.js';
import { escHtml, fmtDt, renderJobAuditTrail, renderWatermarksTable } from './orchestrator-admin-ui.js';
import { accessPanelHtml, ownerLabel } from './orchestrator-acl-ui.js';
import { jobVersion, requireMutationStatus } from './orchestrator-job-actions.js';
import { readCatalogJson, readNotifications, readSchedules } from './orchestrator-catalog-responses.js';
import { readAudit, readDependencyChain, readGrants, readHistory, readJobStates, readNotificationLinks,
    readQualityFailures, readQualityStatuses, readScheduleLinks } from './orchestrator-detail-responses.js';
import type { HistoryRun } from './orchestrator-detail-responses.js';

/**
 * One job's detail panel on the Orchestrator page.
 *
 * Every read belongs to the opening that started it: selecting another job, closing the panel, or
 * leaving the page orphans what is still in flight, so one job's schedules, history, or grants
 * cannot be drawn under another job's name. A section that could not be read says so; it is never
 * drawn as "none". Writes are confirmed by status, one at a time per section.
 */

type Job = Pick<JobDefinition, 'name' | 'script' | 'interval' | 'unit' | 'atTime' | 'lastRun' | 'nextRun'
    | 'version' | 'jobType' | 'targetPath' | 'displayName' | 'description' | 'options' | 'createdBy'>;
type Tab = 'details' | 'flow' | 'deps' | 'quality' | 'audit';
interface Chart { setOption(option: unknown, replace?: boolean): void; dispose(): void }
interface Editor { getValue(): string; dispose(): void }

export interface JobDetailOptions {
    root: Document;
    isAdmin: boolean;
    fetch(url: string, init?: ApiRequestOptions): Promise<Response>;
    notify(message: string, failed?: boolean): void;
    confirm(message: string, options: Parameters<EtlSqlFeedback['confirm']>[1]): Promise<boolean>;
    charts: { init(element: HTMLElement): Chart };
    renderDag(container: HTMLElement, dag: unknown): void;
    /** The editor loads its language support before it exists, so creation is asynchronous. */
    createEditor(host: HTMLElement, options: { value: string; onChange(value: string): void }): Promise<Editor>;
    /** Reloads the job list and resolves to the job as the service now reports it. */
    reloadJob(name: string): Promise<Job | null>;
    selectionChanged(name: string | null): void;
    sessionExpired(): void;
}

const TABS: Tab[] = ['details', 'flow', 'deps', 'quality', 'audit'];
const job = (name: string) => `/api/orchestrator/jobs/${encodeURIComponent(name)}`;

function scheduleText(value: Job): string {
    if (!value.interval) return 'Manual';
    const unit = (value.unit || 'HOUR').toLowerCase();
    const plural = value.interval === 1 ? unit.replace(/s$/, '') : unit.endsWith('s') ? unit : unit + 's';
    return `Every ${value.interval} ${plural}${value.atTime ? ` at ${value.atTime}` : ''}`;
}

function sandboxProfile(options: Job['options']): string {
    if (!options) return 'Default';
    try {
        const parsed: unknown = JSON.parse(options);
        const profile = parsed !== null && typeof parsed === 'object' ? (parsed as Record<string, unknown>).SandboxProfile : null;
        return typeof profile === 'string' && profile ? profile : 'Default';
    } catch {
        // Options is free-form JSON; unparseable means no sandbox profile, so 'Default' stands.
        return 'Default';
    }
}

function seconds(run: HistoryRun): number | null {
    return run.endTime ? Math.round((Date.parse(run.endTime) - Date.parse(run.startTime)) / 1000) : null;
}

export function createJobDetail(options: JobDetailOptions) {
    const { root } = options;
    const lifetime = new AbortController();
    const get = <T extends HTMLElement>(id: string, type: { new(): T }): T => {
        const found = root.getElementById(id);
        if (!(found instanceof type)) throw new Error(`Missing job detail element: ${id}`);
        return found;
    };
    const box = (id: string) => get(id, HTMLElement);
    const field = (id: string) => {
        const found = root.getElementById(id);
        if (!(found instanceof HTMLInputElement || found instanceof HTMLSelectElement))
            throw new Error(`Missing job detail field: ${id}`);
        return found;
    };
    const panel = box('detailPanel'), saveScript = get('saveScriptBtn', HTMLButtonElement);
    const pending = new Set<string>();
    let selected: Job | null = null;
    let scope = new AbortController();
    let tab: Tab = 'details';
    let loadedTabs = new Set<Tab>();
    let editor: Editor | null = null, savedScript = '';
    let sparkline: Chart | null = null, graph: Chart | null = null;
    let metric: 'duration' | 'rows' = 'duration', runs: HistoryRun[] = [];
    let stateKey: string | null = null;

    scope.abort();
    const live = (opened: AbortController) => !lifetime.signal.aborted && opened === scope && !opened.signal.aborted;
    const failed = (subject: string, failure: unknown) =>
        `<div class="error-msg show" role="alert">${escHtml(subject)} could not be loaded: ${escHtml(asError(failure).message)}</div>`;
    const loading = (label: string) => `<span class="spinner" style="width:14px;height:14px;"></span> <small>${escHtml(label)}</small>`;

    async function read(url: string, opened: AbortController): Promise<unknown> {
        const response = await options.fetch(url, { signal: opened.signal, cache: 'no-store' });
        if (response.status === 401) { options.sessionExpired(); throw new Error('Your session has expired.'); }
        return readCatalogJson(response);
    }

    /** Fills one section from one read, unless the panel has moved on to another job meanwhile. */
    async function section<T>(host: HTMLElement, subject: string, load: (opened: AbortController) => Promise<T>,
        draw: (value: T) => void): Promise<void> {
        const opened = scope;
        try {
            const value = await load(opened);
            if (live(opened)) draw(value);
        } catch (failure) {
            if (live(opened)) host.innerHTML = failed(subject, failure);
        }
    }

    /** One confirmed write per key. Announces success, shows the reason otherwise, then redraws. */
    async function write(key: string, send: (signal: AbortSignal) => Promise<Response>, status: number,
        done: string, after: () => unknown, report: (reason: string) => void = reason => options.notify(`Error: ${reason}`, true)): Promise<boolean> {
        const opened = scope;
        if (!live(opened) || pending.has(key)) return false;
        pending.add(key);
        try {
            await requireMutationStatus(await send(lifetime.signal), status);
            if (lifetime.signal.aborted) return false;
            options.notify(done);
            if (live(opened)) void after();
            return true;
        } catch (failure) {
            if (!lifetime.signal.aborted) report(asError(failure).message);
            return false;
        } finally {
            pending.delete(key);
        }
    }

    // ── Attached schedules and notifications ──────────────────────────────────
    function loadSchedules(name: string) {
        const host = box('detailLinkedSchedulesWrap');
        host.innerHTML = loading('Loading schedules…');
        return section(host, 'Attached schedules', async opened => readScheduleLinks(await read(`${job(name)}/schedules`, opened)), links => {
            host.innerHTML = links.length ? `<div class="orch-link-list">${links.map(link => `
                <div class="orch-link-row">
                  <div><strong>${escHtml(link.scheduleName)}</strong>${link.nextRun ? ` <span class="orch-link-meta">Next: ${escHtml(fmtDt(link.nextRun))}</span>` : ''}</div>
                  <button class="btn btn-sm btn-outline btn-danger-outline" type="button" data-detach-schedule="${escHtml(link.scheduleName)}">Detach</button>
                </div>`).join('')}</div>`
                : '<div class="orch-link-empty">No shared schedules attached.</div>';
        });
    }

    function loadNotifications(name: string) {
        const host = box('detailLinkedNotificationsWrap');
        host.innerHTML = loading('Loading notifications…');
        return section(host, 'Attached notifications', async opened => readNotificationLinks(await read(`${job(name)}/notifications`, opened)), links => {
            host.innerHTML = links.length ? `<div class="orch-link-list">${links.map(link => `
                <div class="orch-link-row">
                  <div><strong>${escHtml(link.notificationName)}</strong> <span class="badge">${escHtml(link.trigger)}</span></div>
                  <button class="btn btn-sm btn-outline btn-danger-outline" type="button" data-detach-notification="${escHtml(link.notificationName)}" data-trigger="${escHtml(link.trigger)}">Detach</button>
                </div>`).join('')}</div>`
                : '<div class="orch-link-empty">No notification endpoints attached.</div>';
        });
    }

    box('detailLinkedSchedulesWrap').addEventListener('click', async event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-detach-schedule]') : null;
        const name = selected?.name, schedule = button?.dataset.detachSchedule;
        if (!button || !name || !schedule) return;
        if (!await options.confirm(`Detach schedule '${schedule}' from job '${name}'?`, { title: 'Detach schedule',
            impact: 'The job keeps its definition; it just stops running on this schedule.', confirmLabel: 'Detach schedule',
            danger: true, auditAction: 'orchestrator.job.schedule.detach' })) return;
        await write(`schedule:${schedule}`, signal => options.fetch(`${job(name)}/schedules/${encodeURIComponent(schedule)}`, { method: 'DELETE', signal }),
            200, `Schedule '${schedule}' detached from '${name}'.`, () => loadSchedules(name));
    }, { signal: lifetime.signal });

    box('detailLinkedNotificationsWrap').addEventListener('click', async event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-detach-notification]') : null;
        const name = selected?.name, notification = button?.dataset.detachNotification, trigger = button?.dataset.trigger;
        if (!button || !name || !notification || !trigger) return;
        if (!await options.confirm(`Detach notification '${notification}' from job '${name}'?`, { title: 'Detach notification',
            impact: 'Nobody will be notified for this trigger until it is attached again.', confirmLabel: 'Detach notification',
            danger: true, auditAction: 'orchestrator.job.notification.detach' })) return;
        await write(`notification:${notification}:${trigger}`,
            signal => options.fetch(`${job(name)}/notifications/${encodeURIComponent(notification)}?trigger=${encodeURIComponent(trigger)}`, { method: 'DELETE', signal }),
            200, `Notification '${notification}' detached from '${name}'.`, () => loadNotifications(name));
    }, { signal: lifetime.signal });

    /** An attach dialog offers the current catalog; if that cannot be read it says so and offers nothing. */
    function attachDialog(ids: { open: string; modal: string; select: string; error: string; submit: string },
        choices: (opened: AbortController) => Promise<Array<{ value: string; label: string }>>,
        attach: (name: string, choice: string) => Promise<boolean>) {
        const modal = box(ids.modal), select = get(ids.select, HTMLSelectElement), error = box(ids.error);
        const submit = get(ids.submit, HTMLButtonElement);
        let owner: AbortController | null = null;
        get(ids.open, HTMLButtonElement).addEventListener('click', async () => {
            if (!selected) return;
            const opened = owner = scope;
            select.replaceChildren(new Option('— Loading —', '')); select.disabled = true; submit.disabled = true;
            error.textContent = ''; modal.style.display = 'flex';
            try {
                const available = await choices(opened);
                if (!live(opened) || owner !== opened) return;
                select.replaceChildren(new Option('— Select —', ''), ...available.map(choice => new Option(choice.label, choice.value)));
                select.disabled = false; submit.disabled = false;
            } catch (failure) {
                if (!live(opened) || owner !== opened) return;
                select.replaceChildren(new Option('— Unavailable —', ''));
                error.textContent = `The list could not be loaded: ${asError(failure).message}`;
            }
        }, { signal: lifetime.signal });
        submit.addEventListener('click', async () => {
            const name = selected?.name;
            if (!name || owner !== scope) return;
            if (!select.value) { error.textContent = 'Choose an entry to attach.'; return; }
            submit.disabled = true; error.textContent = '';
            const opened = owner;
            const attached = await attach(name, select.value);
            if (!live(opened) || owner !== opened) return;
            submit.disabled = false;
            if (attached) modal.style.display = 'none';
        }, { signal: lifetime.signal });
        return { close() { owner = null; modal.style.display = 'none'; }, fail(reason: string) { error.textContent = reason; } };
    }

    const attachSchedule = attachDialog(
        { open: 'attachScheduleBtn', modal: 'attachScheduleModal', select: 'as-select', error: 'as-error', submit: 'as-submitBtn' },
        async opened => readSchedules(await read('/api/orchestrator/schedules?limit=1000', opened))
            .map(item => ({ value: item.name, label: `${item.displayName || item.name} (${item.cron})` })),
        (name, schedule) => write(`schedule:${schedule}`,
            signal => options.fetch(`${job(name)}/schedules/${encodeURIComponent(schedule)}`, { method: 'POST', signal }),
            200, `Schedule '${schedule}' attached to '${name}'.`, () => loadSchedules(name), reason => attachSchedule.fail(reason)));
    const attachNotification = attachDialog(
        { open: 'attachNotificationBtn', modal: 'attachNotificationModal', select: 'an-select', error: 'an-error', submit: 'an-submitBtn' },
        async opened => readNotifications(await read('/api/orchestrator/notifications?limit=1000', opened))
            .map(item => ({ value: item.name, label: `${item.displayName || item.name} (${item.connectionName})` })),
        (name, notification) => {
            const trigger = field('an-trigger').value;
            return write(`notification:${notification}:${trigger}`,
                signal => options.fetch(`${job(name)}/notifications/${encodeURIComponent(notification)}`, { method: 'POST', body: { trigger }, signal }),
                200, `Notification '${notification}' attached to '${name}'.`, () => loadNotifications(name), reason => attachNotification.fail(reason));
        });

    // ── Watermarks ────────────────────────────────────────────────────────────
    const stateModal = box('editStateModal'), stateError = box('es-error'), stateSubmit = get('es-submitBtn', HTMLButtonElement);
    let stateOwner: AbortController | null = null;

    function loadStates(name: string) {
        const host = box('watermarksWrap');
        return section(host, 'Watermarks', async opened => readJobStates(await read(`${job(name)}/state`, opened)),
            states => { host.innerHTML = renderWatermarksTable(states, name); });
    }

    function editState(key: string, value: string) {
        stateOwner = scope; stateKey = key || null;
        box('editStateTitle').textContent = key ? `Set Watermark '${key}'` : 'Add Watermark Key';
        field('es-key').value = key; field('es-key').disabled = !!key;
        field('es-val').value = value;
        stateError.textContent = ''; stateSubmit.disabled = false; stateModal.style.display = 'flex';
    }

    box('watermarksWrap').addEventListener('click', async event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
        const name = selected?.name;
        if (!button || !name) return;
        if (button.id === 'addStateBtn') { editState('', ''); return; }
        const key = button.dataset.key;
        if (!key) return;
        if (button.dataset.stateAction === 'edit') { editState(key, button.dataset.val === '<null>' ? '' : button.dataset.val ?? ''); return; }
        if (button.dataset.stateAction !== 'reset') return;
        if (!await options.confirm(`Reset and clear watermark key '${key}'?`, { title: 'Clear watermark',
            impact: 'The next run will backfill from the beginning.', confirmLabel: 'Clear watermark', danger: true,
            auditAction: 'orchestrator.job.watermark.reset' })) return;
        await write(`state:${key}`, signal => options.fetch(`${job(name)}/state/${encodeURIComponent(key)}`, { method: 'DELETE', signal }),
            200, `Watermark '${key}' cleared.`, () => loadStates(name));
    }, { signal: lifetime.signal });

    stateSubmit.addEventListener('click', async () => {
        const name = selected?.name, key = (stateKey ?? field('es-key').value).trim(), value = field('es-val').value.trim();
        if (!name || stateOwner !== scope) return;
        if (!key) { stateError.textContent = 'Key is required.'; return; }
        stateSubmit.disabled = true; stateError.textContent = '';
        const opened = stateOwner;
        const saved = await write(`state:${key}`,
            signal => options.fetch(`${job(name)}/state/${encodeURIComponent(key)}`, { method: 'PUT', body: { value }, signal }),
            200, `Watermark '${key}' saved.`, () => loadStates(name), reason => { if (stateOwner === opened) stateError.textContent = reason; });
        if (!live(opened) || stateOwner !== opened) return;
        stateSubmit.disabled = false;
        if (saved) stateModal.style.display = 'none';
    }, { signal: lifetime.signal });

    // ── Script ────────────────────────────────────────────────────────────────
    async function mountEditor(value: Job) {
        const host = box('detailScriptEditor'), opened = scope;
        editor?.dispose(); editor = null;
        host.replaceChildren();
        savedScript = value.script ?? '';
        saveScript.style.display = 'none';
        try {
            const created = await options.createEditor(host, { value: savedScript,
                onChange: text => { if (live(opened)) saveScript.style.display = text !== savedScript ? '' : 'none'; } });
            // Another job may have been opened while this editor was loading; it must not be the one saved from.
            if (live(opened)) editor = created; else created.dispose();
        } catch (failure) {
            if (live(opened)) host.innerHTML = failed('The script editor', failure);
        }
    }

    saveScript.addEventListener('click', async () => {
        const target = selected, text = editor?.getValue();
        if (!target || text === undefined) return;
        const opened = scope;
        saveScript.disabled = true;
        try {
            // The version comes from the job as last loaded, and is replaced below once the save
            // lands, so a second save in the same session does not send the version it just retired.
            const version = jobVersion(target.version);
            const saved = await write('script', signal => options.fetch(job(target.name),
                { method: 'PUT', body: { scriptText: text }, headers: { 'If-Match': `"${version}"` }, signal }), 204, 'Script saved.', () => undefined);
            if (!saved) return;
            const current = await options.reloadJob(target.name);
            if (!live(opened)) return;
            if (!current) { options.notify('The job is no longer available. Refresh the job list.', true); return; }
            selected = current; savedScript = text;
            box('metaVersion').textContent = `v${current.version}`;
            saveScript.style.display = editor?.getValue() !== savedScript ? '' : 'none';
        } catch (failure) {
            if (live(opened)) options.notify(`Error: ${asError(failure).message}`, true);
        } finally {
            if (!lifetime.signal.aborted) saveScript.disabled = false;
        }
    }, { signal: lifetime.signal });

    // ── Access ────────────────────────────────────────────────────────────────
    function loadAccess(value: Job) {
        const host = box('accessPanelWrap');
        const draw = (state: Parameters<typeof accessPanelHtml>[0]) => { host.innerHTML = accessPanelHtml(state, escHtml, escHtml); };
        const opened = scope;
        return options.fetch(`/api/orchestrator/authorization/Job/${encodeURIComponent(value.name)}`, { signal: opened.signal, cache: 'no-store' })
            .then(async response => {
                if (response.status === 401) { options.sessionExpired(); return; }
                // Listing grants requires MANAGE, so a refusal is an answer about this caller, not a fault.
                const state = response.status === 403
                    ? { job: value, error: 'You can reach this job but are not permitted to administer its access.' }
                    : { job: value, grants: readGrants(await readCatalogJson(response)), isAdmin: options.isAdmin };
                if (live(opened)) draw(state);
            })
            .catch(failure => { if (live(opened)) draw({ job: value, error: `Access could not be loaded: ${asError(failure).message}` }); });
    }

    box('accessPanelWrap').addEventListener('click', async event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
        const target = selected;
        if (!button || !target) return;
        const base = `/api/orchestrator/authorization/Job/${encodeURIComponent(target.name)}`;
        const host = box('accessPanelWrap');
        const value = (id: string) => host.querySelector<HTMLInputElement | HTMLSelectElement>(`#${id}`)?.value.trim() ?? '';
        const principal = (kind: string, key: string) => `${base}/${encodeURIComponent(kind)}/${encodeURIComponent(key)}`;
        if (button.dataset.revokeKind && button.dataset.revokeKey) {
            const kind = button.dataset.revokeKind, key = button.dataset.revokeKey;
            if (!await options.confirm(`Revoke access for '${key}' on job '${target.name}'?`, { title: 'Revoke access',
                confirmLabel: 'Revoke access', danger: true, auditAction: 'orchestrator.grant.revoke' })) return;
            await write(`grant:${kind}:${key}`, signal => options.fetch(principal(kind, key), { method: 'DELETE', signal }),
                204, `Access revoked for '${key}'.`, () => loadAccess(target));
        } else if (button.id === 'aclGrantBtn') {
            const kind = value('aclPrincipalKind'), key = value('aclPrincipalKey'), permission = value('aclPermission');
            if (!key) { options.notify('Error: Enter the principal key to grant access to.', true); return; }
            await write(`grant:${kind}:${key}`, signal => options.fetch(principal(kind, key), { method: 'PUT', body: { permission }, signal }),
                200, `${permission} granted to '${key}'.`, () => loadAccess(target));
        } else if (button.id === 'aclOwnerBtn') {
            const principalKind = value('aclOwnerKind'), principalId = value('aclOwnerKey');
            if (!principalId) { options.notify('Error: Enter the principal key of the new owner.', true); return; }
            const opened = scope;
            const changed = await write('owner', signal => options.fetch(`${base}/owner`, { method: 'PUT', body: { principalKind, principalId }, signal }),
                200, `Owner of '${target.name}' updated.`, () => undefined);
            if (!changed) return;
            const current = await options.reloadJob(target.name);
            if (!live(opened) || !current) return;
            selected = current;
            box('metaOwner').textContent = ownerLabel(current.createdBy);
            void loadAccess(current);
        }
    }, { signal: lifetime.signal });

    // ── History and trend ─────────────────────────────────────────────────────
    function drawTrend() {
        sparkline ??= options.charts.init(box('sparklineChart'));
        const accent = getComputedStyle(root.body).getPropertyValue('--portal-accent').trim() || '#2563eb';
        const data = runs.slice().reverse().map(run => metric === 'rows' ? run.rowsProcessed : seconds(run) ?? 0);
        sparkline.setOption({
            grid: { left: 8, right: 8, top: 8, bottom: 8 },
            xAxis: { type: 'category', show: false, data: data.map((_, index) => index) },
            yAxis: { type: 'value', show: false },
            series: [{ type: 'line', data, smooth: true, symbol: 'none', lineStyle: { color: accent, width: 2 },
                areaStyle: { color: accent, opacity: 0.15 } }]
        }, true);
    }

    function trendMetric(next: 'duration' | 'rows') {
        metric = next;
        box('sparklineMetricDurationBtn').classList.toggle('active', next === 'duration');
        box('sparklineMetricRowsBtn').classList.toggle('active', next === 'rows');
        box('sparklineTitle').textContent = next === 'rows' ? 'Rows processed trend (last 30 runs)' : 'Duration trend (last 30 runs, seconds)';
        drawTrend();
    }
    box('sparklineMetricDurationBtn').addEventListener('click', () => trendMetric('duration'), { signal: lifetime.signal });
    box('sparklineMetricRowsBtn').addEventListener('click', () => trendMetric('rows'), { signal: lifetime.signal });

    function historyRow(run: HistoryRun): string {
        const succeeded = run.status === 'Success' || run.status === 'Completed';
        const eligible = run.status === 'Failed' || run.status === 'Cancelled';
        const duration = seconds(run);
        // Recovery needs three facts; when one is missing the reader is told which, not shown a dash.
        const action = eligible && run.hasResumeSession && run.checkpointLabel
            ? `<button class="btn btn-sm btn-outline history-resume-btn" type="button" data-resume-id="${run.id}" data-resume-label="${escHtml(run.checkpointLabel)}" title="Resume from named checkpoint">Resume · ${escHtml(run.checkpointLabel)}</button>`
            : `<button class="btn btn-sm btn-outline history-resume-btn" type="button" disabled>Unavailable</button><span class="history-resume-reason">${
                eligible ? 'No resumable checkpoint: this run was not persistent or never reached a top-level label.' : 'Only failed or cancelled runs can resume.'}</span>`;
        return `<tr><td>#${run.id}</td>
            <td><span class="badge ${succeeded ? 'badge-success' : run.status === 'Running' ? 'badge-accent' : 'badge-danger'}">${escHtml(run.status)}</span></td>
            <td>${escHtml(fmtDt(run.startTime))}</td><td>${duration === null ? '—' : `${duration}s`}</td>
            <td>${run.rowsProcessed.toLocaleString()}</td><td>${action}</td></tr>`;
    }

    function loadHistory(name: string) {
        const host = box('historyTableWrap');
        return section(host, 'Run history', async opened => readHistory(await read(`${job(name)}/history?limit=30`, opened)), history => {
            runs = history;
            drawTrend();
            host.innerHTML = history.length ? `<div style="overflow-x:auto;"><table class="data-table data-table-sm" style="font-size:.82em;">
                <thead><tr><th>Run ID</th><th>Status</th><th>Started</th><th>Duration</th><th>Rows</th><th>Actions</th></tr></thead>
                <tbody>${history.map(historyRow).join('')}</tbody></table></div>`
                : '<div class="orch-link-empty">No execution history recorded yet.</div>';
        });
    }

    box('historyTableWrap').addEventListener('click', async event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-resume-id]') : null;
        const name = selected?.name, id = button?.dataset.resumeId, label = button?.dataset.resumeLabel;
        if (!button || !name || !id) return;
        // Resuming re-runs everything the checkpoint did not cover; say so before doing it.
        if (!await options.confirm(`Resume run #${id} from checkpoint '${label}'?`, { title: 'Resume from named checkpoint',
            impact: 'Work after that label may run again.', confirmLabel: 'Resume run', auditAction: 'orchestrator.run.resume' })) return;
        button.disabled = true;
        const resumed = await write(`resume:${id}`, signal => options.fetch(`/api/orchestrator/runs/${encodeURIComponent(id)}/resume`, { method: 'POST', signal }),
            202, `Run #${id} queued to resume.`, () => loadHistory(name));
        if (!resumed && button.isConnected) button.disabled = false;
    }, { signal: lifetime.signal });

    // ── Tabs ──────────────────────────────────────────────────────────────────
    function loadTab(name: string) {
        if (tab === 'details' || loadedTabs.has(tab)) return;
        loadedTabs.add(tab);
        if (tab === 'flow') {
            const host = box('jobDagContainer');
            host.innerHTML = loading('Loading flow…');
            void section(host, 'The job flow', opened => read(`${job(name)}/dag`, opened),
                dag => { host.replaceChildren(); options.renderDag(host, dag); });
        } else if (tab === 'audit') {
            const host = box('auditLogWrap');
            host.innerHTML = loading('Loading audit trail…');
            void section(host, 'The audit trail', async opened => readAudit(await read(`${job(name)}/audit`, opened)),
                entries => { host.innerHTML = renderJobAuditTrail(entries); });
        } else if (tab === 'deps') {
            const host = box('dependencyDetailsWrap'), container = box('dependencyGraphContainer');
            host.innerHTML = loading('Loading dependencies…');
            void section(host, 'Dependencies', async opened => readDependencyChain(await read(`${job(name)}/dependencies`, opened), name), chain => {
                graph ??= options.charts.init(container);
                graph.setOption({ series: [{ type: 'graph', layout: 'force', roam: true, edgeSymbol: ['none', 'arrow'], edgeSymbolSize: 6,
                    force: { repulsion: 240, edgeLength: 90 }, lineStyle: { color: '#94a3b8', width: 2, curveness: 0.1 },
                    data: chain.nodes.map(node => ({ name: node.name, value: node.displayName || node.name, symbolSize: node.isCurrent ? 44 : 32,
                        itemStyle: { color: node.isCurrent ? '#2563eb' : node.isEnabled ? '#10b981' : '#94a3b8' },
                        label: { show: true, position: 'bottom', fontSize: 11 } })),
                    links: chain.edges.map(edge => ({ source: edge.from, target: edge.to,
                        label: { show: !!edge.detail, formatter: edge.detail, fontSize: 9 } })) }] }, true);
                const column = (title: string, entries: string[], none: string) => `<div class="orch-meta-card">
                    <div class="orch-meta-label">${title} (${entries.length})</div>
                    ${entries.length ? entries.map(entry => `<div>• <strong>${escHtml(entry)}</strong></div>`).join('') : `<span class="orch-link-meta">${none}</span>`}</div>`;
                host.innerHTML = `<div class="orch-meta-grid">${column('▲ Upstream providers', chain.upstream, 'No direct upstream job dependencies')}
                    ${column('▼ Downstream consumers', chain.downstream, 'No downstream jobs depend on this output')}</div>`;
            });
        } else {
            void loadQuality(name);
        }
    }

    /** The two quality sources are read independently; one being unavailable does not zero the other. */
    async function loadQuality(name: string) {
        const host = box('qualityDetailsWrap'), opened = scope;
        host.innerHTML = loading('Loading data quality…');
        const mine = (value: string | null | undefined) => (value ?? '').toLowerCase() === name.toLowerCase();
        const [statuses, failures] = await Promise.allSettled([
            read('/api/orchestrator/data-quality/status', opened).then(readQualityStatuses),
            read('/api/orchestrator/data-quality/failures', opened).then(readQualityFailures)]);
        if (!live(opened)) return;
        let summary: string, violations: string;
        if (statuses.status === 'rejected') summary = failed('The latest quality result', statuses.reason);
        else {
            const latest = statuses.value.filter(row => mine(row.jobName)).sort((a, b) => Date.parse(b.startTime) - Date.parse(a.startTime))[0];
            const card = (label: string, value: string) => `<div class="orch-meta-card"><div class="orch-meta-label">${label}</div><div class="orch-meta-value">${escHtml(value)}</div></div>`;
            summary = latest ? `<div class="orch-meta-grid">${card('Latest run', fmtDt(latest.startTime))}
                ${card('Rows quarantined', latest.rowsQuarantined.toLocaleString())}${card('Rows warned', latest.rowsWarned.toLocaleString())}
                ${card('Failed rules', String(latest.failedRuleCount))}${card('Freshness', latest.freshnessState)}</div>`
                : '<div class="orch-link-empty">No run of this job has recorded data-quality results.</div>';
        }
        if (failures.status === 'rejected') violations = failed('Rule violations', failures.reason);
        else {
            const rows = failures.value.filter(row => mine(row.jobName));
            violations = rows.length ? `<table class="data-table data-table-sm" style="font-size:.82em;">
                <thead><tr><th>Rule</th><th>Column</th><th>Action</th><th>Failures</th><th>Run started</th></tr></thead>
                <tbody>${rows.map(row => `<tr><td><code>${escHtml(row.rule)}</code></td>
                    <td>${escHtml(row.targetTable ? `${row.targetTable}.${row.columnName}` : row.columnName)}</td><td>${escHtml(row.action)}</td>
                    <td><span class="badge badge-danger">${row.failureCount.toLocaleString()}</span></td><td>${escHtml(fmtDt(row.startTime))}</td></tr>`).join('')}</tbody></table>`
                : '<div class="orch-link-empty">No data quality rule violations recorded for this job.</div>';
        }
        host.innerHTML = `${summary}<h4 style="margin:12px 0 6px;font-size:.88em;">Recent rule violations</h4>${violations}`;
    }

    function showTab(next: Tab) {
        tab = next;
        for (const name of TABS) {
            const suffix = name.charAt(0).toUpperCase() + name.slice(1);
            const button = box(`detailTab${suffix}`);
            button.classList.toggle('active', name === next);
            button.setAttribute('aria-selected', name === next ? 'true' : 'false');
            box(`detailPanel${suffix}`).style.display = name === next ? '' : 'none';
        }
        if (selected) loadTab(selected.name);
    }
    for (const name of TABS)
        box(`detailTab${name.charAt(0).toUpperCase() + name.slice(1)}`).addEventListener('click', () => showTab(name), { signal: lifetime.signal });

    // ── Open and close ────────────────────────────────────────────────────────
    function header(value: Job) {
        const bundle = value.jobType === 1 || (value.targetPath ?? '').startsWith('bundle://');
        box('detailJobName').textContent = value.name;
        box('detailJobDisplayName').textContent = value.displayName && value.displayName !== value.name ? value.displayName : '';
        box('metaTargetKind').textContent = bundle ? 'Bundle' : 'Script';
        box('metaSandboxProfile').textContent = sandboxProfile(value.options);
        box('metaVersion').textContent = `v${value.version}`;
        box('metaOwner').textContent = ownerLabel(value.createdBy);
        box('detailDescSection').style.display = value.description ? '' : 'none';
        box('detailDescription').textContent = value.description ?? '';
        box('detailSchedule').textContent = scheduleText(value);
        box('detailLastRun').textContent = value.lastRun ? fmtDt(value.lastRun) : 'Never';
        box('detailNextRun').textContent = value.nextRun ? fmtDt(value.nextRun) : '—';
    }

    function closeDialogs() {
        attachSchedule.close(); attachNotification.close();
        stateOwner = null; stateModal.style.display = 'none';
    }

    function close() {
        scope.abort();
        selected = null; loadedTabs = new Set(); runs = [];
        closeDialogs();
        panel.classList.remove('open');
        options.selectionChanged(null);
    }

    function open(value: Job) {
        if (lifetime.signal.aborted) return;
        scope.abort();
        scope = new AbortController();
        selected = value; loadedTabs = new Set(); runs = [];
        closeDialogs();
        header(value);
        panel.classList.add('open');
        options.selectionChanged(value.name);
        void mountEditor(value);
        for (const id of ['watermarksWrap', 'accessPanelWrap', 'historyTableWrap'])
            box(id).innerHTML = loading('Loading…');
        void loadSchedules(value.name); void loadNotifications(value.name); void loadStates(value.name);
        void loadAccess(value); void loadHistory(value.name);
        loadTab(value.name);
    }

    get('closeDetailBtn', HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });

    return {
        open, close,
        get selectedName() { return selected?.name ?? null; },
        /** Adopts a freshly loaded copy of the open job without discarding an unsaved script draft. */
        refresh(value: Job) {
            if (!selected || selected.name !== value.name || lifetime.signal.aborted) return;
            selected = value;
            header(value);
        },
        dispose() {
            scope.abort(); lifetime.abort();
            closeDialogs();
            editor?.dispose(); editor = null;
            sparkline?.dispose(); graph?.dispose(); sparkline = graph = null;
        }
    };
}
