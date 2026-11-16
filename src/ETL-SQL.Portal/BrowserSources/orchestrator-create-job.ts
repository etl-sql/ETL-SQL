import { asError, isObject } from './http-error.js';
import type { ApiRequestOptions } from './portal-http.js';
import { requireMutationStatus } from './orchestrator-job-actions.js';
import { readCatalogJson, readSchedules } from './orchestrator-catalog-responses.js';

/**
 * The "New Job" dialog on the Orchestrator page.
 *
 * Its two pickers are read independently, and one that cannot be read says so rather than
 * presenting an empty list as "nothing to choose". The job is announced only after the service
 * creates it; attaching a shared schedule is a second request, and when that one fails the job
 * still exists, so the outcome reports both facts instead of swallowing the second.
 */

export interface CreateJobOptions {
    root: Document;
    fetch(url: string, init?: ApiRequestOptions): Promise<Response>;
    notify(message: string, failed?: boolean): void;
    sessionExpired(): void;
    /** Reloads the job list after a job was created. */
    created(name: string): Promise<void> | void;
}

interface Choice { value: string; label: string }

export function readScriptChoices(value: unknown): Choice[] {
    if (!isObject(value) || !Array.isArray(value.files) || !value.files.every(file => typeof file === 'string' && file.length > 0))
        throw new TypeError('Invalid script list response.');
    return (value.files as string[]).map(file => ({ value: file, label: file }));
}

export function createJobDialog(options: CreateJobOptions) {
    const { root } = options;
    const lifetime = new AbortController();
    const get = <T extends HTMLElement>(id: string, type: { new(): T }): T => {
        const found = root.getElementById(id);
        if (!(found instanceof type)) throw new Error(`Missing create-job element: ${id}`);
        return found;
    };
    const input = (id: string) => get(id, HTMLInputElement), select = (id: string) => get(id, HTMLSelectElement);
    const box = (id: string) => get(id, HTMLElement);
    const modal = box('createJobModal'), error = box('cj-error'), save = get('cj-saveBtn', HTMLButtonElement);
    let opening = 0, saving = false;
    let reads: AbortController | null = null;

    function close() { opening++; reads?.abort(); modal.style.display = 'none'; }

    async function fill(target: HTMLSelectElement, placeholder: string, subject: string, url: string,
        read: (value: unknown) => Choice[], signal: AbortSignal): Promise<string | null> {
        target.replaceChildren(new Option('— Loading —', ''));
        target.disabled = true;
        try {
            const response = await options.fetch(url, { signal, cache: 'no-store' });
            if (response.status === 401) { options.sessionExpired(); throw new Error('Your session has expired.'); }
            const choices = read(await readCatalogJson(response));
            if (signal.aborted) return null;
            target.replaceChildren(new Option(placeholder, ''), ...choices.map(choice => new Option(choice.label, choice.value)));
            target.disabled = false;
            return null;
        } catch (failure) {
            if (signal.aborted) return null;
            target.replaceChildren(new Option('— Unavailable —', ''));
            return `${subject} could not be loaded: ${asError(failure).message}`;
        }
    }

    get('newJobBtn', HTMLButtonElement).addEventListener('click', async () => {
        const current = ++opening;
        reads?.abort();
        const scope = reads = new AbortController();
        reset();
        error.textContent = '';
        save.disabled = saving;
        modal.style.display = 'flex';
        const failures = await Promise.all([
            fill(select('cj-script-select'), '— Select from server —', 'Server scripts', '/api/orchestrator/scripts', readScriptChoices, scope.signal),
            fill(select('cj-shared-schedule-select'), '— Select shared schedule —', 'Shared schedules', '/api/orchestrator/schedules?limit=1000',
                value => readSchedules(value).map(item => ({ value: item.name, label: `${item.displayName || item.name} (${item.cron})` })), scope.signal)
        ]);
        if (lifetime.signal.aborted || current !== opening) return;
        // A typed path still works without its picker, so the dialog stays usable.
        const unavailable = failures.filter((failure): failure is string => failure !== null);
        if (unavailable.length) error.textContent = unavailable.join(' ');
    }, { signal: lifetime.signal });

    /** Shows the rows that belong to the chosen schedule mode. */
    function layout() {
        const shared = input('cj-sched-mode-shared').checked;
        box('cj-inline-sched-wrap').style.display = shared ? 'none' : '';
        box('cj-shared-sched-wrap').style.display = shared ? '' : 'none';
    }

    /** Returns every field to what the page shipped with: a new job must not inherit the last one's target or schedule. */
    function reset() {
        for (const field of modal.querySelectorAll('input, select, textarea')) {
            if (field instanceof HTMLInputElement) { field.value = field.defaultValue; field.checked = field.defaultChecked; }
            else if (field instanceof HTMLTextAreaElement) field.value = field.defaultValue;
            else if (field instanceof HTMLSelectElement) field.selectedIndex = Math.max(0, [...field.options].findIndex(option => option.defaultSelected));
        }
        layout();
    }

    for (const id of ['cj-sched-mode-inline', 'cj-sched-mode-shared'])
        get(id, HTMLElement).addEventListener('change', layout, { signal: lifetime.signal });
    for (const id of ['closeCreateBtn', 'cj-cancelBtn'])
        get(id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });

    save.addEventListener('click', async () => {
        if (saving || lifetime.signal.aborted) return;
        const name = input('cj-name').value.trim();
        const sandbox = select('cj-sandbox-profile').value;
        const targetPath = input('cj-script-path').value.trim() || select('cj-script-select').value;
        const shared = input('cj-sched-mode-shared').checked ? select('cj-shared-schedule-select').value : '';
        if (!name) { error.textContent = 'Job identifier is required.'; return; }
        if (!targetPath) { error.textContent = 'Script path is required.'; return; }
        if (input('cj-sched-mode-shared').checked && !shared) { error.textContent = 'Choose the shared schedule to attach.'; return; }
        const body = {
            name, displayName: input('cj-display-name').value.trim() || name, description: input('cj-description').value.trim() || null,
            jobType: 'Script', targetPath,
            // The interval drives a job only while no schedule is attached, so a job created on a
            // shared schedule names none rather than the values left in the hidden inline fields.
            ...(shared ? {} : { interval: Number(input('cj-interval').value) || 1, unit: select('cj-unit').value, atTime: input('cj-at-time').value || null }),
            maxRetries: Number(input('cj-retries').value) || 0,
            retryDelaySeconds: Number(input('cj-retry-delay').value) || 30, hashPolicy: select('cj-hash-policy').value,
            options: sandbox !== 'Default' ? { SandboxProfile: sandbox } : null, mode: 'Create'
        };
        const current = opening;
        saving = true; save.disabled = true; error.textContent = '';
        try {
            await requireMutationStatus(await options.fetch('/api/orchestrator/jobs', { method: 'POST', body, signal: lifetime.signal }), 201);
            if (lifetime.signal.aborted) return;
            let attachFailure = '';
            if (shared) {
                try {
                    await requireMutationStatus(await options.fetch(
                        `/api/orchestrator/jobs/${encodeURIComponent(name)}/schedules/${encodeURIComponent(shared)}`,
                        { method: 'POST', signal: lifetime.signal }), 200);
                } catch (failure) {
                    attachFailure = asError(failure).message;
                }
                if (lifetime.signal.aborted) return;
            }
            if (current === opening) close();
            if (attachFailure) options.notify(`Job '${name}' was created, but schedule '${shared}' was not attached: ${attachFailure} `
                + 'Attach it from the job\'s detail panel.', true);
            else options.notify(`Job '${name}' created.`);
            await options.created(name);
        } catch (failure) {
            if (lifetime.signal.aborted) return;
            if (current === opening) error.textContent = asError(failure).message;
            else options.notify(`Error: Job '${name}' was not created. ${asError(failure).message}`, true);
        } finally {
            saving = false;
            if (!lifetime.signal.aborted) save.disabled = false;
        }
    }, { signal: lifetime.signal });

    return { dispose() { close(); lifetime.abort(); } };
}
