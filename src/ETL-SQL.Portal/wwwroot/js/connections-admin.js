import { bindMarkdownActions, renderMarkdown } from './markdown-renderer.js';
import { deniedState, failedState, installPortalStateStyles } from './portal-states.js';

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
//   test(alias)       -> { alias, succeeded, steps: [{ layer, status, detail, remedy }] } (409 if disabled)
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
          <option value="PORTAL">PORTAL</option>
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
      <div style="display: flex; gap: 16px; align-items: stretch;">
        <textarea id="conn-options" rows="7" spellcheck="false" placeholder="SERVER=sql01&#10;DATABASE=Sales&#10;PASSWORD=SECRET:sales_db_password" style="flex: 1; min-width: 0;"></textarea>
        <div id="conn-help-box" style="flex: 1; min-width: 0; border: 1px solid var(--portal-border); border-radius: var(--portal-radius-sm); padding: 12px; font-size: 13px; max-height: 148px; overflow-y: auto; background: var(--portal-bg-hover); display: none;">
          <div id="conn-help-title" style="margin-top: 0; margin-bottom: 6px; font-size: 13px; font-weight: 700; color: var(--portal-text); display: flex; justify-content: space-between; align-items: center;">
            <span id="conn-help-title-text">Connection Options Help</span>
            <button class="btn btn-outline btn-sm" id="conn-help-expand-btn" style="padding: 2px 6px; font-size: 11px; cursor: pointer;">⛶ Expand</button>
          </div>
          <div id="conn-help-content" style="white-space: pre-wrap; font-family: inherit; line-height: 1.4;">Loading help...</div>
        </div>
      </div>
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
  </div>

  <!-- Help Modal Overlay -->
  <div id="conn-help-modal" class="modal-overlay" style="display:none" role="dialog" aria-modal="true" aria-labelledby="conn-help-modal-title">
    <div class="modal-card modal-lg" style="max-width: 800px; display: flex; flex-direction: column; max-height: 80vh;">
      <div class="modal-header" style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--portal-border); padding-bottom: 12px; margin-bottom: 16px;">
        <h3 id="conn-help-modal-title" class="modal-title" style="margin: 0; font-size: 18px; color: var(--portal-text);">Connection Help</h3>
        <button class="btn btn-outline btn-sm" id="conn-help-modal-close" style="padding: 4px 8px; font-size: 12px; cursor: pointer;">✕ Close</button>
      </div>
      <div class="modal-body" id="conn-help-modal-content" style="flex: 1; overflow-y: auto; padding: 0 8px; line-height: 1.5; font-size: 14px; color: var(--portal-text);">
        Loading...
      </div>
      <div class="modal-actions" style="margin-top: 16px; border-top: 1px solid var(--portal-border); padding-top: 12px; display: flex; justify-content: flex-end;">
        <button class="btn btn-outline btn-sm" id="conn-help-modal-close-btn" style="cursor: pointer;">Close</button>
      </div>
    </div>
  </div>`;

export function createConnectionsAdmin({ host, connectionsApi }) {
  host.innerHTML = PANEL_HTML;
  const $ = (id) => host.querySelector(`#${id}`);

  async function loadConnectorHelp(type) {
    const helpBox = $('conn-help-box');
    const helpContent = $('conn-help-content');
    const helpTitleText = $('conn-help-title-text');
    
    if (!type) {
      helpBox.style.display = 'none';
      return;
    }
    
    if (helpTitleText) {
      helpTitleText.innerHTML = `<strong>${esc(type)} Connection Options</strong>`;
    }
    helpContent.innerHTML = 'Loading help...';
    helpBox.style.display = 'block';
    
    try {
      const res = await connectionsApi.getHelp(type);
      if (res && res.content) {
        helpContent.innerHTML = renderMarkdown(res.content);
        bindMarkdownActions(helpContent);
      } else {
        helpContent.innerHTML = 'No documentation available for this connector.';
      }
    } catch (err) {
      helpContent.innerHTML = `<span style="color: var(--portal-danger);">Failed to load help: ${esc(err.message || err)}</span>`;
    }
  }

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
                  <button class="btn btn-outline btn-sm" data-act="test">Test</button>
                  ${c.disabled
                    ? '<button class="btn btn-outline btn-sm" data-act="enable">Enable</button>'
                    : '<button class="btn btn-outline btn-sm" data-act="disable">Disable</button>'}
                  <button class="btn btn-danger-soft btn-sm" data-act="delete">Delete</button>
                </td>
              </tr>`).join('')}
          </tbody>
        </table>`;
    } catch (err) {
      // A 403 and an unreachable service produced the same message here, which told the reader the
      // wrong thing half the time: "could not load" reads as a fault to report, when the answer may
      // simply be that this account may not see the catalog.
      installPortalStateStyles();
      $('conn-tableWrap').innerHTML = err?.status === 403 || err?.status === 401
        ? deniedState({
          title: 'You do not have access to the connection catalog.',
          roles: ['Admin'],
        })
        : failedState({
          title: 'The connection catalog is unavailable.',
          body: err?.message,
          retryId: 'conn-retry',
        });
      document.getElementById('conn-retry')?.addEventListener('click', () => load());
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
    loadConnectorHelp(detail.summary.connectorType);
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

  function diagStatusBadge(status) {
    const s = String(status || '').toLowerCase();
    if (s === 'ok') return '<span class="badge badge-ok">OK</span>';
    if (s === 'failed') return '<span class="badge badge-error">FAIL</span>';
    if (s === 'denied') return '<span class="badge badge-error">DENIED</span>';
    return '<span class="badge">—</span>';
  }

  function renderDiagnostic(report) {
    const steps = report.steps || [];
    const summary = report.succeeded
      ? '<div class="form-hint">All attempted checks passed.</div>'
      : '<div class="form-hint">One or more checks did not pass — see the remedies below.</div>';
    if (!steps.length) return `${summary}<div class="empty-state">No diagnostic steps returned.</div>`;
    return `
      ${summary}
      <table class="data-table">
        <thead><tr><th>Layer</th><th>Status</th><th>Detail</th><th>Remedy</th></tr></thead>
        <tbody>${steps.map((s) => `
          <tr>
            <td><code>${esc(s.layer)}</code></td>
            <td>${diagStatusBadge(s.status)}</td>
            <td>${esc(s.detail)}</td>
            <td>${s.remedy ? esc(s.remedy) : '—'}</td>
          </tr>`).join('')}</tbody>
      </table>`;
  }

  async function showDiagnostic(alias) {
    $('conn-detailAlias').textContent = `${alias} — connection test`;
    $('conn-detailBody').innerHTML = '<div class="form-hint">Running DNS → TCP → TLS diagnostic…</div>';
    $('conn-detailCard').style.display = '';
    try {
      const report = await connectionsApi.test(alias);
      $('conn-detailBody').innerHTML = renderDiagnostic(report);
    } catch (err) {
      const msg = err.body?.status === 'disabled'
        ? 'This connection is disabled — enable it before testing.'
        : err.message;
      $('conn-detailBody').innerHTML = `<div class="error-msg show">${esc(msg)}</div>`;
    }
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
      } else if (btn.dataset.act === 'test') {
        await showDiagnostic(alias);
      } else if (btn.dataset.act === 'disable') {
        if (!await window.ETLSQLFeedback.confirm(`Disable shared connection '${alias}'?`, { title: 'Disable shared connection', impact: `SHARED:${alias} will fail until it is re-enabled.`, confirmLabel: 'Disable connection', danger: true, auditAction: 'admin.connection.disable' })) return;
        await connectionsApi.disable(alias);
        await load();
      } else if (btn.dataset.act === 'enable') {
        await connectionsApi.enable(alias);
        await load();
      } else if (btn.dataset.act === 'delete') {
        if (!await window.ETLSQLFeedback.confirm(`Permanently delete shared connection '${alias}'?`, { title: 'Delete shared connection', confirmLabel: 'Delete connection', danger: true, auditAction: 'admin.connection.delete' })) return;
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

  $('conn-type').addEventListener('change', () => {
    loadConnectorHelp($('conn-type').value);
  });
  loadConnectorHelp($('conn-type').value);

  $('conn-help-expand-btn').addEventListener('click', () => {
    const type = $('conn-type').value;
    $('conn-help-modal-title').textContent = `${type} Connection Guidelines`;
    $('conn-help-modal-content').innerHTML = $('conn-help-content').innerHTML;
    $('conn-help-modal').style.display = 'flex';
  });

  const closeModal = () => { $('conn-help-modal').style.display = 'none'; };
  $('conn-help-modal-close').addEventListener('click', closeModal);
  $('conn-help-modal-close-btn').addEventListener('click', closeModal);
  $('conn-help-modal').addEventListener('click', (e) => {
    if (e.target === $('conn-help-modal')) closeModal();
  });

  return { load };
}
