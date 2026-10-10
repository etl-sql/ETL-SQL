/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/orchestrator-create-job.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { asError, isObject } from './http-error.js';
import { requireMutationStatus } from './orchestrator-job-actions.js';
import { readCatalogJson, readSchedules } from './orchestrator-catalog-responses.js';
export function readScriptChoices(value) {
    if (!isObject(value) || !Array.isArray(value.files) || !value.files.every(file => typeof file === 'string' && file.length > 0))
        throw new TypeError('Invalid script list response.');
    return value.files.map(file => ({ value: file, label: file }));
}
export function readBundleChoices(value) {
    if (!Array.isArray(value))
        throw new TypeError('Invalid bundle list response.');
    // The service lists published versions; the picker offers each bundle once, at its newest version.
    const newest = new Map();
    for (const bundle of value) {
        if (!isObject(bundle) || typeof bundle.bundleName !== 'string' || !bundle.bundleName
            || typeof bundle.version !== 'number' || !Number.isSafeInteger(bundle.version) || bundle.version < 1)
            throw new TypeError('Invalid bundle list response.');
        newest.set(bundle.bundleName, Math.max(newest.get(bundle.bundleName) ?? 0, bundle.version));
    }
    return Array.from(newest, ([name, version]) => ({ value: `bundle://${name}/latest`, label: `${name} (latest: v${version})` }));
}
export function createJobDialog(options) {
    const { root } = options;
    const lifetime = new AbortController();
    const get = (id, type) => {
        const found = root.getElementById(id);
        if (!(found instanceof type))
            throw new Error(`Missing create-job element: ${id}`);
        return found;
    };
    const input = (id) => get(id, HTMLInputElement), select = (id) => get(id, HTMLSelectElement);
    const box = (id) => get(id, HTMLElement);
    const modal = box('createJobModal'), error = box('cj-error'), save = get('cj-saveBtn', HTMLButtonElement);
    let opening = 0, saving = false;
    let reads = null;
    function close() { opening++; reads?.abort(); modal.style.display = 'none'; }
    async function fill(target, placeholder, subject, url, read, signal) {
        target.replaceChildren(new Option('— Loading —', ''));
        target.disabled = true;
        try {
            const response = await options.fetch(url, { signal, cache: 'no-store' });
            if (response.status === 401) {
                options.sessionExpired();
                throw new Error('Your session has expired.');
            }
            const choices = read(await readCatalogJson(response));
            if (signal.aborted)
                return null;
            target.replaceChildren(new Option(placeholder, ''), ...choices.map(choice => new Option(choice.label, choice.value)));
            target.disabled = false;
            return null;
        }
        catch (failure) {
            if (signal.aborted)
                return null;
            target.replaceChildren(new Option('— Unavailable —', ''));
            return `${subject} could not be loaded: ${asError(failure).message}`;
        }
    }
    get('newJobBtn', HTMLButtonElement).addEventListener('click', async () => {
        const current = ++opening;
        reads?.abort();
        const scope = reads = new AbortController();
        for (const id of ['cj-name', 'cj-display-name', 'cj-description'])
            input(id).value = '';
        error.textContent = '';
        save.disabled = saving;
        modal.style.display = 'flex';
        const failures = await Promise.all([
            fill(select('cj-script-select'), '— Select from server —', 'Server scripts', '/api/orchestrator/scripts', readScriptChoices, scope.signal),
            fill(select('cj-bundle-select'), '— Select published bundle —', 'Published bundles', '/api/orchestrator/bundles', readBundleChoices, scope.signal),
            fill(select('cj-shared-schedule-select'), '— Select shared schedule —', 'Shared schedules', '/api/orchestrator/schedules?limit=1000', value => readSchedules(value).map(item => ({ value: item.name, label: `${item.displayName || item.name} (${item.cron})` })), scope.signal)
        ]);
        if (lifetime.signal.aborted || current !== opening)
            return;
        // A typed path or bundle URI still works without its picker, so the dialog stays usable.
        const unavailable = failures.filter((failure) => failure !== null);
        if (unavailable.length)
            error.textContent = unavailable.join(' ');
    }, { signal: lifetime.signal });
    select('cj-target-kind').addEventListener('change', event => {
        const bundle = event.target.value === 'Bundle';
        box('cj-script-target-row').style.display = bundle ? 'none' : '';
        box('cj-bundle-target-row').style.display = bundle ? '' : 'none';
    }, { signal: lifetime.signal });
    for (const [id, shared] of [['cj-sched-mode-inline', false], ['cj-sched-mode-shared', true]])
        input(id).addEventListener('change', () => {
            box('cj-inline-sched-wrap').style.display = shared ? 'none' : '';
            box('cj-shared-sched-wrap').style.display = shared ? '' : 'none';
        }, { signal: lifetime.signal });
    for (const id of ['closeCreateBtn', 'cj-cancelBtn'])
        get(id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });
    save.addEventListener('click', async () => {
        if (saving || lifetime.signal.aborted)
            return;
        const name = input('cj-name').value.trim();
        const kind = select('cj-target-kind').value, sandbox = select('cj-sandbox-profile').value;
        const targetPath = kind === 'Bundle'
            ? input('cj-bundle-uri').value.trim() || select('cj-bundle-select').value
            : input('cj-script-path').value.trim() || select('cj-script-select').value;
        const shared = input('cj-sched-mode-shared').checked ? select('cj-shared-schedule-select').value : '';
        if (!name) {
            error.textContent = 'Job identifier is required.';
            return;
        }
        if (!targetPath) {
            error.textContent = 'Target path or bundle is required.';
            return;
        }
        if (input('cj-sched-mode-shared').checked && !shared) {
            error.textContent = 'Choose the shared schedule to attach.';
            return;
        }
        const body = {
            name, displayName: input('cj-display-name').value.trim() || name, description: input('cj-description').value.trim() || null,
            jobType: kind, targetPath, interval: Number(input('cj-interval').value) || 1, unit: select('cj-unit').value,
            atTime: input('cj-at-time').value || null, maxRetries: Number(input('cj-retries').value) || 0,
            retryDelaySeconds: Number(input('cj-retry-delay').value) || 30, hashPolicy: select('cj-hash-policy').value,
            options: sandbox !== 'Default' ? { SandboxProfile: sandbox } : null, mode: 'Create'
        };
        const current = opening;
        saving = true;
        save.disabled = true;
        error.textContent = '';
        try {
            await requireMutationStatus(await options.fetch('/api/orchestrator/jobs', { method: 'POST', body, signal: lifetime.signal }), 201);
            if (lifetime.signal.aborted)
                return;
            let attachFailure = '';
            if (shared) {
                try {
                    await requireMutationStatus(await options.fetch(`/api/orchestrator/jobs/${encodeURIComponent(name)}/schedules/${encodeURIComponent(shared)}`, { method: 'POST', signal: lifetime.signal }), 200);
                }
                catch (failure) {
                    attachFailure = asError(failure).message;
                }
                if (lifetime.signal.aborted)
                    return;
            }
            if (current === opening)
                close();
            if (attachFailure)
                options.notify(`Job '${name}' was created, but schedule '${shared}' was not attached: ${attachFailure} `
                    + 'Attach it from the job\'s detail panel.', true);
            else
                options.notify(`Job '${name}' created.`);
            await options.created(name);
        }
        catch (failure) {
            if (lifetime.signal.aborted)
                return;
            if (current === opening)
                error.textContent = asError(failure).message;
            else
                options.notify(`Error: Job '${name}' was not created. ${asError(failure).message}`, true);
        }
        finally {
            saving = false;
            if (!lifetime.signal.aborted)
                save.disabled = false;
        }
    }, { signal: lifetime.signal });
    return { dispose() { close(); lifetime.abort(); } };
}
