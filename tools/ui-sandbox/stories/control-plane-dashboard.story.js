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
    const fixtureDef = FIXTURES[fixtureId] || FIXTURES.healthy;
    // Clone fixture data so sandbox interactions mutate state locally
    const fixture = JSON.parse(JSON.stringify(fixtureDef));

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

        <!-- Tabs -->
        <div class="cp-tabs" role="tablist" style="display:flex;gap:8px;border-bottom:1px solid var(--portal-border);margin-bottom:16px;">
          <button class="cp-tab active" data-tab="tenants" style="padding:8px 16px;background:none;border:none;border-bottom:2px solid var(--portal-accent);color:var(--portal-text);font-weight:600;cursor:pointer;">Tenant Estate</button>
          <button class="cp-tab" data-tab="audit" style="padding:8px 16px;background:none;border:none;color:var(--portal-muted);font-weight:500;cursor:pointer;">Platform Audit Log</button>
          <button class="cp-tab" data-tab="fleet" style="padding:8px 16px;background:none;border:none;color:var(--portal-muted);font-weight:500;cursor:pointer;">Worker &amp; Gateway Fleet</button>
        </div>

        <!-- Receipt Alert -->
        <div id="receiptAlert" style="display:none;padding:12px 16px;margin-bottom:16px;background:rgba(16,185,129,0.1);border:1px solid #10b981;border-radius:6px;font-size:0.85rem;color:#10b981;"></div>

        <!-- Panels -->
        <div class="cp-panel active" id="panel-tenants">
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:6px;overflow:hidden;">
            <div style="padding:12px 16px;border-bottom:1px solid var(--portal-border);display:flex;justify-content:space-between;align-items:center;">
              <div>
                <span style="font-weight:600;font-size:0.95rem;">Tenant Inventory</span>
                <span style="margin-left:8px;font-size:0.8rem;color:var(--portal-muted);">Quotas, release versions &amp; compute load</span>
              </div>
              <div style="display:flex;gap:10px;align-items:center;">
                <input type="search" id="tenantSearch" placeholder="Filter tenants..." style="padding:6px 12px;border:1px solid var(--portal-border);border-radius:4px;font-size:0.85rem;background:var(--portal-bg);color:var(--portal-text);width:200px;">
                <button id="btnOpenProvision" style="padding:6px 14px;background:var(--portal-accent);color:#fff;border:none;border-radius:4px;font-size:0.85rem;font-weight:600;cursor:pointer;">+ Provision Tenant</button>
              </div>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:0.85rem;" id="tenantTable">
              <thead>
                <tr style="text-align:left;background:var(--portal-surface-subtle);border-bottom:1px solid var(--portal-border);">
                  <th style="padding:10px 14px;">Tenant ID</th>
                  <th style="padding:10px 14px;">State</th>
                  <th style="padding:10px 14px;">Release</th>
                  <th style="padding:10px 14px;">Concurrent Jobs</th>
                  <th style="padding:10px 14px;">Storage</th>
                  <th style="padding:10px 14px;">Workload</th>
                  <th style="padding:10px 14px;">Gateways</th>
                  <th style="padding:10px 14px;">Quota Load</th>
                  <th style="padding:10px 14px;text-align:right;">Actions</th>
                </tr>
              </thead>
              <tbody id="tenantTableBody"></tbody>
            </table>
          </div>
        </div>

        <div class="cp-panel" id="panel-audit" style="display:none;">
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:6px;overflow:hidden;">
            <div style="padding:12px 16px;border-bottom:1px solid var(--portal-border);font-weight:600;font-size:0.95rem;">
              Platform Lifecycle Audit Receipts
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:0.85rem;" id="auditTable">
              <thead>
                <tr style="text-align:left;background:var(--portal-surface-subtle);border-bottom:1px solid var(--portal-border);">
                  <th style="padding:10px 14px;">Operation ID</th>
                  <th style="padding:10px 14px;">Tenant</th>
                  <th style="padding:10px 14px;">Kind</th>
                  <th style="padding:10px 14px;">Status</th>
                  <th style="padding:10px 14px;">Operator</th>
                  <th style="padding:10px 14px;">Auth Ref</th>
                  <th style="padding:10px 14px;">Reason</th>
                  <th style="padding:10px 14px;">Receipt Hash</th>
                </tr>
              </thead>
              <tbody id="auditTableBody"></tbody>
            </table>
          </div>
        </div>

        <div class="cp-panel" id="panel-fleet" style="display:none;">
          <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:6px;padding:20px;">
            <h3 style="margin:0 0 16px;font-size:1.05rem;">Worker Pool &amp; Gateway Capacity</h3>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:20px;">
              <div style="background:var(--portal-surface-subtle);padding:16px;border-radius:6px;border:1px solid var(--portal-border);">
                <h4 style="margin:0 0 10px;font-size:0.95rem;">Compute &amp; Sandboxing</h4>
                <ul style="margin:0;padding-left:20px;font-size:0.85rem;color:var(--portal-text-soft);line-height:1.6;">
                  <li>Isolation Profile: <strong>Hardened OCI Sandbox (runc)</strong></li>
                  <li>Mount Policy: <strong>Per-Attempt Scratch Mounts (No cross-tenant residue)</strong></li>
                  <li>Process Trimming: <strong>Lean Evaluator Profile</strong></li>
                  <li>Noisy Neighbor Protection: <strong>Strict Fair-Share Fifo Queues</strong></li>
                </ul>
              </div>
              <div style="background:var(--portal-surface-subtle);padding:16px;border-radius:6px;border:1px solid var(--portal-border);">
                <h4 style="margin:0 0 10px;font-size:0.95rem;">Gateway Broker Posture</h4>
                <ul style="margin:0;padding-left:20px;font-size:0.85rem;color:var(--portal-text-soft);line-height:1.6;">
                  <li>Protocol: <strong>Reverse WebSocket (Strict Egress Fencing)</strong></li>
                  <li>Credentials: <strong>One-Time Consumable Tokens + Thumbprint Binding</strong></li>
                  <li>Frame Traffic Metering: <strong>Attributed into ITenantMeteringLedger</strong></li>
                  <li>Replay Protection: <strong>Frame-Level Operation Bounds</strong></li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Modal: Provision Tenant -->
      <div id="modalProvision" style="display:none;position:fixed;inset:0;background:rgba(0,0,0,0.6);z-index:9999;align-items:center;justify-content:center;">
        <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:8px;width:100%;max-width:480px;padding:24px;box-shadow:0 12px 32px rgba(0,0,0,0.4);">
          <h3 style="margin:0 0 16px;font-size:1.15rem;">Provision New Tenant</h3>
          <form id="formProvision" style="display:flex;flex-direction:column;gap:12px;">
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Tenant ID *</label>
              <input type="text" id="provTenantId" required placeholder="e.g. tenant-acme" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;">
              <div>
                <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Max Jobs</label>
                <input type="number" id="provMaxJobs" value="10" min="1" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
              </div>
              <div>
                <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Storage (MB)</label>
                <input type="number" id="provStorageMb" value="20480" min="512" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
              </div>
            </div>
            <div style="border-top:1px solid var(--portal-border);margin:4px 0;padding-top:8px;">
              <span style="font-size:0.75rem;font-weight:600;color:var(--portal-accent);text-transform:uppercase;">Attributed Operator Authority</span>
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Operator Identity *</label>
              <input type="text" id="provOperator" required value="operator@platform.internal" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Authorization Reference *</label>
              <input type="text" id="provAuthRef" required value="CHG-2026-0819" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Reason *</label>
              <input type="text" id="provReason" required value="New enterprise tenant onboarding" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:8px;">
              <button type="button" class="btn-cancel" style="padding:8px 16px;border:1px solid var(--portal-border);border-radius:4px;background:transparent;color:var(--portal-text);cursor:pointer;">Cancel</button>
              <button type="submit" style="padding:8px 16px;border:none;border-radius:4px;background:var(--portal-accent);color:#fff;font-weight:600;cursor:pointer;">Provision</button>
            </div>
          </form>
        </div>
      </div>

      <!-- Modal: Edit Quotas -->
      <div id="modalQuotas" style="display:none;position:fixed;inset:0;background:rgba(0,0,0,0.6);z-index:9999;align-items:center;justify-content:center;">
        <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:8px;width:100%;max-width:440px;padding:24px;box-shadow:0 12px 32px rgba(0,0,0,0.4);">
          <h3 style="margin:0 0 16px;font-size:1.15rem;">Update Tenant Quotas</h3>
          <form id="formQuotas" style="display:flex;flex-direction:column;gap:12px;">
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Tenant ID</label>
              <input type="text" id="quotaTenantId" readonly style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-surface-subtle);color:var(--portal-muted);font-family:monospace;">
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;">
              <div>
                <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Concurrent Jobs</label>
                <input type="number" id="quotaMaxJobs" min="1" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
              </div>
              <div>
                <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Storage (MB)</label>
                <input type="number" id="quotaStorageMb" min="512" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
              </div>
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Operator Identity *</label>
              <input type="text" id="quotaOperator" required value="operator@platform.internal" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Authorization Reference *</label>
              <input type="text" id="quotaAuthRef" required value="CHG-2026-QUOTA" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Reason *</label>
              <input type="text" id="quotaReason" required value="Quota adjustment for customer growth" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:8px;">
              <button type="button" class="btn-cancel" style="padding:8px 16px;border:1px solid var(--portal-border);border-radius:4px;background:transparent;color:var(--portal-text);cursor:pointer;">Cancel</button>
              <button type="submit" style="padding:8px 16px;border:none;border-radius:4px;background:var(--portal-accent);color:#fff;font-weight:600;cursor:pointer;">Save Quotas</button>
            </div>
          </form>
        </div>
      </div>

      <!-- Modal: Change State -->
      <div id="modalState" style="display:none;position:fixed;inset:0;background:rgba(0,0,0,0.6);z-index:9999;align-items:center;justify-content:center;">
        <div style="background:var(--portal-surface);border:1px solid var(--portal-border);border-radius:8px;width:100%;max-width:440px;padding:24px;box-shadow:0 12px 32px rgba(0,0,0,0.4);">
          <h3 style="margin:0 0 16px;font-size:1.15rem;">Set Tenant Operational State</h3>
          <form id="formState" style="display:flex;flex-direction:column;gap:12px;">
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Tenant ID</label>
              <input type="text" id="stateTenantId" readonly style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-surface-subtle);color:var(--portal-muted);font-family:monospace;">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Target State</label>
              <select id="stateSelect" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
                <option value="Active">Active</option>
                <option value="Maintenance">Maintenance</option>
                <option value="Quarantined">Quarantined</option>
              </select>
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Operator Identity *</label>
              <input type="text" id="stateOperator" required value="operator@platform.internal" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Authorization Reference *</label>
              <input type="text" id="stateAuthRef" required value="INC-2026-STATE" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div>
              <label style="display:block;font-size:0.8rem;margin-bottom:4px;color:var(--portal-muted);">Reason *</label>
              <input type="text" id="stateReason" required value="Scheduled operational adjustment" style="width:100%;padding:8px 10px;border:1px solid var(--portal-border);border-radius:4px;background:var(--portal-bg);color:var(--portal-text);">
            </div>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:8px;">
              <button type="button" class="btn-cancel" style="padding:8px 16px;border:1px solid var(--portal-border);border-radius:4px;background:transparent;color:var(--portal-text);cursor:pointer;">Cancel</button>
              <button type="submit" style="padding:8px 16px;border:none;border-radius:4px;background:var(--portal-accent);color:#fff;font-weight:600;cursor:pointer;">Update State</button>
            </div>
          </form>
        </div>
      </div>
    `;

    function renderUI() {
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
        tenantTbody.innerHTML = '<tr><td colspan="9" style="text-align:center;padding:16px;color:var(--portal-muted);">No tenants in fleet.</td></tr>';
      } else {
        tenantTbody.innerHTML = tenants.map(t => `
          <tr style="border-bottom:1px solid var(--portal-border-soft);">
            <td style="padding:10px 14px;font-weight:600;font-family:monospace;">${t.tenantId}</td>
            <td style="padding:10px 14px;"><span style="padding:2px 6px;border-radius:4px;font-size:0.75rem;background:var(--portal-accent-soft);color:var(--portal-accent);">${t.state}</span></td>
            <td style="padding:10px 14px;">${t.activeRelease || 'v0.18.0'}</td>
            <td style="padding:10px 14px;">${t.maxConcurrentJobs} max</td>
            <td style="padding:10px 14px;">${t.maxStorageMb} MB</td>
            <td style="padding:10px 14px;">${t.activeExecutions} act / ${t.queuedExecutions} q</td>
            <td style="padding:10px 14px;">${t.connectedGateways}</td>
            <td style="padding:10px 14px;">
              <div style="display:flex;align-items:center;gap:8px;">
                <div style="flex:1;background:var(--portal-border);height:6px;border-radius:3px;overflow:hidden;min-width:50px;">
                  <div style="width:${Math.min(100, t.quotaUtilizationPercentage || 0)}%;background:${(t.quotaUtilizationPercentage || 0) >= 90 ? 'var(--portal-danger)' : 'var(--portal-accent)'};height:100%;"></div>
                </div>
                <span style="font-size:0.75rem;color:var(--portal-muted);">${t.quotaUtilizationPercentage || 0}%</span>
              </div>
            </td>
            <td style="padding:10px 14px;text-align:right;">
              <button class="btn-quota" data-tenant="${t.tenantId}" data-jobs="${t.maxConcurrentJobs}" data-storage="${t.maxStorageMb}" style="padding:4px 8px;font-size:0.75rem;border:1px solid var(--portal-border);border-radius:3px;background:var(--portal-surface-subtle);color:var(--portal-text);cursor:pointer;margin-right:4px;">Quotas</button>
              <button class="btn-state" data-tenant="${t.tenantId}" data-state="${t.state}" style="padding:4px 8px;font-size:0.75rem;border:1px solid var(--portal-border);border-radius:3px;background:var(--portal-surface-subtle);color:var(--portal-text);cursor:pointer;">State</button>
            </td>
          </tr>
        `).join('');
      }

      const auditTbody = stage.querySelector('#auditTableBody');
      if (audit.length === 0) {
        auditTbody.innerHTML = '<tr><td colspan="8" style="text-align:center;padding:16px;color:var(--portal-muted);">No platform audit events.</td></tr>';
      } else {
        auditTbody.innerHTML = audit.map(a => `
          <tr style="border-bottom:1px solid var(--portal-border-soft);">
            <td style="padding:10px 14px;font-family:monospace;font-size:0.8rem;">${a.operationId}</td>
            <td style="padding:10px 14px;font-weight:600;">${a.tenantId}</td>
            <td style="padding:10px 14px;">${a.kind}</td>
            <td style="padding:10px 14px;">${a.status}</td>
            <td style="padding:10px 14px;">${a.platformOperator}</td>
            <td style="padding:10px 14px;font-family:monospace;">${a.authorizationReference}</td>
            <td style="padding:10px 14px;color:var(--portal-muted);">${a.reason}</td>
            <td style="padding:10px 14px;font-family:monospace;font-size:0.75rem;">${a.receiptHash}</td>
          </tr>
        `).join('');
      }

      ctx?.stat?.(`${tenants.length} tenants, ${audit.length} audit receipts`);
    }

    // Interactive tabs
    const tabs = stage.querySelectorAll('.cp-tab');
    tabs.forEach(tab => {
      tab.addEventListener('click', () => {
        tabs.forEach(t => {
          t.classList.remove('active');
          t.style.borderBottom = 'none';
          t.style.color = 'var(--portal-muted)';
        });
        tab.classList.add('active');
        tab.style.borderBottom = '2px solid var(--portal-accent)';
        tab.style.color = 'var(--portal-text)';

        const target = tab.dataset.tab;
        stage.querySelectorAll('.cp-panel').forEach(p => p.style.display = 'none');
        const panel = stage.querySelector(`#panel-${target}`);
        if (panel) panel.style.display = 'block';
      });
    });

    // Search filter
    const searchInput = stage.querySelector('#tenantSearch');
    if (searchInput) {
      searchInput.addEventListener('input', (e) => {
        const query = e.target.value.toLowerCase();
        const rows = stage.querySelectorAll('#tenantTableBody tr');
        rows.forEach(row => {
          const text = row.textContent.toLowerCase();
          row.style.display = text.includes(query) ? '' : 'none';
        });
      });
    }

    // Modal controls
    function showModal(id) {
      const el = stage.querySelector(id);
      if (el) el.style.display = 'flex';
    }
    function hideModals() {
      stage.querySelectorAll('#modalProvision, #modalQuotas, #modalState').forEach(m => m.style.display = 'none');
    }
    stage.querySelectorAll('.btn-cancel').forEach(btn => btn.addEventListener('click', hideModals));

    stage.querySelector('#btnOpenProvision')?.addEventListener('click', () => showModal('#modalProvision'));

    stage.querySelector('#tenantTableBody')?.addEventListener('click', (e) => {
      const target = e.target;
      if (target.classList.contains('btn-quota')) {
        stage.querySelector('#quotaTenantId').value = target.dataset.tenant;
        stage.querySelector('#quotaMaxJobs').value = target.dataset.jobs;
        stage.querySelector('#quotaStorageMb').value = target.dataset.storage;
        showModal('#modalQuotas');
      } else if (target.classList.contains('btn-state')) {
        stage.querySelector('#stateTenantId').value = target.dataset.tenant;
        stage.querySelector('#stateSelect').value = target.dataset.state;
        showModal('#modalState');
      }
    });

    function showReceipt(msg) {
      const alert = stage.querySelector('#receiptAlert');
      if (alert) {
        alert.style.display = 'block';
        alert.innerHTML = msg;
        setTimeout(() => { alert.style.display = 'none'; }, 8000);
      }
    }

    // Mock handlers in sandbox
    stage.querySelector('#formProvision')?.addEventListener('submit', (e) => {
      e.preventDefault();
      const tid = stage.querySelector('#provTenantId').value.trim();
      const jobs = parseInt(stage.querySelector('#provMaxJobs').value, 10);
      const storage = parseInt(stage.querySelector('#provStorageMb').value, 10);
      const op = stage.querySelector('#provOperator').value.trim();
      const ref = stage.querySelector('#provAuthRef').value.trim();
      const reason = stage.querySelector('#provReason').value.trim();

      const opId = `op-prov-${Math.random().toString(36).substring(2, 8)}`;
      const hash = Math.random().toString(16).substring(2, 18);

      fixture.data.tenants.push({
        tenantId: tid,
        state: 'Active',
        activeRelease: 'v0.18.0',
        maxConcurrentJobs: jobs,
        maxStorageMb: storage,
        maxReportSessions: 50,
        activeExecutions: 0,
        queuedExecutions: 0,
        connectedGateways: 0,
        quotaUtilizationPercentage: 0
      });
      fixture.data.overview.totalTenants++;
      fixture.data.overview.activeTenants++;

      fixture.data.audit.unshift({
        operationId: opId,
        tenantId: tid,
        kind: 'Provision',
        status: 'Completed',
        platformOperator: op,
        authorizationReference: ref,
        reason: reason,
        receiptHash: hash
      });

      hideModals();
      renderUI();
      showReceipt(`<strong>Receipt ${opId}:</strong> Successfully provisioned tenant <code>${tid}</code>. Hash: <code>${hash}</code>`);
    });

    stage.querySelector('#formQuotas')?.addEventListener('submit', (e) => {
      e.preventDefault();
      const tid = stage.querySelector('#quotaTenantId').value;
      const jobs = parseInt(stage.querySelector('#quotaMaxJobs').value, 10);
      const storage = parseInt(stage.querySelector('#quotaStorageMb').value, 10);
      const op = stage.querySelector('#quotaOperator').value.trim();
      const ref = stage.querySelector('#quotaAuthRef').value.trim();
      const reason = stage.querySelector('#quotaReason').value.trim();

      const t = fixture.data.tenants.find(x => x.tenantId === tid);
      if (t) {
        t.maxConcurrentJobs = jobs;
        t.maxStorageMb = storage;
        t.quotaUtilizationPercentage = Math.round((t.activeExecutions + t.queuedExecutions) / jobs * 100);
      }

      const opId = `op-quota-${Math.random().toString(36).substring(2, 8)}`;
      const hash = Math.random().toString(16).substring(2, 18);

      fixture.data.audit.unshift({
        operationId: opId,
        tenantId: tid,
        kind: 'UpdateQuotas',
        status: 'Completed',
        platformOperator: op,
        authorizationReference: ref,
        reason: reason,
        receiptHash: hash
      });

      hideModals();
      renderUI();
      showReceipt(`<strong>Receipt ${opId}:</strong> Updated quotas for <code>${tid}</code> (${jobs} jobs, ${storage} MB). Hash: <code>${hash}</code>`);
    });

    stage.querySelector('#formState')?.addEventListener('submit', (e) => {
      e.preventDefault();
      const tid = stage.querySelector('#stateTenantId').value;
      const state = stage.querySelector('#stateSelect').value;
      const op = stage.querySelector('#stateOperator').value.trim();
      const ref = stage.querySelector('#stateAuthRef').value.trim();
      const reason = stage.querySelector('#stateReason').value.trim();

      const t = fixture.data.tenants.find(x => x.tenantId === tid);
      if (t) {
        t.state = state;
      }

      const opId = `op-state-${Math.random().toString(36).substring(2, 8)}`;
      const hash = Math.random().toString(16).substring(2, 18);

      fixture.data.audit.unshift({
        operationId: opId,
        tenantId: tid,
        kind: `SetState:${state}`,
        status: 'Completed',
        platformOperator: op,
        authorizationReference: ref,
        reason: reason,
        receiptHash: hash
      });

      hideModals();
      renderUI();
      showReceipt(`<strong>Receipt ${opId}:</strong> Changed state for <code>${tid}</code> to <code>${state}</code>. Hash: <code>${hash}</code>`);
    });

    renderUI();
  }
};
