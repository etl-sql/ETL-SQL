export function makeOperationsApi(fixtureId = 'attention') {
  const attention = fixtureId === 'attention', now = '2026-10-07T14:42:00Z';
  const item = { id: 7, operationId: 'op-write-7f3a', tenantId: 'tenant-a', gatewayId: 'hq-gateway',
    resourceId: 'corp-sql-sales', correlationId: 'corr-98d1', executedAtUtc: now,
    state: 'Acknowledged', priority: 'High', owner: 'database-operations', resolution: null,
    version: 2, events: [{ eventType: 'Detected', actor: 'Gateway', note: 'Ambiguous outcome.',
      evidenceReference: null, resolution: null, createdAtUtc: now }] };
  const accounts = [{ id: 'machine-1', clientId: 'client-1', name: 'Nightly reports', ownerUserId: 1,
    scopes: ['portal.read'], isEnabled: true, revokedAt: null, expiresAt: null, lastUsedAt: now, version: 1 }];
  let approvals = attention ? [{ id: 3, reportTitle: 'Revenue', requesterUserName: 'reader',
    requesterEmail: 'reader@example.test', reason: 'Monthly review', createdAt: now }] : [];
  const audit = [];
  const record = (account, action) => audit.push({ timestamp: now, username: 'admin', userId: 1,
    action, detail: account.name, resourceId: account.id });
  const accountFor = (id, version) => {
    const account = accounts.find(value => value.id === id);
    if (!account || account.version !== version) throw new Error('Account changed. Refresh and try again.');
    return account;
  };
  const changeCase = (id, body, eventType) => {
    if (id !== item.id || body.version !== item.version) throw new Error('Case changed. Refresh and try again.');
    if (eventType === 'Resolved' && !body.evidenceReference?.trim()) throw new Error('Reconciliation evidence is required.');
    if (eventType === 'Assigned') item.owner = body.owner;
    if (eventType === 'Resolved' || eventType === 'Acknowledged') item.state = eventType;
    if (eventType === 'Resolved') item.resolution = body.resolution;
    item.version++;
    item.events.push({ eventType, actor: 'admin', note: body.note, evidenceReference: body.evidenceReference,
      resolution: body.resolution, createdAtUtc: now });
    return structuredClone(item);
  };
  const api = {
    async operationalMetrics() { return {
      queuedExecutions: attention ? 12 : 0, activeExecutions: 4, executionCap: 16,
      auditOutboxPending: attention ? 18 : 0, auditOutboxFailed: 0, auditOutboxOldestPendingAgeSeconds: 540,
      windowHours: 24, recentExecutionFailures: attention ? 7 : 0, recentExecutions: 182,
      staleDatasets: attention ? 2 : 0, datasetStorageBytes: 19756849561,
      securityEventPending: 0, securityEventFailed: 0, nodeId: 'portal-chi-01', schemaUpToDate: true
    }; },
    async fleetStatus() { return { status: attention ? 'Degraded' : 'Healthy', environment: 'Test',
      storage: 'ok', inventory: { nodeId: 'portal-chi-01', installedVersion: '0.21.0',
        upgradeReadiness: { ready: true, findings: [] } } }; },
    async gatewayAmbiguousWrites() { return attention ? structuredClone([item]) : []; },
    async pendingAccessRequests() { return structuredClone(approvals); },
    async listServiceAccounts() { return structuredClone(accounts); },
    async anonymousReportAccess() { return []; },
    async listAdminServices() { return [{ name: 'failure-digest', enabled: true, intervalHours: 24,
      smtpAlias: 'operations-mail', recipients: 'ops@example.test;oncall@example.test', lastRun: { outcome: 'Sent', startedAtUtc: now,
        completedAtUtc: now, attempts: 1, nodeName: 'portal-chi-01', detail: null } }]; },
    async listUsers() { return [{ id: 1, username: 'admin', email: null, isActive: true }]; },
    async adminServiceHistory() { return [{ outcome: 'Sent', startedAtUtc: now, completedAtUtc: now,
      attempts: 1, nodeName: 'portal-chi-01', detail: null }]; },
    async auditLog(_page, _pageSize, _action, _user, _type, id) {
      const items = audit.filter(value => value.resourceId === id);
      return { items: structuredClone(items), total: items.length, page: 1, pageSize: 100 };
    },
    async createServiceAccount(body) {
      const id = 'machine-' + (accounts.length + 1);
      const account = { ...body, id, clientId: 'client-' + id, version: 1, isEnabled: true,
        revokedAt: null, lastUsedAt: null };
      accounts.push(account); record(account, 'CREATE_SERVICE_ACCOUNT');
      return { account: structuredClone(account), clientSecret: 'fixture-one-time-value' };
    },
    async rotateServiceAccount(id, version) {
      const account = accountFor(id, version); account.version++; record(account, 'ROTATE_SERVICE_ACCOUNT');
      return { account: structuredClone(account), clientSecret: 'fixture-one-time-value' };
    },
    async revokeServiceAccount(id, version) {
      const account = accountFor(id, version); account.revokedAt = now; account.version++;
      record(account, 'REVOKE_SERVICE_ACCOUNT'); return structuredClone(account);
    },
    async approveAccessRequest(id) { approvals = approvals.filter(value => value.id !== id); return {}; },
    async denyAccessRequest(id) { approvals = approvals.filter(value => value.id !== id); return {}; },
    async revokeAnonymousReportAccess() { return {}; },
    async acknowledgeGatewayAmbiguousWrite(id, body) { return changeCase(id, body, 'Acknowledged'); },
    async assignGatewayAmbiguousWrite(id, body) { return changeCase(id, body, 'Assigned'); },
    async addGatewayAmbiguousWriteEvidence(id, body) { return changeCase(id, body, 'EvidenceAdded'); },
    async resolveGatewayAmbiguousWrite(id, body) { return changeCase(id, body, 'Resolved'); }
  };
  if (fixtureId === 'unavailable') {
    const unavailable = async () => { throw new Error('Service temporarily unavailable.'); };
    api.operationalMetrics = unavailable;
    api.pendingAccessRequests = unavailable;
    api.gatewayAmbiguousWrites = unavailable;
  }
  if (fixtureId === 'invalid') {
    api.operationalMetrics = async () => ({ queuedExecutions: '0' });
    api.listServiceAccounts = async () => [{ id: 'machine-1', scopes: 'portal.read' }];
    api.gatewayAmbiguousWrites = async () => ({ items: [] });
  }
  return api;
}

export default {
  id: 'portal-operations', title: 'Portal Operations hub',
  subtitle: 'A signal-to-owner control room for administrators',
  fixtures: [
    { id: 'attention', label: 'Needs attention' }, { id: 'steady', label: 'Steady state' },
    { id: 'unavailable', label: 'Sources unavailable' }, { id: 'invalid', label: 'Malformed responses' }
  ],
  async mount(stage, fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = `Portal Operations — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0;background:white';
    const loaded = new Promise(resolve => frame.addEventListener('load', resolve, { once: true }));
    frame.srcdoc = `<!doctype html><html lang="en"><head><meta charset="utf-8">
      <link rel="stylesheet" href="/src/ETL-SQL.Portal/wwwroot/css/portal.css"></head>
      <body><main id="operations"></main><script type="module">
      import { createOperationsAdmin } from '/src/ETL-SQL.Portal/wwwroot/js/operations-admin.js';
      import { makeOperationsApi } from '/tools/ui-sandbox/stories/portal-operations.story.js';
      window.__operationsApi = makeOperationsApi(${JSON.stringify(fixtureId)});
      window.__operations = createOperationsAdmin({ host: document.getElementById('operations'), adminApi: window.__operationsApi });
      await window.__operations.load();
      </script></body></html>`;
    stage.replaceChildren(frame);
    await loaded;
    ctx.stat('Operations controller — native controls and partial-source failures');
    return { dispose() { frame.contentWindow?.__operations?.dispose(); }, resize() {} };
  }
};
