import { auth } from './portal-http.js';
import { adminAuditApi } from './admin-audit-api.js';
import { renderCatalogPager } from './admin-catalog-ui.js';
import { asError } from './http-error.js';
import { getSessionIdentity } from './session-identity.js';
import { failedState, loadingState } from './portal-states.js';

function element<T extends HTMLElement>(doc: Document, id: string, kind: new (...args: never[]) => T): T {
  const value = doc.getElementById(id);
  if (!(value instanceof kind)) throw new Error(`Missing admin audit element: ${id}`);
  return value;
}
const escape = (value: string) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
  .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');

/** Own audit page/filter snapshots, request lifetime, and validated CSV downloads. */
export function createAdminAudit(doc: Document = document) {
  const feedback = window.ETLSQLFeedback;
  if (!feedback) throw new Error('Portal feedback must be initialized before audit administration.');
  const notify = feedback.notify.bind(feedback);
  const wrap = element(doc, 'auditTableWrap', HTMLDivElement), pager = element(doc, 'auditPager', HTMLDivElement);
  const filter = element(doc, 'auditFilter', HTMLInputElement), refresh = element(doc, 'auditRefreshBtn', HTMLButtonElement);
  const exportButton = element(doc, 'auditExportBtn', HTMLButtonElement), status = element(doc, 'auditExportStatus', HTMLDivElement);
  const sessionKey = () => {
    try {
      const identity = getSessionIdentity(auth.getToken());
      return JSON.stringify([identity.subject, identity.roles, identity.claims.tenant_id, identity.claims.service_account_id]);
    } catch { return null; }
  };
  const identity = sessionKey(), listeners = new AbortController();
  const downloadUrls = new Map<string, ReturnType<typeof setTimeout>>();
  let disposed = false, page = 1, readSequence = 0, exportSequence = 0, readyAction: string | null = null;
  let readRequest: AbortController | null = null, exportRequest: AbortController | null = null;
  const alive = () => !disposed && identity !== null && sessionKey() === identity;
  const listen = (target: EventTarget, event: string, handler: EventListener) =>
    target.addEventListener(event, handler, { signal: listeners.signal });
  function controls() { exportButton.disabled = !alive() || readyAction !== filter.value.trim() || exportRequest !== null; }
  function clearExport() {
    ++exportSequence; exportRequest?.abort(); exportRequest = null;
    status.textContent = ''; status.hidden = true; controls();
  }
  function releaseDownload(url: string) {
    const timer = downloadUrls.get(url);
    if (timer !== undefined) clearTimeout(timer);
    downloadUrls.delete(url); URL.revokeObjectURL(url);
  }

  async function reload() {
    if (!alive()) return false;
    readRequest?.abort();
    const request = new AbortController(), sequence = ++readSequence, action = filter.value.trim();
    readRequest = request; readyAction = null; controls();
    wrap.dataset.auditState = 'loading'; wrap.setAttribute('aria-busy', 'true');
    wrap.innerHTML = loadingState('Loading audit log…'); pager.replaceChildren();
    const current = () => alive() && sequence === readSequence && filter.value.trim() === action;
    try {
      const result = await adminAuditApi.catalog(page, action, request.signal);
      if (!current()) return false;
      page = result.page; readyAction = action;
      wrap.innerHTML = `<table class="data-table"><thead><tr><th>Time</th><th>User</th><th>Action</th><th>Resource</th><th>Detail</th></tr></thead>
        <tbody>${result.items.map(entry => `<tr data-audit-id="${entry.id}">
          <td class="text-sm">${escape(new Date(entry.timestamp).toLocaleString())}</td>
          <td>${escape(entry.username ?? (entry.userId == null ? 'No account' : 'Unavailable account'))}</td>
          <td><code>${escape(entry.action)}</code></td>
          <td class="text-sm text-muted">${escape(entry.resourceType ?? '')} ${escape(entry.resourceId ?? '')}</td>
          <td class="text-sm text-muted">${escape(entry.detail ?? '')}</td></tr>`).join('')
          || '<tr><td colspan="5" class="empty-state">No entries match this action filter.</td></tr>'}</tbody></table>`;
      renderCatalogPager(pager, result, nextPage => {
        const focused = doc.activeElement instanceof HTMLButtonElement ? doc.activeElement.textContent : null;
        page = nextPage;
        void reload().then(loaded => {
          if (!loaded || !alive() || !focused) return;
          const button = [...pager.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === focused && !item.disabled);
          if (button) button.focus(); else pager.focus();
        });
      });
      wrap.dataset.auditState = 'ready'; return true;
    } catch (error) {
      if (!current()) return false;
      readyAction = null; wrap.dataset.auditState = 'failed';
      wrap.innerHTML = failedState({ title: 'Audit entries could not be loaded.', body: asError(error).message });
      pager.replaceChildren(); return false;
    } finally {
      if (sequence === readSequence) {
        readRequest = null; wrap.setAttribute('aria-busy', 'false');
        if (!alive()) {
          readyAction = null; wrap.dataset.auditState = 'failed'; pager.replaceChildren();
          wrap.innerHTML = failedState({ title: 'The Portal session changed.', body: 'Reload this page to view audit entries for the current session.' });
        }
        controls();
      }
    }
  }

  async function exportCsv() {
    if (!alive() || exportRequest || readyAction !== filter.value.trim()) return;
    const action = filter.value.trim(), sequence = ++exportSequence, request = new AbortController();
    exportRequest = request; controls(); status.hidden = false;
    status.textContent = 'Preparing audit CSV…'; status.setAttribute('role', 'status');
    const current = () => alive() && sequence === exportSequence && filter.value.trim() === action;
    try {
      const blob = await adminAuditApi.exportCsv(action, request.signal);
      if (!current()) return;
      const url = URL.createObjectURL(blob), anchor = doc.createElement('a');
      // Keep the object URL through the click; release it on the next turn or on disposal.
      downloadUrls.set(url, setTimeout(() => releaseDownload(url), 0));
      try {
        anchor.href = url; anchor.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`;
        anchor.hidden = true; doc.body.append(anchor); anchor.click();
      } finally { anchor.remove(); }
      status.textContent = 'Audit CSV download started.';
    } catch (error) {
      if (!current()) return;
      status.textContent = asError(error).message; status.setAttribute('role', 'alert');
      notify(`Audit export failed: ${asError(error).message}`, { tone: 'error' });
    } finally {
      if (sequence === exportSequence) {
        exportRequest = null;
        if (!alive()) { status.textContent = ''; status.hidden = true; }
        controls();
      }
    }
  }

  listen(filter, 'input', () => {
    ++readSequence; readRequest?.abort(); readRequest = null; readyAction = null; page = 1; clearExport();
    wrap.dataset.auditState = 'pending'; wrap.setAttribute('aria-busy', 'false');
    wrap.innerHTML = '<div class="empty-state" role="status">Press Enter or Refresh to apply the action filter.</div>';
    pager.replaceChildren();
  });
  listen(filter, 'keydown', event => {
    if (event instanceof KeyboardEvent && event.key === 'Enter') { event.preventDefault(); page = 1; void reload(); }
  });
  listen(refresh, 'click', () => { page = 1; void reload(); });
  listen(exportButton, 'click', () => { void exportCsv(); });
  controls();
  return { reload, dispose() {
    if (disposed) return;
    disposed = true; ++readSequence; listeners.abort(); readRequest?.abort(); readRequest = null;
    clearExport(); for (const url of downloadUrls.keys()) releaseDownload(url);
    readyAction = null; wrap.replaceChildren(); pager.replaceChildren(); wrap.setAttribute('aria-busy', 'false'); controls();
  } };
}
