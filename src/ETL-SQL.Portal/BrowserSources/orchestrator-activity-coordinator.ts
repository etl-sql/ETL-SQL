import { asError, isObject } from './http-error.js';
import { loadScheduledJobCatalog } from './orchestrator-activity.js';
import { createScheduledJobActivityView } from './orchestrator-activity-view.js';

interface CoordinatorOptions {
    view: Omit<Parameters<typeof createScheduledJobActivityView>[0], 'onUnavailable'>;
    status: (signal: AbortSignal) => Promise<unknown>;
    onlineChanged: (online: boolean) => void;
    renderCatalog: (jobs: JobDefinition[]) => void;
    clearCatalog: () => void;
    catalogError: (error: Error) => void;
}

/** Coordinate the selected activity page with the complete authorized timeline catalog. */
export function createScheduledJobActivityCoordinator(options: CoordinatorOptions) {
    let disposed = false;
    let generation = 0;
    let cancellation: AbortController | undefined;
    let statusCancellation: AbortController | undefined;
    let catalogAvailable = false;
    let catalogLoading = false;
    let polling = false;

    const view = createScheduledJobActivityView({
        ...options.view,
        onUnavailable() {
            catalogAvailable = false;
            catalogLoading = false;
            generation++;
            cancellation?.abort();
            options.clearCatalog();
        }
    });

    async function refresh(): Promise<void> {
        if (disposed) return;
        const current = ++generation;
        catalogLoading = true;
        cancellation?.abort();
        const pending = cancellation = new AbortController();
        const tableRefresh = view.refresh();
        try {
            const jobs = await loadScheduledJobCatalog(options.view.fetchPage, pending.signal);
            if (disposed || current !== generation || pending.signal.aborted) return;
            catalogAvailable = true;
            options.renderCatalog(jobs);
        } catch (error) {
            if (disposed || current !== generation || pending.signal.aborted) return;
            catalogAvailable = false;
            options.clearCatalog();
            options.catalogError(asError(error));
        } finally {
            await tableRefresh;
            if (current === generation) catalogLoading = false;
        }
    }

    async function poll(): Promise<void> {
        if (disposed || polling) return;
        polling = true;
        const pending = statusCancellation = new AbortController();
        let online = false;
        try {
            try {
                const status = await options.status(pending.signal);
                if (disposed) return;
                if (!isObject(status) || typeof status.online !== 'boolean')
                    throw new TypeError('Invalid Orchestrator status response.');
                online = status.online;
            } catch {
                if (disposed) return;
            }
            options.onlineChanged(online);
            // Refresh activity even after a failed health request so old counts do not survive
            // a lost connection. Recovery must also reload the full timeline catalog.
            if (!view.loading) {
                if (online && !catalogAvailable && !catalogLoading) await refresh();
                else await view.refresh({ background: true });
            }
        } finally {
            polling = false;
        }
    }

    return {
        refresh,
        poll,
        dispose() {
            disposed = true;
            generation++;
            cancellation?.abort();
            statusCancellation?.abort();
            view.dispose();
        }
    };
}
