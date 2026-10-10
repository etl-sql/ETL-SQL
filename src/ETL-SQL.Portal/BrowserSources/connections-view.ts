export function esc(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s: unknown) {
  return esc(s).replace(/'/g, '&#39;');
}
function formatDate(value: string | null | undefined) {
  return value ? new Date(value).toLocaleString() : '—';
}

export const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Connection catalog — scripts use SHARED:alias</span>
        <h3>Shared Connections</h3>
      </div>
      <div class="admin-action-group">
        <span id="conn-status" class="form-hint"></span>
        <button class="btn btn-primary btn-sm" id="conn-wizardBtn">Connection Wizard</button>
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
    <div id="conn-bindingHint" class="form-hint"></div>
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
        <button class="btn btn-outline btn-sm" id="conn-help-modal-close" data-dialog-close style="padding: 4px 8px; font-size: 12px; cursor: pointer;">✕ Close</button>
      </div>
      <div class="modal-body" id="conn-help-modal-content" style="flex: 1; overflow-y: auto; padding: 0 8px; line-height: 1.5; font-size: 14px; color: var(--portal-text);">
        Loading...
      </div>
      <div class="modal-actions" style="margin-top: 16px; border-top: 1px solid var(--portal-border); padding-top: 12px; display: flex; justify-content: flex-end;">
        <button class="btn btn-outline btn-sm" id="conn-help-modal-close-btn" data-dialog-close style="cursor: pointer;">Close</button>
      </div>
    </div>
  </div>`;

export function renderConnectionTable(entries: PortalSharedConnectionSummary[]): string {
  if (!entries.length) return '<div class="empty-state">No shared connections cataloged yet.</div>';
  return `<table class="data-table"><thead><tr><th>Alias</th><th>Type</th><th>Scope</th><th>Status</th><th>Last used</th><th>Last verified</th><th></th></tr></thead>
    <tbody>${entries.map(entry => `<tr data-alias="${escAttr(entry.alias)}">
      <td><code>${esc(entry.alias)}</code>${entry.gateway ? ` <span class="chip chip-active" title="Gateway: ${escAttr(entry.gateway.gatewayId)} / Resource: ${escAttr(entry.gateway.resourceId)}">⚡ ${esc(entry.gateway.gatewayId)}</span>` : ''}</td>
      <td>${esc(entry.connectorType)}</td><td>${esc(entry.environmentScope || '—')}</td>
      <td class="conn-row-status"><span class="chip ${entry.disabled ? 'chip-inactive' : 'chip-active'}">${entry.disabled ? 'Disabled' : 'Active'}</span></td>
      <td>${esc(formatDate(entry.lastUsedAtUtc))}</td><td>${esc(formatDate(entry.lastVerifiedAtUtc))}</td>
      <td class="table-actions">${['detail', 'impact', 'verify', 'test', entry.disabled ? 'enable' : 'disable', 'delete']
        .map(action => `<button class="btn ${action === 'delete' ? 'btn-danger-soft' : 'btn-outline'} btn-sm" data-act="${action}">${action[0].toUpperCase() + action.slice(1)}</button>`).join('')}</td>
    </tr>`).join('')}</tbody></table>`;
}
export function renderConnectionDetail(detail: PortalSharedConnectionDetail): string {
  const options = Object.entries(detail.options);
  return `<div class="form-hint">${esc(detail.summary.connectorType)}${detail.summary.environmentScope ? ` · ${esc(detail.summary.environmentScope)}` : ''}
    · created ${esc(formatDate(detail.summary.createdAtUtc))} · updated ${esc(formatDate(detail.summary.updatedAtUtc))}</div>
    ${detail.gateway ? `<p><strong>Gateway Binding:</strong> <code>${esc(detail.gateway.gatewayId)}</code> → <code>${esc(detail.gateway.resourceId)}</code> <span class="chip chip-active">Gateway Bound</span></p>` : ''}
    ${detail.target ? `<p><strong>Target:</strong> <code>${esc(detail.target)}</code></p>` : ''}
    ${detail.sensitiveFields.length ? `<p><strong>Sensitive fields:</strong> ${detail.sensitiveFields.map(field => `<code>${esc(field)}</code>`).join(' ')}</p>` : ''}
    ${options.length ? `<table class="data-table"><thead><tr><th>Option</th><th>Value</th></tr></thead><tbody>
      ${options.map(([key, value]) => `<tr><td>${esc(key)}</td><td><code>${esc(value)}</code></td></tr>`).join('')}</tbody></table>` : '<div class="empty-state">No options.</div>'}`;
}
export function renderConnectionImpact(impact: ImpactReport): string {
  if (!impact.consumers.length) return '<div class="empty-state">No known consumers reference this entry.</div>';
  return `<div class="form-hint">${impact.consumerCount} consumer(s) reference <code>${esc(impact.reference)}</code>. Review before disabling or deleting.</div>
    <table class="data-table"><thead><tr><th>Type</th><th>Name</th><th>Detail</th><th>Last used</th><th>Uses</th></tr></thead><tbody>
      ${impact.consumers.map(entry => `<tr><td>${esc(entry.type)}</td><td>${esc(entry.name)}</td><td>${esc(entry.detail || '—')}</td>
        <td>${esc(formatDate(entry.lastUsedAtUtc))}</td><td>${entry.useCount ?? '—'}</td></tr>`).join('')}</tbody></table>`;
}
export function renderSharedConnectionDiagnostic(report: SharedConnectionDiagnosticDto): string {
  const badge = (status: string) => `<span class="badge ${status === 'ok' ? 'badge-ok' : status === 'skipped' ? '' : 'badge-error'}">${esc(status.toUpperCase())}</span>`;
  return `<div class="form-hint">${report.succeeded ? 'All attempted checks passed.' : 'One or more checks did not pass — see the remedies below.'}</div>
    <table class="data-table"><thead><tr><th>Layer</th><th>Status</th><th>Detail</th><th>Remedy</th></tr></thead><tbody>
      ${report.steps.map(step => `<tr><td><code>${esc(step.layer)}</code></td><td>${badge(step.status)}</td><td>${esc(step.detail)}</td><td>${esc(step.remedy || '—')}</td></tr>`).join('')}</tbody></table>`;
}
