// Control Plane Dashboard UI Logic (P2 SaaS Multi-Tenancy)
// Operates under Platform Identity Isolation; works with live API or mock fixture injection.

(function () {
  'use strict';

  function esc(s) {
    if (s == null) return '';
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function renderStateBadge(state) {
    const s = (state || 'Unknown').toLowerCase();
    let cls = 'cp-state-provisioning';
    if (s === 'active') cls = 'cp-state-active';
    else if (s === 'maintenance') cls = 'cp-state-maintenance';
    else if (s === 'quarantined' || s === 'deleting' || s === 'deleted') cls = 'cp-state-quarantined';
    return `<span class="cp-state-badge ${cls}">${esc(state)}</span>`;
  }

  function renderControlPlane(container, data) {
    if (!data) return;

    const { overview, tenants, audit } = data;

    // Overview KPIs
    if (overview) {
      const kpiTenants = container.querySelector('#kpiTenants');
      const kpiTenantsSub = container.querySelector('#kpiTenantsSub');
      const kpiExecutions = container.querySelector('#kpiExecutions');
      const kpiQueueDepth = container.querySelector('#kpiQueueDepth');
      const kpiGateways = container.querySelector('#kpiGateways');
      const kpiGatewayTenants = container.querySelector('#kpiGatewayTenants');
      const kpiAuditOutbox = container.querySelector('#kpiAuditOutbox');
      const kpiAuditPending = container.querySelector('#kpiAuditPending');

      if (kpiTenants) kpiTenants.textContent = `${overview.activeTenants} / ${overview.totalTenants}`;
      if (kpiTenantsSub) kpiTenantsSub.textContent = `${overview.provisioningTenants} provisioning, ${overview.maintenanceTenants} maint`;
      if (kpiExecutions) kpiExecutions.textContent = `${overview.activeExecutions}`;
      if (kpiQueueDepth) kpiQueueDepth.textContent = `Queue depth: ${overview.queuedExecutions}`;
      if (kpiGateways) kpiGateways.textContent = `${overview.connectedGateways}`;
      if (kpiGatewayTenants) kpiGatewayTenants.textContent = `Across ${overview.uniqueGatewayTenants} tenants`;
      if (kpiAuditOutbox) kpiAuditOutbox.textContent = overview.auditOutboxFailed > 0 ? `${overview.auditOutboxFailed} Failed` : 'Healthy';
      if (kpiAuditPending) kpiAuditPending.textContent = `${overview.auditOutboxPending} pending msgs`;
    }

    // Tenants Table
    const tbody = container.querySelector('#tenantTableBody');
    if (tbody && Array.isArray(tenants)) {
      if (tenants.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:24px;color:var(--portal-muted);">No tenants found in shared fleet.</td></tr>';
      } else {
        tbody.innerHTML = tenants.map(t => `
          <tr style="border-bottom:1px solid var(--portal-border-soft);">
            <td style="padding:10px 16px;font-weight:600;font-family:monospace;">${esc(t.tenantId)}</td>
            <td style="padding:10px 16px;">${renderStateBadge(t.state)}</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${esc(t.activeRelease || 'default')}</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${t.maxConcurrentJobs} max</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${t.maxStorageMb} MB</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${t.activeExecutions} active / ${t.queuedExecutions} queued</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${t.connectedGateways}</td>
            <td style="padding:10px 16px;">
              <div style="display:flex;align-items:center;gap:8px;">
                <div style="flex:1;background:var(--portal-border);height:6px;border-radius:3px;overflow:hidden;min-width:60px;">
                  <div style="width:${Math.min(100, t.quotaUtilizationPercentage || 0)}%;background:${(t.quotaUtilizationPercentage || 0) >= 90 ? 'var(--portal-danger)' : 'var(--portal-accent)'};height:100%;"></div>
                </div>
                <span style="font-size:0.75rem;color:var(--portal-muted);">${t.quotaUtilizationPercentage || 0}%</span>
              </div>
            </td>
          </tr>
        `).join('');
      }
    }

    // Audit Table
    const auditTbody = container.querySelector('#auditTableBody');
    if (auditTbody && Array.isArray(audit)) {
      if (audit.length === 0) {
        auditTbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:24px;color:var(--portal-muted);">No platform audit events recorded.</td></tr>';
      } else {
        auditTbody.innerHTML = audit.map(a => `
          <tr style="border-bottom:1px solid var(--portal-border-soft);">
            <td style="padding:10px 16px;font-family:monospace;font-size:0.8rem;">${esc(a.operationId)}</td>
            <td style="padding:10px 16px;font-weight:600;">${esc(a.tenantId)}</td>
            <td style="padding:10px 16px;font-size:0.85rem;"><span class="badge">${esc(a.kind)}</span></td>
            <td style="padding:10px 16px;font-size:0.85rem;">${esc(a.status)}</td>
            <td style="padding:10px 16px;font-size:0.85rem;">${esc(a.platformOperator)}</td>
            <td style="padding:10px 16px;font-size:0.85rem;font-family:monospace;">${esc(a.authorizationReference)}</td>
            <td style="padding:10px 16px;font-size:0.85rem;color:var(--portal-muted);">${esc(a.reason)}</td>
            <td style="padding:10px 16px;font-family:monospace;font-size:0.75rem;color:var(--portal-muted);">${esc(a.receiptHash)}</td>
          </tr>
        `).join('');
      }
    }
  }

  function initInteractiveControls(container) {
    // Setup tabs
    const tabs = container.querySelectorAll('.cp-tab');
    tabs.forEach(tab => {
      tab.addEventListener('click', () => {
        tabs.forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        const target = tab.dataset.tab;
        container.querySelectorAll('.cp-panel').forEach(p => p.classList.remove('active'));
        const panel = container.querySelector(`#panel-${target}`);
        if (panel) panel.classList.add('active');
      });
    });

    // Setup search filter
    const searchInput = container.querySelector('#tenantSearch');
    if (searchInput) {
      searchInput.addEventListener('input', (e) => {
        const query = e.target.value.toLowerCase();
        const rows = container.querySelectorAll('#tenantTableBody tr');
        rows.forEach(row => {
          const text = row.textContent.toLowerCase();
          row.style.display = text.includes(query) ? '' : 'none';
        });
      });
    }
  }

  // Export for Sandbox and live page
  window.ControlPlaneUI = {
    render: function (container, data) {
      renderControlPlane(container, data);
      initInteractiveControls(container);
    },
    init: initInteractiveControls
  };

  if (document.getElementById('kpiStrip')) {
    initInteractiveControls(document.body);

    // Auto-fetch on standalone page
    fetch('/api/platform/control-plane/overview')
      .then(r => r.ok ? r.json() : null)
      .then(async (overview) => {
        if (!overview) return;
        const tenants = await fetch('/api/platform/control-plane/tenants').then(r => r.ok ? r.json() : []);
        const audit = await fetch('/api/platform/control-plane/audit').then(r => r.ok ? r.json() : []);
        renderControlPlane(document.body, { overview, tenants, audit });
      })
      .catch(err => console.warn('Control plane API offline or unauthorized.', err));
  }
})();
