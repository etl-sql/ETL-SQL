import { asError } from './http-error.js';
import type { ApiRequestOptions } from './portal-http.js';
import { escHtml } from './orchestrator-admin-ui.js';
import { unownedListHtml } from './orchestrator-acl-ui.js';
import { readAdoption, readCatalogJson, readUnownedObjects } from './orchestrator-catalog-responses.js';

/**
 * The "objects with no owner" dialog on the Orchestrator page, for administrators.
 *
 * An object nobody owns is reachable only by an administrator, so after a Portal is first attached
 * every existing job, schedule, and notification needs an owner assigned. The dialog lists them and
 * assigns one owner to all. The list is read each time it opens; when it cannot be read the reason
 * is shown and nothing can be assigned, because "none" and "unknown" call for opposite actions.
 */
export interface UnownedDialogOptions {
    root: Document;
    fetch(url: string, init?: ApiRequestOptions): Promise<Response>;
    notify(message: string, failed?: boolean): void;
    confirm(message: string, options: Parameters<EtlSqlFeedback['confirm']>[1]): Promise<boolean>;
    sessionExpired(): void;
    /** Ownership changed, so the job list and any open detail panel are out of date. */
    changed(): Promise<void> | void;
}

export function createUnownedDialog(options: UnownedDialogOptions) {
    const { root } = options;
    const lifetime = new AbortController();
    const get = <T extends HTMLElement>(id: string, type: { new(): T }): T => {
        const found = root.getElementById(id);
        if (!(found instanceof type)) throw new Error(`Missing ownership element: ${id}`);
        return found;
    };
    const opener = get('unownedBtn', HTMLButtonElement), modal = get('unownedModal', HTMLElement), list = get('unownedList', HTMLElement);
    const adopt = get('unownedAdoptBtn', HTMLButtonElement), kind = get('unownedOwnerKind', HTMLSelectElement), key = get('unownedOwnerKey', HTMLInputElement);
    let opening = 0, count: number | null = null, adopting = false;
    let request: AbortController | null = null;

    const shown = (opened: number) => !lifetime.signal.aborted && opened === opening && modal.style.display !== 'none';
    function controls() { adopt.disabled = adopting || !count; }
    function label(known: number | null) { opener.textContent = known ? `Unowned objects (${known})` : 'Unowned objects'; }

    async function read(signal: AbortSignal) {
        const response = await options.fetch('/api/orchestrator/authorization/unowned', { signal, cache: 'no-store' });
        if (response.status === 401) { options.sessionExpired(); throw new Error('Your session has expired.'); }
        return readUnownedObjects(await readCatalogJson(response));
    }

    async function load() {
        const opened = opening;
        request?.abort();
        const current = request = new AbortController();
        count = null; controls();
        list.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading objects…</span></div>';
        try {
            const objects = await read(current.signal);
            if (!shown(opened)) return;
            count = objects.length; label(count);
            list.innerHTML = unownedListHtml(objects, escHtml);
        } catch (failure) {
            if (!shown(opened)) return;
            list.innerHTML = `<div class="error-msg show" role="alert">Objects without an owner could not be listed: ${escHtml(asError(failure).message)}</div>`;
        }
        controls();
    }

    function close() { opening++; request?.abort(); request = null; modal.style.display = 'none'; }

    opener.addEventListener('click', () => {
        opening++;
        key.value = '';
        modal.style.display = 'flex';
        void load();
    }, { signal: lifetime.signal });
    for (const id of ['unownedCloseBtn', 'unownedCancelBtn'])
        get(id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });

    adopt.addEventListener('click', async () => {
        const principalKind = kind.value, principalId = key.value.trim(), total = count, opened = opening;
        if (adopting || !total || lifetime.signal.aborted) return;
        if (!principalId) { options.notify('Error: Enter the principal key of the new owner.', true); key.focus(); return; }
        if (!await options.confirm(`Assign ${principalKind.toLowerCase()} '${principalId}' as owner of ${total} object${total === 1 ? '' : 's'}?`,
            { title: 'Assign owner to all', impact: 'The owner can manage every object listed. Each assignment is audited.',
                confirmLabel: 'Assign owner', auditAction: 'orchestrator.ownership.adopt' })) return;
        if (lifetime.signal.aborted || adopting) return;
        adopting = true; controls();
        try {
            const response = await options.fetch('/api/orchestrator/authorization/adopt',
                { method: 'POST', body: { principalKind, principalId, kind: null }, signal: lifetime.signal });
            if (response.status === 401) { options.sessionExpired(); throw new Error('Your session has expired.'); }
            const adopted = readAdoption(await readCatalogJson(response), principalKind, principalId);
            if (lifetime.signal.aborted) return;
            options.notify(`Owner assigned to ${adopted} object${adopted === 1 ? '' : 's'}.`);
            await options.changed();
        } catch (failure) {
            if (!lifetime.signal.aborted) options.notify(`Error: ${asError(failure).message}`, true);
        } finally {
            adopting = false;
        }
        // Read the list again either way: after a failure some objects may already have an owner.
        if (shown(opened)) await load(); else if (!lifetime.signal.aborted) void refreshCount();
    }, { signal: lifetime.signal });

    /** Puts the number of unowned objects on the opener. A failed read leaves it without one; the dialog says why. */
    async function refreshCount() {
        try {
            const objects = await read(lifetime.signal);
            if (!lifetime.signal.aborted) label(objects.length);
        } catch {
            if (!lifetime.signal.aborted) label(null);
        }
    }

    return { refreshCount, dispose() { close(); lifetime.abort(); } };
}
