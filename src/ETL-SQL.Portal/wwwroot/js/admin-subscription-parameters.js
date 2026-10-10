/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-subscription-parameters.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { adminSubscriptionsApi } from './admin-subscriptions-api.js';
import { asError } from './http-error.js';
import { readParameterDraft, renderParameterInputs } from './subscription-parameter-fields.js';
export function createSubscriptionParameterEditor(doc, alive, canWrite, saveDraft) {
    function element(id, kind) {
        const value = doc.getElementById(id);
        if (!(value instanceof kind))
            throw new Error(`Missing subscription parameter element: ${id}`);
        return value;
    }
    const modal = element('editParamsModal', HTMLDivElement), fields = element('editParamsFields', HTMLDivElement);
    const title = element('editParamsTitle', HTMLHeadingElement), error = element('editParamsError', HTMLDivElement);
    const save = element('editParamsSaveBtn', HTMLButtonElement), cancel = element('editParamsCancelBtn', HTMLButtonElement);
    const listeners = new AbortController();
    let sequence = 0, target = null, request = null;
    let inputs = null;
    let opener = null;
    const visible = () => modal.style.display !== 'none';
    const current = (opened) => alive() && visible() && sequence === opened;
    function message(value) { error.textContent = value; error.classList.add('show'); }
    function controls() { save.disabled = !alive() || !canWrite() || !target || !inputs; }
    function close() {
        ++sequence;
        request?.abort();
        request = null;
        target = null;
        inputs = null;
        modal.style.display = 'none';
        fields.replaceChildren();
        controls();
        if (alive() && opener?.isConnected)
            opener.focus();
        opener = null;
    }
    async function open(subscription) {
        if (!alive())
            return;
        request?.abort();
        const opened = ++sequence, pending = new AbortController();
        request = pending;
        target = { id: subscription.id, reportId: subscription.reportId, version: subscription.version };
        inputs = null;
        const values = structuredClone(subscription.parameters ?? {});
        opener = doc.activeElement instanceof HTMLElement ? doc.activeElement : null;
        title.textContent = `Edit Parameters — ${subscription.name || subscription.reportName}`;
        error.textContent = '';
        error.classList.remove('show');
        modal.style.display = 'flex';
        fields.textContent = 'Loading parameters…';
        controls();
        cancel.focus();
        try {
            const definitions = await adminSubscriptionsApi.parameters(subscription.reportId, pending.signal);
            if (!current(opened))
                return;
            inputs = renderParameterInputs(doc, fields, definitions, values, `subscription-parameter-${opened}`, listeners.signal);
            if (!definitions.length)
                fields.textContent = 'This report has no input parameters.';
            inputs[0]?.input.focus();
        }
        catch (failure) {
            if (current(opened)) {
                fields.textContent = 'Parameter definitions could not be loaded. Close and reopen this editor to retry.';
                message(asError(failure).message);
            }
        }
        finally {
            if (current(opened)) {
                request = null;
                controls();
            }
        }
    }
    async function submit() {
        if (!alive() || !visible() || !canWrite() || !target || !inputs)
            return;
        const opened = sequence, snapshot = { ...target };
        error.classList.remove('show');
        const draft = readParameterDraft(inputs);
        if ('missing' in draft) {
            message(`${draft.missing.parameter.name} requires a value.`);
            draft.missing.input.focus();
            return;
        }
        // Capture the complete draft before a write or any other asynchronous work begins.
        await saveDraft(snapshot, { ...draft.values }, () => { if (current(opened))
            close(); }, value => { if (current(opened))
            message(value); });
    }
    save.addEventListener('click', () => { void submit(); }, { signal: listeners.signal });
    cancel.addEventListener('click', close, { signal: listeners.signal });
    modal.addEventListener('keydown', event => { if (event.key === 'Escape')
        close(); }, { signal: listeners.signal });
    return { open, controls, reconcile(records) {
            if (!target || !records.has(target.id))
                return;
            const latest = records.get(target.id);
            if (!latest || latest.reportId !== target.reportId) {
                target = null;
                message('This subscription is no longer available. Close the editor to continue.');
            }
            else {
                target = { id: latest.id, reportId: latest.reportId, version: latest.version };
                message('The latest subscription version is loaded. Review your retained draft before saving another change.');
            }
            controls();
        }, dispose() { close(); listeners.abort(); } };
}
