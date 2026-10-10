import { asError } from './http-error.js';
import { escHtml, renderNotificationsTable, renderSchedulesTable } from './orchestrator-admin-ui.js';
import { requireMutationStatus } from './orchestrator-job-actions.js';
import { readDispatchOutcome, readNotifications, readSchedules } from './orchestrator-catalog-responses.js';
import type { NotificationView, ScheduleView } from './orchestrator-catalog-responses.js';

/**
 * Shared schedule and notification catalogs on the Orchestrator page.
 *
 * A catalog is either loaded or unavailable, never silently empty: a failed read clears the rows
 * and the count and says so. Writes are confirmed by the status each endpoint documents, one write
 * per definition at a time, and a response that arrives after its dialog was closed or reused, or
 * after the page was left, changes nothing on screen.
 */

type Notify = (message: string, failed?: boolean) => void;
type Confirm = (message: string, options: Parameters<EtlSqlFeedback['confirm']>[1]) => Promise<boolean>;

interface CatalogHost {
    root: Document;
    notify: Notify;
    confirm: Confirm;
}

export interface ScheduleCatalogApi {
    list(signal: AbortSignal): Promise<unknown>;
    create(body: CreateScheduleRequest, signal: AbortSignal): Promise<Response>;
    update(name: string, body: UpdateScheduleRequest, signal: AbortSignal): Promise<Response>;
    remove(name: string, signal: AbortSignal): Promise<Response>;
}

export interface NotificationCatalogApi {
    list(signal: AbortSignal): Promise<unknown>;
    create(body: CreateNotificationRequest, signal: AbortSignal): Promise<Response>;
    update(name: string, body: UpdateNotificationRequest, signal: AbortSignal): Promise<Response>;
    remove(name: string, signal: AbortSignal): Promise<Response>;
    dispatch(name: string, body: OrchestratorNotificationDispatchRequest, signal: AbortSignal): Promise<Response>;
}

interface CatalogOptions<T extends { name: string }> {
    host: CatalogHost;
    lifetime: AbortController;
    wrapId: string;
    countId: string;
    refreshId: string;
    plural: string;
    list(signal: AbortSignal): Promise<T[]>;
    render(items: T[]): string;
    act(action: string, item: T, button: HTMLButtonElement): Promise<void> | void;
}

function element<T extends HTMLElement>(root: Document, id: string, type: { new(): T }): T {
    const found = root.getElementById(id);
    if (!(found instanceof type)) throw new Error(`Missing Orchestrator page element: ${id}`);
    return found;
}

function createCatalog<T extends { name: string }>(options: CatalogOptions<T>) {
    const { host, lifetime } = options;
    const wrap = element(host.root, options.wrapId, HTMLElement);
    const count = element(host.root, options.countId, HTMLElement);
    const refresh = element(host.root, options.refreshId, HTMLButtonElement);
    const pending = new Set<string>();
    let items: T[] | null = null;
    let generation = 0;
    let reads: AbortController | null = null;

    function markPending() {
        for (const button of wrap.querySelectorAll<HTMLButtonElement>('button[data-action][data-name]'))
            button.disabled = pending.has(button.dataset.name ?? '');
    }

    function paint() {
        if (items === null) return;
        const focused = host.root.activeElement instanceof HTMLButtonElement && wrap.contains(host.root.activeElement)
            ? host.root.activeElement.dataset : null;
        wrap.innerHTML = options.render(items);
        count.textContent = String(items.length);
        markPending();
        if (!focused?.action || !focused.name) return;
        const replacement = Array.from(wrap.querySelectorAll<HTMLButtonElement>('button[data-action][data-name]'))
            .find(button => button.dataset.action === focused.action && button.dataset.name === focused.name);
        (replacement && !replacement.disabled ? replacement : refresh).focus();
    }

    async function load(): Promise<void> {
        if (lifetime.signal.aborted) return;
        const current = ++generation;
        reads?.abort();
        reads = new AbortController();
        try {
            const loaded = await options.list(reads.signal);
            if (lifetime.signal.aborted || current !== generation) return;
            items = loaded;
            paint();
        } catch (failure) {
            if (lifetime.signal.aborted || current !== generation) return;
            items = null;
            count.textContent = '—';
            wrap.innerHTML = `<div class="error-msg show" role="alert">${escHtml(options.plural)} are unavailable: `
                + `${escHtml(asError(failure).message)}</div>`;
        }
    }

    /**
     * Runs one write for one definition and announces it once confirmed. Resolves to null on
     * success, to the reason on failure, and to undefined when the page was left meanwhile.
     */
    async function write(name: string, work: (signal: AbortSignal) => Promise<string>): Promise<string | null | undefined> {
        if (lifetime.signal.aborted) return undefined;
        if (pending.has(name)) return `Another change to '${name}' is still in progress.`;
        pending.add(name);
        markPending();
        let reason: string | null = null;
        try {
            const message = await work(lifetime.signal);
            if (lifetime.signal.aborted) return undefined;
            host.notify(message);
        } catch (failure) {
            if (lifetime.signal.aborted) return undefined;
            reason = asError(failure).message;
        } finally {
            pending.delete(name);
        }
        // Reload after a rejected write as well: the row on screen may be what the server refused.
        await load();
        return lifetime.signal.aborted ? undefined : reason;
    }

    /** A write started from a table row reports its failure on the toast surface. */
    async function rowWrite(name: string, work: (signal: AbortSignal) => Promise<string>): Promise<void> {
        const reason = await write(name, work);
        if (reason) host.notify(`Error: ${reason}`, true);
    }

    wrap.addEventListener('click', event => {
        if (!(event.target instanceof Element)) return;
        const button = event.target.closest<HTMLButtonElement>('button[data-action][data-name]');
        const name = button?.dataset.name, action = button?.dataset.action;
        if (!button || !name || !action || button.disabled) return;
        const item = items?.find(candidate => candidate.name === name);
        if (item) void options.act(action, item, button);
    }, { signal: lifetime.signal });
    refresh.addEventListener('click', () => { void load(); }, { signal: lifetime.signal });
    lifetime.signal.addEventListener('abort', () => { generation++; reads?.abort(); }, { once: true });

    return { load, write, rowWrite };
}

/** A modal whose pending save belongs to one opening; closing or reopening it orphans that save. */
function createDialog(host: CatalogHost, lifetime: AbortController, ids: { modal: string; error: string; save: string; close: string[] }) {
    const { root } = host;
    const modal = element(root, ids.modal, HTMLElement), error = element(root, ids.error, HTMLElement);
    const save = element(root, ids.save, HTMLButtonElement);
    let opening = 0, saving = false;
    function close() { opening++; modal.style.display = 'none'; error.textContent = ''; }
    for (const id of ids.close)
        element(root, id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });
    lifetime.signal.addEventListener('abort', close, { once: true });
    return {
        save, close,
        open() { opening++; error.textContent = ''; save.disabled = saving; modal.style.display = 'flex'; },
        fail(message: string) { error.textContent = message; },
        /**
         * Submits once. The work resolves like a catalog write. Its result reaches this dialog only
         * if the dialog is still the opening that sent it; otherwise a failure goes to the toast surface.
         */
        async submit(work: () => Promise<string | null | undefined>): Promise<void> {
            if (saving || lifetime.signal.aborted) return;
            const current = opening;
            saving = true; save.disabled = true; error.textContent = '';
            try {
                const reason = await work();
                if (reason === undefined || lifetime.signal.aborted) return;
                if (current !== opening) { if (reason) host.notify(`Error: ${reason}`, true); }
                else if (reason) error.textContent = reason;
                else close();
            } finally {
                saving = false;
                if (!lifetime.signal.aborted) save.disabled = false;
            }
        }
    };
}

function field(root: Document, id: string): HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement {
    const found = root.getElementById(id);
    if (!(found instanceof HTMLInputElement || found instanceof HTMLSelectElement || found instanceof HTMLTextAreaElement))
        throw new Error(`Missing Orchestrator page field: ${id}`);
    return found;
}

export function createScheduleCatalog(host: CatalogHost, api: ScheduleCatalogApi) {
    const lifetime = new AbortController();
    const { root } = host;
    const dialog = createDialog(host, lifetime,
        { modal: 'scheduleModal', error: 'cs-error', save: 'cs-saveBtn', close: ['closeScheduleModalBtn', 'cs-cancelBtn'] });
    const title = element(root, 'scheduleModalTitle', HTMLElement);
    const name = field(root, 'cs-name'), display = field(root, 'cs-display-name');
    const description = field(root, 'cs-description'), cron = field(root, 'cs-cron');
    const timeZone = field(root, 'cs-timezone'), enabled = element(root, 'cs-enabled', HTMLInputElement);
    let editing: string | null = null;

    const catalog = createCatalog<ScheduleView>({
        host, lifetime, wrapId: 'schedulesTableWrap', countId: 'schedulesNavCount', refreshId: 'refreshSchedulesBtn',
        plural: 'Schedules',
        list: async signal => readSchedules(await api.list(signal)),
        render: items => renderSchedulesTable(items, null),
        async act(action, schedule) {
            if (action === 'edit-schedule') { edit(schedule); return; }
            if (action === 'toggle-schedule') {
                const enable = !schedule.isEnabled;
                await catalog.rowWrite(schedule.name, async signal => {
                    await requireMutationStatus(await api.update(schedule.name, { isEnabled: enable }, signal), 200);
                    return `Schedule '${schedule.name}' ${enable ? 'enabled' : 'disabled'}.`;
                });
                return;
            }
            if (action !== 'delete-schedule') return;
            if (!await host.confirm(`Delete schedule '${schedule.name}'?`, {
                title: 'Delete schedule', impact: 'This schedule will be permanently deleted.',
                confirmLabel: 'Delete schedule', danger: true, auditAction: 'orchestrator.schedule.delete' })) return;
            await catalog.rowWrite(schedule.name, async signal => {
                await requireMutationStatus(await api.remove(schedule.name, signal), 200);
                return `Schedule '${schedule.name}' deleted.`;
            });
        }
    });

    function edit(schedule: ScheduleView | null) {
        editing = schedule?.name ?? null;
        title.textContent = schedule ? `Edit Schedule '${schedule.name}'` : 'Create Schedule';
        name.value = schedule?.name ?? ''; name.disabled = schedule !== null;
        display.value = schedule ? schedule.displayName || schedule.name : '';
        description.value = schedule?.description ?? '';
        cron.value = schedule?.cron ?? '0 0 * * *';
        timeZone.value = schedule?.timeZone ?? 'UTC';
        enabled.checked = schedule?.isEnabled ?? true;
        dialog.open();
    }

    element(root, 'newScheduleBtn', HTMLButtonElement)
        .addEventListener('click', () => edit(null), { signal: lifetime.signal });
    dialog.save.addEventListener('click', () => {
        const target = editing, key = target ?? name.value.trim();
        const body = {
            displayName: display.value.trim() || key, description: description.value.trim() || null,
            cron: cron.value.trim(), timeZone: timeZone.value, isEnabled: enabled.checked
        };
        if (!key) { dialog.fail('Name is required.'); return; }
        if (!body.cron) { dialog.fail('Cron expression is required.'); return; }
        void dialog.submit(() => catalog.write(key, async signal => {
            if (target) await requireMutationStatus(await api.update(target, body, signal), 200);
            else await requireMutationStatus(await api.create({ name: key, ...body }, signal), 201);
            return `Schedule '${key}' ${target ? 'updated' : 'created'}.`;
        }));
    }, { signal: lifetime.signal });

    return { load: catalog.load, dispose() { lifetime.abort(); } };
}

export function createNotificationCatalog(host: CatalogHost, api: NotificationCatalogApi) {
    const lifetime = new AbortController();
    const { root } = host;
    const dialog = createDialog(host, lifetime, { modal: 'notificationModal', error: 'cn-error', save: 'cn-saveBtn',
        close: ['closeNotificationModalBtn', 'cn-cancelBtn'] });
    const dispatchDialog = createDialog(host, lifetime, { modal: 'dispatchModal', error: 'disp-error', save: 'disp-sendBtn',
        close: ['closeDispatchModalBtn', 'disp-cancelBtn'] });
    const title = element(root, 'notificationModalTitle', HTMLElement);
    const dispatchTitle = element(root, 'dispatchModalTitle', HTMLElement);
    const name = field(root, 'cn-name'), display = field(root, 'cn-display-name');
    const description = field(root, 'cn-description'), connection = field(root, 'cn-connection');
    const recipient = field(root, 'cn-recipient'), enabled = element(root, 'cn-enabled', HTMLInputElement);
    let editing: string | null = null, dispatching: string | null = null;

    const catalog = createCatalog<NotificationView>({
        host, lifetime, wrapId: 'notificationsTableWrap', countId: 'notificationsNavCount',
        refreshId: 'refreshNotificationsBtn', plural: 'Notifications',
        list: async signal => readNotifications(await api.list(signal)),
        render: items => renderNotificationsTable(items, null),
        async act(action, notification) {
            if (action === 'edit-notification') { edit(notification); return; }
            if (action === 'dispatch-notification') {
                dispatching = notification.name;
                dispatchTitle.textContent = `Test Dispatch to '${notification.name}'`;
                dispatchDialog.open();
                return;
            }
            if (action !== 'delete-notification') return;
            if (!await host.confirm(`Delete notification endpoint '${notification.name}'?`, {
                title: 'Delete notification', impact: 'This notification config will be permanently deleted.',
                confirmLabel: 'Delete notification', danger: true, auditAction: 'orchestrator.notification.delete' })) return;
            await catalog.rowWrite(notification.name, async signal => {
                await requireMutationStatus(await api.remove(notification.name, signal), 200);
                return `Notification '${notification.name}' deleted.`;
            });
        }
    });

    function edit(notification: NotificationView | null) {
        editing = notification?.name ?? null;
        title.textContent = notification ? `Edit Notification '${notification.name}'` : 'Create Notification Endpoint';
        name.value = notification?.name ?? ''; name.disabled = notification !== null;
        display.value = notification ? notification.displayName || notification.name : '';
        description.value = notification?.description ?? '';
        connection.value = notification?.connectionName ?? '';
        recipient.value = notification?.recipient ?? '';
        enabled.checked = notification?.isEnabled ?? true;
        dialog.open();
    }

    element(root, 'newNotificationBtn', HTMLButtonElement)
        .addEventListener('click', () => edit(null), { signal: lifetime.signal });
    dialog.save.addEventListener('click', () => {
        const target = editing, key = target ?? name.value.trim();
        const body = {
            displayName: display.value.trim() || key, description: description.value.trim() || null,
            connectionName: connection.value.trim(), recipient: recipient.value.trim() || null,
            isEnabled: enabled.checked
        };
        if (!key) { dialog.fail('Name is required.'); return; }
        if (!body.connectionName) { dialog.fail('Connection name is required.'); return; }
        void dialog.submit(() => catalog.write(key, async signal => {
            if (target) await requireMutationStatus(await api.update(target, body, signal), 200);
            else await requireMutationStatus(await api.create({ name: key, ...body }, signal), 201);
            return `Notification '${key}' ${target ? 'updated' : 'created'}.`;
        }));
    }, { signal: lifetime.signal });

    dispatchDialog.save.addEventListener('click', () => {
        const target = dispatching;
        const heading = field(root, 'disp-title').value.trim(), text = field(root, 'disp-text').value.trim();
        if (!target) return;
        if (!heading || !text) { dispatchDialog.fail('Title and text are required.'); return; }
        const body = {
            sourceKind: field(root, 'disp-source-kind').value, title: heading, text,
            trigger: field(root, 'disp-trigger').value,
            recipientOverride: field(root, 'disp-recipient-override').value.trim() || null
        };
        void dispatchDialog.submit(async () => {
            try {
                const outcome = await readDispatchOutcome(await api.dispatch(target, body, lifetime.signal));
                if (lifetime.signal.aborted) return undefined;
                if (outcome.delivered) { host.notify(`Test notification sent to '${target}'.`); return null; }
                return outcome.skipped
                    ? `Nothing was sent to '${target}'. ${outcome.detail || 'The endpoint skipped the dispatch.'}`
                    : `Delivery to '${target}' failed. ${outcome.detail}`.trim();
            } catch (failure) {
                return lifetime.signal.aborted ? undefined : asError(failure).message;
            }
        });
    }, { signal: lifetime.signal });

    return { load: catalog.load, dispose() { lifetime.abort(); } };
}
