/** UI actions bind to the job and version loaded when the user starts the action. */
export interface JobActionTarget { name: string; version: number }

export function jobVersion(value: unknown): number {
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 1)
        throw new Error('The current job version is unavailable. Refresh the job before changing it.');
    return value;
}

export async function requireMutationStatus(response: Response, status: number): Promise<void> {
    if (response.status === status) return;
    let body: unknown = await response.json().catch(() => null);
    // The Portal relays some service errors as a JSON string containing the service JSON.
    if (typeof body === 'string') {
        try { body = JSON.parse(body); } catch { body = null; }
    }
    const error = body !== null && typeof body === 'object' && !Array.isArray(body)
        ? (body as Record<string, unknown>).error : null;
    throw new Error(typeof error === 'string' && error.trim() ? error
        : `Unexpected response (${response.status}). The action was not confirmed.`);
}

interface ActionOptions {
    findTarget(name: string): JobActionTarget | null;
    setEnabled(target: JobActionTarget, enabled: boolean, signal: AbortSignal): Promise<Response>;
    deleteJob(target: JobActionTarget, signal: AbortSignal): Promise<Response>;
    kill(target: JobActionTarget, signal: AbortSignal): Promise<Response>;
    confirmDelete(name: string): Promise<boolean>;
    changed(action: 'toggle' | 'delete', target: JobActionTarget, signal: AbortSignal): Promise<void>;
    notify(message: string, failed: boolean): void;
}

export function createJobActionController(options: ActionOptions) {
    const lifetime = new AbortController();
    const pending = new Set<string>();
    return {
        async run(button: HTMLButtonElement): Promise<void> {
            const name = button.dataset.name, action = button.dataset.action;
            if (!name || lifetime.signal.aborted || pending.has(name)) return;
            if (action !== 'toggle' && action !== 'delete' && action !== 'kill') return;
            pending.add(name);
            button.disabled = true;
            try {
                const loaded = options.findTarget(name);
                if (!loaded) throw new Error('The job is no longer loaded. Refresh the job list.');
                const target = { name: loaded.name, version: jobVersion(loaded.version) };
                if (action === 'delete' && !await options.confirmDelete(name)) return;
                if (lifetime.signal.aborted) return;
                if (action === 'toggle') {
                    const enabled = button.dataset.enabled !== 'true';
                    await requireMutationStatus(await options.setEnabled(target, enabled, lifetime.signal), 204);
                    if (lifetime.signal.aborted) return;
                    options.notify(`Job '${name}' ${enabled ? 'enabled' : 'disabled'}.`, false);
                } else if (action === 'delete') {
                    await requireMutationStatus(await options.deleteJob(target, lifetime.signal), 200);
                    if (lifetime.signal.aborted) return;
                    options.notify(`Job '${name}' deleted.`, false);
                } else {
                    await requireMutationStatus(await options.kill(target, lifetime.signal), 200);
                    if (!lifetime.signal.aborted) options.notify(`Kill signal sent to '${name}'.`, false);
                    return;
                }
                await options.changed(action, target, lifetime.signal);
            } catch (error) {
                if (!lifetime.signal.aborted)
                    options.notify(`Error: ${error instanceof Error ? error.message : String(error)}`, true);
            } finally {
                pending.delete(name);
                if (!lifetime.signal.aborted && button.isConnected) button.disabled = false;
            }
        },
        dispose() { lifetime.abort(); }
    };
}

interface RunOptions {
    root: Document;
    trigger(name: string, variables: Record<string, string>, signal: AbortSignal): Promise<Response>;
    notify(message: string): void;
    refresh(): Promise<void>;
}

export function parseJobRunOverrides(rows: Iterable<{ name: string; value: string }>): Record<string, string> {
    const variables: Record<string, string> = Object.create(null);
    for (const row of rows) {
        let key = row.name.trim();
        if (!key && !row.value) continue;
        if (!key.startsWith('@')) key = '@' + key;
        if (!/^@[A-Za-z_][A-Za-z0-9_]*$/.test(key))
            throw new Error(`'${key}' is not a variable name. Use @ followed by a letter or underscore, then letters, numbers or underscores.`);
        variables[key] = row.value;
    }
    return variables;
}

export function createJobRunDialog(options: RunOptions) {
    const lifetime = new AbortController();
    const get = <T extends HTMLElement>(id: string, type: { new(): T }): T => {
        const element = options.root.getElementById(id);
        if (!(element instanceof type)) throw new Error(`Missing run dialog element: ${id}`);
        return element;
    };
    const modal = get('runJobModal', HTMLDivElement), title = get('runJobTitle', HTMLElement);
    const rows = get('runOverrideRows', HTMLElement), error = get('runJobError', HTMLElement);
    const submit = get('runJobSubmitBtn', HTMLButtonElement);
    let name: string | null = null, generation = 0, pending = false;
    function close() { generation++; name = null; modal.style.display = 'none'; }
    for (const id of ['runJobCloseBtn', 'runJobCancelBtn'])
        get(id, HTMLButtonElement).addEventListener('click', close, { signal: lifetime.signal });
    get('runAddOverrideBtn', HTMLButtonElement).addEventListener('click', () => {
        const row = options.root.createElement('div');
        row.className = 'run-override-row';
        row.innerHTML = '<div class="form-group"><label>Variable<input class="run-override-name" type="text" placeholder="@start_date" autocomplete="off"></label></div>'
            + '<div class="form-group"><label>Value<input class="run-override-value" type="text" placeholder="2026-08-01" autocomplete="off"></label></div>'
            + '<button class="btn-icon run-override-remove" type="button" title="Remove">✕</button>';
        row.querySelector('button')!.addEventListener('click', () => row.remove(), { signal: lifetime.signal });
        rows.appendChild(row);
    }, { signal: lifetime.signal });
    submit.addEventListener('click', async () => {
        if (pending || !name || lifetime.signal.aborted) return;
        const target = name, current = generation;
        let variables: Record<string, string>;
        try {
            variables = parseJobRunOverrides(Array.from(rows.querySelectorAll('.run-override-row'), row => ({
                name: row.querySelector<HTMLInputElement>('.run-override-name')!.value,
                value: row.querySelector<HTMLInputElement>('.run-override-value')!.value
            })));
        } catch (failure) {
            error.textContent = failure instanceof Error ? failure.message : String(failure);
            return;
        }
        pending = true; submit.disabled = true; error.textContent = '';
        try {
            await requireMutationStatus(await options.trigger(target, variables, lifetime.signal), 202);
            if (lifetime.signal.aborted || current !== generation) return;
            close();
            options.notify(`Job '${target}' triggered.`);
            await options.refresh();
        } catch (failure) {
            if (!lifetime.signal.aborted && current === generation)
                error.textContent = failure instanceof Error ? failure.message : String(failure);
        } finally {
            pending = false;
            if (!lifetime.signal.aborted) submit.disabled = false;
        }
    }, { signal: lifetime.signal });
    return {
        open(jobName: string) {
            if (lifetime.signal.aborted) return;
            generation++; name = jobName;
            title.textContent = `Run ${jobName}`;
            rows.replaceChildren(); error.textContent = '';
            submit.disabled = pending; modal.style.display = 'flex';
        },
        dispose() { close(); lifetime.abort(); }
    };
}
