import { adminSettingsApi, AdminSettingsOutcomeUnconfirmed } from './admin-settings-api.js';
import { renderPortalBranding } from './branding.js';

function element<T extends HTMLElement>(id: string, type: { new(): T }): T {
  const found = document.getElementById(id);
  if (!(found instanceof type)) throw new TypeError(`Admin settings element is missing: ${id}`);
  return found;
}

interface SettingsState<T> {
  confirmed: T | null;
  revision: number;
  sequence: number;
  dirty: boolean;
  reading: boolean;
  writing: boolean;
  uncertain: boolean;
  abort: AbortController | null;
}
function state<T>(): SettingsState<T> {
  return { confirmed: null, revision: 0, sequence: 0, dirty: false, reading: false, writing: false, uncertain: false, abort: null };
}

export function createAdminSettings(api = adminSettingsApi) {
  const url = element('orch-url', HTMLInputElement), key = element('orch-key', HTMLInputElement);
  const clear = element('orch-clear-check', HTMLInputElement), clearRow = element('orch-clear-row', HTMLElement);
  const keyStatus = element('orch-key-status', HTMLElement), testResult = element('orch-test-result', HTMLElement);
  const orchError = element('orch-error', HTMLElement), orchResult = element('orch-save-result', HTMLElement);
  const orchSave = element('orchSaveBtn', HTMLButtonElement), orchRefresh = element('orchRefreshBtn', HTMLButtonElement);
  const test = element('orchTestBtn', HTMLButtonElement);
  const name = element('brand-name', HTMLInputElement), logo = element('brand-logo', HTMLInputElement);
  const footer = element('brand-footer', HTMLInputElement), brandError = element('brand-error', HTMLElement);
  const brandResult = element('brand-save-result', HTMLElement), brandSave = element('brandSaveBtn', HTMLButtonElement);
  const brandRefresh = element('brandRefreshBtn', HTMLButtonElement);
  const orch = state<OrchestratorSettingsDto>(), brand = state<PortalBrandingDto>();
  const listeners: Array<() => void> = [];
  let disposed = false, statusSequence = 0, statusAbort: AbortController | null = null, testing = false;

  function listen(node: HTMLElement, event: string, callback: EventListener) {
    node.addEventListener(event, callback);
    listeners.push(() => node.removeEventListener(event, callback));
  }
  function error(node: HTMLElement, message: string) {
    node.textContent = message;
    node.classList.toggle('show', !!message);
  }
  function controls() {
    orchSave.disabled = disposed || orch.reading || orch.writing || orch.uncertain || !orch.confirmed;
    brandSave.disabled = disposed || brand.reading || brand.writing || brand.uncertain || !brand.confirmed;
    orchRefresh.disabled = disposed || orch.writing;
    brandRefresh.disabled = disposed || brand.writing;
    test.disabled = orchSave.disabled || orch.dirty || testing;
    key.disabled = disposed || clear.checked;
  }
  function invalidateTest() {
    statusSequence++;
    statusAbort?.abort();
    testing = false;
    testResult.textContent = '';
  }
  function editOrchestrator() {
    orch.revision++; orch.dirty = true;
    invalidateTest();
    orchResult.textContent = orch.uncertain ? 'Previous change outcome is unconfirmed. Refresh saved settings.' : 'Unsaved changes.';
    controls();
  }
  function editBranding() {
    brand.revision++; brand.dirty = true;
    brandResult.textContent = brand.uncertain ? 'Previous change outcome is unconfirmed. Refresh saved branding.' : 'Unsaved changes.';
    controls();
  }

  async function readOrchestrator(): Promise<boolean> {
    if (disposed || orch.writing) return false;
    const sequence = ++orch.sequence, revision = orch.revision, replace = !orch.dirty;
    orch.abort?.abort(); orch.abort = new AbortController(); orch.reading = true;
    invalidateTest(); controls(); error(orchError, '');
    try {
      const saved = await api.orchestrator(orch.abort.signal);
      if (disposed || sequence !== orch.sequence) return false;
      orch.confirmed = saved;
      if (replace && revision === orch.revision) {
        url.value = saved.apiUrl ?? ''; key.value = ''; clear.checked = false;
        orch.dirty = false;
      }
      keyStatus.textContent = saved.hasApiKey ? '— key is set' : '— not set';
      clearRow.style.display = saved.hasApiKey || clear.checked ? '' : 'none';
      if (orch.uncertain) orchResult.textContent = 'Previous change outcome remains unconfirmed. Saved settings refreshed; review your draft before saving.';
      orch.uncertain = false;
      return true;
    } catch {
      if (disposed || sequence !== orch.sequence) return false;
      orch.confirmed = null;
      keyStatus.textContent = '— status unavailable';
      error(orchError, 'Saved Orchestrator settings could not be loaded. Refresh to retry; your draft is preserved.');
      return false;
    } finally {
      if (!disposed && sequence === orch.sequence) { orch.reading = false; controls(); }
    }
  }

  async function readBranding(apply = false): Promise<boolean> {
    if (disposed || brand.writing) return false;
    const sequence = ++brand.sequence, revision = brand.revision, replace = !brand.dirty;
    brand.abort?.abort(); brand.abort = new AbortController(); brand.reading = true;
    controls(); error(brandError, '');
    try {
      const saved = await api.branding(brand.abort.signal);
      if (disposed || sequence !== brand.sequence) return false;
      brand.confirmed = saved;
      if (replace && revision === brand.revision) {
        name.value = saved.displayName ?? ''; logo.value = saved.logoUrl ?? ''; footer.value = saved.footerText ?? '';
        brand.dirty = false;
      }
      if (brand.uncertain) brandResult.textContent = 'Previous change outcome remains unconfirmed. Saved branding refreshed; review your draft before saving.';
      brand.uncertain = false;
      if (apply) renderPortalBranding(saved);
      return true;
    } catch {
      if (disposed || sequence !== brand.sequence) return false;
      brand.confirmed = null;
      error(brandError, 'Saved branding could not be loaded. Refresh to retry; your draft is preserved.');
      return false;
    } finally {
      if (!disposed && sequence === brand.sequence) { brand.reading = false; controls(); }
    }
  }

  async function saveOrchestrator() {
    if (orchSave.disabled) return;
    const body: Required<UpdateOrchestratorSettingsRequest> = { apiUrl: url.value.trim() || null,
      apiKey: clear.checked ? '' : key.value || null };
    if (body.apiUrl && !validApiUrl(body.apiUrl)) {
      error(orchError, 'Enter an absolute http/https Orchestrator URL without credentials.'); return;
    }
    const revision = orch.revision;
    orch.writing = true; invalidateTest(); controls(); error(orchError, ''); orchResult.textContent = 'Saving…';
    try {
      await api.saveOrchestrator(body);
      if (disposed) return;
      if (revision === orch.revision) { orch.dirty = false; key.value = ''; clear.checked = false; }
      orch.writing = false;
      const refreshed = await readOrchestrator();
      if (!disposed) orchResult.textContent = refreshed
        ? orch.dirty ? 'Saved. Newer edits have not been saved.' : 'Saved.'
        : 'Saved. Refresh to confirm the current stored settings.';
    } catch (failure) {
      if (disposed) return;
      orch.uncertain = failure instanceof AdminSettingsOutcomeUnconfirmed;
      orchResult.textContent = '';
      error(orchError, orch.uncertain ? 'Change outcome is unconfirmed. Refresh saved settings before making another change.'
        : 'The settings change was rejected. Review your draft and try again.');
    } finally { if (!disposed) { orch.writing = false; controls(); } }
  }

  async function saveBranding() {
    if (brandSave.disabled) return;
    const body: Required<UpdatePortalBrandingRequest> = { displayName: name.value.trim() || null,
      footerText: footer.value.trim() || null, logoUrl: logo.value.trim() || null };
    const revision = brand.revision;
    brand.writing = true; controls(); error(brandError, ''); brandResult.textContent = 'Saving…';
    try {
      await api.saveBranding(body);
      if (disposed) return;
      if (revision === brand.revision) brand.dirty = false;
      brand.writing = false;
      const refreshed = await readBranding(true);
      if (!disposed) brandResult.textContent = refreshed
        ? brand.dirty ? 'Saved. Newer edits have not been saved.' : 'Saved.'
        : 'Saved. Refresh to confirm the current stored branding.';
    } catch (failure) {
      if (disposed) return;
      brand.uncertain = failure instanceof AdminSettingsOutcomeUnconfirmed;
      brandResult.textContent = '';
      error(brandError, brand.uncertain ? 'Change outcome is unconfirmed. Refresh saved branding before making another change.'
        : 'The branding change was rejected. Review your draft and try again.');
    } finally { if (!disposed) { brand.writing = false; controls(); } }
  }

  async function testConnection() {
    if (test.disabled) return;
    const sequence = ++statusSequence, revision = orch.revision;
    statusAbort?.abort(); statusAbort = new AbortController(); testing = true;
    testResult.textContent = 'Testing saved connection…'; controls();
    try {
      const result = await api.status(statusAbort.signal);
      if (!disposed && sequence === statusSequence && revision === orch.revision)
        testResult.textContent = result.online ? 'Online' : 'Offline — service unreachable';
    } catch {
      if (!disposed && sequence === statusSequence && revision === orch.revision)
        testResult.textContent = 'Connection status could not be confirmed.';
    } finally { if (!disposed && sequence === statusSequence) { testing = false; controls(); } }
  }

  for (const input of [url, key]) listen(input, 'input', editOrchestrator);
  listen(clear, 'change', editOrchestrator);
  for (const input of [name, logo, footer]) listen(input, 'input', editBranding);
  listen(orchSave, 'click', () => { void saveOrchestrator(); });
  listen(brandSave, 'click', () => { void saveBranding(); });
  listen(orchRefresh, 'click', () => { void readOrchestrator(); });
  listen(brandRefresh, 'click', () => { void readBranding(true); });
  listen(test, 'click', () => { void testConnection(); });
  controls();
  return {
    reload: () => Promise.all([readOrchestrator(), readBranding()]),
    dispose() {
      disposed = true; orch.sequence++; brand.sequence++; statusSequence++;
      orch.abort?.abort(); brand.abort?.abort(); statusAbort?.abort();
      listeners.splice(0).forEach(remove => remove()); key.value = ''; controls();
    }
  };
}

function validApiUrl(value: string) {
  try { const url = new URL(value); return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password; }
  catch { return false; }
}
