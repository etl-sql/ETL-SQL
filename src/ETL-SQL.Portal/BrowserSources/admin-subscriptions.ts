import { auth } from './portal-http.js';
import { getSessionIdentity } from './session-identity.js';
import { adminSubscriptionsApi, AdminSubscriptionOutcomeUnconfirmed } from './admin-subscriptions-api.js';
import { createSubscriptionParameterEditor } from './admin-subscription-parameters.js';
import { bindSelection, selectedIds, selectionCell, headerSelectionCell, renderCatalogPager } from './admin-catalog-ui.js';
import { renderSubscriptionHistory } from './subscription-history-ui.js';
import { asError } from './http-error.js';
import { loadingState, failedState } from './portal-states.js';

const escape = (value: unknown) => String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');

/** Own catalog snapshots, versioned writes, and subscription dialog lifetimes. */
export function createAdminSubscriptions(doc: Document = document) {
  function element<T extends HTMLElement>(id: string, kind: new (...args: never[]) => T): T {
    const value = doc.getElementById(id);
    if (!(value instanceof kind)) throw new Error(`Missing admin subscription element: ${id}`);
    return value;
  }
  const feedback = window.ETLSQLFeedback;
  if (!feedback) throw new Error('Portal feedback must be initialized before subscription administration.');
  const notify = feedback.notify.bind(feedback), confirmDelete = feedback.confirm.bind(feedback);
  const wrap = element('subsTableWrap', HTMLDivElement), pager = element('subsPager', HTMLDivElement);
  const filter = element('subsFilter', HTMLInputElement), status = element('subsStatusFilter', HTMLSelectElement);
  const refresh = element('subsRefreshBtn', HTMLButtonElement), resume = element('subsResumeBtn', HTMLButtonElement);
  const pause = element('subsPauseBtn', HTMLButtonElement), notice = element('subsMutationStatus', HTMLDivElement);
  const editorRefresh = element('editParamsRefreshBtn', HTMLButtonElement);
  const historyModal = element('subscriptionHistoryModal', HTMLDivElement), historyBody = element('subscriptionHistoryBody', HTMLDivElement);
  const historyTitle = element('subscriptionHistoryTitle', HTMLHeadingElement), historyClose = element('subscriptionHistoryCloseBtn', HTMLButtonElement);
  const sessionKey = () => {
    try {
      const identity = getSessionIdentity(auth.getToken());
      return JSON.stringify([identity.subject, identity.roles, identity.claims.tenant_id, identity.claims.service_account_id]);
    } catch { return null; }
  };
  const identity = sessionKey(), listeners = new AbortController();
  let disposed = false, busy = false, ready = false, page = 1, sequence = 0, historySequence = 0;
  let rows: SubscriptionDto[] = [], request: AbortController | null = null, historyRequest: AbortController | null = null;
  let historyOpener: HTMLElement | null = null;
  const unconfirmed = new Set<number>();
  const alive = () => !disposed && identity !== null && sessionKey() === identity;
  const canWrite = () => alive() && !busy && unconfirmed.size === 0;
  const listen = (target: EventTarget, event: string, handler: EventListener) => target.addEventListener(event, handler, { signal: listeners.signal });
  const editor = createSubscriptionParameterEditor(doc, alive, canWrite, async (target, parameters, accepted, failed) => {
    await perform(async () => {
      await adminSubscriptionsApi.update(target, { parameters });
      if (alive()) accepted();
      return 'Subscription parameters saved.';
    }, failed);
  });
  function controls() {
    const selected = ready ? selectedIds(wrap).length : 0;
    resume.disabled = pause.disabled = !canWrite() || selected === 0; refresh.disabled = !alive() || busy;
    editorRefresh.disabled = !alive() || busy || request !== null;
    wrap.querySelectorAll<HTMLButtonElement>('[data-action="toggle"], [data-action="delete"]').forEach(button => { button.disabled = !ready || !canWrite(); });
    editor.controls();
  }
  function announce(value: string, warning = false) {
    notice.textContent = value; notice.hidden = !value;
    if (value) notify(value, { title: 'Subscriptions', tone: warning ? 'warning' : 'success' });
  }
  async function reload(explicit = false): Promise<boolean> {
    if (!alive()) return false;
    request?.abort(); const pending = new AbortController(), opened = ++sequence;
    request = pending; ready = false; rows = []; controls();
    const query = filter.value.trim(), state = status.value;
    const current = () => alive() && sequence === opened && filter.value.trim() === query && status.value === state;
    wrap.dataset.subscriptionsState = 'loading'; wrap.setAttribute('aria-busy', 'true');
    wrap.innerHTML = loadingState('Loading subscriptions…'); pager.replaceChildren();
    try {
      let result = await adminSubscriptionsApi.catalog(page, query, state, pending.signal);
      if (!current()) return false;
      const lastPage = Math.max(1, Math.ceil(result.total / 25));
      if (page > lastPage) { page = lastPage; return reload(explicit); }
      if (explicit && !busy && unconfirmed.size) {
        const records = new Map<number, SubscriptionDto | null>();
        for (const id of unconfirmed) records.set(id, await adminSubscriptionsApi.get(id, pending.signal));
        if (!current() || busy) return false;
        result = await adminSubscriptionsApi.catalog(page, query, state, pending.signal);
        if (!current() || busy) return false;
        editor.reconcile(records); unconfirmed.clear();
        announce('Subscription state refreshed. Review the current values before making another change.');
      }
      if (!current()) return false;
      rows = result.items; ready = true;
      wrap.innerHTML = `<table class="data-table"><thead><tr>${headerSelectionCell('subscriptions')}<th>Recipients</th><th>Report</th><th>Name</th><th>Schedule</th><th>Format</th><th>Parameters</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>${rows.map(row => `<tr>${selectionCell(row.id, `subscription ${escape(row.name || row.reportName)}`)}
          <td class="text-sm">${escape(row.recipients)}</td><td>${escape(row.reportName)}</td><td>${escape(row.name || '—')}</td>
          <td class="text-sm">${escape(row.deliverOnRefresh ? 'On Refresh' : row.schedule || '—')}</td><td>${escape(row.format)}</td>
          <td class="text-sm text-muted">${escape(row.parameterSummary || '—')}</td><td><span class="chip ${row.isActive ? 'chip-active' : 'chip-inactive'}">${row.isActive ? 'Active' : 'Paused'}</span>
          ${row.failCount ? `<div class="sub-warning">${row.failCount} failed send${row.failCount === 1 ? '' : 's'}</div>`
            : `<div class="text-sm text-muted">Last: ${escape(row.lastSentAt ? new Date(row.lastSentAt).toLocaleString() : '—')}</div>`}</td>
          <td><div class="table-actions"><button class="btn btn-outline btn-sm" data-action="history" data-id="${row.id}">History</button>
          <button class="btn btn-outline btn-sm" data-action="edit-params" data-id="${row.id}">Edit Params</button>
          <button class="btn btn-outline btn-sm" data-action="toggle" data-id="${row.id}">${row.isActive ? 'Pause' : 'Resume'}</button>
          <button class="btn btn-outline btn-sm btn-danger-soft" data-action="delete" data-id="${row.id}">Delete</button></div></td></tr>`).join('')
          || '<tr><td colspan="9" class="empty-state">No subscriptions match these filters.</td></tr>'}</tbody></table>`;
      wrap.querySelectorAll<HTMLButtonElement>('[data-action]').forEach(button => {
        const target = rows.find(row => row.id === Number(button.dataset.id));
        if (target) listen(button, 'click', () => { void action(button.dataset.action, structuredClone(target)); });
      });
      bindSelection(wrap, controls);
      renderCatalogPager(pager, result, next => { page = next; void reload(); });
      wrap.dataset.subscriptionsState = 'ready'; return true;
    } catch (failure) {
      if (!current()) return false;
      ready = false; rows = []; pager.replaceChildren(); wrap.dataset.subscriptionsState = 'failed';
      wrap.innerHTML = failedState({ title: 'Subscriptions could not be loaded.', body: asError(failure).message }); return false;
    } finally {
      if (opened === sequence) { request = null; wrap.setAttribute('aria-busy', 'false'); controls(); }
    }
  }
  async function perform(work: () => Promise<string | { message: string; warning: boolean }>, failed?: (message: string) => void) {
    if (!canWrite()) return;
    busy = true; controls();
    try {
      const result = await work();
      if (!alive()) return;
      if (typeof result === 'string') announce(result); else announce(result.message, result.warning);
      await reload();
    } catch (failure) {
      if (!alive()) return;
      if (failure instanceof AdminSubscriptionOutcomeUnconfirmed) failure.subscriptionIds.forEach(id => unconfirmed.add(id));
      const message = asError(failure).message; failed?.(message); announce(message, true);
    } finally { busy = false; controls(); }
  }
  async function action(kind: string | undefined, target: SubscriptionDto) {
    if (!alive() || !ready) return;
    if (kind === 'edit-params') { await editor.open(target); return; }
    if (kind === 'history') { await openHistory(target); return; }
    if (kind === 'toggle') { await perform(async () => { await adminSubscriptionsApi.update(target, { isActive: !target.isActive }); return `Subscription ${target.isActive ? 'paused' : 'resumed'}.`; }); }
    if (kind === 'delete') {
      const context = sequence;
      await perform(async () => {
        if (!await confirmDelete('Delete this subscription?', { title: 'Delete subscription', impact: 'This stops future deliveries and removes its generated job.', confirmLabel: 'Delete subscription', danger: true, auditAction: 'admin.subscription.delete' })
          || !alive() || context !== sequence) return '';
        await adminSubscriptionsApi.delete(target); return 'Subscription deleted.';
      });
    }
  }
  async function bulk(isActive: boolean) {
    if (!ready) return;
    const targets = selectedIds(wrap).map(id => rows.find(row => row.id === id));
    if (!targets.length || targets.some(row => !row)) return;
    const snapshot = targets.filter((row): row is SubscriptionDto => !!row).map(row => ({ id: row.id, version: row.version }));
    await perform(async () => {
      const result = await adminSubscriptionsApi.bulkStatus(snapshot, isActive);
      const skipped = result.results.length - result.updated;
      return { message: `${result.updated} subscriptions ${isActive ? 'resumed' : 'paused'}.${skipped ? ` ${skipped} were not changed; review the refreshed list.` : ''}`, warning: skipped > 0 };
    });
  }
  function closeHistory() {
    ++historySequence; historyRequest?.abort(); historyRequest = null; historyModal.style.display = 'none'; historyBody.replaceChildren();
    if (alive() && historyOpener?.isConnected) historyOpener.focus(); historyOpener = null;
  }
  async function openHistory(target: SubscriptionDto) {
    historyRequest?.abort(); const opened = ++historySequence, pending = new AbortController(); historyRequest = pending;
    historyOpener = doc.activeElement instanceof HTMLElement ? doc.activeElement : null;
    historyTitle.textContent = `${target.name || target.reportName} Scheduler Trigger History`;
    historyModal.style.display = 'flex'; historyBody.innerHTML = loadingState('Loading scheduler trigger history…'); historyClose.focus();
    const current = () => alive() && historySequence === opened && historyModal.style.display !== 'none';
    try {
      const records = await adminSubscriptionsApi.history(target.id, pending.signal);
      if (current()) historyBody.innerHTML = records.length ? renderSubscriptionHistory(records, { esc: escape,
        formatDate: value => value ? new Date(value).toLocaleString() : '—' })
        : '<div class="empty-state">No scheduler triggers have been recorded for this subscription.</div>';
    } catch (failure) {
      if (current()) historyBody.innerHTML = failedState({ title: 'Scheduler trigger history could not be loaded.', body: asError(failure).message });
    } finally { if (current()) historyRequest = null; }
  }
  listen(filter, 'input', () => { page = 1; void reload(); });
  listen(status, 'change', () => { page = 1; void reload(); });
  listen(refresh, 'click', () => { void reload(true); });
  listen(editorRefresh, 'click', () => { void reload(true); });
  listen(resume, 'click', () => { void bulk(true); }); listen(pause, 'click', () => { void bulk(false); });
  listen(historyClose, 'click', closeHistory);
  listen(historyModal, 'keydown', event => { if (event instanceof KeyboardEvent && event.key === 'Escape') closeHistory(); });
  controls();
  return { reload, dispose() {
    if (disposed) return;
    disposed = true; ++sequence; request?.abort(); rows = []; ready = false;
    editor.dispose(); closeHistory(); listeners.abort(); controls();
  } };
}
