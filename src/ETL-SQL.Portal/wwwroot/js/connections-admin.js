/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/connections-admin.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { bindMarkdownActions, renderMarkdown } from './markdown-renderer.js';
import { deniedState, failedState, installPortalStateStyles } from './portal-states.js';
import { installDialogAccessibility } from './dialog-a11y.js';
import { asError, isObject } from './http-error.js';
import { connectionWizardApi, createConnectionWizardLauncher } from './gateway-wizard.js';
import { parseConnectionOptions, readConnectionSummaries, readConnectionDetail, readConnectionImpact, readSharedConnectionDiagnostic, readConnectionVerification, readConnectionHelp, readConnectionExports, readConnectionImport } from './connection-responses.js';
import { PANEL_HTML, renderConnectionTable, renderConnectionDetail, renderConnectionImpact, renderSharedConnectionDiagnostic } from './connections-view.js';
const surfaces = new WeakMap();
/** Shared-catalog administration; every displayed outcome comes from a validated server response. */
export function createConnectionsAdmin({ host, connectionsApi, wizardApi = undefined }) {
    if (!host)
        throw new TypeError('Connection administration needs a host.');
    const root = host;
    surfaces.get(root)?.dispose();
    root.innerHTML = PANEL_HTML;
    const lifetime = new AbortController();
    const releaseDialogs = installDialogAccessibility(root);
    let disposed = false, busy = false, loaded = false, uncertain = false, exporting = false;
    let listVersion = 0, detailVersion = 0, helpVersion = 0, formVersion = 0, importVersion = 0;
    let listRequest = null, detailRequest = null, helpRequest = null;
    let entries = new Map();
    let editing = null;
    function element(id) {
        const result = root.querySelector(`#${id}`);
        if (!result)
            throw new TypeError(`Missing connection control: ${id}`);
        return result;
    }
    function input(id) {
        const result = element(id);
        if (!(result instanceof HTMLInputElement || result instanceof HTMLTextAreaElement || result instanceof HTMLSelectElement)) {
            throw new TypeError(`Invalid connection input: ${id}`);
        }
        return result;
    }
    function status(message) { if (!disposed)
        element('conn-status').textContent = message; }
    function error(id, message) {
        if (disposed)
            return;
        const box = element(id);
        box.textContent = message;
        box.classList.toggle('show', !!message);
    }
    function updateControls() {
        if (disposed)
            return;
        for (const button of root.querySelectorAll('#conn-saveBtn, #conn-importRunBtn, #conn-wizardBtn, button[data-act="verify"], button[data-act="enable"], button[data-act="disable"], button[data-act="delete"]')) {
            button.disabled = busy || uncertain || !loaded;
        }
        element('conn-exportBtn').toggleAttribute('disabled', exporting);
    }
    function unconfirmed() {
        uncertain = true;
        error('conn-error', 'Connection mutation outcome is unconfirmed. Refresh the catalog to inspect it before retrying.');
        status('Connection outcome unconfirmed.');
        updateControls();
    }
    function canMutate() { return !disposed && loaded && !busy && !uncertain; }
    function knownRejection(reason) {
        return isObject(reason) && typeof reason.status === 'number' && [400, 401, 403, 404, 409].includes(reason.status);
    }
    function verificationCell(alias) {
        return [...root.querySelectorAll('tr[data-alias]')].find(row => row.dataset.alias === alias)
            ?.querySelector('.conn-row-status') ?? null;
    }
    function closeDetail() {
        detailVersion++;
        detailRequest?.abort();
        detailRequest = null;
        element('conn-detailCard').style.display = 'none';
        element('conn-detailBody').replaceChildren();
    }
    async function load(manual = false) {
        if (disposed)
            return;
        const generation = ++listVersion, request = new AbortController();
        listRequest?.abort();
        listRequest = request;
        loaded = false;
        entries.clear();
        closeDetail();
        status('Loading connection catalog…');
        updateControls();
        root.setAttribute('aria-busy', 'true');
        try {
            const value = await connectionsApi.list(request.signal);
            if (disposed || generation !== listVersion)
                return;
            const rows = readConnectionSummaries(value);
            entries = new Map(rows.map(entry => [entry.alias, entry]));
            loaded = true;
            if (manual && !busy) {
                uncertain = false;
                error('conn-error', '');
            }
            element('conn-tableWrap').innerHTML = renderConnectionTable(rows);
            status(uncertain ? 'Connection outcome unconfirmed. Inspect the catalog with Refresh.' : `${rows.length} shared connection(s).`);
        }
        catch (reason) {
            if (disposed || generation !== listVersion)
                return;
            installPortalStateStyles();
            const denied = isObject(reason) && (reason.status === 401 || reason.status === 403);
            element('conn-tableWrap').innerHTML = denied
                ? deniedState({ title: 'You do not have access to the connection catalog.', roles: ['Admin'] })
                : failedState({ title: 'The connection catalog is unavailable.', body: 'The catalog could not be loaded or validated.', retryId: 'conn-retry' });
            status(denied ? 'Connection catalog access denied.' : 'Connection catalog unavailable.');
        }
        finally {
            if (!disposed && generation === listVersion) {
                root.setAttribute('aria-busy', 'false');
                updateControls();
            }
        }
    }
    async function loadHelp(type) {
        const generation = ++helpVersion, request = new AbortController();
        helpRequest?.abort();
        helpRequest = request;
        element('conn-help-modal').style.display = 'none';
        element('conn-help-box').style.display = type ? '' : 'none';
        element('conn-help-title-text').textContent = `${type} Connection Options`;
        element('conn-help-content').textContent = 'Loading help…';
        if (!type)
            return;
        try {
            const value = await connectionsApi.getHelp(type, request.signal);
            if (disposed || generation !== helpVersion)
                return;
            element('conn-help-content').innerHTML = renderMarkdown(readConnectionHelp(value).content);
            bindMarkdownActions(element('conn-help-content'));
        }
        catch {
            if (!disposed && generation === helpVersion)
                element('conn-help-content').textContent = 'Connector documentation is unavailable.';
        }
    }
    async function showDetail(alias, kind) {
        if (disposed || !entries.has(alias))
            return;
        const generation = ++detailVersion, revision = formVersion, request = new AbortController();
        detailRequest?.abort();
        detailRequest = request;
        element('conn-detailAlias').textContent = kind === 'detail' ? alias : `${alias} — ${kind === 'test' ? 'connection test' : 'impact'}`;
        element('conn-detailBody').textContent = kind === 'test' ? 'Running connection diagnostic…' : 'Loading connection details…';
        element('conn-detailCard').style.display = '';
        try {
            const value = await connectionsApi[kind](alias, request.signal);
            if (disposed || generation !== detailVersion)
                return;
            if (kind === 'impact')
                element('conn-detailBody').innerHTML = renderConnectionImpact(readConnectionImpact(value, alias));
            else if (kind === 'test')
                element('conn-detailBody').innerHTML = renderSharedConnectionDiagnostic(readSharedConnectionDiagnostic(value, alias));
            else {
                const detail = readConnectionDetail(value, alias);
                element('conn-detailBody').innerHTML = renderConnectionDetail(detail);
                if (revision !== formVersion)
                    return;
                editing = { alias, connectorType: detail.summary.connectorType, gateway: detail.gateway ?? null };
                const selector = input('conn-type');
                if (selector instanceof HTMLSelectElement && ![...selector.options].some(option => option.value === detail.summary.connectorType)) {
                    selector.add(new Option(detail.summary.connectorType, detail.summary.connectorType));
                }
                input('conn-alias').value = alias;
                selector.value = detail.summary.connectorType;
                input('conn-scope').value = detail.summary.environmentScope ?? '';
                input('conn-target').value = detail.target ?? '';
                input('conn-options').value = Object.entries(detail.options).map(([key, value]) => `${key}=${value}`).join('\n');
                input('conn-sensitive').value = detail.sensitiveFields.join('\n');
                element('conn-bindingHint').textContent = editing.gateway
                    ? `Gateway binding retained: ${editing.gateway.gatewayId} / ${editing.gateway.resourceId}. Use the wizard to select a different binding.`
                    : 'Masked display values must be replaced with a valid value or reference before saving.';
                formVersion++;
                void loadHelp(detail.summary.connectorType);
            }
        }
        catch (reason) {
            if (disposed || generation !== detailVersion)
                return;
            const disabled = isObject(reason) && isObject(reason.body) && reason.body.status === 'disabled';
            element('conn-detailBody').textContent = disabled ? 'This connection is disabled — enable it before testing.'
                : kind === 'test' ? 'Connection diagnostic unavailable.' : `Connection ${kind} unavailable.`;
        }
    }
    /** An exact 204 is enforced by the client; injected clients must preserve its void acknowledgement. */
    function acknowledge(value) {
        if (value !== undefined)
            throw new TypeError('Connection mutation outcome is unconfirmed.');
    }
    async function save(alias, body, signal) {
        if (!canMutate())
            throw new Error('Refresh the connection catalog before saving.');
        busy = true;
        updateControls();
        error('conn-error', '');
        status('Saving connection…');
        try {
            acknowledge(await connectionsApi.set(alias, body, signal));
            if (disposed || signal.aborted)
                throw new Error('Connection administration closed.');
        }
        catch (reason) {
            if (!disposed) {
                if (knownRejection(reason)) {
                    error('conn-error', asError(reason).message);
                    status('Connection save rejected.');
                }
                else
                    unconfirmed();
            }
            throw reason;
        }
        finally {
            busy = false;
            updateControls();
        }
    }
    const wizard = createConnectionWizardLauncher(document.body, message => error('conn-error', message), alias => { void load().then(() => { if (!disposed && loaded)
        status(`Shared connection '${alias}' saved.`); }); }, { ...(wizardApi ?? connectionWizardApi), save });
    async function rowAction(alias, action) {
        if (action === 'detail' || action === 'impact' || action === 'test') {
            await showDetail(alias, action);
            return;
        }
        if (!canMutate() || !entries.has(alias))
            return;
        const generation = listVersion;
        busy = true;
        updateControls();
        error('conn-error', '');
        let sent = false;
        try {
            if (action === 'disable' || action === 'delete') {
                if (!window.ETLSQLFeedback)
                    throw new Error('Connection action confirmation is unavailable.');
                const confirmed = await window.ETLSQLFeedback.confirm(`${action === 'delete' ? 'Permanently delete' : 'Disable'} shared connection '${alias}'?`, {
                    title: `${action === 'delete' ? 'Delete' : 'Disable'} shared connection`,
                    confirmLabel: action === 'delete' ? 'Delete connection' : 'Disable connection', danger: true,
                    ...(action === 'disable' ? { impact: `SHARED:${alias} will fail until it is re-enabled.` } : {}),
                    auditAction: `admin.connection.${action}`
                });
                if (!confirmed || disposed || generation !== listVersion)
                    return;
            }
            if (!['verify', 'disable', 'enable', 'delete'].includes(action) || disposed || generation !== listVersion)
                return;
            sent = true;
            if (action === 'verify') {
                const previous = verificationCell(alias);
                if (previous)
                    previous.textContent = 'Verifying…';
                const value = readConnectionVerification(await connectionsApi.verify(alias, lifetime.signal), alias);
                if (disposed || generation !== listVersion)
                    return;
                const cell = verificationCell(alias);
                if (cell)
                    cell.innerHTML = `<span class="badge badge-ok">Verified (${value.secretReferences} secret ref(s))</span>`;
            }
            else {
                const operation = action === 'delete' ? connectionsApi.remove : connectionsApi[action];
                acknowledge(await operation.call(connectionsApi, alias, lifetime.signal));
                if (disposed)
                    return;
                if (generation !== listVersion) {
                    unconfirmed();
                    return;
                }
                await load();
                if (loaded)
                    status(`Shared connection '${alias}' ${action === 'delete' ? 'deleted' : action + 'd'}.`);
            }
        }
        catch (reason) {
            if (disposed)
                return;
            if (action === 'verify' && generation === listVersion) {
                const cell = verificationCell(alias);
                if (cell)
                    cell.textContent = 'Verification unavailable';
            }
            if (sent && !knownRejection(reason))
                unconfirmed();
            else
                error('conn-error', action === 'verify' ? 'Connection verification failed. Inspect its status and secret references.' : asError(reason).message);
        }
        finally {
            busy = false;
            updateControls();
        }
    }
    root.addEventListener('click', event => {
        if (!(event.target instanceof Element))
            return;
        const button = event.target.closest('button[data-act]');
        const alias = button?.closest('tr[data-alias]')?.dataset.alias;
        if (button && alias)
            void rowAction(alias, button.dataset.act ?? '');
        if (event.target.closest('#conn-retry'))
            void load(true);
    }, { signal: lifetime.signal });
    function on(id, handler) { element(id).addEventListener('click', handler, { signal: lifetime.signal }); }
    on('conn-detailCloseBtn', closeDetail);
    on('conn-refreshBtn', () => { void load(true); });
    on('conn-saveBtn', () => {
        if (!canMutate())
            return;
        let body;
        const alias = input('conn-alias').value.trim(), connectorType = input('conn-type').value.trim();
        try {
            if (!alias || !connectorType)
                throw new TypeError('Both an alias and a connector type are required.');
            const options = parseConnectionOptions(input('conn-options').value), target = input('conn-target').value.trim() || null;
            if ((target ?? '').includes('********') || Object.values(options).some(value => value.includes('********'))) {
                throw new TypeError('Replace masked display values with valid values or references before saving.');
            }
            if (editing?.gateway && (editing.alias !== alias || editing.connectorType !== connectorType || target || Object.keys(options).length)) {
                throw new TypeError('Use the Connection Wizard to change a gateway alias, connector, or resource binding.');
            }
            body = { connectorType, target, options, environmentScope: input('conn-scope').value.trim() || null,
                sensitiveFields: [...new Set(input('conn-sensitive').value.split(/[\n,]/).map(value => value.trim().toUpperCase()).filter(Boolean))], gateway: editing?.gateway ?? null };
        }
        catch (reason) {
            error('conn-error', asError(reason).message);
            return;
        }
        void save(alias, body, lifetime.signal).then(async () => {
            if (disposed)
                return;
            await load();
            if (loaded)
                status(`Shared connection '${alias}' saved.`);
        }).catch(() => { });
    });
    on('conn-wizardBtn', () => { if (canMutate())
        void wizard.launch(); });
    on('conn-importBtn', () => { importVersion++; element('conn-importCard').style.display = ''; });
    on('conn-importCancelBtn', () => { importVersion++; element('conn-importCard').style.display = 'none'; input('conn-importJson').value = ''; error('conn-importError', ''); });
    input('conn-importJson').addEventListener('input', () => { importVersion++; }, { signal: lifetime.signal });
    on('conn-importRunBtn', () => { void importEntries(); });
    async function importEntries() {
        if (!canMutate())
            return;
        const generation = importVersion;
        error('conn-importError', '');
        let imports;
        try {
            imports = readConnectionExports(JSON.parse(input('conn-importJson').value));
            if (!imports.length)
                throw new TypeError('Supply at least one connection definition.');
        }
        catch {
            error('conn-importError', 'Invalid import JSON. Supply a nonempty array of complete, unique connection definitions.');
            return;
        }
        busy = true;
        updateControls();
        status('Importing connections…');
        try {
            const result = readConnectionImport(await connectionsApi.importAll(imports, lifetime.signal), imports.length);
            if (disposed)
                return;
            if (generation !== importVersion) {
                unconfirmed();
                return;
            }
            input('conn-importJson').value = '';
            element('conn-importCard').style.display = 'none';
            await load();
            if (loaded)
                status(`Imported: ${result.created} created, ${result.updated} updated.`);
        }
        catch (reason) {
            if (!disposed) {
                if (knownRejection(reason)) {
                    error('conn-importError', asError(reason).message);
                    status('Connection import rejected.');
                }
                else
                    unconfirmed();
            }
        }
        finally {
            busy = false;
            updateControls();
        }
    }
    on('conn-exportBtn', () => { void exportEntries(); });
    async function exportEntries() {
        if (disposed || exporting)
            return;
        exporting = true;
        updateControls();
        try {
            const values = readConnectionExports(await connectionsApi.exportAll(lifetime.signal));
            if (disposed)
                return;
            const url = URL.createObjectURL(new Blob([JSON.stringify(values, null, 2)], { type: 'application/json' }));
            try {
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = 'shared-connections.json';
                anchor.click();
            }
            finally {
                URL.revokeObjectURL(url);
            }
            status(`Exported ${values.length} ${values.length === 1 ? 'entry' : 'entries'} (metadata only).`);
        }
        catch {
            if (!disposed)
                status('Connection export unavailable.');
        }
        finally {
            exporting = false;
            updateControls();
        }
    }
    for (const id of ['conn-alias', 'conn-type', 'conn-scope', 'conn-target', 'conn-options', 'conn-sensitive']) {
        input(id).addEventListener('input', () => { formVersion++; }, { signal: lifetime.signal });
    }
    input('conn-type').addEventListener('change', () => { formVersion++; void loadHelp(input('conn-type').value); }, { signal: lifetime.signal });
    on('conn-help-expand-btn', () => {
        element('conn-help-modal-title').textContent = `${input('conn-type').value} Connection Guidelines`;
        element('conn-help-modal-content').innerHTML = element('conn-help-content').innerHTML;
        bindMarkdownActions(element('conn-help-modal-content'));
        element('conn-help-modal').style.display = 'flex';
    });
    const closeHelp = () => { element('conn-help-modal').style.display = 'none'; };
    on('conn-help-modal-close', closeHelp);
    on('conn-help-modal-close-btn', closeHelp);
    element('conn-help-modal').addEventListener('click', event => { if (event.target === element('conn-help-modal'))
        closeHelp(); }, { signal: lifetime.signal });
    void loadHelp(input('conn-type').value);
    updateControls();
    const surface = { load: () => load(), dispose() {
            if (disposed)
                return;
            closeDetail();
            closeHelp();
            disposed = true;
            lifetime.abort();
            listRequest?.abort();
            helpRequest?.abort();
            wizard.dispose();
            releaseDialogs?.();
            entries.clear();
            editing = null;
            input('conn-importJson').value = '';
            input('conn-target').value = '';
            input('conn-options').value = '';
            root.setAttribute('aria-busy', 'false');
            surfaces.delete(root);
        } };
    surfaces.set(root, surface);
    return surface;
}
