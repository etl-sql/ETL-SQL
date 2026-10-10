export function escapeControlPlane(value: string | number): string {
  return String(value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export function renderControlPlaneOverview(container: HTMLElement, overview: ControlPlaneFleetOverview | null): void {
  const values: Record<string, string> = overview ? {
    kpiTenants: `${overview.activeTenants} / ${overview.totalTenants}`,
    kpiTenantsSub: `${overview.provisioningTenants} provisioning, ${overview.maintenanceTenants} maint`,
    kpiExecutions: String(overview.activeExecutions), kpiQueueDepth: `Queue depth: ${overview.queuedExecutions}`,
    kpiGateways: String(overview.connectedGateways), kpiGatewayTenants: `Across ${overview.uniqueGatewayTenants} tenants`,
    kpiAuditOutbox: overview.auditOutboxFailed > 0 ? `${overview.auditOutboxFailed} Failed` : 'Healthy',
    kpiAuditPending: `${overview.auditOutboxPending} pending msgs`
  } : { kpiTenants: 'Unavailable', kpiTenantsSub: 'Fleet counts unavailable', kpiExecutions: 'Unavailable',
    kpiQueueDepth: 'Queue depth unavailable', kpiGateways: 'Unavailable', kpiGatewayTenants: 'Tenant count unavailable',
    kpiAuditOutbox: 'Unavailable', kpiAuditPending: 'Outbox status unavailable' };
  for (const [id, value] of Object.entries(values)) {
    const element = container.querySelector('#' + id);
    if (element) element.textContent = value;
  }
}

function stateBadge(state: string): string {
  const cls = state.toLowerCase() === 'active' ? 'active' : state.toLowerCase() === 'maintenance' ? 'maintenance'
    : ['quarantined', 'deleting', 'deleted'].includes(state.toLowerCase()) ? 'quarantined' : 'provisioning';
  return `<span class="cp-state-badge cp-state-${cls}">${escapeControlPlane(state)}</span>`;
}
function tableMessage(container: HTMLElement, id: string, columns: number, message: string): void {
  const body = container.querySelector('#' + id);
  if (body) body.innerHTML = `<tr><td colspan="${columns}" style="text-align:center;padding:24px;color:var(--portal-muted)">${escapeControlPlane(message)}</td></tr>`;
}
export function renderControlPlaneTenants(container: HTMLElement, tenants: ControlPlaneTenantDto[] | null,
  query = '', unavailable = 'Tenant inventory unavailable.'): void {
  if (!tenants) return tableMessage(container, 'tenantTableBody', 9, unavailable);
  const matches = tenants.filter(tenant => [tenant.tenantId, tenant.state, tenant.activeRelease]
    .some(value => value.toLowerCase().includes(query.toLowerCase())));
  if (!matches.length) return tableMessage(container, 'tenantTableBody', 9,
    tenants.length ? 'No tenants match this filter.' : 'No tenants found in shared fleet.');
  const body = container.querySelector('#tenantTableBody');
  if (body) body.innerHTML = matches.map(tenant => `
    <tr data-tenant="${escapeControlPlane(tenant.tenantId)}">
      <td style="font-weight:600;font-family:monospace">${escapeControlPlane(tenant.tenantId)}</td>
      <td>${stateBadge(tenant.state)}</td><td>${escapeControlPlane(tenant.activeRelease || 'default')}</td>
      <td>${tenant.maxConcurrentJobs} max</td><td>${tenant.maxStorageMb} MB</td>
      <td>${tenant.activeExecutions} active / ${tenant.queuedExecutions} queued</td><td>${tenant.connectedGateways}</td>
      <td><div style="display:flex;align-items:center;gap:8px">
        <div style="flex:1;background:var(--portal-border);height:6px;border-radius:3px;overflow:hidden;min-width:60px">
          <div style="width:${Math.min(100, tenant.quotaUtilizationPercentage)}%;background:${tenant.quotaUtilizationPercentage >= 90 ? 'var(--portal-danger)' : 'var(--portal-accent)'};height:100%"></div>
        </div><span style="font-size:0.75rem;color:var(--portal-muted)">${escapeControlPlane(tenant.quotaUtilizationPercentage)}%</span>
      </div></td>
      <td style="text-align:right;white-space:nowrap">
        <button type="button" class="btn-quota" data-tenant="${escapeControlPlane(tenant.tenantId)}">Quotas</button>
        <button type="button" class="btn-state" data-tenant="${escapeControlPlane(tenant.tenantId)}">State</button>
      </td>
    </tr>`).join('');
}
export function renderControlPlaneAudit(container: HTMLElement, audit: PlatformAuditReceiptDto[] | null,
  unavailable = 'Platform audit trail unavailable.'): void {
  if (!audit || !audit.length) return tableMessage(container, 'auditTableBody', 8,
    audit ? 'No platform audit events recorded.' : unavailable);
  const body = container.querySelector('#auditTableBody');
  if (body) body.innerHTML = audit.map(receipt => `<tr>
    <td style="font-family:monospace">${escapeControlPlane(receipt.operationId)}</td>
    <td>${escapeControlPlane(receipt.tenantId)}</td><td>${escapeControlPlane(receipt.kind)}</td>
    <td>${escapeControlPlane(receipt.status)}</td><td>${escapeControlPlane(receipt.platformOperator)}</td>
    <td>${escapeControlPlane(receipt.authorizationReference)}</td><td>${escapeControlPlane(receipt.reason)}</td>
    <td style="font-family:monospace">${escapeControlPlane(receipt.receiptHash)}</td></tr>`).join('');
}
