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
        <span class="section-kicker">Hybrid Connectivity</span>
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
      <div id="gw-enroll-error" class="error-msg"></div>
      <div class="form-actions">
        <button class="btn btn-primary btn-sm" id="gw-enrollSubmitBtn">Generate Token</button>
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
            <button class="btn btn-outline btn-sm" id="gw-copyTokenBtn">Copy</button>
          </div>
        </div>
      </div>
      <div class="form-row">
        <div class="form-group">
          <label for="gw-result-cmd">Quick Setup Command</label>
          <div class="inline-control inline-control-gap">
            <input id="gw-result-cmd" type="text" readonly class="param-input" style="font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: .85em;">
            <button class="btn btn-outline btn-sm" id="gw-copyCmdBtn">Copy</button>
          </div>
        </div>
      </div>
      <div class="form-actions">
        <button class="btn btn-primary btn-sm" id="gw-enrollDoneBtn">Done</button>
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
        <button class="btn btn-ghost btn-sm" id="gw-nodesCloseBtn" aria-label="Close" style="font-size: 1.1em; line-height: 1;">✕</button>
      </div>
      <div class="modal-body">
        <div id="gw-nodesTableWrap"></div>
      </div>
      <div class="modal-actions">
        <button class="btn btn-outline btn-sm" id="gw-nodesDismissBtn">Close</button>
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

  // Enroll elements
  const $enrollForm = host.querySelector('#gw-enrollForm');
  const $enrollResult = host.querySelector('#gw-enrollResult');
  const $inputId = host.querySelector('#gw-input-id');
  const $inputExpiry = host.querySelector('#gw-input-expiry');
  const $enrollError = host.querySelector('#gw-enroll-error');
  const $enrollSubmitBtn = host.querySelector('#gw-enrollSubmitBtn');
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

  function focusEnrollForm() {
    $enrollForm.style.display = '';
    $enrollResult.style.display = 'none';
    $inputId.scrollIntoView({ behavior: 'smooth', block: 'center' });
    $inputId.focus();
  }

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
        <div class="empty-state">
          <h2>No Data Gateways Enrolled</h2>
          <p class="form-hint" style="margin: 8px auto 16px; max-width: 460px;">Enroll an on-premises Data Gateway to securely query internal databases without opening firewall ports.</p>
          <button class="btn btn-primary btn-sm" id="gw-emptyEnrollBtn">Enroll First Gateway</button>
        </div>`;
      host.querySelector('#gw-emptyEnrollBtn')?.addEventListener('click', focusEnrollForm);
      $status.textContent = '0 Gateways';
      return;
    }

    const totalNodes = gatewaysCache.reduce((sum, g) => sum + (g.activeNodes || 0), 0);
    $status.textContent = `${gatewaysCache.length} Gateway${gatewaysCache.length === 1 ? '' : 's'} • ${totalNodes} Active Node${totalNodes === 1 ? '' : 's'}`;

    let html = `
      <table class="data-table">
        <thead>
          <tr>
            <th>Gateway ID</th>
            <th>Status</th>
            <th>Cluster Nodes</th>
            <th>Enrolled</th>
            <th>Workload Identity</th>
            <th></th>
          </tr>
        </thead>
        <tbody>`;

    for (const g of gatewaysCache) {
      let statusBadge = '';
      if (g.state === 'Revoked') {
        statusBadge = '<span class="chip chip-inactive">Revoked</span>';
      } else if (g.state === 'Pending') {
        statusBadge = '<span class="badge badge-warning">Enrollment Pending</span>';
      } else if (g.isOnline) {
        statusBadge = `<span class="chip chip-active">● ${g.activeNodes} Node${g.activeNodes === 1 ? '' : 's'} Online</span>`;
      } else {
        statusBadge = '<span class="badge badge-neutral">○ Disconnected</span>';
      }

      const thumbprint = g.workloadPublicKeyThumbprint
        ? `<code>${esc(g.workloadPublicKeyThumbprint.substring(0, 12))}…</code>`
        : '<span class="text-muted">—</span>';

      const nodesCountText = g.nodes && g.nodes.length > 0
        ? `<a href="#" class="gw-view-nodes btn-link" data-gw-id="${escAttr(g.gatewayId)}">${g.activeNodes} / ${g.totalNodes} Active</a>`
        : `${g.activeNodes} Active`;

      const enrolledDate = g.consumedUtc || g.createdUtc || g.consumedAtUtc || g.issuedAtUtc;

      html += `
        <tr data-gw-id="${escAttr(g.gatewayId)}">
          <td><code>${esc(g.gatewayId)}</code></td>
          <td>${statusBadge}</td>
          <td>${nodesCountText}</td>
          <td>${esc(formatDate(enrolledDate))}</td>
          <td>${thumbprint}</td>
          <td class="table-actions">
            ${g.state !== 'Revoked' && g.isOnline ? `<button class="btn btn-outline btn-sm gw-create-conn-btn" data-gw-id="${escAttr(g.gatewayId)}">Bind Connection</button>` : ''}
            ${g.nodes && g.nodes.length > 0 ? `<button class="btn btn-outline btn-sm gw-view-nodes-btn" data-gw-id="${escAttr(g.gatewayId)}">Nodes</button>` : ''}
            ${g.state !== 'Revoked' ? `<button class="btn btn-danger-soft btn-sm gw-revoke-btn" data-gw-id="${escAttr(g.gatewayId)}">Revoke</button>` : ''}
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
        const confirmed = window.ETLSQLFeedback?.confirm
          ? await window.ETLSQLFeedback.confirm(`Are you sure you want to revoke Gateway '${gwId}'? Connected nodes will be disconnected immediately.`, {
              title: 'Revoke Gateway',
              impact: 'Connected nodes will be disconnected immediately.',
              confirmLabel: 'Revoke gateway',
              danger: true,
              auditAction: 'admin.gateway.revoke'
            })
          : confirm(`Are you sure you want to revoke Gateway '${gwId}'? Connected nodes will be disconnected immediately.`);
        if (!confirmed) return;

        try {
          await gatewaysApi.revoke(gwId);
          if (window.ETLSQLFeedback?.notify) {
            window.ETLSQLFeedback.notify(`Gateway '${gwId}' revoked.`, { title: 'Gateway Revoked', tone: 'success' });
          }
          await load();
        } catch (err) {
          if (window.ETLSQLFeedback?.notify) {
            window.ETLSQLFeedback.notify(err.message || 'Failed to revoke gateway.', { title: 'Revocation failed', tone: 'error' });
          } else {
            alert('Failed to revoke gateway: ' + (err.message || err));
          }
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

  function openNodesModal(gatewayId) {
    const gw = gatewaysCache.find(g => (g.gatewayId || '').toLowerCase() === (gatewayId || '').toLowerCase());
    if (!gw) return;

    $nodesModalGatewayId.textContent = gw.gatewayId;
    if (!gw.nodes || !gw.nodes.length) {
      $nodesTableWrap.innerHTML = '<p class="form-hint" style="padding: 16px; margin: 0;">No live nodes connected to this gateway cluster.</p>';
    } else {
      let nodeHtml = `
        <table class="data-table">
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
        const isOnline = node.isActive || node.status === 'Active';
        const connectedDate = node.connectedUtc || node.connectedAtUtc;
        const thumbprint = node.workloadPublicKeyThumbprint
          ? `<code>${esc(node.workloadPublicKeyThumbprint.substring(0, 12))}…</code>`
          : '<span class="text-muted">—</span>';
        nodeHtml += `
          <tr>
            <td><code>${esc(node.nodeId)}</code></td>
            <td>${isOnline ? '<span class="chip chip-active">● Online</span>' : '<span class="chip chip-inactive">○ Disconnected</span>'}</td>
            <td>${esc(formatDate(connectedDate))}</td>
            <td>${thumbprint}</td>
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
  $enrollBtn.addEventListener('click', focusEnrollForm);
  $nodesCloseBtn.addEventListener('click', closeNodesModal);
  $nodesDismissBtn.addEventListener('click', closeNodesModal);
  $nodesModal.addEventListener('click', (e) => {
    if (e.target === $nodesModal) closeNodesModal();
  });

  $enrollSubmitBtn.addEventListener('click', async () => {
    const gatewayId = $inputId.value.trim();
    const expiry = parseInt($inputExpiry.value, 10) || 60;
    if (!gatewayId) {
      $enrollError.textContent = 'Please enter a Gateway ID.';
      $enrollError.classList.add('show');
      return;
    }

    $enrollSubmitBtn.disabled = true;
    $enrollError.textContent = '';
    $enrollError.classList.remove('show');
    try {
      const res = await gatewaysApi.enroll(gatewayId, expiry);
      $resultToken.value = res.oneTimeToken;
      const portalOrigin = window.location.origin;
      $resultCmd.value = `etlsql gateway setup --portal ${portalOrigin} --tenant ${res.tenantId || 'default'} --gateway-id ${res.gatewayId || gatewayId} --token ${res.oneTimeToken}`;
      $enrollForm.style.display = 'none';
      $enrollResult.style.display = '';
    } catch (err) {
      $enrollError.textContent = err.message || 'Failed to generate enrollment token.';
      $enrollError.classList.add('show');
    } finally {
      $enrollSubmitBtn.disabled = false;
    }
  });

  $enrollDoneBtn.addEventListener('click', () => {
    $inputId.value = '';
    $inputExpiry.value = '60';
    $enrollResult.style.display = 'none';
    $enrollForm.style.display = '';
    load();
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
