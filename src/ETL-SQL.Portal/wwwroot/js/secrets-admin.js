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

function esc(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s) {
  return esc(s).replace(/'/g, '&#39;');
}
function formatDate(value) {
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

export function createSecretsAdmin({ host, secretsApi }) {
  host.innerHTML = PANEL_HTML;
  const $ = (id) => host.querySelector(`#${id}`);
  const statusLine = $('sec-status');
  const errorBox = $('sec-error');

  function setStatus(text) { statusLine.textContent = text || ''; }
  function setError(text) {
    errorBox.textContent = text || '';
    errorBox.classList.toggle('show', !!text);
  }

  function statusChip(secret) {
    return secret.disabled
      ? '<span class="chip chip-inactive">Disabled</span>'
      : '<span class="chip chip-active">Active</span>';
  }

  async function load() {
    setError('');
    try {
      const secrets = await secretsApi.list();
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
      $('sec-tableWrap').innerHTML = `<div class="error-msg show">Could not load secrets: ${esc(err.message)}</div>`;
    }
  }

  host.addEventListener('click', async (e) => {
    const btn = e.target.closest('button[data-act]');
    if (!btn) return;
    const row = btn.closest('tr');
    const name = row?.dataset.name;
    if (!name) return;
    setError('');
    try {
      if (btn.dataset.act === 'verify') {
        try {
          await secretsApi.verify(name);
          row.querySelector('.sec-row-status').innerHTML = '<span class="badge badge-ok">Verified</span>';
        } catch (err) {
          const status = err.body?.status || (err.status === 404 ? 'missing' : 'error');
          row.querySelector('.sec-row-status').innerHTML = `<span class="badge badge-error">${esc(status)}</span>`;
        }
      } else if (btn.dataset.act === 'impact') {
        const impact = await secretsApi.impact(name);
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
        if (!await window.ETLSQLFeedback.confirm(`Disable secret '${name}'?`, { title: 'Disable secret', impact: `SECRET:${name} will fail until it is re-enabled.`, confirmLabel: 'Disable secret', danger: true, auditAction: 'admin.secret.disable' })) return;
        await secretsApi.disable(name);
        await load();
      } else if (btn.dataset.act === 'enable') {
        await secretsApi.enable(name);
        await load();
      } else if (btn.dataset.act === 'delete') {
        if (!await window.ETLSQLFeedback.confirm(`Permanently delete secret '${name}'?`, { title: 'Delete secret', confirmLabel: 'Delete secret', danger: true, auditAction: 'admin.secret.delete' })) return;
        await secretsApi.remove(name);
        await load();
      }
    } catch (err) {
      setError(err.message);
    }
  });

  $('sec-refreshBtn').addEventListener('click', load);

  $('sec-verifyAllBtn').addEventListener('click', async () => {
    setStatus('Verifying…');
    try {
      const result = await secretsApi.verifyAll();
      setStatus(result.failedCount === 0
        ? `All ${result.secretCount} secret(s) decryptable.`
        : `${result.failedCount} of ${result.secretCount} secret(s) NOT decryptable (first: ${result.firstFailedName}).`);
    } catch (err) {
      setStatus(`Verify-all failed: ${err.message}`);
    }
  });

  $('sec-saveBtn').addEventListener('click', async () => {
    const name = $('sec-name').value.trim();
    const value = $('sec-value').value;
    setError('');
    if (!name || !value) { setError('Both a name and a value are required.'); return; }
    try {
      await secretsApi.set(name, value);
      $('sec-name').value = '';
      $('sec-value').value = '';
      setStatus(`Secret '${name}' saved.`);
      await load();
    } catch (err) {
      setError(err.message);
    }
  });

  return { load };
}
