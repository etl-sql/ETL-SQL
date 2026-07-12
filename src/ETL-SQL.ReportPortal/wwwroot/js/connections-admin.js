// Canonical "Shared Connections" admin surface (Admin → Connections) over api/admin/connections.
//
// Extracted so it can be previewed in the UI sandbox without the portal. The module owns its
// markup (renders into `host`) and takes its API client as an injected dependency, so the
// sandbox story can drive it with a fixture-backed fake. Credential fields hold SECRET:name
// references, never values — the server rejects raw credentials on save and masks any
// non-reference credential value in detail responses; this module renders exactly what the
// server returns and always through esc().
//
// Usage (portal):
//   const connections = createConnectionsAdmin({ host: document.getElementById('panel-connections'), connectionsApi });
//   connections.load();
//
// Injected api contract:
//   list()            -> [{ alias, connectorType, disabled, environmentScope, createdAtUtc,
//                           updatedAtUtc, lastUsedAtUtc, lastVerifiedAtUtc, version }]
//   detail(alias)     -> { summary: {...as list row}, target, options: {KEY: value}, sensitiveFields: [] }
//   set(alias, entry) -> {}   entry = { connectorType, target, options, environmentScope, sensitiveFields }
//   verify(alias)     -> { alias, status: 'ok', secretReferences } (throws .status 404/409 otherwise)
//   disable(alias)    -> {}
//   remove(alias)     -> {}
//   exportAll()       -> [entry...]
//   importAll(list)   -> { created, updated }

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
        <span class="section-kicker">Connection catalog — scripts use SHARED:alias</span>
        <h3>Shared Connections</h3>
      </div>
      <div class="admin-action-group">
        <span id="conn-status" class="form-hint"></span>
        <button class="btn btn-outline btn-sm" id="conn-exportBtn">Export</button>
        <button class="btn btn-outline btn-sm" id="conn-importBtn">Import</button>
        <button class="btn btn-outline btn-sm" id="conn-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="conn-tableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading connections…</span></div></div>
  </div>

  <div class="card" id="conn-detailCard" style="display:none">
    <div class="card-header">
      <h3>Detail — <span id="conn-detailAlias"></span></h3>
      <button class="btn btn-outline btn-sm" id="conn-detailCloseBtn">Close</button>
    </div>
    <div id="conn-detailBody"></div>
  </div>

  <div class="card" id="conn-importCard" style="display:none">
    <div class="card-header"><h3>Import entries</h3></div>
    <div class="form-group">
      <label for="conn-importJson">Exported JSON (metadata only — options hold SECRET:name references, never values)</label>
      <textarea id="conn-importJson" rows="8" spellcheck="false"></textarea>
    </div>
    <div id="conn-importError" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="conn-importRunBtn">Import</button>
      <button class="btn btn-outline btn-sm" id="conn-importCancelBtn">Cancel</button>
    </div>
  </div>

  <div class="card">
    <div class="card-header"><h3>Create / update a shared connection</h3></div>
    <div class="form-row">
      <div class="form-group">
        <label for="conn-alias">Alias</label>
        <input id="conn-alias" type="text" placeholder="sales_dw" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="conn-type">Connector type</label>
        <select id="conn-type">
          <option value="MSSQL">MSSQL</option>
          <option value="POSTGRES">POSTGRES</option>
          <option value="ORACLE">ORACLE</option>
          <option value="MYSQL">MYSQL</option>
          <option value="SQLITE">SQLITE</option>
          <option value="SNOWFLAKE">SNOWFLAKE</option>
          <option value="BIGQUERY">BIGQUERY</option>
          <option value="FLATFILE">FLATFILE (CSV)</option>
          <option value="EXCEL">EXCEL</option>
          <option value="JSON">JSON</option>
          <option value="XML">XML</option>
          <option value="PARQUET">PARQUET</option>
          <option value="AVRO">AVRO</option>
          <option value="API">API (REST)</option>
          <option value="SFTP">SFTP</option>
          <option value="FTP">FTP</option>
          <option value="AZURE_BLOB">AZURE_BLOB</option>
          <option value="S3">S3</option>
          <option value="SHAREPOINT">SHAREPOINT</option>
          <option value="ACTIVE_DIRECTORY">ACTIVE_DIRECTORY</option>
          <option value="SMTP">SMTP (Email)</option>
          <option value="DIRECTORY">DIRECTORY</option>
          <option value="REPORTPORTAL">REPORTPORTAL</option>
          <option value="ORCHESTRATOR">ORCHESTRATOR</option>
          <option value="MONGODB">MONGODB</option>
          <option value="KAFKA">KAFKA</option>
          <option value="NEO4J">NEO4J</option>
          <option value="ODBC">ODBC</option>
          <option value="MOCKDB">MOCKDB</option>
        </select>
      </div>
      <div class="form-group">
        <label for="conn-scope">Environment scope (optional)</label>
        <input id="conn-scope" type="text" placeholder="Prod" autocomplete="off">
      </div>
    </div>
    <div class="form-group">
      <label for="conn-target">Target connection string (optional; credentials as SECRET:name)</label>
      <input id="conn-target" type="text" placeholder="Server=sql01;Database=Sales;Password=SECRET:sales_db_password" autocomplete="off">
    </div>
    <div class="form-group">
      <label for="conn-options">Options — one KEY=VALUE per line (credentials as SECRET:name)</label>
      <textarea id="conn-options" rows="5" spellcheck="false" placeholder="SERVER=sql01&#10;DATABASE=Sales&#10;PASSWORD=SECRET:sales_db_password"></textarea>
    </div>
    <div class="form-group">
      <label for="conn-sensitive">Sensitive metadata fields — one field per line or comma-separated</label>
      <textarea id="conn-sensitive" rows="3" spellcheck="false" placeholder="HOST&#10;BUCKET&#10;PATH"></textarea>
      <span class="form-hint">These fields are masked in catalog displays and may use SECRET:name references for this shared connection.</span>
    </div>
    <div id="conn-error" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="conn-saveBtn">Save</button>
      <span class="form-hint">Raw credential values are rejected — store them with the secret manager and reference SECRET:name.</span>
    </div>
  </div>`;

export function createConnectionsAdmin({ host, connectionsApi }) {
  host.innerHTML = PANEL_HTML;
  const $ = (id) => host.querySelector(`#${id}`);

  function setStatus(text) { $('conn-status').textContent = text || ''; }
  function setError(id, text) {
    const box = $(id);
    box.textContent = text || '';
    box.classList.toggle('show', !!text);
  }

  function statusChip(c) {
    return c.disabled
      ? '<span class="chip chip-inactive">Disabled</span>'
      : '<span class="chip chip-active">Active</span>';
  }

  function parseSensitiveFields(value) {
    return [...new Set(String(value || '')
      .split(/[\n,]/)
      .map((v) => v.trim())
      .filter(Boolean)
      .map((v) => v.toUpperCase()))];
  }

  async function load() {
    setError('conn-error', '');
    try {
      const connections = await connectionsApi.list();
      if (!connections.length) {
        $('conn-tableWrap').innerHTML = '<div class="empty-state">No shared connections cataloged yet.</div>';
        return;
      }

      $('conn-tableWrap').innerHTML = `
        <table class="data-table">
          <thead><tr><th>Alias</th><th>Type</th><th>Scope</th><th>Status</th><th>Last used</th><th>Last verified</th><th></th></tr></thead>
          <tbody>
            ${connections.map((c) => `
              <tr data-alias="${escAttr(c.alias)}">
                <td><code>${esc(c.alias)}</code></td>
                <td>${esc(c.connectorType)}</td>
                <td>${esc(c.environmentScope || '—')}</td>
                <td class="conn-row-status">${statusChip(c)}</td>
                <td>${esc(formatDate(c.lastUsedAtUtc))}</td>
                <td>${esc(formatDate(c.lastVerifiedAtUtc))}</td>
                <td class="table-actions">
                  <button class="btn btn-outline btn-sm" data-act="detail">Detail</button>
                  <button class="btn btn-outline btn-sm" data-act="impact">Impact</button>
                  <button class="btn btn-outline btn-sm" data-act="verify">Verify</button>
                  ${c.disabled
                    ? '<button class="btn btn-outline btn-sm" data-act="enable">Enable</button>'
                    : '<button class="btn btn-outline btn-sm" data-act="disable">Disable</button>'}
                  <button class="btn btn-danger-soft btn-sm" data-act="delete">Delete</button>
                </td>
              </tr>`).join('')}
          </tbody>
        </table>`;
    } catch (err) {
      $('conn-tableWrap').innerHTML = `<div class="error-msg show">Could not load connections: ${esc(err.message)}</div>`;
    }
  }

  async function showDetail(alias) {
    const detail = await connectionsApi.detail(alias);
    $('conn-detailAlias').textContent = alias;
    const options = Object.entries(detail.options || {});
    const sensitiveFields = detail.sensitiveFields || [];
    $('conn-detailBody').innerHTML = `
      <div class="form-hint">${esc(detail.summary.connectorType)}${detail.summary.environmentScope ? ` · ${esc(detail.summary.environmentScope)}` : ''}
        · created ${esc(formatDate(detail.summary.createdAtUtc))} · updated ${esc(formatDate(detail.summary.updatedAtUtc))}</div>
      ${detail.target ? `<p><strong>Target:</strong> <code>${esc(detail.target)}</code></p>` : ''}
      ${sensitiveFields.length ? `<p><strong>Sensitive fields:</strong> ${sensitiveFields.map((f) => `<code>${esc(f)}</code>`).join(' ')}</p>` : ''}
      ${options.length ? `
        <table class="data-table">
          <thead><tr><th>Option</th><th>Value</th></tr></thead>
          <tbody>${options.map(([k, v]) => `<tr><td>${esc(k)}</td><td><code>${esc(v)}</code></td></tr>`).join('')}</tbody>
        </table>` : '<div class="empty-state">No options.</div>'}`;
    $('conn-detailCard').style.display = '';

    // Pre-fill the edit form for quick updates.
    $('conn-alias').value = alias;
    $('conn-type').value = detail.summary.connectorType;
    $('conn-scope').value = detail.summary.environmentScope || '';
    $('conn-target').value = detail.target || '';
    $('conn-options').value = options.map(([k, v]) => `${k}=${v}`).join('\n');
    $('conn-sensitive').value = sensitiveFields.join('\n');
  }

  async function showImpact(alias) {
    const impact = await connectionsApi.impact(alias);
    $('conn-detailAlias').textContent = `${alias} — impact`;
    $('conn-detailBody').innerHTML = renderImpact(impact);
    $('conn-detailCard').style.display = '';
  }

  function renderImpact(impact) {
    if (!impact.consumers.length) {
      return '<div class="empty-state">No known consumers reference this entry.</div>';
    }
    return `
      <div class="form-hint">${esc(impact.consumerCount)} consumer(s) reference <code>${esc(impact.reference)}</code>. Review before disabling or deleting.</div>
      <table class="data-table">
        <thead><tr><th>Type</th><th>Name</th><th>Detail</th><th>Last used</th><th>Uses</th></tr></thead>
        <tbody>${impact.consumers.map((c) => `
          <tr>
            <td>${esc(c.type)}</td>
            <td>${esc(c.name)}</td>
            <td>${esc(c.detail || '—')}</td>
            <td>${esc(formatDate(c.lastUsedAtUtc))}</td>
            <td>${esc(c.useCount ?? '—')}</td>
          </tr>`).join('')}</tbody>
      </table>`;
  }

  host.addEventListener('click', async (e) => {
    const btn = e.target.closest('button[data-act]');
    if (!btn) return;
    const row = btn.closest('tr');
    const alias = row?.dataset.alias;
    if (!alias) return;
    setError('conn-error', '');
    try {
      if (btn.dataset.act === 'detail') {
        await showDetail(alias);
      } else if (btn.dataset.act === 'impact') {
        await showImpact(alias);
      } else if (btn.dataset.act === 'verify') {
        try {
          const result = await connectionsApi.verify(alias);
          row.querySelector('.conn-row-status').innerHTML =
            `<span class="badge badge-ok">Verified (${esc(result.secretReferences)} secret ref(s))</span>`;
        } catch (err) {
          const status = err.body?.status || (err.status === 404 ? 'missing' : 'error');
          row.querySelector('.conn-row-status').innerHTML = `<span class="badge badge-error">${esc(status)}</span>`;
        }
      } else if (btn.dataset.act === 'disable') {
        if (!window.confirm(`Disable shared connection '${alias}'? SHARED:${alias} will fail until it is re-enabled.`)) return;
        await connectionsApi.disable(alias);
        await load();
      } else if (btn.dataset.act === 'enable') {
        await connectionsApi.enable(alias);
        await load();
      } else if (btn.dataset.act === 'delete') {
        if (!window.confirm(`Permanently delete shared connection '${alias}'?`)) return;
        await connectionsApi.remove(alias);
        await load();
      }
    } catch (err) {
      setError('conn-error', err.message);
    }
  });

  $('conn-detailCloseBtn').addEventListener('click', () => { $('conn-detailCard').style.display = 'none'; });
  $('conn-refreshBtn').addEventListener('click', load);

  $('conn-saveBtn').addEventListener('click', async () => {
    setError('conn-error', '');
    const alias = $('conn-alias').value.trim();
    const connectorType = $('conn-type').value.trim();
    if (!alias || !connectorType) { setError('conn-error', 'Both an alias and a connector type are required.'); return; }

    const options = {};
    for (const line of $('conn-options').value.split('\n')) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      const idx = trimmed.indexOf('=');
      if (idx <= 0) { setError('conn-error', `Option '${trimmed}' is not in KEY=VALUE form.`); return; }
      options[trimmed.slice(0, idx).trim()] = trimmed.slice(idx + 1);
    }

    try {
      await connectionsApi.set(alias, {
        connectorType,
        target: $('conn-target').value.trim() || null,
        options,
        environmentScope: $('conn-scope').value.trim() || null,
        sensitiveFields: parseSensitiveFields($('conn-sensitive').value),
      });
      setStatus(`Shared connection '${alias}' saved.`);
      await load();
    } catch (err) {
      setError('conn-error', err.message);
    }
  });

  $('conn-exportBtn').addEventListener('click', async () => {
    try {
      const entries = await connectionsApi.exportAll();
      const blob = new Blob([JSON.stringify(entries, null, 2)], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = 'shared-connections.json';
      a.click();
      URL.revokeObjectURL(a.href);
      setStatus(`Exported ${entries.length} entr${entries.length === 1 ? 'y' : 'ies'} (metadata only).`);
    } catch (err) {
      setStatus(`Export failed: ${err.message}`);
    }
  });

  $('conn-importBtn').addEventListener('click', () => { $('conn-importCard').style.display = ''; });
  $('conn-importCancelBtn').addEventListener('click', () => {
    $('conn-importCard').style.display = 'none';
    setError('conn-importError', '');
  });
  $('conn-importRunBtn').addEventListener('click', async () => {
    setError('conn-importError', '');
    let entries;
    try {
      entries = JSON.parse($('conn-importJson').value);
      if (!Array.isArray(entries)) throw new Error('Expected a JSON array of entries.');
    } catch (err) {
      setError('conn-importError', `Invalid JSON: ${err.message}`);
      return;
    }
    try {
      const result = await connectionsApi.importAll(entries);
      setStatus(`Imported: ${result.created} created, ${result.updated} updated.`);
      $('conn-importCard').style.display = 'none';
      $('conn-importJson').value = '';
      await load();
    } catch (err) {
      setError('conn-importError', err.message);
    }
  });

  return { load };
}
