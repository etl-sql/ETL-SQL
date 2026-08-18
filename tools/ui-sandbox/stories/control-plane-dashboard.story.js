// Control Plane Dashboard UI Story (SaaS Platform Admin)

const FIXTURES = {
  healthy: {
    label: 'Healthy Multi-Tenant Fleet',
    note: 'Balanced load across 12 tenants, 8 worker nodes, 15 connected Gateways, 0 noisy neighbors.',
    data: {
      overview: {
        totalTenants: 12,
        activeTenants: 10,
        provisioningTenants: 1,
        maintenanceTenants: 1,
        quarantinedTenants: 0,
        deletingTenants: 0,
        activeExecutions: 6,
        queuedExecutions: 2,
        connectedGateways: 15,
        uniqueGatewayTenants: 9,
        auditOutboxPending: 0,
        auditOutboxFailed: 0,
        environment: 'production-saas'
      },
      tenants: [
        {
          tenantId: 'acme-corp',
          state: 'Active',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 10,
          maxStorageMb: 20480,
          maxReportSessions: 50,
          activeExecutions: 2,
          queuedExecutions: 0,
          connectedGateways: 3,
          quotaUtilizationPercentage: 20.0
        },
        {
          tenantId: 'globex-inc',
          state: 'Active',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 20,
          maxStorageMb: 51200,
          maxReportSessions: 100,
          activeExecutions: 3,
          queuedExecutions: 1,
          connectedGateways: 5,
          quotaUtilizationPercentage: 20.0
        },
        {
          tenantId: 'initech-ops',
          state: 'Provisioning',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 5,
          maxStorageMb: 10240,
          maxReportSessions: 20,
          activeExecutions: 0,
          queuedExecutions: 0,
          connectedGateways: 1,
          quotaUtilizationPercentage: 0.0
        },
        {
          tenantId: 'umbrella-pharma',
          state: 'Maintenance',
          activeRelease: 'v0.17.9',
          maxConcurrentJobs: 15,
          maxStorageMb: 40960,
          maxReportSessions: 60,
          activeExecutions: 1,
          queuedExecutions: 1,
          connectedGateways: 4,
          quotaUtilizationPercentage: 13.3
        }
      ],
      audit: [
        {
          operationId: 'op-9102-prov',
          tenantId: 'initech-ops',
          kind: 'Provision',
          status: 'Completed',
          phase: 'Activated',
          platformOperator: 'provisioner@platform.test',
          authorizationReference: 'CHG-2026-0819',
          reason: 'New enterprise tenant onboarding',
          receiptHash: 'a8f103b2c9d4e5f6'
        },
        {
          operationId: 'op-8841-upgr',
          tenantId: 'acme-corp',
          kind: 'Upgrade',
          status: 'Completed',
          phase: 'Activated',
          platformOperator: 'release-eng@platform.test',
          authorizationReference: 'REL-0180-ROLLOUT',
          reason: 'Canary rollout v0.18.0',
          receiptHash: '7c390b1e4a5d8f22'
        }
      ]
    }
  },
  noisyNeighbor: {
    label: 'Noisy Neighbor / Quota Headroom Contained',
    note: 'Tenant umbrella-pharma at 100% capacity; fair-share queueing isolates impact from other tenants.',
    data: {
      overview: {
        totalTenants: 4,
        activeTenants: 4,
        provisioningTenants: 0,
        maintenanceTenants: 0,
        quarantinedTenants: 0,
        deletingTenants: 0,
        activeExecutions: 18,
        queuedExecutions: 24,
        connectedGateways: 8,
        uniqueGatewayTenants: 4,
        auditOutboxPending: 4,
        auditOutboxFailed: 0,
        environment: 'production-saas'
      },
      tenants: [
        {
          tenantId: 'umbrella-pharma',
          state: 'Active',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 15,
          maxStorageMb: 40960,
          maxReportSessions: 60,
          activeExecutions: 15,
          queuedExecutions: 22,
          connectedGateways: 4,
          quotaUtilizationPercentage: 100.0
        },
        {
          tenantId: 'acme-corp',
          state: 'Active',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 10,
          maxStorageMb: 20480,
          maxReportSessions: 50,
          activeExecutions: 2,
          queuedExecutions: 1,
          connectedGateways: 2,
          quotaUtilizationPercentage: 30.0
        },
        {
          tenantId: 'globex-inc',
          state: 'Active',
          activeRelease: 'v0.18.0',
          maxConcurrentJobs: 20,
          maxStorageMb: 51200,
          maxReportSessions: 100,
          activeExecutions: 1,
          queuedExecutions: 1,
          connectedGateways: 2,
          quotaUtilizationPercentage: 10.0
        }
      ],
      audit: [
        {
          operationId: 'op-9201-limit',
          tenantId: 'umbrella-pharma',
          kind: 'QuotaAdjust',
          status: 'Completed',
          phase: 'Enforced',
          platformOperator: 'ops-lead@platform.test',
          authorizationReference: 'INC-8890-NOISY',
          reason: 'Temporary cap to prevent burst abuse',
          receiptHash: 'f491c20e7a83b194'
        }
      ]
    }
  },
  empty: {
    label: 'Empty Fleet (Bootstrap)',
    note: 'Initial empty cluster state before tenant provisioning.',
    data: {
      overview: {
        totalTenants: 0,
        activeTenants: 0,
        provisioningTenants: 0,
        maintenanceTenants: 0,
        quarantinedTenants: 0,
        deletingTenants: 0,
        activeExecutions: 0,
        queuedExecutions: 0,
        connectedGateways: 0,
        uniqueGatewayTenants: 0,
        auditOutboxPending: 0,
        auditOutboxFailed: 0,
        environment: 'bootstrap'
      },
      tenants: [],
      audit: []
    }
  }
};

export default {
  id: 'control-plane-dashboard',
  title: 'Control Plane — SaaS Platform Admin',
  category: 'Platform',
  fixtures: Object.entries(FIXTURES).map(([id, fixture]) => ({ id, label: fixture.label })),
  async mount(stage, fixtureId, ctx) {
    const fixture = FIXTURES[fixtureId] || FIXTURES.healthy;

    stage.innerHTML = `
      <div style="font-family:var(--portal-font, system-ui, sans-serif);max-width:1200px;margin:0 auto;padding:16px;">
        <div style="margin-bottom:16px;">
          <h2 style="margin:0 0 4px;font-size:1.2rem;">${fixture.label}</h2>
          <p style="margin:0 0 12px;font-size:0.85rem;color:var(--portal-muted);">${fixture.note}</p>
        </div>

        <div style="background:#0f172a;border-radius:6px;border-left:4px solid #3b82f6;padding:10px 14px;margin-bottom:16px;color:#94a3b8;font-size:0.8rem;">
          <strong style="color:#f1f5f9;">Platform Identity Isolation:</strong> Only platform operators with <code>PlatformAccessGrant</code> may access fleet telemetry. Customer scripts and data are strictly inexpressible in this UI.
        </div>

        <div class="cp-kpis" id="kpiStrip" style="display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:20px;">
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);padding:12px;border-radius:6px;">
            <div style="font-size:0.75rem;color:var(--portal-muted);text-transform:uppercase;font-weight:600;">Active Tenants</div>
            <div id="kpiTenants" style="font-size:1.5rem;font-weight:700;margin-top:2px;">-</div>
            <div id="kpiTenantsSub" style="font-size:0.7rem;color:var(--portal-muted);"></div>
          </div>
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);padding:12px;border-radius:6px;">
            <div style="font-size:0.75rem;color:var(--portal-muted);text-transform:uppercase;font-weight:600;">Active Executions</div>
            <div id="kpiExecutions" style="font-size:1.5rem;font-weight:700;margin-top:2px;">-</div>
            <div id="kpiQueueDepth" style="font-size:0.7rem;color:var(--portal-muted);"></div>
          </div>
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);padding:12px;border-radius:6px;">
            <div style="font-size:0.75rem;color:var(--portal-muted);text-transform:uppercase;font-weight:600;">Connected Gateways</div>
            <div id="kpiGateways" style="font-size:1.5rem;font-weight:700;margin-top:2px;">-</div>
            <div id="kpiGatewayTenants" style="font-size:0.7rem;color:var(--portal-muted);"></div>
          </div>
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);padding:12px;border-radius:6px;">
            <div style="font-size:0.75rem;color:var(--portal-muted);text-transform:uppercase;font-weight:600;">Audit Outbox</div>
            <div id="kpiAuditOutbox" style="font-size:1.5rem;font-weight:700;margin-top:2px;">-</div>
            <div id="kpiAuditPending" style="font-size:0.7rem;color:var(--portal-muted);"></div>
          </div>
        </div>

        <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:6px;overflow:hidden;">
          <div style="padding:12px 16px;border-bottom:1px solid var(--portal-border);font-weight:600;font-size:0.95rem;">
            Tenant Estate Inventory
          </div>
          <table style="width:100%;border-collapse:collapse;font-size:0.85rem;">
            <thead>
              <tr style="text-align:left;background:var(--portal-surface-subtle);border-bottom:1px solid var(--portal-border);">
                <th style="padding:8px 12px;">Tenant ID</th>
                <th style="padding:8px 12px;">State</th>
                <th style="padding:8px 12px;">Release</th>
                <th style="padding:8px 12px;">Job Quota</th>
                <th style="padding:8px 12px;">Storage</th>
                <th style="padding:8px 12px;">Workload</th>
                <th style="padding:8px 12px;">Gateways</th>
                <th style="padding:8px 12px;">Quota Load</th>
              </tr>
            </thead>
            <tbody id="tenantTableBody"></tbody>
          </table>
        </div>

        <div style="margin-top:20px;background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:6px;overflow:hidden;">
          <div style="padding:12px 16px;border-bottom:1px solid var(--portal-border);font-weight:600;font-size:0.95rem;">
            Platform Lifecycle Audit Receipts
          </div>
          <table style="width:100%;border-collapse:collapse;font-size:0.85rem;">
            <thead>
              <tr style="text-align:left;background:var(--portal-surface-subtle);border-bottom:1px solid var(--portal-border);">
                <th style="padding:8px 12px;">Operation ID</th>
                <th style="padding:8px 12px;">Tenant</th>
                <th style="padding:8px 12px;">Kind</th>
                <th style="padding:8px 12px;">Status</th>
                <th style="padding:8px 12px;">Operator</th>
                <th style="padding:8px 12px;">Auth Ref</th>
                <th style="padding:8px 12px;">Reason</th>
                <th style="padding:8px 12px;">Receipt Hash</th>
              </tr>
            </thead>
            <tbody id="auditTableBody"></tbody>
          </table>
        </div>
      </div>
    `;

    // Render using ControlPlaneUI logic
    const { overview, tenants, audit } = fixture.data;
    stage.querySelector('#kpiTenants').textContent = `${overview.activeTenants} / ${overview.totalTenants}`;
    stage.querySelector('#kpiTenantsSub').textContent = `${overview.provisioningTenants} provisioning, ${overview.maintenanceTenants} maint`;
    stage.querySelector('#kpiExecutions').textContent = `${overview.activeExecutions}`;
    stage.querySelector('#kpiQueueDepth').textContent = `Queue depth: ${overview.queuedExecutions}`;
    stage.querySelector('#kpiGateways').textContent = `${overview.connectedGateways}`;
    stage.querySelector('#kpiGatewayTenants').textContent = `Across ${overview.uniqueGatewayTenants} tenants`;
    stage.querySelector('#kpiAuditOutbox').textContent = overview.auditOutboxFailed > 0 ? `${overview.auditOutboxFailed} Failed` : 'Healthy';
    stage.querySelector('#kpiAuditPending').textContent = `${overview.auditOutboxPending} pending msgs`;

    const tenantTbody = stage.querySelector('#tenantTableBody');
    if (tenants.length === 0) {
      tenantTbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:16px;color:var(--portal-muted);">No tenants in fleet.</td></tr>';
    } else {
      tenantTbody.innerHTML = tenants.map(t => `
        <tr style="border-bottom:1px solid var(--portal-border-soft);">
          <td style="padding:8px 12px;font-weight:600;font-family:monospace;">${t.tenantId}</td>
          <td style="padding:8px 12px;"><span style="padding:2px 6px;border-radius:4px;font-size:0.75rem;background:var(--portal-accent-soft);color:var(--portal-accent);">${t.state}</span></td>
          <td style="padding:8px 12px;">${t.activeRelease}</td>
          <td style="padding:8px 12px;">${t.maxConcurrentJobs}</td>
          <td style="padding:8px 12px;">${t.maxStorageMb} MB</td>
          <td style="padding:8px 12px;">${t.activeExecutions} act / ${t.queuedExecutions} q</td>
          <td style="padding:8px 12px;">${t.connectedGateways}</td>
          <td style="padding:8px 12px;">${t.quotaUtilizationPercentage}%</td>
        </tr>
      `).join('');
    }

    const auditTbody = stage.querySelector('#auditTableBody');
    if (audit.length === 0) {
      auditTbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:16px;color:var(--portal-muted);">No platform audit events.</td></tr>';
    } else {
      auditTbody.innerHTML = audit.map(a => `
        <tr style="border-bottom:1px solid var(--portal-border-soft);">
          <td style="padding:8px 12px;font-family:monospace;">${a.operationId}</td>
          <td style="padding:8px 12px;font-weight:600;">${a.tenantId}</td>
          <td style="padding:8px 12px;">${a.kind}</td>
          <td style="padding:8px 12px;">${a.status}</td>
          <td style="padding:8px 12px;">${a.platformOperator}</td>
          <td style="padding:8px 12px;font-family:monospace;">${a.authorizationReference}</td>
          <td style="padding:8px 12px;color:var(--portal-muted);">${a.reason}</td>
          <td style="padding:8px 12px;font-family:monospace;font-size:0.75rem;">${a.receiptHash}</td>
        </tr>
      `).join('');
    }

    ctx?.stat?.(`${tenants.length} tenants, ${audit.length} audit receipts`);
  }
};
