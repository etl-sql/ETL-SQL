import { renderTriageBoard } from './triage-ui.js';
import type { TriageEvidenceState } from './triage-ui.js';
import { readTriageBoard, readTriageRunDetail, readTriageRerunResponse } from './triage-responses.js';

export interface TriageApi {
  board(lookbackHours: number, signal: AbortSignal): Promise<unknown>;
  detail(runId: number, signal: AbortSignal): Promise<unknown>;
  rerun(jobNames: string[], signal: AbortSignal): Promise<unknown>;
}
interface TriageControllerOptions {
  root: HTMLElement;
  api: TriageApi;
  lookback?: HTMLSelectElement;
  refreshButton?: HTMLElement;
  lookbackHours?: number;
  pollMs?: number;
  runOne(jobName: string): void;
  confirmRerun(jobNames: string[]): Promise<boolean>;
  notify(message: string, failed: boolean): void;
  onTriggered?(): void | Promise<void>;
}
interface TriageController {
  reload(): Promise<void>;
  dispose(): void;
}
const controllers = new WeakMap<HTMLElement, TriageController>();
export function createTriageController(options: TriageControllerOptions): TriageController {
  const { root, api } = options;
  const existing = controllers.get(root);
  if (existing) return existing;
  let board: TriageBoardDto | null = null;
  const expanded = new Set<string>(), selected = new Set<string>(), openRuns = new Set<number>();
  const details = new Map<number, TriageEvidenceState>();
  const detailRequests = new Map<number, AbortController>();
  let boardRequest: AbortController | undefined, mutationRequest: AbortController | undefined;
  let generation = 0, scope = 0, disposed = false, pending = false, unconfirmed = false, mutationSent = false;
  let lookback = options.lookbackHours ?? 24;
  const selectedIndexes = (signatures: Set<string>) => new Set(board?.incidents
    .flatMap((incident, index) => signatures.has(incident.signature) ? [index] : []) ?? []);

  function draw() {
    if (disposed) return;
    const active = document.activeElement;
    const focus = active instanceof HTMLElement && root.contains(active) ? active : null;
    root.innerHTML = renderTriageBoard(board, { expanded: selectedIndexes(expanded), selected: selectedIndexes(selected),
      openRuns, details, pending: pending || unconfirmed });
    if (unconfirmed) {
      const status = document.createElement('p');
      status.className = 'triage-evidence-state danger'; status.setAttribute('role', 'status');
      status.textContent = 'Re-run outcome is unconfirmed. Use Refresh and check job history before retrying.';
      root.prepend(status);
    }
    if (!focus) return;
    // Stable attributes preserve focus when a poll changes incident order or replaces the markup.
    const target = [...root.querySelectorAll<HTMLElement>('button,input')].find(element =>
      element.className === focus.className && ['signature', 'run', 'job'].every(attribute =>
        element.dataset[attribute] === focus.dataset[attribute]));
    target?.focus({ preventScroll: true });
  }
  function clearDetailRequests() {
    for (const request of detailRequests.values()) request.abort();
    detailRequests.clear();
  }
  function unavailable() {
    board = null;
    selected.clear(); expanded.clear(); openRuns.clear(); details.clear();
    root.innerHTML = '<div class="orch-offline-banner" role="status"><strong>Triage is unavailable.</strong> '
      + 'The service did not return a complete board. <button class="btn btn-sm btn-outline triage-retry" type="button">Retry</button></div>';
  }
  async function loadDetail(run: TriageRunDto) {
    detailRequests.get(run.id)?.abort();
    const request = new AbortController(), currentGeneration = generation;
    detailRequests.set(run.id, request);
    details.set(run.id, { status: 'loading' });
    draw();
    const current = () => !disposed && !request.signal.aborted && currentGeneration === generation
      && detailRequests.get(run.id) === request && openRuns.has(run.id);
    try {
      const detail = readTriageRunDetail(await api.detail(run.id, request.signal), run);
      if (current()) { details.set(run.id, detail); draw(); }
    } catch {
      if (current()) {
        details.set(run.id, { status: 'error', message: 'The service did not return complete evidence for this run. Close and reopen to retry.' });
        draw();
      }
    } finally { if (detailRequests.get(run.id) === request) detailRequests.delete(run.id); }
  }
  async function reload() {
    if (disposed) return;
    const currentGeneration = ++generation;
    boardRequest?.abort(); clearDetailRequests();
    const request = new AbortController(); boardRequest = request;
    // Open panels no longer present retained evidence as current while a new observation is pending.
    for (const id of openRuns) details.set(id, { status: 'loading' });
    draw();
    try {
      const next = readTriageBoard(await api.board(lookback, request.signal), lookback);
      if (disposed || request.signal.aborted || currentGeneration !== generation) return;
      // Capture focus against the old board before replacing it; signature attributes remain stable.
      board = next;
      const signatures = new Set(next.incidents.map(incident => incident.signature));
      for (const signature of [...expanded]) if (!signatures.has(signature)) expanded.delete(signature);
      for (const signature of [...selected]) if (!signatures.has(signature)) selected.delete(signature);
      const runs = new Map(next.incidents.flatMap(incident => incident.runs).map(run => [run.id, run]));
      for (const id of [...openRuns]) if (!runs.has(id)) openRuns.delete(id);
      details.clear(); draw();
      await Promise.all([...openRuns].map(id => { const run = runs.get(id); return run ? loadDetail(run) : undefined; }));
    } catch {
      if (!disposed && !request.signal.aborted && currentGeneration === generation) unavailable();
    }
  }
  function selectedNames(): string[] {
    const names = new Map<string, string>();
    for (const incident of board?.incidents ?? []) {
      if (!selected.has(incident.signature)) continue;
      for (const jobName of incident.jobNames) if (!names.has(jobName.toUpperCase())) names.set(jobName.toUpperCase(), jobName);
    }
    return [...names.values()];
  }
  async function rerun(names: string[]) {
    if (disposed || pending || unconfirmed || !board || !names.length) return;
    if (names.length === 1) { options.runOne(names[0]); return; }
    pending = true; draw();
    const currentScope = scope;
    const submitted = new Set(selected);
    const request = new AbortController(); mutationRequest = request; mutationSent = false;
    const current = () => !disposed && !request.signal.aborted && currentScope === scope;
    try {
      if (!await options.confirmRerun([...names]) || !current()) return;
      mutationSent = true;
      const response = readTriageRerunResponse(await api.rerun(names, request.signal), names);
      if (!current()) return;
      const failed = response.results.filter(result => !result.triggered);
      const triggered = new Set(response.results.filter(result => result.triggered).map(result => result.jobName.toUpperCase()));
      for (const incident of board?.incidents ?? []) {
        if (submitted.has(incident.signature) && incident.jobNames.every(job => triggered.has(job.toUpperCase()))) {
          selected.delete(incident.signature);
        }
      }
      options.notify(`${response.triggered} of ${response.requested} jobs triggered.`
        + (failed.length ? ` Not triggered: ${failed.map(result => result.jobName).join(', ')}.` : ''), failed.length > 0);
      await reload();
      if (current() && response.triggered > 0) await options.onTriggered?.();
    } catch {
      if (current()) {
        unconfirmed = true;
        options.notify('Re-run outcome is unconfirmed. Refresh and check job history before deciding whether to retry.', true);
      }
    } finally {
      if (mutationRequest === request) { mutationRequest = undefined; pending = false; mutationSent = false; if (!disposed) draw(); }
    }
  }
  const click = (event: Event) => {
    const element = event.target instanceof Element ? event.target.closest<HTMLElement>('button') : null;
    if (!element || !root.contains(element)) return;
    if (element.classList.contains('triage-retry')) { refresh(); return; }
    if (!board || element instanceof HTMLButtonElement && element.disabled) return;
    if (element.classList.contains('triage-incident-toggle')) {
      const incident = board.incidents[Number(element.dataset.incident)];
      if (!incident) return;
      expanded.has(incident.signature) ? expanded.delete(incident.signature) : expanded.add(incident.signature);
      draw();
    } else if (element.classList.contains('triage-run-evidence')) {
      const run = board.incidents.flatMap(incident => incident.runs).find(run => run.id === Number(element.dataset.run));
      if (!run) return;
      if (openRuns.has(run.id)) {
        openRuns.delete(run.id); detailRequests.get(run.id)?.abort(); detailRequests.delete(run.id); details.delete(run.id); draw();
      } else { openRuns.add(run.id); void loadDetail(run); }
    } else if (element.classList.contains('triage-rerun-selected')) { void rerun(selectedNames()); }
    else if (element.classList.contains('triage-rerun-one')) {
      const job = [...board.incidents.flatMap(incident => incident.jobNames), ...board.missed.map(job => job.jobName)]
        .find(name => name === element.dataset.job);
      if (job) void rerun([job]);
    }
  };
  const change = (event: Event) => {
    if (!(event.target instanceof HTMLInputElement) || !event.target.classList.contains('triage-incident-check')) return;
    const incident = board?.incidents[Number(event.target.dataset.incident)];
    if (!incident) return;
    event.target.checked ? selected.add(incident.signature) : selected.delete(incident.signature);
    draw();
  };
  const refresh = () => {
    void reload().then(() => { if (!disposed && board) { unconfirmed = false; draw(); } });
  };
  const changeLookback = () => {
    const hours = Number(options.lookback?.value);
    if (!Number.isSafeInteger(hours) || hours < 1 || hours > 720) return;
    scope++;
    if (mutationRequest) {
      // Cancelling the browser request cannot roll back a trigger already received by the server.
      unconfirmed ||= mutationSent;
      mutationRequest.abort(); mutationRequest = undefined; pending = false; mutationSent = false;
    }
    lookback = hours;
    selected.clear(); expanded.clear(); openRuns.clear(); details.clear(); board = null;
    void reload();
  };
  root.addEventListener('click', click); root.addEventListener('change', change);
  options.refreshButton?.addEventListener('click', refresh);
  options.lookback?.addEventListener('change', changeLookback);
  if (options.lookback) { const hours = Number(options.lookback.value); if (hours >= 1 && hours <= 720) lookback = hours; }
  const timer = options.pollMs && options.pollMs > 0 ? setInterval(() => { void reload(); }, options.pollMs) : undefined;
  const controller: TriageController = {
    reload,
    dispose() {
      if (disposed) return;
      disposed = true; generation++; scope++; boardRequest?.abort(); mutationRequest?.abort(); clearDetailRequests();
      clearInterval(timer); root.removeEventListener('click', click); root.removeEventListener('change', change);
      options.refreshButton?.removeEventListener('click', refresh); options.lookback?.removeEventListener('change', changeLookback);
      board = null; selected.clear(); expanded.clear(); openRuns.clear(); details.clear(); root.replaceChildren();
      controllers.delete(root);
    }
  };
  controllers.set(root, controller);
  return controller;
}
