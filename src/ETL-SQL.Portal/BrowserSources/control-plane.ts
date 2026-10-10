import { asError, isObject } from './http-error.js';
import { installDialogAccessibility } from './dialog-a11y.js';
import { readControlPlaneOverview, readControlPlaneTenants, readControlPlaneAudit,
  readControlPlaneReceipt } from './control-plane-responses.js';
import { renderControlPlaneOverview, renderControlPlaneTenants, renderControlPlaneAudit } from './control-plane-view.js';

export interface ControlPlaneData {
  overview: ControlPlaneFleetOverview;
  tenants: ControlPlaneTenantDto[];
  audit: PlatformAuditReceiptDto[];
}
export interface ControlPlaneController {
  reload(): Promise<void>;
  render(data: unknown): void;
  dispose(): void;
}
type Reload = () => void | Promise<void>;
type Mutation = ProvisionTenantAdminRequest | UpdateTenantQuotasAdminRequest | UpdateTenantStateAdminRequest;
const controllers = new WeakMap<HTMLElement, ControlPlaneController>();

// This endpoint uses platform authority, independent of tenant JWTs. The browser never collects
// or persists the management key; a trusted platform deployment must supply its authentication.
export function createControlPlaneController(container: HTMLElement, onReload?: Reload,
  fetcher: typeof fetch = window.fetch.bind(window)): ControlPlaneController {
  const existing = controllers.get(container);
  if (existing) return existing;
  const listeners = new AbortController();
  const disposeDialogs = installDialogAccessibility(container);
  let disposed = false, generation = 0, modalVersion = 0, loading = false, busy = false, uncertain = false;
  let reads: AbortController | null = null;
  let overview: ControlPlaneFleetOverview | null = null;
  let tenants: ControlPlaneTenantDto[] | null = null;
  let audit: PlatformAuditReceiptDto[] | null = null;
  let tenantError = 'Tenant inventory unavailable.', auditError = 'Platform audit trail unavailable.';
  const element = <T extends HTMLElement>(id: string) => container.querySelector<T>('#' + id);
  const input = (id: string): HTMLInputElement => {
    const value = element<HTMLInputElement>(id);
    if (!value) throw new Error(`Missing control-plane field ${id}.`);
    return value;
  };
  const status = (message: string) => { const target = element('controlPlaneStatus'); if (target) target.textContent = message; };
  const canMutate = () => !disposed && !loading && !busy && !uncertain && overview !== null && tenants !== null && audit !== null;
  function updateControls(): void {
    container.querySelectorAll<HTMLButtonElement>('#btnOpenProvision, .btn-quota, .btn-state, form button[type="submit"]')
      .forEach(button => { button.disabled = !canMutate(); });
    const refresh = element<HTMLButtonElement>('btnRefreshControlPlane');
    if (refresh) refresh.disabled = busy || loading || disposed;
  }
  function renderSources(): void {
    renderControlPlaneOverview(container, overview);
    renderControlPlaneTenants(container, tenants, element<HTMLInputElement>('tenantSearch')?.value || '', tenantError);
    renderControlPlaneAudit(container, audit, auditError);
    updateControls();
  }
  function closeModals(): void {
    modalVersion++;
    container.querySelectorAll<HTMLElement>('#modalProvision, #modalQuotas, #modalState').forEach(modal => {
      modal.style.display = 'none';
      modal.querySelector<HTMLFormElement>('form')?.reset();
      const error = modal.querySelector('[role="alert"]');
      if (error) error.textContent = '';
    });
  }
  function openModal(id: string): void {
    if (!canMutate()) return;
    closeModals();
    const modal = element(id);
    if (modal) { modal.removeAttribute('aria-hidden'); modal.style.display = 'flex'; }
  }
  function showReceipt(receipt: PlatformAuditReceiptDto): void {
    const alert = element('receiptAlert');
    if (!alert) return;
    alert.style.display = 'block';
    alert.textContent = `Receipt ${receipt.operationId}: ${receipt.kind} — ${receipt.status} (${receipt.phase}). `
      + `Tenant: ${receipt.tenantId}. Operator: ${receipt.platformOperator}. `
      + `Ref: ${receipt.authorizationReference}. Receipt Hash: ${receipt.receiptHash}`;
  }
  async function request(path: string, init: RequestInit): Promise<unknown> {
    const response = await fetcher(path, init);
    if (!response.ok) {
      if (response.status === 401 || response.status === 403) throw new Error('Platform management credentials required.');
      if (response.status === 404 && init.method !== 'POST') throw new Error('Control plane is unavailable in this deployment.');
      const body: unknown = await response.json().catch(() => null);
      throw new Error(isObject(body) && typeof body.error === 'string' && body.error.trim()
        ? body.error : `Control-plane request failed (HTTP ${response.status}).`);
    }
    return response.json();
  }
  async function reload(): Promise<void> {
    if (disposed || busy) return;
    const current = ++generation;
    reads?.abort(); reads = new AbortController();
    const signal = reads.signal;
    loading = true; overview = null; tenants = null; audit = null;
    tenantError = 'Loading tenant inventory...'; auditError = 'Loading platform audit trail...';
    closeModals(); renderSources(); status('Loading control-plane metadata...');
    const results = await Promise.allSettled([
      request('/api/platform/control-plane/overview', { signal }).then(readControlPlaneOverview),
      request('/api/platform/control-plane/tenants', { signal }).then(readControlPlaneTenants),
      request('/api/platform/control-plane/audit', { signal }).then(readControlPlaneAudit)
    ]);
    if (disposed || current !== generation) return;
    const [fleetResult, tenantsResult, auditResult] = results;
    overview = fleetResult.status === 'fulfilled' ? fleetResult.value : null;
    tenants = tenantsResult.status === 'fulfilled' ? tenantsResult.value : null;
    audit = auditResult.status === 'fulfilled' ? auditResult.value : null;
    tenantError = tenantsResult.status === 'rejected' ? 'Tenant inventory unavailable. ' + asError(tenantsResult.reason).message : '';
    auditError = auditResult.status === 'rejected' ? 'Platform audit trail unavailable. ' + asError(auditResult.reason).message : '';
    const failures = results.filter(result => result.status === 'rejected').map(result => asError(result.reason).message);
    loading = false;
    if (!failures.length) uncertain = false;
    renderSources();
    status(failures.length ? [...new Set(failures)].join(' ') + ' Refresh to retry.'
      : `Fleet metadata captured ${overview?.capturedAtUtc}.`);
  }
  const field = (id: string): string => {
    const value = input(id).value.trim();
    if (!value) throw new Error('Complete all required fields.');
    return value;
  };
  const quota = (id: string, minimum: number): number => {
    const value = input(id).valueAsNumber;
    if (!Number.isSafeInteger(value) || value < minimum || value > 2147483647) {
      throw new Error(`Enter a whole-number quota between ${minimum} and 2147483647.`);
    }
    return value;
  };
  const attribution = (prefix: string) => ({ platformOperator: field(prefix + 'Operator'),
    authorizationReference: field(prefix + 'AuthRef'), reason: field(prefix + 'Reason') });
  async function submit(form: HTMLFormElement, path: string, tenantId: string, kind: string,
    payload: Mutation): Promise<void> {
    if (!canMutate()) return;
    busy = true; updateControls();
    const version = modalVersion;
    const alert = form.querySelector('[role="alert"]');
    if (alert) alert.textContent = '';
    const previousReceipt = element('receiptAlert');
    if (previousReceipt) { previousReceipt.textContent = ''; previousReceipt.style.display = 'none'; }
    let submitted = false;
    try {
      submitted = true;
      const receipt = readControlPlaneReceipt(await request(path, { method: 'POST',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }));
      if (receipt.tenantId !== tenantId || receipt.kind !== kind
        || receipt.platformOperator !== payload.platformOperator || receipt.authorizationReference !== payload.authorizationReference
        || receipt.reason !== payload.reason) throw new Error('Control-plane receipt does not match the requested action.');
      if (!('state' in payload) && ((payload.maxConcurrentJobs != null && receipt.targetMaxConcurrentJobs !== payload.maxConcurrentJobs)
        || (payload.maxStorageMb != null && receipt.targetMaxStorageMb !== payload.maxStorageMb)
        || (payload.maxReportSessions != null && receipt.targetMaxReportSessions !== payload.maxReportSessions))) {
        throw new Error('Control-plane receipt quotas do not match the requested action.');
      }
      if (receipt.status !== 'Completed') throw new Error(`Action reported ${receipt.status}. Review the audit trail before submitting again.`);
      if (disposed) return;
      if (version === modalVersion) { closeModals(); showReceipt(receipt); }
      busy = false;
      if (onReload) await onReload(); else await reload();
    } catch (error) {
      if (disposed) return;
      uncertain = submitted;
      const message = asError(error).message + (submitted ? ' Refresh inventory and audit before submitting again.' : '');
      if (version === modalVersion && alert) alert.textContent = message;
      status(message);
    } finally {
      busy = false;
      if (!disposed) updateControls();
    }
  }
  function bindForm(id: string, prepare: () => { path: string; tenantId: string; kind: string; payload: Mutation }): void {
    const form = element<HTMLFormElement>(id);
    form?.addEventListener('submit', event => {
      event.preventDefault();
      if (!canMutate()) return;
      try {
        const request = prepare();
        void submit(form, request.path, request.tenantId, request.kind, request.payload);
      } catch (error) {
        const alert = form.querySelector('[role="alert"]');
        if (alert) alert.textContent = asError(error).message;
      }
    }, { signal: listeners.signal });
  }
  bindForm('formProvision', () => {
    const tenantId = field('provTenantId');
    const payload: ProvisionTenantAdminRequest = { tenantId, activeRelease: null,
      maxConcurrentJobs: quota('provMaxJobs', 1), maxStorageMb: quota('provStorageMb', 512),
      maxReportSessions: null, ...attribution('prov') };
    return { path: '/api/platform/control-plane/tenants/provision', tenantId, kind: 'Provision', payload };
  });
  bindForm('formQuotas', () => {
    const tenantId = field('quotaTenantId');
    const payload: UpdateTenantQuotasAdminRequest = { maxConcurrentJobs: quota('quotaMaxJobs', 1),
      maxStorageMb: quota('quotaStorageMb', 512), maxReportSessions: null, ...attribution('quota') };
    return { path: `/api/platform/control-plane/tenants/${encodeURIComponent(tenantId)}/quotas`, tenantId, kind: 'UpdateQuotas', payload };
  });
  bindForm('formState', () => {
    const tenantId = field('stateTenantId'), state = element<HTMLSelectElement>('stateSelect')?.value || '';
    if (!['Active', 'Maintenance', 'Quarantined'].includes(state)) throw new Error('Choose a supported tenant state.');
    const payload: UpdateTenantStateAdminRequest = { state, ...attribution('state') };
    return { path: `/api/platform/control-plane/tenants/${encodeURIComponent(tenantId)}/state`, tenantId, kind: `SetState:${state}`, payload };
  });
  const tabs = [...container.querySelectorAll<HTMLButtonElement>('.cp-tab')];
  function selectTab(tab: HTMLButtonElement): void {
    for (const candidate of tabs) {
      const selected = candidate === tab;
      candidate.classList.toggle('active', selected);
      candidate.setAttribute('aria-selected', String(selected)); candidate.tabIndex = selected ? 0 : -1;
      const panel = element('panel-' + candidate.dataset.tab);
      panel?.classList.toggle('active', selected);
    }
  }
  for (const tab of tabs) {
    tab.addEventListener('click', () => selectTab(tab), { signal: listeners.signal });
    tab.addEventListener('keydown', event => {
      if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const index = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
        : (tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      const target = tabs[index]; if (target) { selectTab(target); target.focus(); }
    }, { signal: listeners.signal });
  }
  element('tenantSearch')?.addEventListener('input', () => { renderSources(); }, { signal: listeners.signal });
  element('btnRefreshControlPlane')?.addEventListener('click', () => { void reload(); }, { signal: listeners.signal });
  element('btnOpenProvision')?.addEventListener('click', () => openModal('modalProvision'), { signal: listeners.signal });
  container.querySelectorAll('.btn-cancel').forEach(button => button.addEventListener('click', closeModals, { signal: listeners.signal }));
  element('tenantTableBody')?.addEventListener('click', event => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('.btn-quota, .btn-state') : null;
    const tenant = tenants?.find(candidate => candidate.tenantId === button?.dataset.tenant);
    if (!button || !tenant || !canMutate()) return;
    if (button.classList.contains('btn-quota')) {
      openModal('modalQuotas'); input('quotaTenantId').value = tenant.tenantId;
      input('quotaMaxJobs').value = String(tenant.maxConcurrentJobs); input('quotaStorageMb').value = String(tenant.maxStorageMb);
    } else {
      openModal('modalState'); input('stateTenantId').value = tenant.tenantId;
      const select = element<HTMLSelectElement>('stateSelect'); if (select) select.value = tenant.state;
    }
  }, { signal: listeners.signal });
  const controller: ControlPlaneController = {
    reload,
    render(data) {
      if (disposed) return;
      if (!isObject(data)) throw new TypeError('Invalid control-plane data.');
      const snapshot: ControlPlaneData = { overview: readControlPlaneOverview(data.overview),
        tenants: readControlPlaneTenants(data.tenants), audit: readControlPlaneAudit(data.audit) };
      generation++; reads?.abort(); loading = false;
      overview = snapshot.overview; tenants = snapshot.tenants; audit = snapshot.audit; renderSources();
    },
    dispose() {
      if (disposed) return;
      disposed = true; generation++; reads?.abort(); listeners.abort(); closeModals(); disposeDialogs?.();
      const receipt = element('receiptAlert'); if (receipt) { receipt.textContent = ''; receipt.style.display = 'none'; }
      overview = null; tenants = null; audit = null; controllers.delete(container); updateControls();
    }
  };
  controllers.set(container, controller); updateControls();
  return controller;
}

export const ControlPlaneUI = {
  render(container: HTMLElement, data: unknown, onReload?: Reload): ControlPlaneController {
    const controller = createControlPlaneController(container, onReload); controller.render(data); return controller;
  },
  init: createControlPlaneController,
  reload: () => controllers.get(document.body)?.reload(),
  dispose: () => controllers.get(document.body)?.dispose()
};
window.ControlPlaneUI = ControlPlaneUI;
if (document.getElementById('kpiStrip')) void createControlPlaneController(document.body).reload();
