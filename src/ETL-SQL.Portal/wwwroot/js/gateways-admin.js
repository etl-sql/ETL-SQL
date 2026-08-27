// Canonical "Data Gateways" admin surface (Admin → Data Gateways) over api/admin/gateways.
import { createConnectionWizard } from '../designer/connection-wizard.js';
import { connectionsApi } from './api.js';

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
        <span class="section-kicker">Hybrid Connectivity — Zero-Trust on-premises gateway clusters</span>
        <h3>Data Gateways</h3>
      </div>
      <div class="admin-action-group">
        <span id="gw-status" class="form-hint"></span>
        <button class="btn btn-outline btn-sm" id="gw-enrollBtn">Enroll Gateway</button>
        <button class="btn btn-outline btn-sm" id="gw-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="gw-tableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading gateways…</span></div></div>
  </div>

  <!-- Enroll Gateway Modal -->
  <div class="modal-overlay" id="gw-enrollModal" style="display:none" role="dialog" aria-modal="true" aria-labelledby="gw-enrollModal-title">
    <div class="modal-card" style="max-width: 580px;">
      <div class="modal-header">
        <h3 id="gw-enrollModal-title">Enroll Data Gateway</h3>
        <button class="modal-close" id="gw-enrollCloseBtn" aria-label="Close">&times;</button>
      </div>
      <div class="modal-body">
        <div id="gw-enrollForm">
          <p class="form-hint" style="margin-bottom: 12px;">
            Issue a cryptographically verified one-time enrollment token to connect an on-premises Gateway daemon cluster to this Portal.
          </p>
          <div class="form-group">
            <label for="gw-input-id">Gateway Cluster ID</label>
            <input type="text" id="gw-input-id" placeholder="e.g. corp-mssql-gw" class="form-control" />
            <small class="form-hint">Unique identifier for this logical gateway or cluster.</small>
          </div>
          <div class="form-group" style="margin-top: 12px;">
            <label for="gw-input-expiry">Token Expiration (minutes)</label>
            <input type="number" id="gw-input-expiry" value="60" min="5" max="1440" class="form-control" />
          </div>
          <div id="gw-enroll-error" class="error-msg" style="display:none; margin-top: 10px;"></div>
          <div style="margin-top: 20px; display: flex; justify-content: flex-end; gap: 8px;">
            <button class="btn btn-outline" id="gw-enrollCancelBtn">Cancel</button>
            <button class="btn btn-primary" id="gw-enrollSubmitBtn">Generate Token</button>
          </div>
        </div>

        <div id="gw-enrollResult" style="display:none;">
          <div class="alert alert-success" style="margin-bottom: 16px;">
            <strong>Enrollment Token Generated!</strong>
            <p style="margin: 4px 0 0 0; font-size: 0.9em;">
              This token is shown only once. Use it to install or start your on-premises Gateway daemon.
            </p>
          </div>
          <div class="form-group">
            <label>One-Time Token</label>
            <div style="display: flex; gap: 8px;">
              <input type="text" id="gw-result-token" readonly class="form-control" style="font-family: monospace; font-size: 0.85em;" />
              <button class="btn btn-outline btn-sm" id="gw-copyTokenBtn">Copy</button>
            </div>
          </div>
          <div class="form-group" style="margin-top: 14px;">
            <label>Quick Setup Command</label>
            <div style="display: flex; gap: 8px;">
              <textarea id="gw-result-cmd" readonly class="form-control" rows="2" style="font-family: monospace; font-size: 0.85em; resize: none;"></textarea>
              <button class="btn btn-outline btn-sm" id="gw-copyCmdBtn">Copy</button>
            </div>
          </div>
          <div style="margin-top: 20px; display: flex; justify-content: flex-end;">
            <button class="btn btn-primary" id="gw-enrollDoneBtn">Done</button>
          </div>
        </div>
      </div>
    </div>
  </div>

  <!-- Cluster Nodes Modal -->
  <div class="modal-overlay" id="gw-nodesModal" style="display:none" role="dialog" aria-modal="true" aria-labelledby="gw-nodesModal-title">
    <div class="modal-card" style="max-width: 650px;">
      <div class="modal-header">
        <h3 id="gw-nodesModal-title">Cluster Nodes — <span id="gw-nodesModalGatewayId"></span></h3>
        <button class="modal-close" id="gw-nodesCloseBtn" aria-label="Close">&times;</button>
      </div>
      <div class="modal-body">
        <div id="gw-nodesTableWrap"></div>
        <div style="margin-top: 20px; display: flex; justify-content: flex-end;">
          <button class="btn btn-outline" id="gw-nodesDismissBtn">Close</button>
        </div>
      </div>
    </div>
  </div>
`;

export function createGatewaysAdmin({ host, gatewaysApi }) {
  if (!host) throw new Error('host element required');
  if (!gatewaysApi) throw new Error('gatewaysApi required');

  host.innerHTML = PANEL_HTML;

  const $status = host.querySelector('#gw-status');
  const $tableWrap = host.querySelector('#gw-tableWrap');
  const $enrollBtn = host.querySelector('#gw-enrollBtn');
  const $refreshBtn = host.querySelector('#gw-refreshBtn');

  // Enroll modal elements
  const $enrollModal = host.querySelector('#gw-enrollModal');
  const $enrollForm = host.querySelector('#gw-enrollForm');
  const $enrollResult = host.querySelector('#gw-enrollResult');
  const $inputId = host.querySelector('#gw-input-id');
  const $inputExpiry = host.querySelector('#gw-input-expiry');
  const $enrollError = host.querySelector('#gw-enroll-error');
  const $enrollSubmitBtn = host.querySelector('#gw-enrollSubmitBtn');
  const $enrollCancelBtn = host.querySelector('#gw-enrollCancelBtn');
  const $enrollCloseBtn = host.querySelector('#gw-enrollCloseBtn');
  const $enrollDoneBtn = host.querySelector('#gw-enrollDoneBtn');
  const $resultToken = host.querySelector('#gw-result-token');
  const $resultCmd = host.querySelector('#gw-result-cmd');
  const $copyTokenBtn = host.querySelector('#gw-copyTokenBtn');
  const $copyCmdBtn = host.querySelector('#gw-copyCmdBtn');

  // Nodes modal elements
  const $nodesModal = host.querySelector('#gw-nodesModal');
  const $nodesModalGatewayId = host.querySelector('#gw-nodesModalGatewayId');
  const $nodesTableWrap = host.querySelector('#gw-nodesTableWrap');
  const $nodesCloseBtn = host.querySelector('#gw-nodesCloseBtn');
  const $nodesDismissBtn = host.querySelector('#gw-nodesDismissBtn');

  let gatewaysCache = [];

  async function load() {
    $tableWrap.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading gateways…</span></div>';
    try {
      const list = await gatewaysApi.list();
      gatewaysCache = list || [];
      render();
    } catch (err) {
      $tableWrap.innerHTML = `<div class="error-msg show">${esc(err.message || 'Failed to load gateways.')}</div>`;
    }
  }

  function render() {
    if (!gatewaysCache.length) {
      $tableWrap.innerHTML = `
        <div class="empty-state" style="padding: 32px; text-align: center;">
          <h4>No Data Gateways Enrolled</h4>
          <p class="form-hint" style="margin: 8px 0 16px;">Enroll an on-premises Data Gateway to securely query internal databases without opening firewall ports.</p>
          <button class="btn btn-primary btn-sm" id="gw-emptyEnrollBtn">Enroll First Gateway</button>
        </div>`;
      host.querySelector('#gw-emptyEnrollBtn')?.addEventListener('click', openEnrollModal);
      $status.textContent = '0 Gateways';
      return;
    }

    const onlineCount = gatewaysCache.filter(g => g.isOnline).length;
    const totalNodes = gatewaysCache.reduce((sum, g) => sum + (g.activeNodes || 0), 0);
    $status.textContent = `${gatewaysCache.length} Gateway${gatewaysCache.length === 1 ? '' : 's'} • ${totalNodes} Active Node${totalNodes === 1 ? '' : 's'}`;

    let html = `
      <table class="admin-table">
        <thead>
          <tr>
            <th>Gateway ID</th>
            <th>Status</th>
            <th>Cluster Nodes</th>
            <th>Enrolled</th>
            <th>Workload Identity</th>
            <th style="text-align: right;">Actions</th>
          </tr>
        </thead>
        <tbody>`;

    for (const g of gatewaysCache) {
      let statusBadge = '';
      if (g.state === 'Revoked') {
        statusBadge = '<span class="status-pill status-pill-bad">Revoked</span>';
      } else if (g.state === 'Pending') {
        statusBadge = '<span class="status-pill status-pill-warn">Enrollment Pending</span>';
      } else if (g.isOnline) {
        statusBadge = `<span class="status-pill status-pill-good">● ${g.activeNodes} Node${g.activeNodes === 1 ? '' : 's'} Online</span>`;
      } else {
        statusBadge = '<span class="status-pill status-pill-neutral">○ Disconnected</span>';
      }

      const thumbprint = g.workloadPublicKeyThumbprint
        ? `<code style="font-size: 0.8em;">${esc(g.workloadPublicKeyThumbprint.substring(0, 12))}…</code>`
        : '<span class="form-hint">None</span>';

      const nodesCountText = g.nodes && g.nodes.length > 0
        ? `<a href="#" class="gw-view-nodes" data-gw-id="${escAttr(g.gatewayId)}">${g.activeNodes} / ${g.totalNodes} Active</a>`
        : `${g.activeNodes} Active`;

      html += `
        <tr>
          <td><strong>${esc(g.gatewayId)}</strong></td>
          <td>${statusBadge}</td>
          <td>${nodesCountText}</td>
          <td>${formatDate(g.consumedUtc || g.createdUtc)}</td>
          <td>${thumbprint}</td>
          <td style="text-align: right;">
            ${g.state !== 'Revoked' && g.isOnline ? `<button class="btn btn-outline btn-xs gw-create-conn-btn" data-gw-id="${escAttr(g.gatewayId)}">Bind Connection</button>` : ''}
            ${g.nodes && g.nodes.length > 0 ? `<button class="btn btn-outline btn-xs gw-view-nodes-btn" data-gw-id="${escAttr(g.gatewayId)}">Nodes</button>` : ''}
            ${g.state !== 'Revoked' ? `<button class="btn btn-outline btn-xs btn-danger gw-revoke-btn" data-gw-id="${escAttr(g.gatewayId)}">Revoke</button>` : ''}
          </td>
        </tr>`;
    }

    html += '</tbody></table>';
    $tableWrap.innerHTML = html;

    // Attach row events
    $tableWrap.querySelectorAll('.gw-create-conn-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        const gwId = btn.getAttribute('data-gw-id');
        launchConnectionWizard(gwId);
      });
    });

    $tableWrap.querySelectorAll('.gw-view-nodes, .gw-view-nodes-btn').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        const gwId = btn.getAttribute('data-gw-id');
        openNodesModal(gwId);
      });
    });

    $tableWrap.querySelectorAll('.gw-revoke-btn').forEach(btn => {
      btn.addEventListener('click', async () => {
        const gwId = btn.getAttribute('data-gw-id');
        if (!confirm(`Are you sure you want to revoke Gateway '${gwId}'? Connected nodes will be disconnected immediately.`)) {
          return;
        }
        try {
          await gatewaysApi.revoke(gwId);
          await load();
        } catch (err) {
          alert('Failed to revoke gateway: ' + (err.message || err));
        }
      });
    });
  }

  function launchConnectionWizard(gatewayId, resourceId = '') {
    createConnectionWizard({
      host: document.body,
      mode: 'admin',
      initialGateway: gatewayId,
      initialResourceId: resourceId,
      fetchSchemas: async () => {
        const res = await fetch('/api/connectors/schema');
        const json = await res.json();
        return Array.isArray(json) ? json : (json.schemas || []);
      },
      fetchGateways: async () => {
        try {
          const json = await fetch('/api/connectors/gateways').then(r => r.json());
          return Array.isArray(json) ? json : (json.gateways || []);
        } catch {
          return [];
        }
      },
      fetchGatewayResources: async (gwId) => {
        const res = await fetch(`/api/connectors/gateways/${encodeURIComponent(gwId)}/resources`);
        if (!res.ok) throw new Error('Gateway resource discovery failed.');
        const json = await res.json();
        return Array.isArray(json) ? json : (json.resources || []);
      },
      fetchSecrets: async () => {
        try {
          const list = await (window.secretsApi ? window.secretsApi.list() : fetch('/api/admin/secrets').then(r => r.json()));
          return Array.isArray(list) ? list.map(s => s.name || s) : [];
        } catch {
          return [];
        }
      },
      onTest: async (req) => {
        const res = await fetch('/api/connectors/test', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(req)
        });
        return await res.json();
      },
      onSave: async (entry) => {
        await connectionsApi.set(entry.alias, {
          connectorType: entry.connectorType,
          target: entry.target,
          options: entry.options,
          gateway: entry.gateway,
          environmentScope: entry.environmentScope,
          sensitiveFields: []
        });
      }
    });
  }

  function openEnrollModal() {
    $inputId.value = '';
    $inputExpiry.value = '60';
    $enrollError.style.display = 'none';
    $enrollForm.style.display = 'block';
    $enrollResult.style.display = 'none';
    $enrollModal.style.display = 'flex';
  }

  function closeEnrollModal() {
    $enrollModal.style.display = 'none';
  }

  function openNodesModal(gatewayId) {
    const gw = gatewaysCache.find(g => g.gatewayId.toLowerCase() === gatewayId.toLowerCase());
    if (!gw) return;

    $nodesModalGatewayId.textContent = gw.gatewayId;
    if (!gw.nodes || !gw.nodes.length) {
      $nodesTableWrap.innerHTML = '<p class="form-hint" style="padding: 16px;">No live nodes connected to this gateway cluster.</p>';
    } else {
      let nodeHtml = `
        <table class="admin-table">
          <thead>
            <tr>
              <th>Node Identifier</th>
              <th>Status</th>
              <th>Connected Since</th>
              <th>Identity Thumbprint</th>
            </tr>
          </thead>
          <tbody>`;
      for (const node of gw.nodes) {
        nodeHtml += `
          <tr>
            <td><code>${esc(node.nodeId)}</code></td>
            <td>${node.isActive ? '<span class="status-pill status-pill-good">Online</span>' : '<span class="status-pill status-pill-bad">Disconnected</span>'}</td>
            <td>${formatDate(node.connectedUtc)}</td>
            <td><code style="font-size: 0.8em;">${esc((node.workloadPublicKeyThumbprint || '').substring(0, 12))}…</code></td>
          </tr>`;
      }
      nodeHtml += '</tbody></table>';
      $nodesTableWrap.innerHTML = nodeHtml;
    }
    $nodesModal.style.display = 'flex';
  }

  function closeNodesModal() {
    $nodesModal.style.display = 'none';
  }

  // Event bindings
  $refreshBtn.addEventListener('click', load);
  $enrollBtn.addEventListener('click', openEnrollModal);
  $enrollCancelBtn.addEventListener('click', closeEnrollModal);
  $enrollCloseBtn.addEventListener('click', closeEnrollModal);
  $enrollDoneBtn.addEventListener('click', () => { closeEnrollModal(); load(); });

  $nodesCloseBtn.addEventListener('click', closeNodesModal);
  $nodesDismissBtn.addEventListener('click', closeNodesModal);

  $enrollSubmitBtn.addEventListener('click', async () => {
    const gatewayId = $inputId.value.trim();
    const expiry = parseInt($inputExpiry.value, 10) || 60;
    if (!gatewayId) {
      $enrollError.textContent = 'Please enter a Gateway ID.';
      $enrollError.style.display = 'block';
      return;
    }

    $enrollSubmitBtn.disabled = true;
    $enrollError.style.display = 'none';
    try {
      const res = await gatewaysApi.enroll(gatewayId, expiry);
      $resultToken.value = res.oneTimeToken;
      const portalOrigin = window.location.origin;
      $resultCmd.value = `etlsql gateway setup --portal ${portalOrigin} --tenant ${res.tenantId} --gateway-id ${res.gatewayId} --token ${res.oneTimeToken}`;
      $enrollForm.style.display = 'none';
      $enrollResult.style.display = 'block';
    } catch (err) {
      $enrollError.textContent = err.message || 'Failed to generate enrollment token.';
      $enrollError.style.display = 'block';
    } finally {
      $enrollSubmitBtn.disabled = false;
    }
  });

  $copyTokenBtn.addEventListener('click', () => {
    navigator.clipboard.writeText($resultToken.value);
    $copyTokenBtn.textContent = 'Copied!';
    setTimeout(() => { $copyTokenBtn.textContent = 'Copy'; }, 2000);
  });

  $copyCmdBtn.addEventListener('click', () => {
    navigator.clipboard.writeText($resultCmd.value);
    $copyCmdBtn.textContent = 'Copied!';
    setTimeout(() => { $copyCmdBtn.textContent = 'Copy'; }, 2000);
  });

  return {
    load
  };
}
