import { asError, isObject } from './http-error.js';
import type { ApiRequestOptions } from './portal-http.js';
import { requireMutationStatus } from './orchestrator-job-actions.js';
import { readCatalogJson, readSchedules } from './orchestrator-catalog-responses.js';

/**
 * The "New Job" dialog on the Orchestrator page.
 *
 * Its three pickers are read independently, and one that cannot be read says so rather than
 * presenting an empty list as "nothing to choose". Bundle targets emit canonical
 * `orch://<bundle>@<version>/<entryPath>` URIs matching `BundleUri.TryParse`. The job is
 * announced only after the service creates it; attaching a shared schedule is a second request,
 * and when that one fails the job still exists, so the outcome reports both facts instead of
 * swallowing the second.
 */

export interface CreateJobOptions {
    root: Document;
    fetch(url: string, init?: ApiRequestOptions): Promise<Response>;
    notify(message: string, failed?: boolean): void;
    sessionExpired(): void;
    /** Reloads the job list after a job was created. */
    created(name: string): Promise<void> | void;
}

export interface Choice { value: string; label: string }

export interface BundleVersionSummary {
    bundleName: string;
    version: number;
    entryPath: string;
}

export interface ParsedBundleUri {
    bundleName: string;
    version: number | null;
    entryPath: string;
}

const maxBundleVersion = 2147483647; // BundleUri.Version is a C# Int32.

export function normalizeBundleEntryPath(path: string): string {
    return path.replace(/\\/g, '/').replace(/^\/+/, '').trim();
}

export function formatBundleUri(bundleName: string, version: number, entryPath: string): string {
    const trimmedName = bundleName.trim();
    const normalized = normalizeBundleEntryPath(entryPath);
    if (!trimmedName || /[/@]/.test(trimmedName) || !Number.isSafeInteger(version) || version < 1 || version > maxBundleVersion || !normalized)
        throw new TypeError('Invalid bundle target.');
    return `orch://${trimmedName}@${version}/${normalized}`;
}

export function parseBundleUri(value: string): ParsedBundleUri | null {
    const match = /^orch:\/\/([^/@]+)(?:@(\d+))?\/(.+)$/i.exec(value.trim());
    if (!match) return null;
    const version = match[2] !== undefined ? Number(match[2]) : null;
    if (version !== null && (!Number.isSafeInteger(version) || version < 1 || version > maxBundleVersion)) return null;
    const entryPath = normalizeBundleEntryPath(match[3]);
    if (!entryPath) return null;
    return { bundleName: match[1], version, entryPath };
}

function readBundleEntryList(value: unknown, failureMessage: string): BundleVersionSummary[] {
    if (!Array.isArray(value)) throw new TypeError(failureMessage);
    const items: BundleVersionSummary[] = [];
    for (const bundle of value as unknown[]) {
        if (!isObject(bundle) || typeof bundle.bundleName !== 'string' || !bundle.bundleName.trim() || /[/@]/.test(bundle.bundleName)
            || typeof bundle.version !== 'number' || !Number.isSafeInteger(bundle.version) || bundle.version < 1 || bundle.version > maxBundleVersion
            || typeof bundle.entryPath !== 'string' || !normalizeBundleEntryPath(bundle.entryPath))
            throw new TypeError(failureMessage);
        items.push({
            bundleName: bundle.bundleName.trim(),
            version: bundle.version,
            entryPath: normalizeBundleEntryPath(bundle.entryPath)
        });
    }
    return items;
}

export function readScriptChoices(value: unknown): Choice[] {
    if (!isObject(value) || !Array.isArray(value.files) || !value.files.every(file => typeof file === 'string' && file.length > 0))
        throw new TypeError('Invalid script list response.');
    return (value.files as string[]).map(file => ({ value: file, label: file }));
}

export function readBundleChoices(value: unknown): Choice[] {
    const entries = readBundleEntryList(value, 'Invalid bundle list response.');
    // The service lists published versions; the picker offers each bundle once, at its newest version.
    const newest = new Map<string, BundleVersionSummary>();
    for (const entry of entries) {
        const current = newest.get(entry.bundleName);
        if (!current || entry.version > current.version) newest.set(entry.bundleName, entry);
    }
    return Array.from(newest.values(), entry => ({
        value: formatBundleUri(entry.bundleName, entry.version, entry.entryPath),
        label: `${entry.bundleName} (latest: v${entry.version})`
    }));
}

export function readBundleVersions(value: unknown): BundleVersionSummary[] {
    return readBundleEntryList(value, 'Invalid bundle version list response.')
        .sort((left, right) => right.version - left.version);
}

export function readBundleFiles(value: unknown): Choice[] {
    if (!Array.isArray(value)) throw new TypeError('Invalid bundle file list response.');
    const paths = new Set<string>();
    for (const file of value as unknown[]) {
        if (!isObject(file) || typeof file.virtualPath !== 'string')
            throw new TypeError('Invalid bundle file list response.');
        const normalized = normalizeBundleEntryPath(file.virtualPath);
        if (!normalized) throw new TypeError('Invalid bundle file list response.');
        paths.add(normalized);
    }
    return Array.from(paths).sort((a, b) => a.localeCompare(b)).map(path => ({ value: path, label: path }));
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
    let opening = 0, bundleSelection = 0, saving = false;
    let reads: AbortController | null = null;
    let bundleReads: AbortController | null = null;
    let versionEntryDefaults = new Map<number, string>();

    function close() {
        opening++;
        bundleSelection++;
        reads?.abort();
        bundleReads?.abort();
        modal.style.display = 'none';
    }

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

    function setBundleDetailPlaceholder() {
        versionEntryDefaults = new Map();
        const versionSelect = select('cj-bundle-version');
        versionSelect.replaceChildren(new Option('— Latest —', ''));
        versionSelect.disabled = true;
        const entrySelect = select('cj-bundle-entry');
        entrySelect.replaceChildren(new Option('— Default entry —', ''));
        entrySelect.disabled = true;
    }

    function syncBundleUriFromPickers() {
        const selectedBundle = parseBundleUri(select('cj-bundle-select').value);
        if (!selectedBundle) return;
        const pickedVersion = Number(select('cj-bundle-version').value);
        const version = Number.isSafeInteger(pickedVersion) && pickedVersion >= 1
            ? pickedVersion
            : selectedBundle.version;
        if (!version) return;
        const entry = select('cj-bundle-entry').value.trim()
            || versionEntryDefaults.get(version)
            || selectedBundle.entryPath;
        input('cj-bundle-uri').value = formatBundleUri(selectedBundle.bundleName, version, entry);
    }

    async function loadBundleFiles(bundleName: string, version: number, defaultEntry: string, seq: number, signal: AbortSignal,
        updateUri = true) {
        const entrySelect = select('cj-bundle-entry');
        const normalizedDefault = normalizeBundleEntryPath(defaultEntry);
        entrySelect.replaceChildren(new Option(normalizedDefault, normalizedDefault));
        entrySelect.value = normalizedDefault;
        entrySelect.disabled = false;
        if (updateUri) input('cj-bundle-uri').value = formatBundleUri(bundleName, version, normalizedDefault);
        try {
            const response = await options.fetch(
                `/api/orchestrator/bundles/${encodeURIComponent(bundleName)}/versions/${version}/files`,
                { signal, cache: 'no-store' });
            if (response.status === 401) { options.sessionExpired(); return; }
            if (!response.ok) return;
            const files = readBundleFiles(await readCatalogJson(response));
            if (signal.aborted || lifetime.signal.aborted || seq !== bundleSelection) return;
            const choices = files.some(file => file.value === normalizedDefault)
                ? files
                : [{ value: normalizedDefault, label: normalizedDefault }, ...files];
            entrySelect.replaceChildren(...choices.map(choice => new Option(choice.label, choice.value)));
            entrySelect.value = normalizedDefault;
            entrySelect.disabled = false;
            // Loading choices must not overwrite a URI the operator typed while the request ran.
        } catch {
            // Keep the version's advertised EntryPath selectable even when file listing is unavailable.
        }
    }

    async function loadBundleVersionsAndFiles(bundleName: string, seedVersion: number, seedEntry: string) {
        const seq = ++bundleSelection;
        bundleReads?.abort();
        const scope = bundleReads = new AbortController();
        const versionSelect = select('cj-bundle-version');
        versionEntryDefaults = new Map([[seedVersion, seedEntry]]);
        versionSelect.replaceChildren(new Option(`v${seedVersion}`, String(seedVersion)));
        versionSelect.value = String(seedVersion);
        versionSelect.disabled = true;
        const seededUri = formatBundleUri(bundleName, seedVersion, seedEntry);
        input('cj-bundle-uri').value = seededUri;
        const entrySelect = select('cj-bundle-entry');
        entrySelect.replaceChildren(new Option(seedEntry, seedEntry));
        entrySelect.disabled = true;
        try {
            const response = await options.fetch(
                `/api/orchestrator/bundles/${encodeURIComponent(bundleName)}/versions`,
                { signal: scope.signal, cache: 'no-store' });
            if (response.status === 401) { options.sessionExpired(); return; }
            const versions = readBundleVersions(await readCatalogJson(response));
            if (scope.signal.aborted || lifetime.signal.aborted || seq !== bundleSelection || !versions.length) return;
            versionEntryDefaults = new Map(versions.map(item => [item.version, item.entryPath]));
            const selectedVersion = versions.some(item => item.version === seedVersion) ? seedVersion : versions[0].version;
            versionSelect.replaceChildren(...versions.map(item => new Option(`v${item.version}`, String(item.version))));
            versionSelect.value = String(selectedVersion);
            versionSelect.disabled = false;
        } catch {
            // Keep the latest version seeded from /api/orchestrator/bundles if version history cannot be read.
        } finally {
            if (!scope.signal.aborted && !lifetime.signal.aborted && seq === bundleSelection) {
                versionSelect.disabled = false;
                const version = Number(versionSelect.value);
                void loadBundleFiles(bundleName, version, versionEntryDefaults.get(version) ?? seedEntry, seq, scope.signal,
                    input('cj-bundle-uri').value === seededUri);
            }
        }
    }

    select('cj-bundle-select').addEventListener('change', () => {
        const parsed = parseBundleUri(select('cj-bundle-select').value);
        if (!parsed || parsed.version === null) {
            bundleSelection++;
            bundleReads?.abort();
            setBundleDetailPlaceholder();
            input('cj-bundle-uri').value = '';
            return;
        }
        void loadBundleVersionsAndFiles(parsed.bundleName, parsed.version, parsed.entryPath);
    }, { signal: lifetime.signal });

    select('cj-bundle-version').addEventListener('change', () => {
        const parsed = parseBundleUri(select('cj-bundle-select').value);
        const version = Number(select('cj-bundle-version').value);
        if (!parsed || !Number.isSafeInteger(version) || version < 1) return;
        const seq = ++bundleSelection;
        bundleReads?.abort();
        const scope = bundleReads = new AbortController();
        const defaultEntry = versionEntryDefaults.get(version) ?? parsed.entryPath;
        void loadBundleFiles(parsed.bundleName, version, defaultEntry, seq, scope.signal);
    }, { signal: lifetime.signal });

    select('cj-bundle-entry').addEventListener('change', () => {
        syncBundleUriFromPickers();
    }, { signal: lifetime.signal });

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
            fill(select('cj-bundle-select'), '— Select published bundle —', 'Published bundles', '/api/orchestrator/bundles', readBundleChoices, scope.signal),
            fill(select('cj-shared-schedule-select'), '— Select shared schedule —', 'Shared schedules', '/api/orchestrator/schedules?limit=1000',
                value => readSchedules(value).map(item => ({ value: item.name, label: `${item.displayName || item.name} (${item.cron})` })), scope.signal)
        ]);
        if (lifetime.signal.aborted || current !== opening) return;
        // A typed path or bundle URI still works without its picker, so the dialog stays usable.
        const unavailable = failures.filter((failure): failure is string => failure !== null);
        if (unavailable.length) error.textContent = unavailable.join(' ');
    }, { signal: lifetime.signal });

    /** Shows the rows that belong to the chosen target kind and schedule mode. */
    function layout() {
        const kind = select('cj-target-kind').value;
        const shared = input('cj-sched-mode-shared').checked;
        box('cj-script-target-row').style.display = kind === 'Script' ? '' : 'none';
        box('cj-bundle-target-row').style.display = kind === 'Bundle' ? '' : 'none';
        box('cj-report-target-row').style.display = kind === 'Report' ? '' : 'none';
        box('cj-inline-sched-wrap').style.display = shared ? 'none' : '';
        box('cj-shared-sched-wrap').style.display = shared ? '' : 'none';
    }

    /** Returns every field to what the page shipped with: a new job must not inherit the last one's target or schedule. */
    function reset() {
        bundleSelection++;
        bundleReads?.abort();
        for (const field of modal.querySelectorAll('input, select, textarea')) {
            if (field instanceof HTMLInputElement) { field.value = field.defaultValue; field.checked = field.defaultChecked; }
            else if (field instanceof HTMLTextAreaElement) field.value = field.defaultValue;
            else if (field instanceof HTMLSelectElement) field.selectedIndex = Math.max(0, [...field.options].findIndex(option => option.defaultSelected));
        }
        setBundleDetailPlaceholder();
        layout();
    }

    for (const id of ['cj-target-kind', 'cj-sched-mode-inline', 'cj-sched-mode-shared'])
        get(id, HTMLElement).addEventListener('change', layout, { signal: lifetime.signal });
    for (const id of ['closeCreateBtn', 'cj-cancelBtn'])
        get(id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });

    save.addEventListener('click', async () => {
        if (saving || lifetime.signal.aborted) return;
        const name = input('cj-name').value.trim();
        const kind = select('cj-target-kind').value;
        const sandbox = select('cj-sandbox-profile').value;
        const shared = input('cj-sched-mode-shared').checked ? select('cj-shared-schedule-select').value : '';
        if (!name) { error.textContent = 'Job identifier is required.'; return; }

        let targetPath: string;
        let jobType: string;
        if (kind === 'Bundle') {
            const rawUri = input('cj-bundle-uri').value.trim() || select('cj-bundle-select').value.trim();
            if (!rawUri) { error.textContent = 'Bundle URI or published bundle is required.'; return; }
            const parsed = parseBundleUri(rawUri);
            if (!parsed) {
                error.textContent = 'Bundle URI must match orch://<bundle>@<version>/<entryPath>.';
                return;
            }
            targetPath = parsed.version !== null
                ? formatBundleUri(parsed.bundleName, parsed.version, parsed.entryPath)
                : `orch://${parsed.bundleName}/${parsed.entryPath}`;
            jobType = 'Script';
        } else if (kind === 'Report') {
            targetPath = input('cj-report-path').value.trim();
            if (!targetPath) { error.textContent = 'Report target path is required.'; return; }
            jobType = 'REPORT';
        } else {
            targetPath = input('cj-script-path').value.trim() || select('cj-script-select').value;
            if (!targetPath) { error.textContent = 'Script path is required.'; return; }
            jobType = 'Script';
        }

        if (input('cj-sched-mode-shared').checked && !shared) { error.textContent = 'Choose the shared schedule to attach.'; return; }
        const body = {
            name, displayName: input('cj-display-name').value.trim() || name, description: input('cj-description').value.trim() || null,
            jobType, targetPath,
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
