import { adminSubscriptionsApi } from './admin-subscriptions-api.js';
import { asError } from './http-error.js';
import type { SubscriptionTarget } from './admin-subscriptions-responses.js';

export function createSubscriptionParameterEditor(doc: Document, alive: () => boolean, canWrite: () => boolean,
  saveDraft: (target: SubscriptionTarget, parameters: Record<string, string>, accepted: () => void,
    failed: (message: string) => void) => Promise<void>) {
  function element<T extends HTMLElement>(id: string, kind: new (...args: never[]) => T): T {
    const value = doc.getElementById(id);
    if (!(value instanceof kind)) throw new Error(`Missing subscription parameter element: ${id}`);
    return value;
  }
  const modal = element('editParamsModal', HTMLDivElement), fields = element('editParamsFields', HTMLDivElement);
  const title = element('editParamsTitle', HTMLHeadingElement), error = element('editParamsError', HTMLDivElement);
  const save = element('editParamsSaveBtn', HTMLButtonElement), cancel = element('editParamsCancelBtn', HTMLButtonElement);
  const listeners = new AbortController();
  let sequence = 0, target: SubscriptionTarget | null = null, request: AbortController | null = null;
  let inputs: { parameter: ReportParameterDto; input: HTMLInputElement }[] | null = null;
  let opener: HTMLElement | null = null;
  const visible = () => modal.style.display !== 'none';
  const current = (opened: number) => alive() && visible() && sequence === opened;
  function message(value: string) { error.textContent = value; error.classList.add('show'); }
  function controls() { save.disabled = !alive() || !canWrite() || !target || !inputs; }
  function close() {
    ++sequence; request?.abort(); request = null; target = null; inputs = null;
    modal.style.display = 'none'; fields.replaceChildren(); controls();
    if (alive() && opener?.isConnected) opener.focus();
    opener = null;
  }
  async function open(subscription: SubscriptionDto) {
    if (!alive()) return;
    request?.abort();
    const opened = ++sequence, pending = new AbortController(); request = pending;
    target = { id: subscription.id, reportId: subscription.reportId, version: subscription.version }; inputs = null;
    const values = structuredClone(subscription.parameters ?? {});
    opener = doc.activeElement instanceof HTMLElement ? doc.activeElement : null;
    title.textContent = `Edit Parameters — ${subscription.name || subscription.reportName}`;
    error.textContent = ''; error.classList.remove('show');
    modal.style.display = 'flex'; fields.textContent = 'Loading parameters…'; controls(); cancel.focus();
    try {
      const definitions = await adminSubscriptionsApi.parameters(subscription.reportId, pending.signal);
      if (!current(opened)) return;
      fields.replaceChildren(); inputs = [];
      if (!definitions.length) fields.textContent = 'This report has no input parameters.';
      definitions.forEach((parameter, index) => {
        const row = doc.createElement('div'); row.className = 'param-row';
        const heading = doc.createElement('div'); heading.className = 'param-heading';
        const input = doc.createElement('input'); input.className = 'param-input'; input.type = 'text';
        input.id = `subscription-parameter-${opened}-${index}`;
        input.value = Object.hasOwn(values, parameter.name) ? values[parameter.name] : parameter.default ?? '';
        const label = doc.createElement('label'); label.className = 'param-label'; label.htmlFor = input.id; label.textContent = parameter.name;
        const type = doc.createElement('span'); type.className = 'param-type'; type.textContent = parameter.type;
        const requirement = doc.createElement('span'); requirement.className = parameter.required ? 'required-marker' : 'optional-marker';
        requirement.textContent = parameter.required ? 'Required' : 'Optional';
        input.setAttribute('aria-required', String(parameter.required));
        heading.append(label, type, requirement); row.append(heading);
        if (parameter.description) {
          const hint = doc.createElement('div'); hint.className = 'param-hint'; hint.id = `${input.id}-hint`;
          hint.textContent = parameter.description; input.setAttribute('aria-describedby', hint.id); row.append(hint);
        }
        row.append(input); fields.append(row); inputs!.push({ parameter, input });
        if (parameter.type.toUpperCase() === 'RELDATE') {
          input.placeholder = 'e.g. D-1, M-1, Y-1';
          const picks = doc.createElement('div'); picks.className = 'reldate-quickpicks';
          ['D-0', 'D-1', 'D-7', 'D-30', 'M-1', 'M-3', 'Y-1'].forEach(value => {
            const button = doc.createElement('button'); button.type = 'button'; button.textContent = value === 'D-0' ? 'Today' : value;
            button.addEventListener('click', () => { if (current(opened)) { input.value = value; input.focus(); } }, { signal: listeners.signal });
            picks.append(button);
          });
          row.append(picks);
        }
      });
      inputs[0]?.input.focus();
    } catch (failure) {
      if (current(opened)) { fields.textContent = 'Parameter definitions could not be loaded. Close and reopen this editor to retry.'; message(asError(failure).message); }
    } finally { if (current(opened)) { request = null; controls(); } }
  }
  async function submit() {
    if (!alive() || !visible() || !canWrite() || !target || !inputs) return;
    const opened = sequence, snapshot = { ...target }, entries: [string, string][] = [];
    error.classList.remove('show');
    for (const { parameter, input } of inputs) {
      const value = input.value.trim();
      if (!value && parameter.required && !parameter.default?.trim()) {
        message(`${parameter.name} requires a value.`); input.focus(); return;
      }
      if (value) entries.push([parameter.name, value]);
    }
    // Capture the complete draft before a write or any other asynchronous work begins.
    await saveDraft(snapshot, Object.fromEntries(entries), () => { if (current(opened)) close(); },
      value => { if (current(opened)) message(value); });
  }
  save.addEventListener('click', () => { void submit(); }, { signal: listeners.signal });
  cancel.addEventListener('click', close, { signal: listeners.signal });
  modal.addEventListener('keydown', event => { if (event.key === 'Escape') close(); }, { signal: listeners.signal });
  return { open, controls, reconcile(records: Map<number, SubscriptionDto | null>) {
    if (!target || !records.has(target.id)) return;
    const latest = records.get(target.id);
    if (!latest || latest.reportId !== target.reportId) { target = null; message('This subscription is no longer available. Close the editor to continue.'); }
    else { target = { id: latest.id, reportId: latest.reportId, version: latest.version }; message('The latest subscription version is loaded. Review your retained draft before saving another change.'); }
    controls();
  }, dispose() { close(); listeners.abort(); } };
}
