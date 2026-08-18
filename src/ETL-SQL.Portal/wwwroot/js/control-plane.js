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
            <td style="padding:10px 16px;text-align:right;">
              <button class="btn-quota" data-tenant="${esc(t.tenantId)}" data-jobs="${t.maxConcurrentJobs}" data-storage="${t.maxStorageMb}" data-reports="${t.maxReportSessions}" style="padding:4px 8px;font-size:0.75rem;border:1px solid var(--portal-border);border-radius:3px;background:var(--portal-surface-subtle);color:var(--portal-text);cursor:pointer;margin-right:4px;">Quotas</button>
              <button class="btn-state" data-tenant="${esc(t.tenantId)}" data-state="${esc(t.state)}" style="padding:4px 8px;font-size:0.75rem;border:1px solid var(--portal-border);border-radius:3px;background:var(--portal-surface-subtle);color:var(--portal-text);cursor:pointer;">State</button>
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

  function initInteractiveControls(container, onReload) {
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

    // Modal helpers
    function showModal(id) {
      const el = container.querySelector(id);
      if (el) el.style.display = 'flex';
    }
    function hideModals() {
      container.querySelectorAll('#modalProvision, #modalQuotas, #modalState').forEach(m => m.style.display = 'none');
    }

    container.querySelectorAll('.btn-cancel').forEach(btn => btn.addEventListener('click', hideModals));

    // Provision button
    const btnOpenProv = container.querySelector('#btnOpenProvision');
    if (btnOpenProv) {
      btnOpenProv.addEventListener('click', () => showModal('#modalProvision'));
    }

    // Row buttons (delegated)
    const tbody = container.querySelector('#tenantTableBody');
    if (tbody) {
      tbody.addEventListener('click', (e) => {
        const target = e.target;
        if (target.classList.contains('btn-quota')) {
          const tenant = target.dataset.tenant;
          const jobs = target.dataset.jobs;
          const storage = target.dataset.storage;
          const modal = container.querySelector('#modalQuotas');
          if (modal) {
            modal.querySelector('#quotaTenantId').value = tenant;
            modal.querySelector('#quotaMaxJobs').value = jobs || 10;
            modal.querySelector('#quotaStorageMb').value = storage || 20480;
            showModal('#modalQuotas');
          }
        } else if (target.classList.contains('btn-state')) {
          const tenant = target.dataset.tenant;
          const state = target.dataset.state;
          const modal = container.querySelector('#modalState');
          if (modal) {
            modal.querySelector('#stateTenantId').value = tenant;
            modal.querySelector('#stateSelect').value = state || 'Active';
            showModal('#modalState');
          }
        }
      });
    }

    function showReceipt(receipt) {
      const alert = container.querySelector('#receiptAlert');
      if (alert) {
        alert.style.display = 'block';
        alert.innerHTML = `<strong>Receipt ${esc(receipt.operationId)}:</strong> Action <code>${esc(receipt.kind)}</code> executed by <code>${esc(receipt.platformOperator)}</code> (Ref: <code>${esc(receipt.authorizationReference)}</code>). Receipt Hash: <code>${esc(receipt.receiptHash)}</code>`;
        setTimeout(() => { alert.style.display = 'none'; }, 10000);
      }
    }

    // Forms
    const formProv = container.querySelector('#formProvision');
    if (formProv) {
      formProv.addEventListener('submit', async (e) => {
        e.preventDefault();
        const payload = {
          tenantId: formProv.querySelector('#provTenantId').value.trim(),
          maxConcurrentJobs: parseInt(formProv.querySelector('#provMaxJobs').value, 10),
          maxStorageMb: parseInt(formProv.querySelector('#provStorageMb').value, 10),
          platformOperator: formProv.querySelector('#provOperator').value.trim(),
          authorizationReference: formProv.querySelector('#provAuthRef').value.trim(),
          reason: formProv.querySelector('#provReason').value.trim()
        };

        try {
          const res = await fetch('/api/platform/control-plane/tenants/provision', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.ok) {
            const receipt = await res.json();
            hideModals();
            showReceipt(receipt);
            if (onReload) onReload();
          } else {
            const err = await res.json();
            alert('Provisioning failed: ' + (err.error || res.statusText));
          }
        } catch (err) {
          alert('Network error: ' + err.message);
        }
      });
    }

    const formQuotas = container.querySelector('#formQuotas');
    if (formQuotas) {
      formQuotas.addEventListener('submit', async (e) => {
        e.preventDefault();
        const tenantId = formQuotas.querySelector('#quotaTenantId').value;
        const payload = {
          maxConcurrentJobs: parseInt(formQuotas.querySelector('#quotaMaxJobs').value, 10),
          maxStorageMb: parseInt(formQuotas.querySelector('#quotaStorageMb').value, 10),
          platformOperator: formQuotas.querySelector('#quotaOperator').value.trim(),
          authorizationReference: formQuotas.querySelector('#quotaAuthRef').value.trim(),
          reason: formQuotas.querySelector('#quotaReason').value.trim()
        };

        try {
          const res = await fetch(`/api/platform/control-plane/tenants/${encodeURIComponent(tenantId)}/quotas`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.ok) {
            const receipt = await res.json();
            hideModals();
            showReceipt(receipt);
            if (onReload) onReload();
          } else {
            const err = await res.json();
            alert('Updating quotas failed: ' + (err.error || res.statusText));
          }
        } catch (err) {
          alert('Network error: ' + err.message);
        }
      });
    }

    const formState = container.querySelector('#formState');
    if (formState) {
      formState.addEventListener('submit', async (e) => {
        e.preventDefault();
        const tenantId = formState.querySelector('#stateTenantId').value;
        const payload = {
          state: formState.querySelector('#stateSelect').value,
          platformOperator: formState.querySelector('#stateOperator').value.trim(),
          authorizationReference: formState.querySelector('#stateAuthRef').value.trim(),
          reason: formState.querySelector('#stateReason').value.trim()
        };

        try {
          const res = await fetch(`/api/platform/control-plane/tenants/${encodeURIComponent(tenantId)}/state`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.ok) {
            const receipt = await res.json();
            hideModals();
            showReceipt(receipt);
            if (onReload) onReload();
          } else {
            const err = await res.json();
            alert('Updating state failed: ' + (err.error || res.statusText));
          }
        } catch (err) {
          alert('Network error: ' + err.message);
        }
      });
    }
  }

  // Export for Sandbox and live page
  window.ControlPlaneUI = {
    render: function (container, data, onReload) {
      renderControlPlane(container, data);
      initInteractiveControls(container, onReload);
    },
    init: initInteractiveControls
  };

  if (document.getElementById('kpiStrip')) {
    async function loadData() {
      try {
        const overview = await fetch('/api/platform/control-plane/overview').then(r => r.ok ? r.json() : null);
        if (!overview) return;
        const tenants = await fetch('/api/platform/control-plane/tenants').then(r => r.ok ? r.json() : []);
        const audit = await fetch('/api/platform/control-plane/audit').then(r => r.ok ? r.json() : []);
        renderControlPlane(document.body, { overview, tenants, audit });
      } catch (err) {
        console.warn('Control plane API offline or unauthorized.', err);
      }
    }

    initInteractiveControls(document.body, loadData);
    loadData();
  }
})();
