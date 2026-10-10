import { createGovernanceRenderers, esc } from './governance-view.js';
import { readGovernanceDashboard, readGovernanceFindings, readGovernanceCategories, readGovernanceGlossary,
  readGovernanceSettings, readGovernanceQualityRules, confirmGovernanceScan, confirmGovernanceReview,
  confirmGovernanceBadge, confirmGovernanceFinding, confirmGovernanceSettings, confirmGovernanceCategory,
  confirmGovernanceGlossary, confirmGovernanceNoContent, GovernanceScanFailed } from './governance-responses.js';
import { readQualityJobs, readQualityQueue, readQualityTrend, readQualityRules } from './data-quality-responses.js';
import { asError, isObject } from './http-error.js';
import { getSessionIdentity, hasRole } from './session-identity.js';
import type { GovernanceApi, GovernanceQualityApi, GovernanceState, GovernanceTab, GovernancePortal } from './governance-types.js';
const controllers = new WeakMap<HTMLElement, GovernancePortal>();
interface WriteGuard { pending: boolean; uncertain: boolean; listeners: Set<() => void> }
const writeGuards = new Map<string, WriteGuard>();
interface GovernanceEvent extends Event { readonly currentTarget: HTMLElement }
const isGovernanceTab = (value: unknown): value is GovernanceTab => typeof value === 'string' && ['overview','workqueue','exceptions','badges','glossary','quality','settings'].includes(value);
const eventValue = (event: Event): string => {
  const input = event.currentTarget;
  if (!(input instanceof HTMLInputElement || input instanceof HTMLSelectElement || input instanceof HTMLTextAreaElement)) throw new Error('Governance input required.');
  return input.value;
};
const feedback = (): EtlSqlFeedback => { if (!window.ETLSQLFeedback) throw new Error('Governance feedback is unavailable.'); return window.ETLSQLFeedback; };
function identityKey(): string {
  try { const identity = getSessionIdentity(sessionStorage.getItem('etlsql_token')); return JSON.stringify([identity.claims.iss, identity.claims.tenant_id, identity.subject]); }
  catch { return ''; }
}

// Governance dashboard for the ETL-SQL Portal.
//
// Every value shown here comes from `/api/governance/*`. There is no demo data and no local
// workflow state: a finding a steward ignored has to still be ignored after a refresh, on someone
// else's screen, and in an audit six months later. State that lives in a tab satisfies none of
// those, and — worse — looks identical to state that does.
//
// The four states below are rendered honestly and separately, because collapsing them is how a
// dashboard lies:
//   loading      — we do not know yet, so we claim nothing
//   unauthorized — you are not permitted to see this (403), not "there is nothing here"
//   failed       — we asked and could not find out; never a fabricated stand-in
//   empty        — we asked, we know, and the answer is genuinely nothing
//
// A fifth distinction matters just as much: "never scanned" is not "no findings". A KPI tile
// showing zero cannot tell those apart on its own, so the scan banner says which one it is.
export function createGovernancePortal(opts: {
  host: HTMLElement | null; governanceApi: GovernanceApi; dataQualityApi?: GovernanceQualityApi;
  prepare?: (tab: GovernanceTab) => void;
  notifyFn?: (message: string, options?: EtlSqlFeedbackNotifyOptions) => unknown;
  confirmFn?: (message: string, options?: EtlSqlFeedbackDialogOptions) => Promise<boolean>;
}): GovernancePortal {
  const {
    host: suppliedHost,
    governanceApi,
    dataQualityApi,
    prepare = () => {},
    // Named *Fn, not notify/confirm: a local binding called `confirm` reads as the native dialog
    // at every call site below and is indistinguishable from one to any check scanning for them.
    // No `?.` either — if the shared feedback module failed to load, a governance delete that
    // silently resolves to undefined is a cancelled action nobody was told about, and throwing
    // says so.
    notifyFn = (msg, o) => feedback().notify(msg, o),
    confirmFn = (msg, o) => feedback().confirm(msg, o),
  } = opts;

  if (!suppliedHost) throw new Error('Governance host element required.');
  const host = suppliedHost;
  controllers.get(host)?.dispose();
  const identity = identityKey();
  const writeGuard = writeGuards.get(identity) ?? { pending: false, uncertain: false, listeners: new Set<() => void>() };
  writeGuards.set(identity, writeGuard);
  const writeChanged = () => { if (active()) updateMutationControls(); };
  let disposed = false, generation = 0, trendGeneration = 0;
  let read: AbortController | null = null, trendRead: AbortController | null = null;
  let ownedShell: Element | null = null;
  let viewVersion = 0, confirmationPending = false;
  let dialogReturnFocus: HTMLElement | null = null;
  let trappedDialog: HTMLElement | null = null;
  let trapHandler: ((event: KeyboardEvent) => void) | null = null;
  let editingTerm: StewardshipGlossaryTermDto | null = null, editingCategory: GovernanceCategoryDto | null = null;
  function detachTrap(): void {
    if (trappedDialog && trapHandler) trappedDialog.removeEventListener('keydown', trapHandler);
    trappedDialog = null; trapHandler = null;
  }
  function dispose(): void {
    if (disposed) return;
    disposed = true; generation++; trendGeneration++; read?.abort(); trendRead?.abort();
    writeGuard.listeners.delete(writeChanged);
    if (!writeGuard.pending && !writeGuard.uncertain && writeGuard.listeners.size === 0) writeGuards.delete(identity);
    detachTrap(); dialogReturnFocus = null; editingTerm = null; editingCategory = null;
    state.dashboard = null; state.findings = []; state.glossary = []; state.categories = [];
    state.activeDqTrend = null; state.activeDqRules = []; state.dqJobs = []; state.dqTrends = Object.create(null);
    if (ownedShell && host.firstElementChild === ownedShell) host.replaceChildren();
    if (controllers.get(host) === portal) controllers.delete(host);
  }
  function active(): boolean {
    if (!disposed && (identity !== identityKey() || ownedShell && host.firstElementChild !== ownedShell)) dispose();
    return !disposed;
  }
  const element = <T extends HTMLElement>(selector: string): T => {
    const found = host.querySelector<T>(selector); if (!found) throw new Error('Missing governance element ' + selector); return found;
  };
  const input = (selector: string) => element<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(selector);
  const permitted = (...roles: string[]) => {
    const token = sessionStorage.getItem('etlsql_token');
    if (!token) return true; // The sandbox injects APIs without an authenticated Portal session.
    try { return hasRole(getSessionIdentity(token), ...roles); } catch { return false; }
  };
  const state: GovernanceState = {
    mode: 'all',            // 'mine' | 'all'
    tab: 'overview',        // overview | workqueue | exceptions | badges | glossary | settings
    searchFilter: '',
    badgeFilter: 'all',
    categoryFilter: 'all',
    // load: 'idle' | 'loading' | 'ready' | 'unauthorized' | 'failed'
    load: 'idle',
    error: null,
    mutation: 'idle', mutationMessage: null,
    canConfigure: permitted('Admin', 'GovernanceManager', 'StewardshipManager'),
    canDecide: permitted('Admin', 'GovernanceManager', 'StewardshipManager', 'DataSteward'),
    dashboard: null,
    findings: [],
    categories: [],
    glossary: [],
    settings: null,
    editingTerm: null,
    editingCategory: null,
    pendingFindingId: null, pendingDecision: null, pendingVersion: null,
    activeDqTrendJob: null,
    activeDqTrend: null,
    activeDqRules: [],
    activeDqLoading: false, activeDqError: null,
    dqJobs: [],
    dqTrends: Object.create(null),
    dqLoading: false, dqLoad: 'idle',
    allRules: [],
    quarantineQueue: [],
    allRulesLoaded: false,
    dqSearchFilter: '',
  };

  const isForbidden = (error: unknown) => isObject(error) && (error.status === 403 || error.status === 401);

  const configureSelectors = '#btnRunScan,#btnSaveScoringSettings,#btnConfirmGlossaryModal,#btnConfirmCategoryModal,[data-delete-term],[data-disable-cat]';
  const decideSelectors = '[data-review-asset],[data-assign-badge-to],.remove-badge-btn,[data-reopen-finding],[data-ignore-finding],[data-accept-risk],#btnConfirmDecision';
  function updateMutationControls(): void {
    const previousMutation = state.mutation;
    state.mutation = writeGuard.pending ? 'pending' : writeGuard.uncertain ? 'unconfirmed' : 'idle';
    if (state.mutation === 'pending') state.mutationMessage = 'Saving governance change…';
    else if (state.mutation === 'unconfirmed') state.mutationMessage = 'Change outcome is unconfirmed. Refresh successfully and inspect the recorded state before trying another change.';
    else if (previousMutation !== 'idle') state.mutationMessage = null;
    state.canConfigure = permitted('Admin', 'GovernanceManager', 'StewardshipManager');
    state.canDecide = permitted('Admin', 'GovernanceManager', 'StewardshipManager', 'DataSteward');
    const blocked = state.load !== 'ready' || state.mutation !== 'idle' || confirmationPending;
    for (const [selectors, allowed] of [[configureSelectors, state.canConfigure], [decideSelectors, state.canDecide]] as const) {
      host.querySelectorAll<HTMLButtonElement | HTMLSelectElement>(selectors).forEach(control => { control.disabled = blocked || !allowed; });
    }
    host.querySelectorAll<HTMLButtonElement>('#btnAddNewTerm,#btnAddNewCategory,[data-edit-term],[data-edit-cat]')
      .forEach(control => { control.disabled = state.load !== 'ready' || !state.canConfigure; });
    const scan = host.querySelector<HTMLButtonElement>('#btnRunScan');
    if (scan && state.dashboard?.lastScan?.status === 'running') scan.disabled = true;
    const refresh = host.querySelector<HTMLButtonElement>('#btnGovRefresh');
    if (refresh) refresh.disabled = state.mutation === 'pending' || confirmationPending;
    const message = host.querySelector<HTMLElement>('#govMutationStatus');
    if (message) { message.hidden = !state.mutationMessage; message.textContent = state.mutationMessage; }
  }
  /** A sent change is only successful when the server acknowledgement matches its request. */
  async function mutate(action: () => Promise<unknown>, { success, failure, auditAction, validate }: {
    success: string; failure: string; auditAction: string; validate: (value: unknown) => void;
  }) {
    if (!active()) return null;
    updateMutationControls();
    const allowed = auditAction.startsWith('governance.finding.') || auditAction.startsWith('governance.asset.') || auditAction.startsWith('governance.badge.')
      ? state.canDecide : state.canConfigure;
    if (!active() || state.load !== 'ready' || state.mutation !== 'idle' || confirmationPending || !allowed) return null;
    if (auditAction === 'governance.scan' && state.dashboard?.lastScan?.status === 'running') return null;
    const version = viewVersion, scope = state.mode, tab = state.tab;
    const current = () => active() && version === viewVersion && scope === state.mode && tab === state.tab;
    writeGuard.pending = true; writeGuard.uncertain = true;
    state.mutation = 'pending'; state.mutationMessage = 'Saving governance change…'; updateMutationControls();
    try {
      const result = await action();
      validate(result);
      writeGuard.uncertain = false;
      writeGuard.pending = false;
      if (!active()) return null;
      state.mutation = 'idle';
      if (current()) {
        if (auditAction === 'governance.scoring.update') { settingsDirty = false; settingsDraft.clear(); }
        state.mutationMessage = null;
        notifyFn(success, { title: 'Governance', tone: 'success', auditAction });
        await load();
        if (current()) await render();
      } else state.mutationMessage = 'The change was recorded. Refresh to inspect its current state.';
      if (active()) updateMutationControls();
      return result;
    } catch (err) {
      const rejected = isObject(err) && typeof err.status === 'number' && err.status >= 400 && err.status < 500 && err.status !== 408;
      writeGuard.uncertain = !rejected && !(err instanceof GovernanceScanFailed);
      writeGuard.pending = false;
      if (!active()) return null;
      state.mutation = rejected || err instanceof GovernanceScanFailed ? 'idle' : 'unconfirmed';
      state.mutationMessage = state.mutation === 'unconfirmed'
        ? 'Change outcome is unconfirmed. Refresh successfully and inspect the recorded state before trying another change.'
        : isForbidden(err) ? 'Your role does not permit this governance change.' : failure;
      updateMutationControls();
      if (current()) notifyFn(state.mutationMessage, { title: 'Governance', tone: 'warning' });
      return null;
    } finally {
      writeGuard.pending = false;
      for (const listener of writeGuard.listeners) listener();
      if (!writeGuard.uncertain && writeGuard.listeners.size === 0) writeGuards.delete(identity);
    }
  }

  async function confirmChange(message: string, options: EtlSqlFeedbackDialogOptions): Promise<boolean> {
    if (!active()) return false;
    updateMutationControls();
    if (!active() || confirmationPending || state.mutation !== 'idle' || state.load !== 'ready' || !state.canConfigure) return false;
    const version = viewVersion;
    confirmationPending = true; updateMutationControls();
    try { return await confirmFn(message, options) && active() && version === viewVersion; }
    catch { if (active() && version === viewVersion) notifyFn('The confirmation could not be completed.', { title: 'Governance', tone: 'warning' }); return false; }
    finally { confirmationPending = false; if (active()) updateMutationControls(); }
  }

  async function load(): Promise<void> {
    if (!active()) return;
    const version = ++generation, scope = state.mode;
    read?.abort(); read = new AbortController();
    const signal = read.signal;
    const valid = () => active() && version === generation && scope === state.mode;
    state.load = 'loading'; state.error = null;
    state.dashboard = null; state.findings = []; state.categories = []; state.glossary = []; state.settings = null;
    state.dqJobs = []; state.dqTrends = Object.create(null); state.allRules = []; state.quarantineQueue = [];
    state.allRulesLoaded = false; state.dqLoad = 'loading'; state.dqLoading = true;
    try {
      const replies = await Promise.all([
        governanceApi.dashboard({ scope }, signal), governanceApi.findings({ limit: 500 }, signal),
        governanceApi.categories(signal), governanceApi.glossary(signal), governanceApi.settings(signal),
      ]);
      const dashboard = readGovernanceDashboard(replies[0]), findings = readGovernanceFindings(replies[1]);
      const categories = readGovernanceCategories(replies[2]), glossary = readGovernanceGlossary(replies[3]);
      const settings = readGovernanceSettings(replies[4]);
      if (!valid()) return;
      Object.assign(state, { dashboard, findings, categories, glossary, settings });
      if (dataQualityApi) {
        try {
          const [jobs, rules, queue] = await Promise.all([
            dataQualityApi.qualityJobs(signal), dataQualityApi.qualityRulesAll(signal), dataQualityApi.quarantineQueue({}, signal),
          ]);
          const dqJobs = readQualityJobs(jobs), allRules = readGovernanceQualityRules(rules), quarantineQueue = readQualityQueue(queue);
          if (!valid()) return;
          Object.assign(state, { dqJobs, allRules, quarantineQueue, allRulesLoaded: true });
          let nextJob = 0;
          const loadTrends = async () => {
            while (valid() && nextJob < dqJobs.length) {
              const job = dqJobs[nextJob++];
              try {
                const trend = readQualityTrend(await dataQualityApi.qualityTrend({ jobName: job.name }, signal), job.name);
                if (valid()) state.dqTrends[job.name] = trend;
              } catch { /* A missing trend remains unavailable; it cannot contribute invented counts. */ }
            }
          };
          await Promise.all(Array.from({ length: Math.min(5, dqJobs.length) }, loadTrends));
          if (!valid()) return;
          state.dqLoad = 'ready';
        } catch (error) {
          if (!valid()) return;
          state.dqJobs = []; state.dqTrends = Object.create(null); state.allRules = []; state.quarantineQueue = [];
          state.allRulesLoaded = false; state.dqLoad = isForbidden(error) ? 'unauthorized' : 'failed';
        }
      } else state.dqLoad = 'failed';
      if (valid()) state.load = 'ready';
    } catch (error) {
      if (!valid()) return;
      state.dashboard = null; state.findings = []; state.categories = []; state.glossary = []; state.settings = null;
      state.load = isForbidden(error) ? 'unauthorized' : 'failed'; state.error = asError(error).message;
    } finally { if (valid()) state.dqLoading = false; }
  }

  const views = createGovernanceRenderers(state, !!dataQualityApi);
  let renderedTab: GovernanceTab | null = null, renderedScope: GovernanceState['mode'] | null = null;
  let settingsDirty = false;
  const settingsDraft = new Map<string, { value: string; checked: boolean }>();
  const sameElement = (node: HTMLElement | null): HTMLElement | null => {
    if (!node) return null;
    if (node.id) return host.querySelector<HTMLElement>('#' + CSS.escape(node.id));
    for (const attribute of ['data-edit-term', 'data-edit-cat', 'data-ignore-finding', 'data-accept-risk', 'data-view-dq-trend']) {
      const value = node.getAttribute(attribute);
      if (value !== null) return host.querySelector<HTMLElement>(`[${attribute}="${CSS.escape(value)}"]`);
    }
    return null;
  };
  const render = async (): Promise<void> => {
    if (!active()) return;
    prepare(state.tab);
    const pending = state.load === 'idle' ? load() : null;
    const draw = () => {
      if (!active()) return;
      const sameView = renderedTab === state.tab && renderedScope === state.mode;
      const focus = sameView && document.activeElement instanceof HTMLElement ? document.activeElement : null;
      const oldDialog = sameView ? trappedDialog : null, returnFocus = dialogReturnFocus;
      const fields = oldDialog ? [...oldDialog.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('input,select,textarea')]
        .map(field => ({ id: field.id, value: field.value, checked: field instanceof HTMLInputElement && field.checked,
          readOnly: field instanceof HTMLInputElement && field.readOnly })) : [];
      const title = oldDialog?.querySelector('h3')?.textContent;
      if (sameView && settingsDirty) host.querySelectorAll<HTMLInputElement | HTMLSelectElement>('.gov-settings-grid input,.gov-settings-grid select')
        .forEach(field => settingsDraft.set(field.id, { value: field.value, checked: field instanceof HTMLInputElement && field.checked }));
      if (!sameView) { settingsDraft.clear(); settingsDirty = false; }
      const body = state.load !== 'ready' ? views.stateBanner() : ({
        overview: views.renderOverview, workqueue: views.renderWorkqueue, exceptions: views.renderExceptions,
        glossary: views.renderGlossary, quality: views.renderQuality, settings: views.renderSettings,
        badges: views.renderOverview,
      }[state.tab])();
      detachTrap();
      host.innerHTML = views.renderShell(body);
      ownedShell = host.firstElementChild;
      renderedTab = state.tab; renderedScope = state.mode;
      for (const [id, draft] of settingsDraft) {
        const field = host.querySelector<HTMLInputElement | HTMLSelectElement>('#' + CSS.escape(id));
        if (field) { field.value = draft.value; if (field instanceof HTMLInputElement) field.checked = draft.checked; }
      }
      const scoreInput = host.querySelector<HTMLInputElement>('#settingsTargetScore');
      const scoreLabel = host.querySelector<HTMLElement>('.setting-label span b');
      if (scoreInput && scoreLabel) scoreLabel.textContent = scoreInput.value;
      for (const [checkId, valueId] of [['chkEnableMeta','settingsDeductMeta'], ['chkEnablePII','settingsDeductPII'],
        ['chkEnableGlossary','settingsDeductGlossary'], ['chkEnableStale','settingsDeductStale']]) {
        const check = host.querySelector<HTMLInputElement>('#' + checkId), field = host.querySelector<HTMLInputElement>('#' + valueId);
        if (check && field) field.disabled = !check.checked;
      }
      const nextDialog = oldDialog?.id ? host.querySelector<HTMLElement>('#' + CSS.escape(oldDialog.id)) : null;
      if (nextDialog) {
        fields.forEach(draft => {
          const field = nextDialog.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('#' + CSS.escape(draft.id));
          if (field) { field.value = draft.value; if (field instanceof HTMLInputElement) { field.checked = draft.checked; field.readOnly = draft.readOnly; } }
        });
        const heading = nextDialog.querySelector('h3'); if (heading && title) heading.textContent = title;
        dialogReturnFocus = sameElement(returnFocus);
        openDialog(nextDialog, true);
      } else if (state.activeDqTrendJob) {
        dialogReturnFocus = sameElement(focus);
        openDialog(element<HTMLElement>('#govDqTrendDialog'), true);
      } else dialogReturnFocus = null;
      bind();
      if (!nextDialog && state.activeDqTrendJob && trappedDialog) focusable(trappedDialog)[0]?.focus();
      else sameElement(focus)?.focus();
      updateMutationControls();
    };
    draw();
    if (pending) { await pending; draw(); }
  };

  // Opening a dialog moves focus into it and traps Tab; closing returns focus to whatever opened
  // it. Without the return, keyboard focus lands back at the top of the document and the user has
  // to tab through the whole page to get back to where they were — which is why "it works with a
  // mouse" is not evidence a dialog is usable.
  const focusable = (dialog: HTMLElement) => [...dialog.querySelectorAll<HTMLElement>(
    'button, [href], input:not([type=hidden]), select, textarea, [tabindex]:not([tabindex="-1"])')]
    .filter(el => (!('disabled' in el) || !el.disabled) && el.offsetParent !== null);

  function openDialog(dialog: HTMLElement, restored = false) {
    detachTrap();
    if (!restored) {
      viewVersion++;
      dialogReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }
    trappedDialog = dialog;
    dialog.classList.add('open');
    dialog.removeAttribute('aria-hidden');

    const items = focusable(dialog);
    if (!restored) items[0]?.focus();

    trapHandler = event => {
      if (event.key === 'Escape') {
        event.preventDefault();
        if (dialog.id === 'govDqTrendDialog') { closeTrend(); return; }
        closeDialog(dialog); return;
      }
      if (event.key !== 'Tab') return;
      const current = focusable(dialog);
      if (current.length === 0) return;
      const first = current[0];
      const last = current[current.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    dialog.addEventListener('keydown', trapHandler);
  }

  function closeDialog(dialog: HTMLElement) {
    detachTrap(); viewVersion++;
    dialog.classList.remove('open');
    dialog.setAttribute('aria-hidden', 'true');
    if (dialogReturnFocus?.isConnected) dialogReturnFocus.focus();
    dialogReturnFocus = null;
    if (dialog.id === 'decisionModalBackdrop') {
      state.pendingFindingId = null; state.pendingDecision = null; state.pendingVersion = null;
    }
  }

  function closeTrend(): void {
    if (trappedDialog?.id === 'govDqTrendDialog') closeDialog(trappedDialog);
    trendGeneration++; trendRead?.abort();
    state.activeDqTrendJob = null; state.activeDqTrend = null; state.activeDqRules = [];
    state.activeDqError = null; state.activeDqLoading = false;
    void render();
  }

  function dismissView(): void {
    viewVersion++;
    if (trappedDialog) closeDialog(trappedDialog);
    trendGeneration++; trendRead?.abort();
    state.activeDqTrendJob = null; state.activeDqTrend = null; state.activeDqRules = [];
    state.activeDqError = null; state.activeDqLoading = false;
  }

  async function reload(): Promise<void> {
    if (!active() || state.mutation === 'pending' || confirmationPending) return;
    dismissView();
    const version = viewVersion;
    state.load = 'idle';
    await render();
    if (active() && version === viewVersion && readsReady()) {
      writeGuard.uncertain = false;
      state.mutation = 'idle'; state.mutationMessage = null; updateMutationControls();
    }
  }

  function readsReady(): boolean { return state.load === 'ready'; }

  // Listeners are bound rather than inlined: the Portal CSP sets script-src-attr 'none', so an
  // inline onclick silently does nothing.
  function bind() {
    const changed = (event: Event) => {
      if (event.type === 'input' || event.type === 'change') {
        viewVersion++;
        if (event.target instanceof Element && event.target.closest('.gov-settings-grid')) settingsDirty = true;
      }
    };
    const on = (sel: string, evt: string, fn: (event: GovernanceEvent) => unknown) => host.querySelector(sel)?.addEventListener(evt, event => { if (active()) { changed(event); void fn(event as GovernanceEvent); } });
    const each = (sel: string, evt: string, fn: (event: GovernanceEvent) => unknown) => host.querySelectorAll(sel).forEach(el => el.addEventListener(evt, event => { if (active()) { changed(event); void fn(event as GovernanceEvent); } }));
    each('.gov-modal input,.gov-modal select,.gov-modal textarea,.gov-settings-grid input,.gov-settings-grid select', 'input', () => {});

    each('[data-view-dq-trend]', 'click', async e => {
      if (trappedDialog && trappedDialog.id !== 'govDqTrendDialog') return;
      const jobName = e.currentTarget.getAttribute('data-view-dq-trend');
      if (jobName && dataQualityApi) {
        const version = ++trendGeneration;
        trendRead?.abort(); trendRead = new AbortController();
        const signal = trendRead.signal;
        const valid = () => active() && version === trendGeneration && state.activeDqTrendJob === jobName;
        state.activeDqTrendJob = jobName;
        state.activeDqTrend = null;
        state.activeDqRules = [];
        state.activeDqError = null;
        state.activeDqLoading = true;
        viewVersion++;
        await render();
        const dialog = host.querySelector<HTMLElement>('#govDqTrendDialog');
        if (dialog && !trappedDialog?.classList.contains('open')) openDialog(dialog);
        try {
          const [trend, rules] = await Promise.all([
            dataQualityApi.qualityTrend({ jobName }, signal),
            dataQualityApi.qualityRules(jobName, signal)
          ]);
          const validatedTrend = readQualityTrend(trend, jobName), validatedRules = readQualityRules(rules, jobName);
          if (!valid()) return;
          state.activeDqTrend = validatedTrend; state.activeDqRules = validatedRules;
        } catch {
          if (!valid()) return;
          state.activeDqError = 'Data quality trend and rules are unavailable or incomplete.';
        } finally {
          if (valid()) { state.activeDqLoading = false; await render(); }
        }
      }
    });

    on('#govDqTrendCloseBtn', 'click', closeTrend);

    on('#govDqSearch', 'input', e => {
      state.dqSearchFilter = eventValue(e);
      const currentInput = host.querySelector<HTMLInputElement>('#govDqSearch');
      const caret = currentInput?.selectionStart;
      render().then(() => {
        const next = host.querySelector<HTMLInputElement>('#govDqSearch');
        if (next && active()) { next.focus(); next.setSelectionRange(caret ?? null, caret ?? null); }
      });
    });

    host.querySelectorAll('[data-gov-tab]').forEach(btn => btn.addEventListener('click', () => {
      if (!active()) return;
      dismissView();
      const tab = btn.getAttribute('data-gov-tab'); if (isGovernanceTab(tab)) state.tab = tab;
      render();
    }));
    on('#btnGovRetry', 'click', reload);
    on('#btnGovRefresh', 'click', reload);
    on('#govScope', 'change', async e => {
      dismissView();
      state.mode = eventValue(e) === 'mine' ? 'mine' : 'all';
      state.load = 'idle';
      await render();
    });
    on('#govSearch', 'input', e => {
      state.searchFilter = eventValue(e);
      const currentInput = host.querySelector<HTMLInputElement>('#govSearch');
      const caret = currentInput?.selectionStart;
      render().then(() => {
        const next = host.querySelector<HTMLInputElement>('#govSearch');
        if (next && active()) { next.focus(); next.setSelectionRange(caret ?? null, caret ?? null); }
      });
    });

    on('#btnRunScan', 'click', () => {
      const observedId = state.dashboard?.lastScan?.id ?? 0;
      return mutate(() => governanceApi.scan(), {
        success: 'Governance scan completed.', failure: 'The scan could not be run.',
        auditAction: 'governance.scan', validate: value => confirmGovernanceScan(value, observedId),
      });
    });

    each('[data-review-asset]', 'click', e => {
      const btn = e.currentTarget;
      const payload: ReviewAssetRequest = {
        assetKey: btn.getAttribute('data-review-asset') || '',
        assetVersion: btn.getAttribute('data-asset-version') || '',
      };
      mutate(() => governanceApi.reviewAsset(payload), {
        success: 'Asset marked reviewed at its current version.',
        failure: 'The review could not be recorded.',
        auditAction: 'governance.asset.review',
        validate: value => confirmGovernanceReview(value, payload),
      });
    });

    each('[data-assign-badge-to]', 'change', e => {
      const select = e.currentTarget as HTMLSelectElement;
      const badge = select.value;
      if (!badge) return;
      const payload: AssignBadgeRequest = {
        assetKey: select.getAttribute('data-assign-badge-to') || '',
        badge,
        assetVersion: select.getAttribute('data-asset-version'),
      };
      mutate(() => governanceApi.assignBadge(payload), {
        success: `Badge "${badge}" assigned.`,
        failure: 'The badge could not be assigned.',
        auditAction: 'governance.badge.assign',
        validate: value => confirmGovernanceBadge(value, payload),
      });
    });

    each('.remove-badge-btn', 'click', e => {
      e.stopPropagation();
      const btn = e.currentTarget;
      mutate(() => governanceApi.removeBadge({
        assetKey: btn.getAttribute('data-remove-badge-asset') || '',
        badge: btn.getAttribute('data-remove-badge-name') || '',
      }), {
        success: 'Badge removed.',
        failure: 'The badge could not be removed.',
        auditAction: 'governance.badge.remove',
        validate: confirmGovernanceNoContent,
      });
    });

    each('[data-reopen-finding]', 'click', e => {
      const id = Number(e.currentTarget.getAttribute('data-reopen-finding'));
      const payload: DecideFindingRequest = {
        decision: 'reopen',
        reason: 'Reopened by steward from the exceptions queue.',
        assetVersion: '',
      };
      const observed = state.findings.find(item => item.id === id)?.decisions.map(item => item.id) ?? [];
      mutate(() => governanceApi.decideFinding(id, payload), {
        success: 'Finding reopened.',
        failure: 'The finding could not be reopened.',
        auditAction: 'governance.finding.reopen',
        validate: value => confirmGovernanceFinding(value, id, payload, observed),
      });
    });

    const decisionModal = element<HTMLElement>('#decisionModalBackdrop');
    const openDecision = (id: number, version: string | null, decision: 'ignore' | 'accept-risk', assetLabel: string) => {
      state.pendingFindingId = id;
      state.pendingDecision = decision;
      state.pendingVersion = version;
      element<HTMLElement>('#decisionModalTitle').textContent =
        decision === 'ignore' ? 'Ignore as false positive' : 'Accept risk';
      element<HTMLInputElement>('#decisionAsset').value = assetLabel;
      element<HTMLTextAreaElement>('#decisionReason').value = '';
      element<HTMLSelectElement>('#decisionCategory').innerHTML =
        ['<option value="">No category</option>']
          .concat(state.categories.filter(c => !c.disabled)
            .map(c => `<option value="${esc(c.value)}">${esc(c.label)}</option>`))
          .join('');
      openDialog(decisionModal);
    };

    each('[data-accept-risk]', 'click', e => {
      const btn = e.currentTarget;
      const finding = state.findings.find(f => f.id === Number(btn.getAttribute('data-accept-risk')));
      openDecision(Number(btn.getAttribute('data-accept-risk')),
        btn.getAttribute('data-asset-version'), 'accept-risk', finding?.assetKey || '');
    });
    each('[data-ignore-finding]', 'click', e => {
      const btn = e.currentTarget;
      const finding = state.findings.find(f => f.id === Number(btn.getAttribute('data-ignore-finding')));
      openDecision(Number(btn.getAttribute('data-ignore-finding')),
        btn.getAttribute('data-asset-version'), 'ignore', finding?.assetKey || '');
    });

    on('#btnCancelDecision', 'click', () => closeDialog(decisionModal));
    on('#btnConfirmDecision', 'click', async () => {
      if (!decisionModal.classList.contains('open')) return;
      const reason = element<HTMLTextAreaElement>('#decisionReason').value.trim();
      if (!reason) {
        notifyFn('Enter a justification. The decision has to be reviewable later.',
          { title: 'Justification required', tone: 'warning' });
        return;
      }
      const id = state.pendingFindingId, decision = state.pendingDecision;
      if (id === null || decision === null || !state.pendingVersion) return;
      const payload: DecideFindingRequest = {
        decision,
        categoryValue: element<HTMLSelectElement>('#decisionCategory').value || null,
        reason,
        assetVersion: state.pendingVersion || '',
      };
      const observed = state.findings.find(item => item.id === id)?.decisions.map(item => item.id) ?? [];
      closeDialog(decisionModal);
      await mutate(() => governanceApi.decideFinding(id, payload), {
        success: 'Decision recorded.',
        failure: 'The decision could not be recorded.',
        auditAction: `governance.finding.${decision}`,
        validate: value => confirmGovernanceFinding(value, id, payload, observed),
      });
    });

    // ── glossary ──
    const gModal = element<HTMLElement>('#glossaryModalBackdrop');
    on('#btnAddNewTerm', 'click', () => {
      state.editingTerm = null;
      editingTerm = null;
      element<HTMLElement>('#glossaryModalTitle').textContent = 'Define term';
      ['#glossaryTerm', '#glossaryType', '#glossaryAliases', '#glossaryFormula', '#glossaryDesc']
        .forEach(sel => { input(sel).value = ''; });
      element<HTMLInputElement>('#glossaryTerm').readOnly = false;
      openDialog(gModal);
    });
    on('#btnCancelGlossaryModal', 'click', () => closeDialog(gModal));
    each('[data-edit-term]', 'click', e => {
      const term = state.glossary.find(t => t.term === e.currentTarget.getAttribute('data-edit-term'));
      if (!term) return;
      state.editingTerm = term.term;
      editingTerm = term;
      element<HTMLElement>('#glossaryModalTitle').textContent = 'Edit term';
      element<HTMLInputElement>('#glossaryTerm').value = term.term;
      element<HTMLInputElement>('#glossaryTerm').readOnly = true;
      element<HTMLInputElement>('#glossaryType').value = term.dataType;
      element<HTMLInputElement>('#glossaryAliases').value = term.aliases;
      element<HTMLInputElement>('#glossaryFormula').value = term.formula || '';
      element<HTMLTextAreaElement>('#glossaryDesc').value = term.description;
      openDialog(gModal);
    });
    on('#btnConfirmGlossaryModal', 'click', async () => {
      if (!gModal.classList.contains('open')) return;
      const payload: SaveGlossaryTermRequest = {
        term: element<HTMLInputElement>('#glossaryTerm').value.trim(),
        dataType: element<HTMLInputElement>('#glossaryType').value.trim(),
        aliases: element<HTMLInputElement>('#glossaryAliases').value.trim(),
        formula: element<HTMLInputElement>('#glossaryFormula').value.trim() || null,
        description: element<HTMLTextAreaElement>('#glossaryDesc').value.trim(),
        steward: editingTerm?.steward ?? null,
        disabled: editingTerm?.disabled ?? false,
      };
      if (!payload.term || !payload.dataType || !payload.aliases || !payload.description) {
        notifyFn('Term, type, aliases, and definition are required.',
          { title: 'Complete required fields', tone: 'warning' });
        return;
      }
      closeDialog(gModal);
      await mutate(() => governanceApi.saveGlossaryTerm(payload), {
        success: 'Glossary term saved.',
        failure: 'The term could not be saved.',
        auditAction: 'governance.glossary.save',
        validate: value => confirmGovernanceGlossary(value, payload),
      });
    });
    each('[data-delete-term]', 'click', async e => {
      const term = e.currentTarget.getAttribute('data-delete-term');
      const ok = await confirmChange(`Delete glossary term "${term}"?`, {
        title: 'Delete glossary term',
        impact: 'Metadata rules that reference this term may stop matching.',
        confirmLabel: 'Delete term',
        danger: true,
        auditAction: 'governance.glossary.delete',
      });
      if (!ok) return;
      await mutate(() => governanceApi.deleteGlossaryTerm(term || ''), {
        success: 'Glossary term deleted.',
        failure: 'The term could not be deleted.',
        auditAction: 'governance.glossary.delete',
        validate: confirmGovernanceNoContent,
      });
    });

    // ── settings ──
    on('#settingsTargetScore', 'input', e => {
      const label = element<HTMLElement>('.setting-label span b');
      if (label) label.textContent = eventValue(e);
    });
    [['#chkEnableMeta', '#settingsDeductMeta'], ['#chkEnablePII', '#settingsDeductPII'],
    ['#chkEnableGlossary', '#settingsDeductGlossary'], ['#chkEnableStale', '#settingsDeductStale']]
      .forEach(([chk, num]) => on(chk, 'change', e => {
        const target = element<HTMLInputElement>(num);
        if (target) target.disabled = !(e.currentTarget instanceof HTMLInputElement && e.currentTarget.checked);
      }));

    on('#btnSaveScoringSettings', 'click', async () => {
      const num = (sel: string) => {
        const value = input(sel).value.trim();
        return value === '' ? NaN : Number(value);
      };
      const payload: UpdateStewardshipSettingsRequest = {
        targetScore: num('#settingsTargetScore'),
        enableMetadataCheck: element<HTMLInputElement>('#chkEnableMeta').checked,
        enableProtectedDataCheck: element<HTMLInputElement>('#chkEnablePII').checked,
        enableGlossaryCheck: element<HTMLInputElement>('#chkEnableGlossary').checked,
        enableStalenessCheck: element<HTMLInputElement>('#chkEnableStale').checked,
        deductMetadata: num('#settingsDeductMeta'),
        deductProtectedData: num('#settingsDeductPII'),
        deductGlossary: num('#settingsDeductGlossary'),
        deductStaleness: num('#settingsDeductStale'),
        staleAfterDays: num('#settingsStaleDays'),
        policyLevel: element<HTMLSelectElement>('#settingsPolicyLevel').value,
      };
      if (![payload.targetScore, payload.deductMetadata, payload.deductProtectedData, payload.deductGlossary, payload.deductStaleness]
        .every(value => Number.isSafeInteger(value) && value >= 0 && value <= 100)
        || !Number.isSafeInteger(payload.staleAfterDays) || payload.staleAfterDays < 1) {
        notifyFn('Enter whole-number deductions and a target from 0 to 100, and at least one stale-after day.', { title: 'Check scoring values', tone: 'warning' });
        return;
      }
      const observedVersion = state.settings?.version ?? 0;
      await mutate(() => governanceApi.saveSettings(payload), {
        success: 'Scoring configuration saved.',
        failure: 'The scoring configuration could not be saved.',
        auditAction: 'governance.scoring.update',
        validate: value => confirmGovernanceSettings(value, payload, observedVersion),
      });
    });

    const cModal = element<HTMLElement>('#categoryModalBackdrop');
    on('#btnAddNewCategory', 'click', () => {
      state.editingCategory = null;
      editingCategory = null;
      element<HTMLElement>('#categoryModalTitle').textContent = 'Define category';
      element<HTMLInputElement>('#catLabel').value = '';
      element<HTMLInputElement>('#catValue').value = '';
      element<HTMLInputElement>('#catValue').readOnly = false;
      element<HTMLSelectElement>('#catColor').value = 'noise';
      element<HTMLInputElement>('#catExpiry').value = '';
      openDialog(cModal);
    });
    on('#btnCancelCategoryModal', 'click', () => closeDialog(cModal));
    each('[data-edit-cat]', 'click', e => {
      const cat = state.categories.find(c => c.value === e.currentTarget.getAttribute('data-edit-cat'));
      if (!cat) return;
      state.editingCategory = cat.value;
      editingCategory = cat;
      element<HTMLElement>('#categoryModalTitle').textContent = 'Edit category';
      element<HTMLInputElement>('#catLabel').value = cat.label;
      element<HTMLInputElement>('#catValue').value = cat.value;
      element<HTMLInputElement>('#catValue').readOnly = true;
      element<HTMLSelectElement>('#catColor').value = cat.color;
      element<HTMLInputElement>('#catExpiry').value = cat.expiryDays == null ? '' : String(cat.expiryDays);
      openDialog(cModal);
    });
    on('#btnConfirmCategoryModal', 'click', async () => {
      if (!cModal.classList.contains('open')) return;
      const label = element<HTMLInputElement>('#catLabel').value.trim();
      const value = element<HTMLInputElement>('#catValue').value.trim();
      if (!label || !value) {
        notifyFn('Enter both a category label and value.',
          { title: 'Complete required fields', tone: 'warning' });
        return;
      }
      const expiryText = element<HTMLInputElement>('#catExpiry').value.trim();
      const expiry = expiryText ? Number(expiryText) : null;
      if (expiry !== null && (!Number.isSafeInteger(expiry) || expiry < 1)) {
        notifyFn('Enter a positive whole number of expiry days or leave it blank.', { title: 'Check expiry days', tone: 'warning' }); return;
      }
      const payload: SaveGovernanceCategoryRequest = {
        value,
        label,
        color: element<HTMLSelectElement>('#catColor').value,
        expiryDays: expiry,
        disabled: editingCategory?.disabled ?? false,
      };
      closeDialog(cModal);
      await mutate(() => governanceApi.saveCategory(payload), {
        success: 'Category saved.',
        failure: 'The category could not be saved.',
        auditAction: 'governance.category.save',
        validate: value => confirmGovernanceCategory(value, payload),
      });
    });
    each('[data-disable-cat]', 'click', async e => {
      const value = e.currentTarget.getAttribute('data-disable-cat');
      // Disable, never delete: historical suppressions cite this value, and removing it would
      // leave them pointing at a reason nobody can look up.
      const ok = await confirmChange(`Disable bypass category "${value}"?`, {
        title: 'Disable category',
        impact: 'Stewards can no longer choose it. Existing decisions keep citing it.',
        confirmLabel: 'Disable',
        danger: true,
        auditAction: 'governance.category.disable',
      });
      if (!ok) return;
      await mutate(() => governanceApi.disableCategory(value || ''), {
        success: 'Category disabled.',
        failure: 'The category could not be disabled.',
        auditAction: 'governance.category.disable',
        validate: confirmGovernanceNoContent,
      });
    });
  }

  const portal: GovernancePortal = {
    render,
    setTab(tabName: string) {
      if (!active()) return Promise.resolve();
      dismissView();
      state.tab = isGovernanceTab(tabName) ? tabName : 'overview';
      return render();
    },
    reload,
    dispose,
    state,
  };
  controllers.set(host, portal);
  writeGuard.listeners.add(writeChanged);
  return portal;
}
