import { PANEL_HTML, SAMPLE_POLICY, escapePolicy, renderPolicyVersions, renderPolicyMachines,
  defaultPolicyExpiry, parsePolicyExpiry } from './policy-authority-view.js';
import { readPolicyAuthorityStatus, readPolicyValidation, readPolicyVersions, readPolicyMachines,
  readPolicyVersionReceipt, readPolicyMachineReceipt } from './policy-authority-responses.js';
import type { PolicyScope } from './policy-authority-responses.js';

export interface PolicyAuthorityAdminApi {
  status(signal?: AbortSignal): Promise<unknown>;
  validate(json: string, signal?: AbortSignal): Promise<unknown>;
  versions(tenant: string, environment: string, signal?: AbortSignal): Promise<unknown>;
  machines(tenant: string, environment: string, signal?: AbortSignal): Promise<unknown>;
  publish(body: PolicyPublishRequest, signal?: AbortSignal): Promise<unknown>;
  publishCanary(body: PolicyCanaryPublishRequest, signal?: AbortSignal): Promise<unknown>;
  activate(tenant: string, environment: string, version: string, signal?: AbortSignal): Promise<unknown>;
  promoteCanary(tenant: string, environment: string, version: string, signal?: AbortSignal): Promise<unknown>;
  haltCanary(tenant: string, environment: string, version: string, reviewer: string | null, signal?: AbortSignal): Promise<unknown>;
  rollback(body: PolicyRollbackRequest, signal?: AbortSignal): Promise<unknown>;
  registerMachine(body: PolicyMachineRegisterRequest, signal?: AbortSignal): Promise<unknown>;
  revokeMachine(id: string, reason: string, signal?: AbortSignal): Promise<unknown>;
}
export interface PolicyAuthorityAdmin { load(): Promise<void>; dispose(): void }
const controllers = new WeakMap<HTMLElement, PolicyAuthorityAdmin>();
const key = (scope: PolicyScope) => JSON.stringify([scope.tenant, scope.environment]);
const guid = (value: string) => /^[a-f0-9]{32}$/i.test(value);

export function createPolicyAuthorityAdmin({ host: suppliedHost, policyAuthorityApi: api }: {
  host: HTMLElement | null; policyAuthorityApi: PolicyAuthorityAdminApi
}): PolicyAuthorityAdmin {
  if (!suppliedHost) throw new Error('Policy authority host element required.');
  const host = suppliedHost, existing = controllers.get(host);
  if (existing) return existing;
  host.innerHTML = PANEL_HTML;
  const element = <T extends HTMLElement>(id: string): T => {
    const found = host.querySelector<T>('#' + id);
    if (!found) throw new Error(`Missing policy authority field ${id}.`);
    return found;
  };
  const input = (id: string) => element<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(id);
  const error = (id: string, message = '') => { element(id).textContent = message; element(id).classList.toggle('show', !!message); };
  const scope = (): PolicyScope => ({ tenant: input('pa-tenant').value.trim(), environment: input('pa-env').value.trim() });
  const requireScope = (): PolicyScope => {
    const value = scope();
    if (!value.tenant || !value.environment) throw new Error('Tenant and environment are required.');
    return value;
  };
  input('pa-expires').value = defaultPolicyExpiry(); input('pa-policyJson').value = SAMPLE_POLICY;
  const listeners = new AbortController(), lifetime = new AbortController();
  let disposed = false, epoch = 0, busy = false, validating = false, validationVersion = 0;
  let status: PolicyAuthorityStatusDto | null = null, versions: PolicyVersionDto[] | null = null, machines: PolicyMachineDto[] | null = null;
  let statusVersion = 0, versionsVersion = 0, machinesVersion = 0, refreshVersion = 0;
  let statusRead: AbortController | null = null, versionRead: AbortController | null = null, machineRead: AbortController | null = null;
  let validationRead: AbortController | null = null, sentScope: string | null = null;
  const uncertain = new Set<string>();
  const current = (generation: number, requested: PolicyScope) => !disposed && generation === epoch && key(requested) === key(scope());
  function controls(): void {
    const blocked = disposed || busy || uncertain.has(key(scope())) || !scope().tenant || !scope().environment;
    host.querySelectorAll<HTMLButtonElement>('#pa-publishBtn, #pa-publishCanaryBtn').forEach(button => { button.disabled = blocked || status?.configured !== true; });
    element<HTMLButtonElement>('pa-registerMachineBtn').disabled = blocked || machines === null;
    element<HTMLButtonElement>('pa-validateBtn').disabled = disposed || validating;
    host.querySelectorAll<HTMLButtonElement>('[data-pa-act]').forEach(button => {
      button.disabled = blocked || (button.dataset.paAct === 'revoke-machine' ? machines === null : versions === null)
        || ['halt', 'rollback'].includes(button.dataset.paAct || '') && status?.configured !== true;
    });
  }
  function invalidateScope(): void {
    epoch++; versionsVersion++; machinesVersion++; refreshVersion++;
    versionRead?.abort(); machineRead?.abort(); versions = null; machines = null;
    if (sentScope) uncertain.add(sentScope);
    host.setAttribute('aria-busy', 'false');
    element('pa-scopeLabel').textContent = `${scope().tenant}/${scope().environment}`;
    element('pa-versionWrap').innerHTML = '<div class="empty-state">Scope changed. Load versions for this scope.</div>';
    element('pa-machineWrap').innerHTML = '<div class="empty-state">Scope changed. Load machines for this scope.</div>';
    for (const id of ['pa-error', 'pa-canaryError', 'pa-machineError']) error(id);
    element('pa-actionResult').textContent = uncertain.has(key(scope())) ? 'An earlier outcome is unconfirmed. Refresh this scope before retrying.' : '';
    element('pa-canaryResult').textContent = ''; controls();
  }
  async function loadStatus(): Promise<boolean> {
    const version = ++statusVersion;
    statusRead?.abort(); statusRead = new AbortController(); status = null; controls();
    element('pa-status').textContent = 'Checking signer…';
    try {
      const result = readPolicyAuthorityStatus(await api.status(statusRead.signal));
      if (disposed || version !== statusVersion) return false;
      status = result; element('pa-status').textContent = result.configured ? 'Configured' : 'Not configured';
      element('pa-statusBody').innerHTML = result.configured
        ? '<span class="chip chip-active">Signing key available</span><p class="form-hint">Private key material is never exported through this UI.</p>'
        : '<span class="chip chip-inactive">Signing disabled</span><p class="form-hint">' + escapePolicy(result.error) + '</p>';
      return true;
    } catch {
      if (disposed || version !== statusVersion) return false;
      status = null; element('pa-status').textContent = 'Signer status unavailable';
      element('pa-statusBody').innerHTML = '<div class="error-msg show" role="alert">Policy signer status is unavailable or incomplete. Refresh to retry.</div>';
      return false;
    } finally { if (!disposed && version === statusVersion) controls(); }
  }
  async function loadVersions(): Promise<boolean> {
    const generation = epoch, requested = scope(), version = ++versionsVersion;
    versionRead?.abort(); versionRead = new AbortController(); versions = null; controls();
    element('pa-scopeLabel').textContent = `${requested.tenant}/${requested.environment}`;
    element('pa-versionWrap').textContent = 'Loading policy versions…';
    try {
      requireScope();
      const result = readPolicyVersions(await api.versions(requested.tenant, requested.environment, versionRead.signal), requested);
      if (!current(generation, requested) || version !== versionsVersion) return false;
      versions = result; element('pa-versionWrap').innerHTML = renderPolicyVersions(result); return true;
    } catch {
      if (!current(generation, requested) || version !== versionsVersion) return false;
      element('pa-versionWrap').innerHTML = '<div class="error-msg show" role="alert">Policy versions are unavailable or incomplete for this scope. Load versions to retry.</div>'; return false;
    } finally { if (current(generation, requested) && version === versionsVersion) controls(); }
  }
  async function loadMachines(): Promise<boolean> {
    const generation = epoch, requested = scope(), version = ++machinesVersion;
    machineRead?.abort(); machineRead = new AbortController(); machines = null; controls();
    element('pa-machineWrap').textContent = 'Loading machines…';
    try {
      requireScope();
      const result = readPolicyMachines(await api.machines(requested.tenant, requested.environment, machineRead.signal), requested);
      if (!current(generation, requested) || version !== machinesVersion) return false;
      machines = result; element('pa-machineWrap').innerHTML = renderPolicyMachines(result); return true;
    } catch {
      if (!current(generation, requested) || version !== machinesVersion) return false;
      element('pa-machineWrap').innerHTML = '<div class="error-msg show" role="alert">Policy machines are unavailable or incomplete for this scope. Load machines to retry.</div>'; return false;
    } finally { if (current(generation, requested) && version === machinesVersion) controls(); }
  }
  async function load(): Promise<void> {
    if (disposed) return;
    const generation = epoch, requested = scope(), version = ++refreshVersion;
    host.setAttribute('aria-busy', 'true');
    const results = await Promise.all([loadStatus(), loadVersions(), loadMachines()]);
    if (!disposed && version === refreshVersion) {
      host.setAttribute('aria-busy', 'false');
      if (current(generation, requested) && !busy && results.every(Boolean)) {
        if (uncertain.delete(key(requested))) {
          element('pa-actionResult').textContent = 'Scope refreshed. Inspect the latest versions and machine state before retrying.';
          for (const id of ['pa-error', 'pa-canaryError', 'pa-machineError']) error(id);
        }
        controls();
      }
    }
  }
  async function validate(): Promise<void> {
    if (disposed || validating) return;
    const json = input('pa-policyJson').value, version = ++validationVersion;
    validating = true; validationRead = new AbortController(); controls(); error('pa-error');
    element('pa-validateResult').textContent = 'Validating…';
    try {
      const result = readPolicyValidation(await api.validate(json, validationRead.signal));
      if (disposed || version !== validationVersion || json !== input('pa-policyJson').value) return;
      element('pa-validateResult').textContent = result.isValid ? 'Valid' : `Invalid: ${result.errors.join('; ')}`;
    } catch { if (!disposed && version === validationVersion) element('pa-validateResult').textContent = 'Policy validation unavailable.'; }
    finally { if (!disposed && version === validationVersion) { validating = false; controls(); } }
  }
  async function mutate(errorId: string, action: (requested: PolicyScope, send: (operation: () => Promise<unknown>) => Promise<unknown>, stillCurrent: () => boolean) => Promise<string | null>): Promise<void> {
    if (disposed || busy || uncertain.has(key(scope()))) return;
    const requested = scope(), generation = epoch, versionGeneration = versionsVersion, machineGeneration = machinesVersion;
    busy = true; error(errorId); controls();
    const stillCurrent = () => current(generation, requested) && versionGeneration === versionsVersion && machineGeneration === machinesVersion;
    const send = async (operation: () => Promise<unknown>): Promise<unknown> => {
      if (!stillCurrent()) throw new Error('Scope changed.');
      sentScope = key(requested);
      return await operation();
    };
    let message: string | null = null;
    try { requireScope(); message = await action(requested, send, stillCurrent); }
    catch (value) {
      if (sentScope) uncertain.add(key(requested));
      if (stillCurrent()) error(errorId, sentScope
        ? 'Outcome is unconfirmed. Refresh and inspect this scope before retrying.'
        : value instanceof Error ? value.message : 'Policy action unavailable.');
    } finally {
      if (!stillCurrent() && sentScope) uncertain.add(key(requested));
      sentScope = null; busy = false; if (!disposed) controls();
    }
    if (message && stillCurrent()) {
      element('pa-actionResult').textContent = message;
      await Promise.all([loadVersions(), loadMachines()]);
    }
  }
  async function publish(canary: boolean): Promise<void> {
    if (status?.configured !== true) return;
    await mutate(canary ? 'pa-canaryError' : 'pa-error', async (requested, send, stillCurrent) => {
      const version = input(canary ? 'pa-canaryVersion' : 'pa-version').value.trim();
      if (!version) throw new Error('Policy version is required.');
      const common = { ...requested, policyVersion: version, policyJson: input('pa-policyJson').value,
        reviewer: input('pa-reviewer').value.trim() || null, expiresAtUtc: parsePolicyExpiry(input('pa-expires').value) };
      const group = input('pa-cohortType').value === 'group', percentage = Number(input('pa-canaryPercentage').value);
      const cohort = { canaryGroup: group ? input('pa-canaryGroup').value.trim() : null, canaryPercentage: group ? null : percentage };
      if (canary && (group ? !cohort.canaryGroup : !Number.isInteger(percentage) || percentage < 1 || percentage > 100)) throw new Error('Choose a named group or a whole percentage from 1 to 100.');
      const staged = input('pa-staged').value === 'true';
      const result = await send(() => canary ? api.publishCanary({ ...common, ...cohort }, lifetime.signal) : api.publish({ ...common, staged }, lifetime.signal));
      if (!stillCurrent()) return null;
      const receipt = readPolicyVersionReceipt(result, requested, canary ? 'Canary' : staged ? 'Staged' : 'Active', version);
      if (Date.parse(receipt.expiresAtUtc) !== Date.parse(common.expiresAtUtc)
        || canary && (receipt.canaryGroup !== cohort.canaryGroup || receipt.canaryPercentage !== cohort.canaryPercentage)) throw new Error('Publication receipt does not match.');
      return `${canary ? 'Canary' : 'Policy'} '${receipt.policyVersion}' published for ${requested.tenant}/${requested.environment}.`;
    });
  }
  async function register(): Promise<void> {
    if (machines === null) return;
    await mutate('pa-machineError', async (requested, send, stillCurrent) => {
      const machineId = input('pa-machineId').value.trim(), enrollmentId = input('pa-enrollmentId').value.trim();
      if (!guid(machineId) || !guid(enrollmentId)) throw new Error('Machine and enrollment IDs must be 32-character GUIDs.');
      const thumbprint = input('pa-thumbprint').value.replaceAll(' ', '').toUpperCase().trim();
      if (thumbprint && !/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/i.test(thumbprint)) throw new Error('Certificate thumbprint must be a SHA-1 or SHA-256 hexadecimal value.');
      const result = await send(() => api.registerMachine({ ...requested, machineId, enrollmentId, clientCertificateThumbprint: thumbprint || null }, lifetime.signal));
      if (!stillCurrent()) return null;
      const receipt = readPolicyMachineReceipt(result, requested, machineId, false);
      if (receipt.enrollmentId !== enrollmentId || receipt.requiresClientCertificate !== !!thumbprint) throw new Error('Machine receipt does not match.');
      if (input('pa-machineId').value.trim() === machineId && input('pa-enrollmentId').value.trim() === enrollmentId
        && input('pa-thumbprint').value.replaceAll(' ', '').toUpperCase().trim() === thumbprint) {
        for (const id of ['pa-machineId', 'pa-enrollmentId', 'pa-thumbprint']) input(id).value = '';
      }
      return `Machine '${machineId}' registered.`;
    });
  }
  async function rowAction(action: string, row: HTMLTableRowElement): Promise<void> {
    const version = versions?.find(value => value.policyVersion === row.dataset.version);
    const machine = machines?.find(value => value.machineId === row.dataset.machine);
    if (action === 'revoke-machine' ? !machine || machine.revoked : !version) return;
    if (key(action === 'revoke-machine' && machine ? machine : version || scope()) !== key(scope())) { invalidateScope(); return; }
    await mutate(action === 'revoke-machine' ? 'pa-machineError' : 'pa-error', async (requested, send, stillCurrent) => {
      const feedback = window.ETLSQLFeedback;
      if (!feedback) throw new Error('Policy confirmation unavailable.');
      if (action === 'revoke-machine' && machine) {
        const reason = await feedback.prompt(`Explain why machine ${machine.machineId} must be revoked.`, { title: 'Revoke machine', label: 'Revocation reason', multiline: true, required: true, minLength: 8, confirmLabel: 'Revoke machine', danger: true, auditAction: 'policy.machine.revoke' });
        if (!reason || !stillCurrent()) return null;
        const receipt = await send(() => api.revokeMachine(machine.machineId, reason, lifetime.signal));
        if (!stillCurrent()) return null;
        readPolicyMachineReceipt(receipt, requested, machine.machineId, true); return `Machine '${machine.machineId}' revoked.`;
      }
      if (!version) return null;
      if (action === 'activate' || action === 'promote') {
        if (version.rolloutState !== (action === 'activate' ? 'Staged' : 'Canary')) return null;
        if (action === 'promote' && !await feedback.confirm(`Promote canary ${version.policyVersion} to the whole fleet?`, { title: 'Promote policy canary', impact: 'All enrolled machines in this scope will receive this policy.', confirmLabel: 'Promote canary', auditAction: 'policy.canary.promote' })) return null;
        if (!stillCurrent()) return null;
        const result = await send(() => action === 'activate' ? api.activate(requested.tenant, requested.environment, version.policyVersion, lifetime.signal) : api.promoteCanary(requested.tenant, requested.environment, version.policyVersion, lifetime.signal));
        if (!stillCurrent()) return null;
        const receipt = readPolicyVersionReceipt(result, requested, 'Active', version.policyVersion);
        if (receipt.policyHash !== version.policyHash) throw new Error('Activation receipt does not match.');
        return `Policy '${version.policyVersion}' activated.`;
      }
      if (action === 'halt') {
        const active = versions?.find(value => value.rolloutState === 'Active');
        if (!active || version.rolloutState !== 'Canary') throw new Error('Load an active policy and canary before halting.');
        if (!await feedback.confirm(`Halt canary ${version.policyVersion}?`, { title: 'Halt policy canary', impact: 'Canary machines will revert to a fresh issuance of the active policy.', confirmLabel: 'Halt canary', danger: true, auditAction: 'policy.canary.halt' }) || !stillCurrent()) return null;
        const result = await send(() => api.haltCanary(requested.tenant, requested.environment, version.policyVersion, input('pa-reviewer').value.trim() || null, lifetime.signal));
        if (!stillCurrent()) return null;
        const receipt = readPolicyVersionReceipt(result, requested, 'Active');
        if (receipt.supersededVersion !== active.policyVersion || receipt.policyHash !== active.policyHash || receipt.policyVersion === version.policyVersion) throw new Error('Halt receipt does not match.');
        return `Canary halted; policy '${receipt.policyVersion}' is active.`;
      }
      if (action === 'rollback') {
        const newVersion = await feedback.prompt(`Create a rollback policy targeting ${version.policyVersion}.`, { title: 'Create rollback policy', label: 'New policy version', required: true, confirmLabel: 'Create rollback', auditAction: 'policy.rollback.create' });
        if (!newVersion || !stillCurrent()) return null;
        const body: PolicyRollbackRequest = { ...requested, targetPolicyVersion: version.policyVersion, newPolicyVersion: newVersion.trim(), reviewer: input('pa-reviewer').value.trim() || null, expiresAtUtc: parsePolicyExpiry(input('pa-expires').value) };
        const result = await send(() => api.rollback(body, lifetime.signal));
        if (!stillCurrent()) return null;
        const receipt = readPolicyVersionReceipt(result, requested, 'Active', body.newPolicyVersion);
        if (receipt.policyHash !== version.policyHash) throw new Error('Rollback receipt does not match.');
        return `Rollback policy '${receipt.policyVersion}' published.`;
      }
      return null;
    });
  }
  host.addEventListener('input', event => {
    const target = event.target;
    if (!(target instanceof HTMLElement) || disposed) return;
    if (target.id === 'pa-tenant' || target.id === 'pa-env') invalidateScope();
    if (target.id === 'pa-policyJson') {
      validationVersion++; validationRead?.abort(); validating = false;
      element('pa-validateResult').textContent = ''; controls();
    }
  }, { signal: listeners.signal });
  host.addEventListener('change', event => {
    if (event.target instanceof HTMLElement && event.target.id === 'pa-cohortType') {
      const byGroup = input('pa-cohortType').value === 'group';
      element('pa-groupGroup').style.display = byGroup ? '' : 'none'; element('pa-percentageGroup').style.display = byGroup ? 'none' : '';
    }
  }, { signal: listeners.signal });
  host.addEventListener('click', event => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (!button || button.disabled || disposed) return;
    if (button.id === 'pa-refreshBtn') void load();
    else if (button.id === 'pa-loadVersionsBtn') void loadVersions();
    else if (button.id === 'pa-loadMachinesBtn') void loadMachines();
    else if (button.id === 'pa-validateBtn') void validate();
    else if (button.id === 'pa-publishBtn') void publish(false);
    else if (button.id === 'pa-publishCanaryBtn') void publish(true);
    else if (button.id === 'pa-registerMachineBtn') void register();
    else if (button.dataset.paAct) { const row = button.closest('tr'); if (row) void rowAction(button.dataset.paAct, row); }
  }, { signal: listeners.signal });
  const controller: PolicyAuthorityAdmin = { load,
    dispose() {
      if (disposed) return;
      disposed = true; epoch++; statusVersion++; versionsVersion++; machinesVersion++; refreshVersion++; validationVersion++;
      listeners.abort(); lifetime.abort(); statusRead?.abort(); versionRead?.abort(); machineRead?.abort(); validationRead?.abort();
      versions = null; machines = null; status = null; host.setAttribute('aria-busy', 'false'); controls(); controllers.delete(host);
    }
  };
  controls(); controllers.set(host, controller); return controller;
}
