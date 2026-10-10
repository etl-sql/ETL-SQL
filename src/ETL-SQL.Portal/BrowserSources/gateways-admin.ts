import { installDialogAccessibility } from './dialog-a11y.js';
import { readGatewayFleet, currentGatewayFleet, readIssuedGateway, readGatewayRevocation } from './gateway-responses.js';
import type { GatewayFleetEntry } from './gateway-responses.js';
import { PANEL_HTML, renderGatewayFleet, renderGatewayNodes, gatewaySetupCommand } from './gateways-view.js';
import { createGatewayWizardLauncher } from './gateway-wizard.js';
import type { GatewayWizardApi } from './gateway-wizard.js';

export interface GatewayAdminApi {
  list(signal?: AbortSignal): Promise<unknown>;
  enroll(id: string, minutes: number, signal?: AbortSignal): Promise<unknown>;
  revoke(id: string, signal?: AbortSignal): Promise<unknown>;
}
export interface GatewaysAdmin { load(): Promise<void>; dispose(): void }
const controllers = new WeakMap<HTMLElement, GatewaysAdmin>();

export function createGatewaysAdmin({ host: suppliedHost, gatewaysApi, wizardApi = undefined }: {
  host: HTMLElement | null; gatewaysApi: GatewayAdminApi; wizardApi?: GatewayWizardApi
}): GatewaysAdmin {
  if (!suppliedHost) throw new Error('Gateway host element required.');
  const host = suppliedHost;
  const existing = controllers.get(host);
  if (existing) return existing;
  host.innerHTML = PANEL_HTML;
  const listeners = new AbortController(), lifetime = new AbortController();
  const removeDialogs = installDialogAccessibility(host);
  const element = <T extends HTMLElement>(id: string): T => {
    const found = host.querySelector<T>('#' + id);
    if (!found) throw new Error(`Missing gateway field ${id}.`);
    return found;
  };
  const input = (id: string) => element<HTMLInputElement>(id);
  const button = (id: string) => element<HTMLButtonElement>(id);
  const table = element('gw-tableWrap'), status = element('gw-status'), actionStatus = element('gw-action-status');
  const enrollError = element('gw-enroll-error'), nodesModal = element('gw-nodesModal');
  let disposed = false, loadVersion = 0, loading = false, enrolling = false, enrollmentUncertain = false, copyVersion = 0;
  let fleet: GatewayFleetEntry[] | null = null, reads: AbortController | null = null;
  const revoking = new Set<string>(), revocationUncertain = new Set<string>(), copyTimers = new Set<number>();
  const wizard = createGatewayWizardLauncher(document.body, message => { actionStatus.textContent = message; },
    alias => { actionStatus.textContent = `Connection '${alias}' saved.`; }, wizardApi);

  function updateControls(): void {
    button('gw-enrollSubmitBtn').disabled = disposed || loading || fleet === null || enrolling || enrollmentUncertain;
    button('gw-enrollBtn').disabled = disposed || enrolling;
    table.querySelectorAll<HTMLButtonElement>('.gw-revoke-btn, .gw-create-conn-btn').forEach(target => {
      const id = target.dataset.gwId || '';
      target.disabled = disposed || loading || revoking.has(id) || revocationUncertain.has(id);
    });
    host.setAttribute('aria-busy', String(loading));
  }
  function clearCredential(): void {
    copyVersion++;
    for (const timer of copyTimers) window.clearTimeout(timer);
    copyTimers.clear();
    input('gw-result-token').value = ''; input('gw-result-cmd').value = '';
    button('gw-copyTokenBtn').textContent = 'Copy'; button('gw-copyCmdBtn').textContent = 'Copy';
    element('gw-enrollResult').style.display = 'none'; element('gw-enrollForm').style.display = '';
  }
  function focusEnrollment(): void {
    if (disposed || enrolling) return;
    clearCredential(); enrollError.textContent = ''; enrollError.classList.remove('show');
    input('gw-input-id').scrollIntoView({ behavior: 'smooth', block: 'center' }); input('gw-input-id').focus();
  }
  function closeNodes(): void { nodesModal.style.display = 'none'; }
  async function load(recover = true): Promise<void> {
    if (disposed) return;
    const version = ++loadVersion;
    reads?.abort(); reads = new AbortController();
    loading = true; closeNodes(); wizard.close();
    status.textContent = 'Loading gateways…';
    table.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading gateways…</span></div>';
    updateControls();
    try {
      const entries = currentGatewayFleet(readGatewayFleet(await gatewaysApi.list(reads.signal)));
      if (disposed || version !== loadVersion) return;
      fleet = entries;
      if (recover && !enrolling && !revoking.size) {
        enrollmentUncertain = false; revocationUncertain.clear(); actionStatus.textContent = '';
        enrollError.textContent = ''; enrollError.classList.remove('show');
      }
      table.innerHTML = renderGatewayFleet(entries);
      const nodes = entries.reduce((sum, entry) => sum + entry.activeNodes, 0);
      status.textContent = `${entries.length} Gateway${entries.length === 1 ? '' : 's'} • ${nodes} Active Node${nodes === 1 ? '' : 's'}`;
    } catch {
      if (disposed || version !== loadVersion) return;
      fleet = null; status.textContent = 'Gateway fleet unavailable';
      table.innerHTML = '<div class="error-msg show" role="alert">Gateway fleet metadata is unavailable or incomplete. Refresh to retry.</div>';
    } finally {
      if (!disposed && version === loadVersion) { loading = false; updateControls(); }
    }
  }
  async function revoke(id: string): Promise<void> {
    const gateway = fleet?.find(entry => entry.gatewayId === id);
    if (disposed || loading || !gateway || gateway.state === 'Revoked' || revoking.has(id) || revocationUncertain.has(id)) return;
    const version = loadVersion;
    revoking.add(id); updateControls();
    let sent = false;
    try {
      const feedback = window.ETLSQLFeedback;
      if (!feedback) throw new Error('Gateway confirmation is unavailable.');
      const confirmed = await feedback.confirm(`Are you sure you want to revoke Gateway '${id}'?`, {
        title: 'Revoke Gateway', impact: 'Enrollment is revoked and connected nodes will be disconnected.',
        confirmLabel: 'Revoke gateway', danger: true, auditAction: 'admin.gateway.revoke'
      });
      if (!confirmed || disposed || version !== loadVersion) return;
      sent = true;
      readGatewayRevocation(await gatewaysApi.revoke(id, lifetime.signal), id);
      if (disposed) return;
      actionStatus.textContent = `Gateway '${id}' revoked.`;
      await load(false);
    } catch {
      if (disposed) return;
      if (sent) revocationUncertain.add(id);
      actionStatus.textContent = sent ? 'Gateway revocation is unconfirmed. Refresh and inspect the gateway before retrying.'
        : 'Gateway confirmation is unavailable. Try again.';
    } finally { revoking.delete(id); if (!disposed) updateControls(); }
  }
  async function enroll(): Promise<void> {
    if (disposed || loading || !fleet || enrolling || enrollmentUncertain) return;
    const id = input('gw-input-id').value.trim(), expiry = Number(input('gw-input-expiry').value);
    if (!id || !Number.isInteger(expiry) || expiry < 5 || expiry > 1440) {
      enrollError.textContent = !id ? 'Please enter a Gateway ID.' : 'Expiration must be a whole number from 5 to 1440 minutes.';
      enrollError.classList.add('show'); return;
    }
    enrolling = true; clearCredential(); enrollError.textContent = ''; enrollError.classList.remove('show'); updateControls();
    try {
      const receipt = readIssuedGateway(await gatewaysApi.enroll(id, expiry, lifetime.signal), id);
      if (disposed) return;
      input('gw-result-token').value = receipt.oneTimeToken;
      input('gw-result-cmd').value = gatewaySetupCommand(window.location.origin, receipt);
      element('gw-enrollForm').style.display = 'none'; element('gw-enrollResult').style.display = '';
      button('gw-enrollDoneBtn').focus();
    } catch {
      if (disposed) return;
      enrollmentUncertain = true;
      enrollError.textContent = 'Enrollment outcome is unconfirmed. Refresh and inspect the gateway before issuing another token.';
      enrollError.classList.add('show');
    } finally { enrolling = false; if (!disposed) updateControls(); }
  }
  async function copy(field: string, id: string): Promise<void> {
    const value = input(field).value, version = copyVersion;
    if (disposed || !value) return;
    try {
      await navigator.clipboard.writeText(value);
      if (disposed || version !== copyVersion) return;
      button(id).textContent = 'Copied!';
      const timer = window.setTimeout(() => { copyTimers.delete(timer); if (!disposed && version === copyVersion) button(id).textContent = 'Copy'; }, 2000);
      copyTimers.add(timer);
    } catch { if (!disposed && version === copyVersion) actionStatus.textContent = 'Copy failed. Select the value and copy it manually.'; }
  }
  host.addEventListener('click', event => {
    const target = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (!target || disposed || target.disabled) return;
    const id = target.dataset.gwId;
    if (target.id === 'gw-refreshBtn') void load();
    else if (target.id === 'gw-enrollBtn' || target.id === 'gw-emptyEnrollBtn') focusEnrollment();
    else if (target.id === 'gw-enrollSubmitBtn') void enroll();
    else if (target.id === 'gw-enrollDoneBtn') {
      clearCredential(); input('gw-input-id').value = ''; input('gw-input-expiry').value = '60';
      input('gw-input-id').focus(); void load(false);
    } else if (target.id === 'gw-copyTokenBtn') void copy('gw-result-token', target.id);
    else if (target.id === 'gw-copyCmdBtn') void copy('gw-result-cmd', target.id);
    else if (target.id === 'gw-nodesCloseBtn' || target.id === 'gw-nodesDismissBtn') closeNodes();
    else if (id && !loading && target.classList.contains('gw-view-nodes-btn')) {
      const gateway = fleet?.find(entry => entry.gatewayId === id);
      if (gateway) {
        element('gw-nodesModalGatewayId').textContent = id; element('gw-nodesTableWrap').innerHTML = renderGatewayNodes(gateway);
        nodesModal.removeAttribute('aria-hidden'); nodesModal.style.display = 'flex';
      }
    } else if (id && target.classList.contains('gw-revoke-btn')) void revoke(id);
    else if (id && !loading && target.classList.contains('gw-create-conn-btn')) void wizard.launch(id);
  }, { signal: listeners.signal });
  nodesModal.addEventListener('click', event => { if (event.target === nodesModal) closeNodes(); }, { signal: listeners.signal });
  const controller: GatewaysAdmin = { load,
    dispose() {
      if (disposed) return;
      disposed = true; loadVersion++; listeners.abort(); lifetime.abort(); reads?.abort();
      clearCredential(); closeNodes(); wizard.dispose(); removeDialogs?.(); fleet = null;
      loading = false; updateControls(); controllers.delete(host);
    }
  };
  updateControls(); controllers.set(host, controller); return controller;
}
