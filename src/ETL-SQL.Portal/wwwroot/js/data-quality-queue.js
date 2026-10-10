/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/data-quality-queue.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { asError, isObject } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { installDialogAccessibility } from './dialog-a11y.js';
import { renderQualityQueue } from './data-quality-view.js';
import { readQualityQueue, readQualityJobs, readQualityRows, readQualityTrend, readQualityRules, readQualitySubmission, readQualityJobStatus } from './data-quality-responses.js';
import { ROW_STATUSES, TERMINAL_JOB_STATUSES, targetKey, rowId, rowStatus, pendingTarget } from './data-quality-state.js';
export { renderTopFailures } from './data-quality-view.js';
const surfaces = new WeakMap();
const uncertainByScope = new Map();
const inFlightByScope = new Map();
const TRACKED_JOBS_KEY = 'etlsql_dq_tracked_jobs';
function dictionary() { return Object.create(null); }
function sessionScope() {
    try {
        const identity = getSessionIdentity(sessionStorage.getItem('etlsql_token'));
        return identity.subject ? JSON.stringify([identity.claims.iss ?? '', identity.claims.tenant_id ?? '', identity.subject]) : null;
    }
    catch {
        return null;
    }
}
function restoredJob(value) {
    if (!isObject(value) || !['jobId', 'jobName', 'target', 'createdAt'].every(key => typeof value[key] === 'string' && value[key].trim())
        || !['Replay', 'Disposition'].includes(String(value.kind)) || !Number.isFinite(Date.parse(String(value.createdAt)))
        || (value.rowId != null && typeof value.rowId !== 'string'))
        return null;
    return { jobId: String(value.jobId), kind: value.kind, jobName: String(value.jobName), target: String(value.target),
        rowId: value.rowId ?? null, createdAt: String(value.createdAt), status: 'Submitted', verified: false,
        statusUpdatedAtUtc: null, error: null, unknownReason: null, trackingError: null };
}
export function createDataQualityQueue({ host, dataQualityApi, prepare = undefined, storageScope = undefined }) {
    if (!host)
        throw new TypeError('Data quality needs a host.');
    const root = host;
    surfaces.get(root)?.dispose();
    const scope = storageScope === undefined ? sessionScope() : storageScope;
    const storageKey = scope ? `${TRACKED_JOBS_KEY}:${encodeURIComponent(scope)}` : null;
    let storage = null;
    try {
        storage = sessionStorage;
        storage.removeItem(TRACKED_JOBS_KEY);
    }
    catch { /* Tracking remains usable when site storage is blocked. */ }
    const lifetime = new AbortController();
    const pollTimers = new Map();
    const polling = new Set();
    const copyTimers = new Set();
    const releaseDialogs = installDialogAccessibility(root);
    let disposed = false, shown = false, busy = false;
    let queueVersion = 0, rowsVersion = 0, trendVersion = 0, editVersion = 0;
    let queueRequest = null, rowsRequest = null, trendRequest = null;
    let opener = null;
    const state = {
        q: '', replayable: '', items: [], queueReady: false, loading: false, error: null, actionError: null,
        replayingTarget: null, message: null, selectedItem: null, rowStatus: 'quarantined', rows: null,
        rowsLoading: false, rowsError: null, edits: dictionary(), notes: dictionary(), rowAction: null, trendJob: null,
        trend: null, rules: [], trendLoading: false, trendError: null, rulesError: null, trackedJobs: [],
        allJobs: [], jobsError: null, unconfirmed: scope ? uncertainByScope.get(scope) ?? new Set() : new Set()
    };
    if (scope)
        uncertainByScope.set(scope, state.unconfirmed);
    const inFlight = scope ? inFlightByScope.get(scope) ?? new Set() : new Set();
    if (scope)
        inFlightByScope.set(scope, inFlight);
    function active() {
        if (disposed)
            return false;
        if (storageScope === undefined && sessionScope() !== scope) {
            dispose();
            return false;
        }
        return true;
    }
    function persist() {
        if (!storageKey)
            return;
        try {
            storage?.setItem(storageKey, JSON.stringify(state.trackedJobs.map(job => ({ jobId: job.jobId, kind: job.kind,
                jobName: job.jobName, target: job.target, rowId: job.rowId, createdAt: job.createdAt }))));
        }
        catch { /* A blocked or full store must not change the observed execution outcome. */ }
    }
    function restore() {
        if (!storageKey)
            return;
        try {
            const value = JSON.parse(storage?.getItem(storageKey) || '[]');
            const seen = new Set();
            state.trackedJobs = (Array.isArray(value) ? value : []).flatMap(item => {
                const job = restoredJob(item);
                if (!job || seen.has(job.jobId))
                    return [];
                seen.add(job.jobId);
                return [job];
            });
        }
        catch {
            state.trackedJobs = [];
        }
        state.trackedJobs.forEach(job => { void pollJob(job.jobId); });
    }
    function selector(element) {
        if (!element || !root.contains(element))
            return null;
        if (element.id)
            return `#${CSS.escape(element.id)}`;
        const keys = ['editRow', 'editColumn', 'noteRow', 'reviewTarget', 'replayTarget', 'jobName', 'trendJob', 'releaseRow', 'discardRow'];
        const attrs = keys.filter(key => element.dataset[key] !== undefined).map(key => `[data-${key.replace(/[A-Z]/g, letter => '-' + letter.toLowerCase())}="${CSS.escape(element.dataset[key])}"]`);
        return attrs.length ? element.tagName.toLowerCase() + attrs.join('') : null;
    }
    function render() {
        if (!active())
            return;
        prepare?.();
        const focus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        const focusSelector = selector(focus);
        const selection = focus instanceof HTMLInputElement && ['text', 'search'].includes(focus.type)
            ? [focus.selectionStart, focus.selectionEnd] : null;
        const template = document.createElement('template');
        template.innerHTML = renderQualityQueue(state);
        // Keep dialog nodes alive: the page's accessibility observer must not treat a status refresh
        // as a new modal and move focus away from an edited cell.
        for (const previous of root.querySelectorAll('[data-dq-dialog]')) {
            const next = template.content.querySelector(`[data-dq-dialog="${previous.dataset.dqDialog}"]`);
            if (next) {
                previous.innerHTML = next.innerHTML;
                next.replaceWith(previous);
            }
        }
        root.replaceChildren(template.content);
        if (focusSelector) {
            const next = root.querySelector(focusSelector);
            next?.focus();
            if (selection && next instanceof HTMLInputElement)
                next.setSelectionRange(...selection);
        }
    }
    function closePanels() {
        rowsVersion++;
        trendVersion++;
        rowsRequest?.abort();
        trendRequest?.abort();
        state.selectedItem = null;
        state.rows = null;
        state.edits = dictionary();
        state.notes = dictionary();
        editVersion++;
        state.trendJob = null;
        state.trend = null;
        state.rules = [];
        state.rowsError = null;
        state.trendError = null;
        state.rulesError = null;
        state.rowsLoading = false;
        state.trendLoading = false;
    }
    function close() {
        closePanels();
        render();
        if (opener)
            root.querySelector(opener)?.focus();
        opener = null;
    }
    function reconcileRows() {
        if (!state.selectedItem)
            return;
        const selectedKey = targetKey(state.selectedItem);
        const current = state.items.find(item => targetKey(item) === selectedKey);
        if (state.queueReady && current?.rowsReadable) {
            state.selectedItem = current;
            return;
        }
        rowsVersion++;
        rowsRequest?.abort();
        state.selectedItem = current ?? { ...state.selectedItem, rowsReadable: false };
        state.rows = null;
        state.rowsLoading = false;
        state.edits = dictionary();
        state.notes = dictionary();
        editVersion++;
        state.rowsError = current?.rowsUnavailableReason || 'The selected target is unavailable in the current queue. Refresh the queue before reloading rows.';
    }
    function schedulePoll(jobId) {
        if (!active() || pollTimers.has(jobId))
            return;
        pollTimers.set(jobId, setTimeout(() => { pollTimers.delete(jobId); void pollJob(jobId); }, 1000));
    }
    async function pollJob(jobId) {
        if (!active() || polling.has(jobId))
            return;
        const job = state.trackedJobs.find(item => item.jobId === jobId);
        if (!job || (job.verified && TERMINAL_JOB_STATUSES.has(job.status)))
            return;
        polling.add(jobId);
        try {
            const result = readQualityJobStatus(await dataQualityApi.jobStatus(jobId, lifetime.signal), jobId, job.kind, job.target);
            if (!active() || !state.trackedJobs.includes(job))
                return;
            const changed = !job.verified || job.status !== result.status || job.error !== (result.error ?? null)
                || job.unknownReason !== (result.unknownReason ?? null) || !!job.trackingError;
            job.status = result.status;
            job.verified = true;
            job.statusUpdatedAtUtc = result.statusUpdatedAtUtc;
            job.error = result.error ?? null;
            job.unknownReason = result.unknownReason ?? null;
            job.trackingError = null;
            if (changed)
                render();
            if (result.isTerminal) {
                if (result.status === 'Unknown')
                    state.unconfirmed.add(targetKey({ jobName: job.jobName, quarantineTarget: job.target }));
                state.message = result.status === 'Unknown'
                    ? `${job.kind} job ${jobId}: execution outcome unknown. Inspect job history before resubmitting.`
                    : `${job.kind} job ${jobId} ${result.status.toLowerCase()} for ${job.target}.`;
                // A poll does not discard another row's unsaved changes or refresh a different target.
                if (job.kind === 'Disposition' && result.status === 'Completed' && state.selectedItem
                    && state.selectedItem.quarantineTarget === job.target && state.selectedItem.jobName === job.jobName) {
                    state.message += ' Reload rows to inspect the result; current edits are retained.';
                }
                render();
            }
            else
                schedulePoll(jobId);
        }
        catch (reason) {
            if (!active() || !state.trackedJobs.includes(job))
                return;
            job.trackingError = asError(reason).message;
            job.verified = false;
            render();
            schedulePoll(jobId);
        }
        finally {
            polling.delete(jobId);
        }
    }
    function trackJob(jobId, kind, item, id) {
        // Keep all in-flight submissions. Only old confirmed terminal entries can be evicted.
        const retained = state.trackedJobs.filter(job => job.jobId !== jobId);
        while (retained.length >= 20) {
            let terminal = retained.length - 1;
            while (terminal >= 0 && !(retained[terminal].verified && TERMINAL_JOB_STATUSES.has(retained[terminal].status)))
                terminal--;
            if (terminal < 0)
                break;
            retained.splice(terminal, 1);
        }
        state.trackedJobs = [{ jobId, kind, jobName: item.jobName, target: item.quarantineTarget, rowId: id,
                status: 'Submitted', createdAt: new Date().toISOString(), verified: false, statusUpdatedAtUtc: null,
                error: null, unknownReason: null, trackingError: null }, ...retained];
        persist();
        render();
        void pollJob(jobId);
    }
    async function load(explicit = false, deepTarget = null, deepJob = null) {
        if (!active())
            return;
        const version = ++queueVersion;
        queueRequest?.abort();
        const request = queueRequest = new AbortController();
        state.loading = true;
        state.queueReady = false;
        state.items = [];
        state.error = null;
        state.jobsError = null;
        render();
        const [queue, jobs] = await Promise.allSettled([
            dataQualityApi.quarantineQueue({ q: state.q.trim(), replayable: state.replayable, limit: 100 }, request.signal),
            dataQualityApi.qualityJobs(request.signal)
        ]);
        if (!active() || version !== queueVersion)
            return;
        try {
            if (queue.status === 'rejected')
                throw queue.reason;
            state.items = readQualityQueue(queue.value);
            state.queueReady = true;
            if (explicit) {
                for (const key of state.unconfirmed)
                    if (!inFlight.has(key))
                        state.unconfirmed.delete(key);
                state.actionError = null;
            }
        }
        catch (reason) {
            state.items = [];
            state.error = asError(reason).message;
        }
        try {
            if (jobs.status === 'rejected')
                throw jobs.reason;
            state.allJobs = readQualityJobs(jobs.value);
        }
        catch (reason) {
            state.allJobs = [];
            state.jobsError = asError(reason).message;
        }
        reconcileRows();
        state.loading = false;
        render();
        if (state.queueReady && deepTarget) {
            const exact = state.items.find(item => item.quarantineTarget === deepTarget && (!deepJob || item.jobName === deepJob));
            if (exact?.rowsReadable)
                await loadRows(exact);
        }
        if (state.queueReady && (deepTarget || deepJob)) {
            const url = new URL(window.location.href);
            for (const key of ['q', 'target', 'jobName', 'job'])
                url.searchParams.delete(key);
            history.replaceState(history.state, '', `${url.pathname}${url.search}${url.hash}`);
        }
    }
    async function loadRows(item = state.selectedItem, status = state.rowStatus) {
        if (!active() || !state.queueReady || !item?.rowsReadable)
            return;
        const selectedKey = targetKey(item);
        const current = state.items.find(value => targetKey(value) === selectedKey);
        if (!current?.rowsReadable)
            return;
        item = current;
        if (!state.selectedItem || targetKey(item) !== targetKey(state.selectedItem))
            closePanels();
        const version = ++rowsVersion;
        rowsRequest?.abort();
        const request = rowsRequest = new AbortController();
        state.selectedItem = item;
        state.rowStatus = status;
        state.rows = null;
        state.edits = dictionary();
        state.notes = dictionary();
        editVersion++;
        state.rowsLoading = true;
        state.rowsError = null;
        state.actionError = null;
        render();
        try {
            const result = await dataQualityApi.quarantineRows({ quarantineTarget: item.quarantineTarget, jobName: item.jobName, status, limit: 50 }, request.signal);
            if (!active() || version !== rowsVersion)
                return;
            state.rows = readQualityRows(result, item.quarantineTarget, status);
        }
        catch (reason) {
            if (active() && version === rowsVersion)
                state.rowsError = asError(reason).message;
        }
        finally {
            if (active() && version === rowsVersion) {
                state.rowsLoading = false;
                render();
            }
        }
    }
    async function loadTrend(jobName) {
        if (!active() || !jobName)
            return;
        closePanels();
        const version = ++trendVersion;
        const request = trendRequest = new AbortController();
        state.trendJob = jobName;
        state.trendLoading = true;
        render();
        const [trend, rules] = await Promise.allSettled([
            dataQualityApi.qualityTrend({ jobName }, request.signal), dataQualityApi.qualityRules(jobName, request.signal)
        ]);
        if (!active() || version !== trendVersion)
            return;
        try {
            if (trend.status === 'rejected')
                throw trend.reason;
            state.trend = readQualityTrend(trend.value, jobName);
        }
        catch (reason) {
            state.trendError = asError(reason).message;
        }
        try {
            if (rules.status === 'rejected')
                throw rules.reason;
            state.rules = readQualityRules(rules.value, jobName);
        }
        catch (reason) {
            state.rulesError = asError(reason).message;
        }
        state.trendLoading = false;
        render();
    }
    function knownRejection(reason) {
        return isObject(reason) && [400, 401, 403, 404, 409].includes(Number(reason.status));
    }
    async function submit(item, kind, row = null, disposition = '') {
        if (!active() || busy || !state.queueReady || pendingTarget(state, item) || (kind === 'Replay' && !item.isReplayable))
            return;
        const current = state.items.find(value => targetKey(value) === targetKey(item));
        if (!current || (kind === 'Replay' && !current.isReplayable) || (kind === 'Disposition' && !current.rowsReadable))
            return;
        item = current;
        const id = row ? rowId(row) : null;
        if (kind === 'Disposition' && (!row || !id || !['quarantined', 'released', 'replaying'].includes(rowStatus(row))
            || !['released', 'discarded', 'replayed'].includes(disposition) || (disposition === 'released' && !item.isReplayable)))
            return;
        const panelVersion = rowsVersion, inputVersion = editVersion;
        const body = { quarantineTarget: item.quarantineTarget, jobName: item.jobName,
            rowIds: id ? [id] : [], disposition, changes: disposition === 'released' && id && row && rowStatus(row) !== 'replaying' ? { ...state.edits[id] } : null,
            note: id ? state.notes[id] ?? null : null, expectedStatus: row ? rowStatus(row) : 'quarantined' };
        busy = true;
        state.message = null;
        state.actionError = null;
        const key = targetKey(item);
        inFlight.add(key);
        state.unconfirmed.add(key);
        if (kind === 'Replay')
            state.replayingTarget = targetKey(item);
        else
            state.rowAction = id;
        render();
        try {
            const value = kind === 'Replay'
                ? await dataQualityApi.replayQuarantine(item.quarantineTarget, item.jobName, lifetime.signal)
                : await dataQualityApi.updateQuarantineDisposition(body, lifetime.signal);
            if (!active())
                return;
            const result = readQualitySubmission(value, kind, item.replayStatement);
            if (state.trackedJobs.some(job => job.jobId === result.jobId))
                throw new TypeError('Submission acknowledgement reused an existing job id.');
            state.unconfirmed.delete(key);
            if (kind === 'Replay' || (panelVersion === rowsVersion && inputVersion === editVersion))
                state.message = `${kind} job ${result.jobId} submitted; tracking durable execution status.`;
            trackJob(result.jobId, kind, item, id);
        }
        catch (reason) {
            if (knownRejection(reason))
                state.unconfirmed.delete(key);
            if (!active())
                return;
            if (knownRejection(reason))
                state.actionError = asError(reason).message;
            else {
                state.unconfirmed.add(targetKey(item));
                state.actionError = `${kind} outcome is unconfirmed. Inspect job history and refresh the queue before retrying.`;
            }
        }
        finally {
            inFlight.delete(key);
            busy = false;
            state.replayingTarget = null;
            state.rowAction = null;
            if (active())
                render();
        }
    }
    function rememberOpener(button) { opener = selector(button); }
    function click(event) {
        if (!active() || !(event.target instanceof Element))
            return;
        const button = event.target.closest('button');
        if (!button || !root.contains(button) || button.disabled)
            return;
        const id = button.id;
        if (id === 'dqRefreshBtn') {
            void load(true);
            return;
        }
        if (id === 'dqTrendClose' || id === 'dqCloseRows') {
            close();
            return;
        }
        if (id === 'dqReloadRows') {
            void loadRows();
            return;
        }
        if (id === 'dqJobLookupBtn') {
            const jobName = root.querySelector('#dqJobLookupSelect')?.value;
            if (jobName) {
                rememberOpener(button);
                void loadTrend(jobName);
            }
            return;
        }
        if (button.dataset.trendJob) {
            rememberOpener(button);
            void loadTrend(button.dataset.trendJob);
            return;
        }
        const item = state.items.find(value => value.jobName === button.dataset.jobName
            && value.quarantineTarget === (button.dataset.replayTarget ?? button.dataset.reviewTarget));
        if (item && button.dataset.replayTarget) {
            void submit(item, 'Replay');
            return;
        }
        if (item && button.dataset.reviewTarget) {
            rememberOpener(button);
            void loadRows(item);
            return;
        }
        const row = state.rows?.rows.find(value => rowId(value) === (button.dataset.releaseRow ?? button.dataset.discardRow));
        if (row && state.selectedItem) {
            void submit(state.selectedItem, 'Disposition', row, button.dataset.releaseRow ? 'released' : button.dataset.disposition ?? 'discarded');
            return;
        }
        if (button.dataset.copyReplay || button.dataset.copyReview)
            void copy(button);
    }
    async function copy(button) {
        const label = button.textContent;
        try {
            if (!navigator.clipboard?.writeText)
                throw new Error('Clipboard is unavailable.');
            await navigator.clipboard.writeText(button.dataset.copyReplay ?? button.dataset.copyReview ?? '');
            if (!active() || !button.isConnected)
                return;
            button.textContent = 'Copied';
            const timer = setTimeout(() => { copyTimers.delete(timer); if (active() && button.isConnected)
                button.textContent = label; }, 1200);
            copyTimers.add(timer);
        }
        catch (reason) {
            if (active()) {
                state.actionError = asError(reason).message;
                render();
            }
        }
    }
    function input(event) {
        if (!active() || !(event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement))
            return;
        const element = event.target;
        if (!root.contains(element))
            return;
        if (element.id === 'dqQueueSearch')
            state.q = element.value;
        if (element.id === 'dqReplayableFilter')
            state.replayable = element.value;
        const id = element.dataset.editRow, column = element.dataset.editColumn;
        const row = id ? state.rows?.rows.find(value => rowId(value) === id) : null;
        if (id && column && row && ['quarantined', 'released'].includes(rowStatus(row))
            && !column.toLowerCase().startsWith('__dq_') && state.rows?.columns.includes(column)) {
            (state.edits[id] ??= dictionary())[column] = element.value;
            editVersion++;
        }
        const noteId = element.dataset.noteRow;
        if (noteId && state.rows?.rows.some(value => rowId(value) === noteId)) {
            state.notes[noteId] = element.value;
            editVersion++;
        }
    }
    function change(event) {
        input(event);
        if (!(event.target instanceof HTMLSelectElement))
            return;
        if (event.target.id === 'dqRowsStatus' && ROW_STATUSES.includes(event.target.value))
            void loadRows(undefined, event.target.value);
        if (event.target.id === 'dqJobLookupSelect' && event.target.value) {
            rememberOpener(event.target);
            void loadTrend(event.target.value);
        }
    }
    function apply(event) { if (event.target instanceof HTMLFormElement && event.target.id === 'dqQueueForm') {
        event.preventDefault();
        void load();
    } }
    root.addEventListener('click', click);
    root.addEventListener('input', input);
    root.addEventListener('change', change);
    root.addEventListener('submit', apply);
    function dispose() {
        if (disposed)
            return;
        disposed = true;
        lifetime.abort();
        queueRequest?.abort();
        rowsRequest?.abort();
        trendRequest?.abort();
        pollTimers.forEach(timer => clearTimeout(timer));
        pollTimers.clear();
        copyTimers.forEach(timer => clearTimeout(timer));
        copyTimers.clear();
        releaseDialogs?.();
        root.removeEventListener('click', click);
        root.removeEventListener('input', input);
        root.removeEventListener('change', change);
        root.removeEventListener('submit', apply);
        state.edits = dictionary();
        state.notes = dictionary();
        root.replaceChildren();
        if (surfaces.get(root) === surface)
            surfaces.delete(root);
    }
    const surface = {
        async show() {
            if (!active())
                return;
            if (shown) {
                await load();
                return;
            }
            shown = true;
            const params = new URLSearchParams(window.location.search);
            const target = params.get('target') ?? params.get('q'), job = params.get('jobName') ?? params.get('job');
            state.q = target ?? job ?? '';
            restore();
            await load(false, target, job);
        }, dispose
    };
    surfaces.set(root, surface);
    return surface;
}
