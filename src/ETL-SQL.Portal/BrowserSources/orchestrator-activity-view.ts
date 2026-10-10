import { asError } from './http-error.js';
import { createActivityLoader } from './orchestrator-activity.js';
import type { ActivityFetcher, ActivityFilter, ActivityRequest } from './orchestrator-activity.js';

interface ViewOptions {
    root: Document | HTMLElement;
    fetchPage: ActivityFetcher;
    renderPage: (page: ScheduledJobActivityPage) => void;
    clearPage: () => void;
    onUnavailable?: () => void;
}
const chips = [
    ['active', 'activeFilterBtn', 'activeCount', 'Active'],
    ['queued', 'queuedFilterBtn', 'queuedCount', 'Queued'],
    ['completedToday', 'completedTodayFilterBtn', 'completedCount', 'Done today'],
    ['failedToday', 'failedTodayFilterBtn', 'failedCount', 'Failed today']
] as const;

export function createScheduledJobActivityView({ root, fetchPage, renderPage, clearPage, onUnavailable }: ViewOptions) {
    function element<T extends HTMLElement>(id: string): T {
        const node = root.querySelector<T>(`#${id}`);
        if (!node) throw new Error(`Missing scheduled-job activity element: ${id}`);
        return node;
    }
    const table = element('jobsTableWrap');
    const search = element<HTMLInputElement>('jobsSearchInput');
    const clearSearch = element<HTMLButtonElement>('jobsSearchClearBtn');
    const status = element<HTMLSelectElement>('jobsStatusSelect');
    const size = element<HTMLSelectElement>('jobsPageSizeSelect');
    const previous = element<HTMLButtonElement>('jobsPrevPageBtn');
    const next = element<HTMLButtonElement>('jobsNextPageBtn');
    const filterActions = element('jobsFilterActions');
    const pagination = element('jobsPaginationBar');
    const loader = createActivityLoader(fetchPage);
    const listeners = new AbortController();
    const request: ActivityRequest = { filter: 'all', status: 'all', search: '', limit: Number(size.value) || 25, offset: 0 };
    let disposed = false;
    let generation = 0;
    let loading = false;
    let total = 0;

    function selection() {
        for (const [filter, id, , label] of chips) {
            const button = element<HTMLButtonElement>(id);
            button.disabled = false;
            button.setAttribute('aria-pressed', String(request.filter === filter));
            button.classList.toggle('active-filter', request.filter === filter);
            button.title = `Show ${label.toLowerCase()} jobs${filter.endsWith('Today') ? ' since 00:00 UTC' : ''}`;
        }
        const label = chips.find(([filter]) => filter === request.filter)?.[3];
        element('jobsSectionKicker').textContent = label ?? 'All jobs';
        element('jobsSectionTitle').textContent = label && !loading ? `${total} Scheduled ${total === 1 ? 'Job' : 'Jobs'}` : 'Scheduled Jobs';
        element('jobsFilterLabel').textContent = label ? `${label} filter` : '';
        filterActions.style.display = label ? '' : 'none';
    }
    function clearCounts() {
        for (const [, , id] of chips) element(id).textContent = '—';
    }
    async function refresh({ background = false }: { background?: boolean } = {}): Promise<void> {
        if (disposed) return;
        const current = ++generation;
        loading = true;
        table.setAttribute('aria-busy', 'true');
        if (!background) {
            pagination.style.display = 'none';
            clearPage();
            const pendingMessage = document.createElement('div');
            pendingMessage.className = 'loading-state loading-state-compact';
            pendingMessage.textContent = 'Loading jobs…';
            table.replaceChildren(pendingMessage);
        }
        selection();
        try {
            const page = await loader.load({ ...request });
            if (!page || disposed || current !== generation) return;
            const finalOffset = Math.max(0, (Math.ceil(page.total / request.limit) - 1) * request.limit);
            if (request.offset > finalOffset) {
                request.offset = finalOffset;
                await refresh();
                return;
            }
            total = page.total;
            loading = false;
            for (const [filter, , id] of chips) element(id).textContent = String(page.counts[filter]);
            selection();
            pagination.style.display = total ? 'flex' : 'none';
            element('jobsPaginationInfo').textContent = `Showing ${total ? page.offset + 1 : 0}–${page.offset + page.items.length} of ${total} jobs`;
            element('jobsPageIndicator').textContent = `${Math.floor(page.offset / page.limit) + 1} / ${Math.max(1, Math.ceil(total / page.limit))}`;
            previous.disabled = page.offset === 0;
            next.disabled = page.offset + page.limit >= total;
            renderPage(page);
        } catch (error) {
            if (disposed || current !== generation) return;
            total = 0;
            clearCounts();
            clearPage();
            onUnavailable?.();
            pagination.style.display = 'none';
            selection();
            const message = document.createElement('div');
            message.className = 'empty-state';
            message.setAttribute('role', 'alert');
            message.textContent = `Failed to load jobs: ${asError(error).message}`;
            table.replaceChildren(message);
        } finally {
            if (!disposed && current === generation) {
                loading = false;
                table.setAttribute('aria-busy', 'false');
            }
        }
    }
    function bind(node: HTMLElement, event: string, action: () => void) {
        node.addEventListener(event, action, { signal: listeners.signal });
    }
    for (const [filter, id] of chips) {
        bind(element(id), 'click', () => { request.filter = filter; request.offset = 0; void refresh(); });
    }
    bind(element('clearJobsFilterBtn'), 'click', () => { request.filter = 'all'; request.offset = 0; void refresh(); });
    bind(search, 'input', () => {
        request.search = search.value; request.offset = 0;
        clearSearch.style.display = search.value ? '' : 'none'; void refresh();
    });
    bind(clearSearch, 'click', () => {
        search.value = ''; request.search = ''; request.offset = 0;
        clearSearch.style.display = 'none'; search.focus(); void refresh();
    });
    bind(status, 'change', () => {
        const value = status.value;
        if (value !== 'all' && value !== 'enabled' && value !== 'disabled') return;
        request.status = value; request.offset = 0; void refresh();
    });
    bind(size, 'change', () => {
        const limit = Number(size.value);
        if (!Number.isSafeInteger(limit) || limit < 1 || limit > 1000) return;
        request.limit = limit; request.offset = 0; void refresh();
    });
    bind(previous, 'click', () => { request.offset = Math.max(0, request.offset - request.limit); void refresh(); });
    bind(next, 'click', () => { if (request.offset + request.limit < total) { request.offset += request.limit; void refresh(); } });
    clearCounts();
    selection();
    return {
        refresh,
        get loading() { return loading; },
        get filter(): ActivityFilter { return request.filter; },
        dispose() { disposed = true; generation++; listeners.abort(); loader.dispose(); }
    };
}
