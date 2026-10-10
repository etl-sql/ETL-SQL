import { asError } from './http-error.js';
import { readSecretSummaries, readSecretImpact, readSecretKeyRingCheck, secretVerificationError } from './secret-responses.js';
import type { SecretSummary } from './secret-responses.js';

// Canonical "Secrets" admin surface (Admin → Secrets) over api/admin/secrets.
//
// Extracted so it can be previewed in the UI sandbox without the portal. The module owns its
// markup (renders into `host`) and takes its API client as an injected dependency, so the
// sandbox story can drive it with a fixture-backed fake. Secret values are write-only: nothing
// this module renders ever contains a stored value.
//
// Usage (portal):
//   const secrets = createSecretsAdmin({ host: document.getElementById('panel-secrets'), secretsApi });
//   secrets.load();
//
// Injected api contract:
//   list()            -> [{ name, disabled, createdAtUtc, updatedAtUtc, version }]
//   set(name, value)  -> {}                       (PUT; creates, rotates, or re-enables)
//   verify(name)      -> { name, status: 'ok' }   (throws with .status 404/409 otherwise)
//   verifyAll()       -> { secretCount, failedCount, firstFailedName }
//   disable(name)     -> {}
//   remove(name)      -> {}

function esc(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s: unknown) {
  return esc(s).replace(/'/g, '&#39;');
}
function formatDate(value: string | null | undefined) {
  return value ? new Date(value).toLocaleString() : '—';
}

const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Portal secret store</span>
        <h3>Secrets</h3>
      </div>
      <div class="admin-action-group">
        <span id="sec-status" class="form-hint"></span>
        <button class="btn btn-outline btn-sm" id="sec-verifyAllBtn">Verify all</button>
        <button class="btn btn-outline btn-sm" id="sec-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="sec-tableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading secrets…</span></div></div>
    <div id="sec-impactWrap"></div>
  </div>

  <div class="card">
    <div class="card-header"><h3>Set / rotate a secret</h3></div>
    <div class="form-row">
      <div class="form-group">
        <label for="sec-name">Name</label>
        <input id="sec-name" type="text" placeholder="sales_db_password" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="sec-value">Value (never displayed after save)</label>
        <input id="sec-value" type="password" autocomplete="new-password">
      </div>
    </div>
    <div id="sec-error" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="sec-saveBtn">Save</button>
      <span class="form-hint">Scripts resolve it as SECRET:name. Saving an existing name rotates it; saving a disabled name re-enables it.</span>
    </div>
  </div>`;

interface SecretsApi {
  list(): Promise<unknown>;
  set(name: string, value: string): Promise<unknown>;
  verify(name: string): Promise<unknown>;
  verifyAll(): Promise<unknown>;
  disable(name: string): Promise<unknown>;
  enable(name: string): Promise<unknown>;
  impact(name: string): Promise<unknown>;
  remove(name: string): Promise<unknown>;
}

export function createSecretsAdmin({ host, secretsApi }: { host: HTMLElement; secretsApi: SecretsApi }) {
  host.innerHTML = PANEL_HTML;
  const $ = <T extends HTMLElement = HTMLElement>(id: string): T => {
    const element = host.querySelector<T>(`#${id}`);
    if (!element) throw new Error(`Missing secret-store element: ${id}`);
    return element;
  };
  const lifecycle = new AbortController();
  let loadSequence = 0;
  let disposed = false;
  const statusLine = $('sec-status');
  const errorBox = $('sec-error');

  function setStatus(text: string) { if (disposed) return; statusLine.textContent = text || ''; }
  function setError(text: string) {
    if (disposed) return;
    errorBox.textContent = text || '';
    errorBox.classList.toggle('show', !!text);
  }

  function statusChip(secret: SecretSummary) {
    return secret.disabled
      ? '<span class="chip chip-inactive">Disabled</span>'
      : '<span class="chip chip-active">Active</span>';
  }

  async function load() {
    if (disposed) return;
    const sequence = ++loadSequence;
    setError('');
    try {
      const secrets = readSecretSummaries(await secretsApi.list());
      if (disposed || sequence !== loadSequence) return;
      if (!secrets.length) {
        $('sec-tableWrap').innerHTML = '<div class="empty-state">No secrets stored yet.</div>';
        return;
      }

      $('sec-tableWrap').innerHTML = `
        <table class="data-table">
          <thead><tr><th>Name</th><th>Status</th><th>Created</th><th>Updated</th><th>Version</th><th></th></tr></thead>
          <tbody>
            ${secrets.map((s) => `
              <tr data-name="${escAttr(s.name)}">
                <td><code>${esc(s.name)}</code></td>
                <td class="sec-row-status">${statusChip(s)}</td>
                <td>${esc(formatDate(s.createdAtUtc))}</td>
                <td>${esc(formatDate(s.updatedAtUtc))}</td>
                <td>${esc(s.version)}</td>
                <td class="table-actions">
                  <button class="btn btn-outline btn-sm" data-act="impact">Impact</button>
                  <button class="btn btn-outline btn-sm" data-act="verify">Verify</button>
                  ${s.disabled
                    ? '<button class="btn btn-outline btn-sm" data-act="enable">Enable</button>'
                    : '<button class="btn btn-outline btn-sm" data-act="disable">Disable</button>'}
                  <button class="btn btn-danger-soft btn-sm" data-act="delete">Delete</button>
                </td>
              </tr>`).join('')}
          </tbody>
        </table>`;
    } catch (err) {
      if (disposed || sequence !== loadSequence) return;
      $('sec-tableWrap').innerHTML = `<div class="error-msg show">Could not load secrets: ${esc(asError(err).message)}</div>`;
    }
  }

  async function confirmAction(message: string, options: Parameters<EtlSqlFeedback['confirm']>[1]) {
    if (!window.ETLSQLFeedback) throw new Error('Confirmation is unavailable. Reload the page and try again.');
    return window.ETLSQLFeedback.confirm(message, options);
  }

  host.addEventListener('click', async (e) => {
    if (!(e.target instanceof Element)) return;
    const btn = e.target.closest<HTMLButtonElement>('button[data-act]');
    if (!btn) return;
    const row = btn.closest('tr');
    const name = row?.dataset.name;
    if (!row || !name) return;
    setError('');
    try {
      if (btn.dataset.act === 'verify') {
        try {
          await secretsApi.verify(name);
          if (disposed) return;
          row.querySelector<HTMLElement>('.sec-row-status')!.innerHTML = '<span class="badge badge-ok">Verified</span>';
        } catch (err) {
          if (disposed) return;
          const status = secretVerificationError(err);
          row.querySelector<HTMLElement>('.sec-row-status')!.innerHTML = `<span class="badge badge-error">${esc(status)}</span>`;
        }
      } else if (btn.dataset.act === 'impact') {
        const impact = readSecretImpact(await secretsApi.impact(name));
        if (disposed) return;
        $('sec-impactWrap').innerHTML = !impact.consumers.length
          ? `<div class="empty-state">No known consumers reference SECRET:${esc(name)}.</div>`
          : `
            <h4 class="section-kicker">Impact — ${esc(impact.consumerCount)} consumer(s) of <code>${esc(impact.reference)}</code></h4>
            <table class="data-table">
              <thead><tr><th>Type</th><th>Name</th><th>Detail</th><th>Last used</th></tr></thead>
              <tbody>${impact.consumers.map((c) => `
                <tr>
                  <td>${esc(c.type)}</td>
                  <td>${esc(c.name)}</td>
                  <td>${esc(c.detail || '—')}</td>
                  <td>${esc(formatDate(c.lastUsedAtUtc))}</td>
                </tr>`).join('')}</tbody>
            </table>`;
      } else if (btn.dataset.act === 'disable') {
        if (!await confirmAction(`Disable secret '${name}'?`, { title: 'Disable secret', impact: `SECRET:${name} will fail until it is re-enabled.`, confirmLabel: 'Disable secret', danger: true, auditAction: 'admin.secret.disable' })) return;
        await secretsApi.disable(name);
        await load();
      } else if (btn.dataset.act === 'enable') {
        await secretsApi.enable(name);
        await load();
      } else if (btn.dataset.act === 'delete') {
        if (!await confirmAction(`Permanently delete secret '${name}'?`, { title: 'Delete secret', confirmLabel: 'Delete secret', danger: true, auditAction: 'admin.secret.delete' })) return;
        await secretsApi.remove(name);
        await load();
      }
    } catch (err) {
      setError(asError(err).message);
    }
  }, { signal: lifecycle.signal });

  $('sec-refreshBtn').addEventListener('click', load, { signal: lifecycle.signal });

  $('sec-verifyAllBtn').addEventListener('click', async () => {
    setStatus('Verifying…');
    try {
      const result = readSecretKeyRingCheck(await secretsApi.verifyAll());
      setStatus(result.failedCount === 0
        ? `All ${result.secretCount} secret(s) decryptable.`
        : `${result.failedCount} of ${result.secretCount} secret(s) NOT decryptable (first: ${result.firstFailedName}).`);
    } catch (err) {
      setStatus(`Verify-all failed: ${asError(err).message}`);
    }
  }, { signal: lifecycle.signal });

  $('sec-saveBtn').addEventListener('click', async () => {
    const name = $<HTMLInputElement>('sec-name').value.trim();
    const value = $<HTMLInputElement>('sec-value').value;
    setError('');
    if (!name || !value) { setError('Both a name and a value are required.'); return; }
    try {
      await secretsApi.set(name, value);
      if (disposed) return;
      $<HTMLInputElement>('sec-name').value = '';
      $<HTMLInputElement>('sec-value').value = '';
      setStatus(`Secret '${name}' saved.`);
      await load();
    } catch (err) {
      setError(asError(err).message);
    }
  }, { signal: lifecycle.signal });

  return { load, dispose() { disposed = true; loadSequence++; lifecycle.abort(); } };
}
