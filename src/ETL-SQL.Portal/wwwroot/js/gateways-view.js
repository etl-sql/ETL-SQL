/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/gateways-view.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
export const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Hybrid Connectivity</span>
        <h3>Data Gateways</h3>
      </div>
      <div class="admin-action-group">
        <span id="gw-status" class="form-hint" role="status"></span>
        <button type="button" class="btn btn-outline btn-sm" id="gw-enrollBtn">Enroll Gateway</button>
        <button type="button" class="btn btn-outline btn-sm" id="gw-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="gw-action-status" role="status" class="form-hint"></div>
    <div id="gw-tableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading gateways…</span></div></div>
  </div>

  <div class="card" id="gw-enrollCard">
    <div class="card-header"><h3>Enroll a data gateway</h3></div>
    <div id="gw-enrollForm">
      <div class="form-row">
        <div class="form-group">
          <label for="gw-input-id">Gateway Cluster ID</label>
          <input id="gw-input-id" type="text" placeholder="corp-mssql-gw" autocomplete="off">
        </div>
        <div class="form-group">
          <label for="gw-input-expiry">Token Expiration (minutes)</label>
          <input id="gw-input-expiry" type="number" value="60" min="5" max="1440">
        </div>
      </div>
      <div id="gw-enroll-error" class="error-msg" role="alert"></div>
      <div class="form-actions">
        <button type="button" class="btn btn-primary btn-sm" id="gw-enrollSubmitBtn">Generate Token</button>
        <span class="form-hint">Issues a cryptographically verified one-time enrollment token to connect an on-premises Gateway daemon cluster to this Portal.</span>
      </div>
    </div>

    <div id="gw-enrollResult" style="display:none">
      <div class="status-banner status-banner-info">
        <div>
          <strong>Enrollment Token Generated!</strong>
          <div class="text-sm">This token is shown only once. Use it to install or start your on-premises Gateway daemon.</div>
        </div>
      </div>
      <div class="form-row">
        <div class="form-group">
          <label for="gw-result-token">One-Time Token</label>
          <div class="inline-control inline-control-gap">
            <input id="gw-result-token" type="text" readonly class="param-input" style="font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: .85em;">
            <button type="button" class="btn btn-outline btn-sm" id="gw-copyTokenBtn">Copy</button>
          </div>
        </div>
      </div>
      <div class="form-row">
        <div class="form-group">
          <label for="gw-result-cmd">Quick Setup Command (PowerShell)</label>
          <div class="inline-control inline-control-gap">
            <input id="gw-result-cmd" type="text" readonly class="param-input" style="font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: .85em;">
            <button type="button" class="btn btn-outline btn-sm" id="gw-copyCmdBtn">Copy</button>
          </div>
        </div>
      </div>
      <div class="form-actions">
        <button type="button" class="btn btn-primary btn-sm" id="gw-enrollDoneBtn">Done</button>
      </div>
    </div>
  </div>

  <!-- Cluster Nodes Modal -->
  <div class="modal-overlay" id="gw-nodesModal" style="display:none" role="dialog" aria-modal="true" aria-labelledby="gw-nodesModal-title">
    <div class="modal-card modal-lg">
      <div class="modal-header">
        <div>
          <span class="section-kicker">Cluster Nodes</span>
          <h3 class="modal-title" id="gw-nodesModal-title">Cluster Nodes — <code id="gw-nodesModalGatewayId"></code></h3>
        </div>
        <button type="button" class="btn btn-ghost btn-sm" id="gw-nodesCloseBtn" data-dialog-close aria-label="Close" style="font-size: 1.1em; line-height: 1;">✕</button>
      </div>
      <div class="modal-body">
        <div id="gw-nodesTableWrap"></div>
      </div>
      <div class="modal-actions">
        <button type="button" class="btn btn-outline btn-sm" id="gw-nodesDismissBtn">Close</button>
      </div>
    </div>
  </div>
`;
export function escapeGateway(value) {
    return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
}
const formatDate = (value) => value ? new Date(value).toLocaleString() : '—';
/** Single-quoted PowerShell arguments keep gateway and tenant identifiers literal. */
export function gatewaySetupCommand(origin, receipt) {
    const quote = (value) => "'" + value.replaceAll("'", "''") + "'";
    return 'etlsql gateway setup --portal ' + quote(origin) + ' --tenant ' + quote(receipt.tenantId)
        + ' --gateway-id ' + quote(receipt.gatewayId) + ' --token ' + quote(receipt.oneTimeToken);
}
export function renderGatewayFleet(entries) {
    if (!entries.length)
        return '<div class="empty-state"><h2>No Data Gateways Enrolled</h2>'
            + '<p class="form-hint">Enroll an on-premises Data Gateway to securely query internal databases without opening firewall ports.</p>'
            + '<button type="button" class="btn btn-primary btn-sm" id="gw-emptyEnrollBtn">Enroll First Gateway</button></div>';
    const rows = entries.map(gateway => {
        const id = escapeGateway(gateway.gatewayId);
        const badge = gateway.state === 'Revoked' ? '<span class="chip chip-inactive">Revoked</span>'
            : gateway.state === 'Pending' ? '<span class="badge badge-warning">Enrollment Pending</span>'
                : gateway.isOnline ? '<span class="chip chip-active">● ' + gateway.activeNodes + ' Nodes Online</span>'
                    : '<span class="badge badge-neutral">○ Disconnected</span>';
        const action = (className, label) => '<button type="button" class="btn btn-outline btn-sm ' + className
            + '" data-gw-id="' + id + '">' + label + '</button>';
        return '<tr data-gw-id="' + id + '"><td><code>' + id + '</code></td><td>' + badge + '</td><td>'
            + gateway.activeNodes + ' / ' + gateway.totalNodes + ' Active</td><td>'
            + escapeGateway(formatDate(gateway.consumedUtc ?? gateway.createdUtc)) + '</td><td><code>'
            + escapeGateway(gateway.workloadPublicKeyThumbprint?.slice(0, 12) || '—') + '</code></td><td class="table-actions">'
            + (gateway.state !== 'Revoked' && gateway.isOnline ? action('gw-create-conn-btn', 'Bind Connection') : '')
            + (gateway.nodes.length ? action('gw-view-nodes-btn', 'Nodes') : '')
            + (gateway.state !== 'Revoked' ? action('gw-revoke-btn', 'Revoke') : '') + '</td></tr>';
    });
    return '<table class="data-table"><thead><tr><th>Gateway ID</th><th>Status</th><th>Cluster Nodes</th>'
        + '<th>Enrolled</th><th>Workload Identity</th><th>Actions</th></tr></thead><tbody>' + rows.join('') + '</tbody></table>';
}
export function renderGatewayNodes(gateway) {
    if (!gateway.nodes.length)
        return '<p class="form-hint">No live nodes connected to this gateway cluster.</p>';
    return '<table class="data-table"><thead><tr><th>Node Identifier</th><th>Status</th><th>Connected Since</th>'
        + '<th>Identity Thumbprint</th></tr></thead><tbody>' + gateway.nodes.map(node => '<tr><td><code>'
        + escapeGateway(node.nodeId) + '</code></td><td><span class="chip ' + (node.isActive ? 'chip-active' : 'chip-inactive')
        + '">' + (node.isActive ? '● Online' : '○ Disconnected') + '</span></td><td>' + escapeGateway(formatDate(node.connectedUtc))
        + '</td><td><code>' + escapeGateway(node.workloadPublicKeyThumbprint.slice(0, 12)) + '</code></td></tr>').join('') + '</tbody></table>';
}
