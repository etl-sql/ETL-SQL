/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/reader-subscriptions.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { asError } from './http-error.js';
import { adminSubscriptionsApi, AdminSubscriptionOutcomeUnconfirmed } from './admin-subscriptions-api.js';
import { readAdminSubscription } from './admin-subscriptions-responses.js';
import { esc, readSmtpAliases, readSubscriptionList, renderSubscriptionRows } from './reader-subscriptions-view.js';
import { createSubscriptionParameterEditor } from './admin-subscription-parameters.js';
import { readParameterDraft, renderParameterInputs } from './subscription-parameter-fields.js';
import { renderSubscriptionHistory } from './subscription-history-ui.js';
async function readJson(url, fallback, signal) {
    const response = await apiFetch(url, { signal, cache: 'no-store' });
    if (response.status !== 200)
        throw await responseError(response, fallback);
    return response.json();
}
export function createReaderSubscriptions(options) {
    const { root } = options;
    const lifetime = new AbortController();
    const get = (id, type) => {
        const found = root.getElementById(id);
        if (!(found instanceof type))
            throw new Error(`Missing subscription element: ${id}`);
        return found;
    };
    const box = (id) => get(id, HTMLElement);
    const main = box('mainContent');
    const alive = () => !lifetime.signal.aborted;
    const pending = new Set();
    let subscriptions = null;
    let shown = false, listRead = 0;
    let listRequest = null;
    // ── My Subscriptions ──────────────────────────────────────────────────────
    function paint() {
        if (!shown || subscriptions === null)
            return;
        const active = subscriptions.filter(item => item.isActive).length;
        main.innerHTML = `<div class="library-toolbar">
            <div class="library-title"><span class="library-kicker">Delivery</span><h2>My Subscriptions</h2></div>
            <span class="badge badge-ok">${active} active</span><span class="badge badge-refresh">${subscriptions.length} total</span>
            <div class="library-toolbar-spacer"></div>
            <button class="btn btn-outline" id="refreshSubscriptionsBtn" type="button" data-action="refresh">Refresh</button>
          </div>
          <div class="subs-table-wrap" data-subscriptions-state="ready"><table class="data-table">
            <thead><tr><th>Subscription</th><th>Delivery</th><th>Schedule</th><th>Parameters</th><th>Status</th><th>Actions</th></tr></thead>
            <tbody>${renderSubscriptionRows(subscriptions, pending) || `<tr><td colspan="6"><div class="empty-state empty-state-table">
              <div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
              <h2>No subscriptions yet</h2><p>Open a report and choose Subscribe to schedule delivery.</p></div></td></tr>`}</tbody>
          </table></div>`;
    }
    async function load() {
        if (!alive() || !shown)
            return;
        const read = ++listRead;
        listRequest?.abort();
        const request = listRequest = new AbortController();
        try {
            const loaded = readSubscriptionList(await readJson('/api/subscriptions', 'Your subscriptions are unavailable.', request.signal));
            if (!alive() || !shown || read !== listRead)
                return;
            subscriptions = loaded;
            editor.reconcile(new Map(loaded.map(item => [item.id, item])));
            paint();
        }
        catch (failure) {
            if (!alive() || !shown || read !== listRead)
                return;
            // Rows from an earlier read are dropped: they can no longer be vouched for.
            subscriptions = null;
            main.innerHTML = `<div class="empty-state empty-state-panel empty-state-error" role="alert" data-subscriptions-state="failed">
                <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div>
                <h2>Subscriptions did not load</h2><p>${esc(asError(failure).message)}</p>
                <button class="btn btn-outline" type="button" data-action="refresh">Try again</button></div>`;
        }
    }
    /** One confirmed change per subscription. An unconfirmed outcome is reported as exactly that. */
    async function change(id, title, work) {
        if (!alive() || pending.has(id))
            return 'Another change to this subscription is still in progress.';
        pending.add(id);
        paint();
        let reason = null;
        try {
            const done = await work();
            if (alive())
                options.notify(done, { title, tone: 'success' });
        }
        catch (failure) {
            reason = failure instanceof AdminSubscriptionOutcomeUnconfirmed || failure instanceof PortalApiError
                ? failure.message : asError(failure).message;
        }
        finally {
            pending.delete(id);
        }
        // Reload after a failure too: the row on screen may be the version the server refused.
        await load();
        return reason;
    }
    const target = (item) => ({ id: item.id, reportId: item.reportId, version: item.version });
    const editor = createSubscriptionParameterEditor(root, alive, () => true, async (subscription, parameters, accepted, failed) => {
        const reason = await change(subscription.id, 'Subscription parameters saved', async () => {
            await adminSubscriptionsApi.update(subscription, { parameters });
            return 'Future deliveries will use the new parameter values.';
        });
        if (reason)
            failed(reason);
        else
            accepted();
    });
    // ── Delivery history ──────────────────────────────────────────────────────
    const historyModal = box('subscriptionHistoryModal'), historyBody = box('subscriptionHistoryBody');
    let historyRequest = null;
    function closeHistory() { historyRequest?.abort(); historyRequest = null; historyModal.style.display = 'none'; }
    async function showHistory(item) {
        historyRequest?.abort();
        const request = historyRequest = new AbortController();
        box('subscriptionHistoryTitle').textContent = `${item.name || item.reportName} Delivery History`;
        historyBody.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading delivery history…</span></div>';
        historyModal.style.display = 'flex';
        try {
            const history = await adminSubscriptionsApi.history(item.id, request.signal);
            if (alive() && historyRequest === request)
                historyBody.innerHTML = renderSubscriptionHistory(history, { esc, formatDate: value => value ? new Date(value).toLocaleString() : '—' });
        }
        catch (failure) {
            if (alive() && historyRequest === request)
                historyBody.innerHTML = `<div class="empty-state" role="alert">Delivery history could not be loaded: ${esc(asError(failure).message)}</div>`;
        }
    }
    get('subscriptionHistoryCloseBtn', HTMLButtonElement).addEventListener('click', closeHistory, { signal: lifetime.signal });
    main.addEventListener('click', async (event) => {
        const button = event.target instanceof Element ? event.target.closest('button[data-action]') : null;
        if (!shown || !button || button.disabled)
            return;
        if (button.dataset.action === 'refresh') {
            await load();
            return;
        }
        const item = subscriptions?.find(candidate => candidate.id === Number(button.dataset.id));
        if (!item)
            return;
        const report = (reason, title) => { if (reason && alive())
            options.notify(reason, { title, tone: 'error' }); };
        if (button.dataset.action === 'history')
            void showHistory(item);
        else if (button.dataset.action === 'edit-params')
            void editor.open(item);
        else if (button.dataset.action === 'toggle') {
            const isActive = !item.isActive;
            report(await change(item.id, isActive ? 'Subscription resumed' : 'Subscription paused', async () => {
                await adminSubscriptionsApi.update(target(item), { isActive });
                return isActive ? 'Deliveries will resume on the next scheduled run.' : 'No deliveries will be sent while it is paused.';
            }), 'Subscription not updated');
        }
        else if (button.dataset.action === 'delete') {
            if (!await options.confirm('Delete this subscription?', { title: 'Delete subscription',
                impact: 'This stops future deliveries but does not remove generated report snapshots.',
                confirmLabel: 'Delete subscription', danger: true, auditAction: 'subscription.delete' }))
                return;
            report(await change(item.id, 'Subscription deleted', async () => {
                await adminSubscriptionsApi.delete(target(item));
                return 'Future deliveries are cancelled.';
            }), 'Subscription not deleted');
        }
    }, { signal: lifetime.signal });
    // ── Subscribe dialog ──────────────────────────────────────────────────────
    const modal = box('subscribeModal'), error = box('sub-error'), save = get('sub-saveBtn', HTMLButtonElement);
    const format = get('sub-format', HTMLSelectElement), smtp = get('sub-smtp', HTMLSelectElement);
    const field = (id) => {
        const found = root.getElementById(id);
        if (!(found instanceof HTMLInputElement || found instanceof HTMLSelectElement))
            throw new Error(`Missing subscription field: ${id}`);
        return found;
    };
    let opening = 0, saving = false, reportId = null;
    let inputs = null, aliasesReady = false;
    let dialogRequest = null;
    function fail(message) { error.textContent = message; error.classList.toggle('show', !!message); }
    function controls() {
        box('sub-smtp-group').style.display = format.value === 'Link' ? 'none' : '';
        // Parameters must be known before saving; a delivery connection is needed unless the format is a link.
        save.disabled = saving || reportId === null || inputs === null || (format.value !== 'Link' && !aliasesReady);
    }
    function closeSubscribe() { opening++; dialogRequest?.abort(); dialogRequest = null; reportId = null; inputs = null; modal.style.display = 'none'; }
    async function subscribe(id) {
        if (!alive())
            return;
        const opened = ++opening;
        dialogRequest?.abort();
        const request = dialogRequest = new AbortController();
        reportId = id;
        inputs = null;
        aliasesReady = false;
        for (const [name, value] of [['sub-name', ''], ['sub-schedule', 'Daily'], ['sub-attime', ''], ['sub-format', 'PDF'], ['sub-email', '']])
            field(name).value = value;
        fail('');
        smtp.replaceChildren(new Option('Loading connections…', ''));
        smtp.disabled = true;
        const section = box('sub-params-section'), fields = box('sub-params-fields');
        section.style.display = '';
        fields.textContent = 'Loading parameters…';
        controls();
        modal.style.display = 'flex';
        const [aliases, definitions] = await Promise.allSettled([
            readJson('/api/smtp-aliases', 'Delivery connections are unavailable.', request.signal).then(readSmtpAliases),
            adminSubscriptionsApi.parameters(id, request.signal)
        ]);
        if (!alive() || opened !== opening)
            return;
        const problems = [];
        if (aliases.status === 'fulfilled') {
            aliasesReady = aliases.value.length > 0;
            smtp.replaceChildren(...(aliases.value.length ? aliases.value.map(alias => new Option(alias, alias))
                : [new Option('No SMTP connections configured', '')]));
            smtp.disabled = !aliasesReady;
            if (!aliasesReady)
                problems.push('No delivery connection is configured, so only the Link format can be scheduled.');
        }
        else {
            smtp.replaceChildren(new Option('Connections unavailable', ''));
            problems.push(`Delivery connections could not be loaded: ${asError(aliases.reason).message} Only the Link format can be scheduled.`);
        }
        if (definitions.status === 'fulfilled') {
            inputs = renderParameterInputs(root, fields, definitions.value, {}, `subscribe-parameter-${opened}`, lifetime.signal);
            section.style.display = definitions.value.length ? '' : 'none';
        }
        else {
            fields.textContent = 'Parameter definitions could not be loaded. Close and reopen this dialog to retry.';
            problems.push(`This report's parameters could not be loaded: ${asError(definitions.reason).message}`);
        }
        fail(problems.join(' '));
        controls();
    }
    format.addEventListener('change', controls, { signal: lifetime.signal });
    get('sub-cancelBtn', HTMLButtonElement).addEventListener('click', closeSubscribe, { signal: lifetime.signal });
    save.addEventListener('click', async () => {
        if (saving || !alive() || reportId === null || inputs === null)
            return;
        const draft = readParameterDraft(inputs);
        if ('missing' in draft) {
            fail(`${draft.missing.parameter.name} requires a value.`);
            draft.missing.input.focus();
            return;
        }
        const body = {
            reportId, name: field('sub-name').value.trim() || null, schedule: field('sub-schedule').value, format: format.value,
            smtpAlias: format.value !== 'Link' ? smtp.value || null : null, recipientEmail: field('sub-email').value.trim() || null,
            atTime: field('sub-attime').value || null, parameters: Object.keys(draft.values).length ? { ...draft.values } : null
        };
        const opened = opening;
        saving = true;
        fail('');
        controls();
        try {
            const response = await apiFetch('/api/subscriptions', { method: 'POST', body, signal: lifetime.signal });
            if (response.status !== 201)
                throw await responseError(response, `Unexpected response (${response.status}). The subscription was not confirmed.`);
            const created = readAdminSubscription(await response.json());
            if (created.reportId !== body.reportId)
                throw new TypeError('The subscription response is unavailable or incomplete.');
            if (!alive())
                return;
            if (opened === opening)
                closeSubscribe();
            options.notify('Future deliveries are now scheduled.', { title: 'Subscription created', tone: 'success', auditAction: 'subscription.create' });
            if (shown)
                await load();
        }
        catch (failure) {
            if (!alive())
                return;
            if (opened === opening)
                fail(asError(failure).message);
            else
                options.notify(asError(failure).message, { title: 'Subscription not created', tone: 'error' });
        }
        finally {
            saving = false;
            if (alive())
                controls();
        }
    }, { signal: lifetime.signal });
    return {
        subscribe,
        /** Takes over the main area with the reader's subscriptions. */
        async show() {
            if (!alive())
                return;
            shown = true;
            subscriptions = null;
            options.entered();
            main.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading subscriptions…</span></div>';
            await load();
        },
        /** Another view owns the main area now; a list read still in flight must not repaint it. */
        leave() { shown = false; listRead++; listRequest?.abort(); },
        dispose() {
            shown = false;
            listRead++;
            listRequest?.abort();
            closeSubscribe();
            closeHistory();
            editor.dispose();
            lifetime.abort();
        }
    };
}
