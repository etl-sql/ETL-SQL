import { apiFetch, responseError } from './portal-http.js';
import { asError } from './http-error.js';
import { auth, reportsApi } from './api.js';
import { renderDependencies } from './lineage-ui.js';
import { escapeHtml as esc, formatBuiltAt } from './report-format.js';
import { readSubscriptionParameters } from './admin-subscriptions-responses.js';
import { readParameterDraft, renderParameterInputs } from './subscription-parameter-fields.js';
import type { ParameterInput } from './subscription-parameter-fields.js';
import { readAcceptedRun, readRunState, readViewerReport, readViewerSnapshot, renderFirstRunView, renderLiveView, renderReportHistory,
    renderRunFailureView, renderRunningView, renderSnapshotView } from './report-viewer-view.js';
import type { ViewerReport } from './report-viewer-view.js';

/**
 * The report viewer on the catalog page: open a report, run or refresh it and wait for the result,
 * and the structure, export, dependency, and history dialogs.
 *
 * Everything belongs to the report that is open. Opening another report or another view ends the
 * wait for a run, so a run that finishes later cannot pull the reader back to a page they left.
 * A wait that keeps losing contact with the server says so instead of reporting a timeout minutes on.
 */
interface DagHandle { dispose(): void }
export interface ReportViewerOptions {
    root: Document;
    canDesign(): boolean;
    notify(message: string, options: { title: string; tone: 'success' | 'error' }): void;
    renderDag(container: HTMLElement, graph: unknown): DagHandle;
    subscribe(reportId: number): void;
    /** Leaves the viewer for the library it was opened from. */
    back(): void;
    /** The viewer took over the main area for this report. */
    entered(reportId: number, push: boolean, page: string | null): void;
    /** The report inside the frame moved to another page. */
    pageChanged(reportId: number, page: string, userTriggered: boolean): void;
    pollMs?: number;
}

const MAX_POLLS = 120, MAX_CONSECUTIVE_POLL_FAILURES = 5;

async function readJson(url: string, fallback: string, signal: AbortSignal): Promise<unknown> {
    const response = await apiFetch(url, { signal, cache: 'no-store' });
    if (response.status !== 200) throw await responseError(response, fallback);
    return response.json();
}

/**
 * A complete document for a report's `srcdoc` frame, used by the viewer and by library previews.
 *
 * The frame inherits this page's CSP, so its scripts must carry the nonce this page was served with;
 * `getAttribute('nonce')` is empty once CSP is active, and the property still answers. Its fetch
 * adds the session token to Portal API calls. The frame is its own window, so it loads the feedback
 * module itself: the runtime's saved-view prompt, confirmations and toasts all go through it.
 */
export function buildReportFrameDocument(root: Document, id: number, preview: boolean, initialPage: string | null = null): string {
    const nonce = root.querySelector<HTMLScriptElement>('script[nonce]')?.nonce ?? '';
    const token = (auth.getToken() || '').replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    const dark = root.body.classList.contains('theme-dark');
    return `<!DOCTYPE html><html><head><meta charset="UTF-8">
<link rel="stylesheet" href="/css/report-runtime.css?v=0.18.0">
<script nonce="${nonce}" src="/js/feedback.js?v=0.18.0"></script>
<script nonce="${nonce}">
  window.__IS_WEB__ = true; window.__IS_PREVIEW__ = ${preview}; window.__API_BASE__ = '/api/reports/${Number(id)}';
  ${initialPage ? `window.__INITIAL_PAGE__ = ${JSON.stringify(initialPage).replace(/</g, '\\u003c')};` : ''}
  const _f = window.fetch.bind(window);
  window.fetch = (input, init={}) => {
    const url = typeof input === 'string' ? input : input.url;
    if (!url.startsWith('/api/')) return _f(input, init);
    const h = new Headers(init.headers||{});
    if (!h.has('Authorization')) h.set('Authorization', 'Bearer ${token}');
    return _f(input, {...init, headers:h});
  };
</script></head><body style="margin:0" class="${dark ? 'theme-dark' : ''}"><div id="root"></div>
<script nonce="${nonce}" type="module" src="/js/report-runtime.js?v=0.18.0"></script></body></html>`;
}

export function createReportViewer(options: ReportViewerOptions) {
    const { root } = options;
    const lifetime = new AbortController();
    const box = (id: string): HTMLElement => {
        const found = root.getElementById(id);
        if (!found) throw new Error(`Missing report viewer element: ${id}`);
        return found;
    };
    const main = box('mainContent');
    const pollMs = options.pollMs ?? 1500;
    let view = new AbortController();
    let report: ViewerReport | null = null;
    let activePage: string | null = null;
    let runInputs: ParameterInput[] | null = null;
    let structure: DagHandle | null = null;
    let exporting = false;

    view.abort();
    const live = (opened: AbortController) => !lifetime.signal.aborted && opened === view && !opened.signal.aborted;
    const pause = (ms: number, signal: AbortSignal) => new Promise<void>(resolve => {
        const timer = setTimeout(resolve, ms);
        signal.addEventListener('abort', () => { clearTimeout(timer); resolve(); }, { once: true });
    });

    function enter(): AbortController {
        view.abort();
        closeDialogs();
        return view = new AbortController();
    }

    function showSnapshot(current: ViewerReport, snapshot: Parameters<typeof renderSnapshotView>[1]) {
        main.innerHTML = renderSnapshotView(current, snapshot, options.canDesign());
        const frame = root.createElement('iframe');
        frame.title = 'Report viewer';
        frame.style.cssText = 'width:100%;height:100%;border:none;';
        frame.srcdoc = buildReportFrameDocument(root, current.id, false, activePage);
        box('reportFrame').replaceChildren(frame);
    }

    /** A report whose rows depend on the reader has no snapshot: the frame asks for it and it runs for them. */
    function showLive(current: ViewerReport) {
        main.innerHTML = renderLiveView(current, options.canDesign());
        const frame = root.createElement('iframe');
        frame.title = 'Report viewer';
        frame.style.cssText = 'width:100%;height:100%;border:none;';
        frame.srcdoc = buildReportFrameDocument(root, current.id, false, activePage);
        box('reportFrame').replaceChildren(frame);
    }

    /** Runs the report again for this reader, then shows it. Nothing is built or stored. */
    async function refreshLive(current: ViewerReport) {
        const opened = enter();
        report = current; runInputs = null;
        main.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading report…</span></div>';
        try {
            const response = await apiFetch(`/api/reports/${current.id}/live/reset`, { method: 'POST', signal: opened.signal });
            if (response.status !== 204) throw await responseError(response, 'The report could not be refreshed.');
            if (live(opened)) showLive(current);
        } catch (failure) {
            if (live(opened)) main.innerHTML = `<div class="empty-state" role="alert">Failed to refresh report: ${esc(asError(failure).message)}</div>`;
        }
    }

    /** A report with no snapshot offers its first run; its parameters must be known before it can start. */
    async function showFirstRun(current: ViewerReport, opened: AbortController) {
        let definitions: ReportParameterDto[] = [], preflight: string | null = null;
        try {
            definitions = readSubscriptionParameters(await readJson(`/api/reports/${current.id}/parameters`,
                'The report parameters are unavailable.', opened.signal));
        } catch (failure) {
            preflight = asError(failure).message;
        }
        if (!live(opened)) return;
        main.innerHTML = renderFirstRunView(current, definitions.length > 0, preflight, options.canDesign());
        runInputs = preflight ? null : renderParameterInputs(root, box('runParameterFields'), definitions, {}, `run-parameter-${current.id}`, opened.signal);
    }

    async function open(id: number, push = true, initialPage: string | null = null) {
        if (lifetime.signal.aborted) return;
        const opened = enter();
        report = null; runInputs = null;
        if (initialPage) activePage = initialPage;
        options.entered(id, push, activePage);
        main.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading report…</span></div>';
        try {
            const current = readViewerReport(await reportsApi.get(id), id);
            if (!live(opened)) return;
            report = current;
            if (current.perViewer) { showLive(current); return; }
            const response = await apiFetch(`/api/reports/${id}/snapshot?includeManifest=false`, { signal: opened.signal, cache: 'no-store' });
            if (!live(opened)) return;
            if (response.status === 404) { await showFirstRun(current, opened); return; }
            if (response.status !== 200) throw await responseError(response, 'The report snapshot is unavailable.');
            const snapshot = readViewerSnapshot(await response.json());
            if (live(opened)) showSnapshot(current, snapshot);
        } catch (failure) {
            if (live(opened)) main.innerHTML = `<div class="empty-state" role="alert">Failed to load report: ${esc(asError(failure).message)}</div>`;
        }
    }

    async function run(current: ViewerReport, parameters: Record<string, string>) {
        const opened = enter();
        report = current; runInputs = null;
        const failed = (message: string) => { if (live(opened)) main.innerHTML = renderRunFailureView(current, message); };
        main.innerHTML = renderRunningView(current);
        let jobId: string;
        try {
            jobId = readAcceptedRun(await reportsApi.execute(current.id, parameters));
        } catch (failure) {
            failed(`Failed to start job: ${asError(failure).message}`);
            return;
        }
        let misses = 0;
        for (let attempt = 0; attempt < MAX_POLLS; attempt++) {
            await pause(pollMs, opened.signal);
            // Leaving the view ends the wait. The run continues on the server; its snapshot is there on return.
            if (!live(opened)) return;
            try {
                const state = readRunState(await reportsApi.pollJob(jobId), jobId);
                if (!live(opened)) return;
                misses = 0;
                if (state.status === 'Completed') { await open(current.id, false); return; }
                if (state.status !== 'Running') { failed(state.reason); return; }
            } catch (failure) {
                if (!live(opened)) return;
                if (++misses >= MAX_CONSECUTIVE_POLL_FAILURES) {
                    failed(`Lost contact with the run: ${asError(failure).message} It may still finish; open the report again to check.`);
                    return;
                }
            }
        }
        failed('Timed out waiting for report execution.');
    }

    // ── Dialogs ───────────────────────────────────────────────────────────────
    function closeDialogs() {
        for (const id of ['structureModal', 'exportModal', 'dependenciesModal', 'historyModal']) box(id).style.display = 'none';
        structure?.dispose(); structure = null;
    }

    /** Opens a dialog and fills it from one read, unless the viewer has moved on or the dialog was closed. */
    async function dialog(modalId: string, bodyId: string, label: string, load: (signal: AbortSignal) => Promise<(body: HTMLElement) => void>) {
        const opened = view, modal = box(modalId), body = box(bodyId);
        if (!live(opened)) return;
        body.innerHTML = `<div class="loading-state"><span class="spinner"></span><span>Loading ${label}…</span></div>`;
        modal.style.display = 'flex';
        const shown = () => live(opened) && modal.style.display !== 'none';
        try {
            const fill = await load(opened.signal);
            if (shown()) fill(body);
        } catch (failure) {
            if (shown()) body.innerHTML = `<div class="empty-state" role="alert">Failed to load ${label}: ${esc(asError(failure).message)}</div>`;
        }
    }

    function showStructure(id: number) {
        structure?.dispose(); structure = null;
        return dialog('structureModal', 'structureDag', 'structure', async signal => {
            const graph = await readJson(`/api/reports/${id}/structure`, 'The report structure is unavailable.', signal);
            return body => { body.replaceChildren(); structure = options.renderDag(body, graph); };
        });
    }

    function showDependencies(id: number) {
        return dialog('dependenciesModal', 'dependenciesBody', 'dependencies', async signal => {
            const data = await readJson(`/api/reports/${id}/dependencies`, 'The report dependencies are unavailable.', signal) as Parameters<typeof renderDependencies>[0];
            const sources = Array.isArray(data?.sources) ? data.sources : [];
            const names = [...new Set(sources.map(source => source.name || source.objectName).filter((name): name is string => !!name))].slice(0, 10);
            const lookups = await Promise.allSettled(names.map(name =>
                readJson(`/api/catalog/lineage/downstream?table=${encodeURIComponent(name)}`, 'Downstream lineage is unavailable.', signal)));
            const newest = new Map<string | number, DownstreamReportDto>();
            let unavailable = 0;
            for (const lookup of lookups) {
                if (lookup.status === 'rejected' || !Array.isArray(lookup.value)) { unavailable++; continue; }
                for (const item of lookup.value as DownstreamReportDto[]) {
                    const key = item.reportId ?? item.reportName;
                    const seen = key ? newest.get(key) : undefined;
                    if (key && (!seen || seen.lastSeen < item.lastSeen)) newest.set(key, item);
                }
            }
            const downstream = [...newest.values()].sort((a, b) => Date.parse(b.lastSeen) - Date.parse(a.lastSeen));
            // A lookup that failed is not "nothing downstream": say how much of the answer is missing.
            const warning = unavailable ? `<div class="status-banner status-banner-warning" role="alert"><span>Downstream reports could not be read for
                ${unavailable} of ${names.length} source${names.length === 1 ? '' : 's'}; the list below may be incomplete.</span></div>` : '';
            return body => { body.innerHTML = warning + renderDependencies(data, downstream, { formatBuiltAt }); };
        });
    }

    function showHistory(id: number) {
        return dialog('historyModal', 'historyBody', 'history', async signal => {
            const markup = renderReportHistory(await readJson(`/api/reports/${id}/history`, 'The report history is unavailable.', signal));
            return body => { body.innerHTML = markup; };
        });
    }

    async function exportAs(format: 'csv' | 'xlsx' | 'pdf') {
        const current = report;
        if (!current || exporting) return;
        exporting = true;
        box('exportModal').style.display = 'none';
        try {
            await reportsApi.exportFile(current.id, format);
        } catch (failure) {
            if (!lifetime.signal.aborted) options.notify(asError(failure).message || 'Export failed.', { title: 'Export failed', tone: 'error' });
        } finally {
            exporting = false;
        }
    }

    const listen = (id: string, handler: () => void) => box(id).addEventListener('click', handler, { signal: lifetime.signal });
    listen('structureCloseBtn', () => { box('structureModal').style.display = 'none'; structure?.dispose(); structure = null; });
    listen('dependenciesCloseBtn', () => { box('dependenciesModal').style.display = 'none'; });
    listen('historyCloseBtn', () => { box('historyModal').style.display = 'none'; });
    listen('exportCancelBtn', () => { box('exportModal').style.display = 'none'; });
    listen('exportCsvBtn', () => { void exportAs('csv'); });
    listen('exportXlsxBtn', () => { void exportAs('xlsx'); });
    listen('exportPdfBtn', () => { void exportAs('pdf'); });
    box('dependenciesModal').addEventListener('click', event => {
        const link = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-downstream-report-id]') : null;
        const id = Number(link?.dataset.downstreamReportId);
        if (!link) return;
        event.preventDefault();
        if (Number.isSafeInteger(id) && id > 0) void open(id);
    }, { signal: lifetime.signal });

    main.addEventListener('click', event => {
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button[data-viewer-action]') : null;
        const current = report;
        if (!button || button.disabled || view.signal.aborted) return;
        const name = button.dataset.viewerAction;
        if (name === 'back') { options.back(); return; }
        if (!current) return;
        if (name === 'refresh' && current.perViewer) void refreshLive(current);
        else if (name === 'refresh' || name === 'retryRun') void run(current, {});
        else if (name === 'run') {
            if (!runInputs) return;
            const draft = readParameterDraft(runInputs), error = box('runParameterError');
            if ('missing' in draft) {
                error.textContent = `${draft.missing.parameter.name} requires a value.`; error.classList.add('show'); draft.missing.input.focus();
                return;
            }
            void run(current, { ...draft.values });
        }
        else if (name === 'design') window.location.href = `/designer.html?id=${current.id}`;
        else if (name === 'structure') void showStructure(current.id);
        else if (name === 'dependencies') void showDependencies(current.id);
        else if (name === 'history') void showHistory(current.id);
        else if (name === 'export') box('exportModal').style.display = 'flex';
        else if (name === 'sub') options.subscribe(current.id);
    }, { signal: lifetime.signal });

    // The report frame reports the page it is showing, so a refresh or Back returns to it.
    window.addEventListener('message', event => {
        const data: unknown = event.data;
        if (view.signal.aborted || !report || data === null || typeof data !== 'object') return;
        const message = data as { type?: unknown; page?: unknown; userTriggered?: unknown };
        if (message.type !== 'etl-page-changed' || typeof message.page !== 'string') return;
        activePage = message.page;
        options.pageChanged(report.id, message.page, message.userTriggered === true);
    }, { signal: lifetime.signal });

    return {
        open,
        get activePage() { return activePage; },
        set activePage(page: string | null) { activePage = page; },
        /** Another view owns the main area now; any wait or read for this report ends here. */
        leave() { view.abort(); report = null; runInputs = null; closeDialogs(); },
        dispose() { view.abort(); closeDialogs(); lifetime.abort(); }
    };
}
