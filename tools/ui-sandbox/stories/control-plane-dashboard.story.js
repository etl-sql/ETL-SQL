// The story hosts the actual page and controller. Fixtures contain platform metadata only.
export function makeControlPlaneSandbox(fixtureId = 'healthy') {
  const now = '2026-10-07T16:00:00Z';
  const tenant = (tenantId, jobs, active, queued) => ({ tenantId, state: 'Active', activeRelease: 'v0.21.0',
    maxConcurrentJobs: jobs, maxStorageMb: 20480, maxReportSessions: 50, fenceEpoch: 1,
    createdAtUtc: now, updatedAtUtc: now, deletedAtUtc: null, activeExecutions: active,
    queuedExecutions: queued, connectedGateways: 1, quotaUtilizationPercentage: (active + queued) / jobs * 100 });
  const tenants = fixtureId === 'empty' ? [] : [tenant('acme-corp', 10, 2, 1),
    tenant('globex-inc', 20, 3, 0), tenant('umbrella-pharma', 5, 5, fixtureId === 'noisyNeighbor' ? 8 : 0)];
  const audit = [], calls = [];
  let failure = fixtureId, sequence = 0, pausedRead = false, pausedMutation = false;
  const pendingReads = [], pendingMutations = [];
  const response = (body, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' }
  });
  const receipt = (tenantId, kind, body) => ({ operationId: 'op-fixture-' + (++sequence), tenantId, kind,
    status: 'Completed', phase: 'Applied', platformOperator: body.platformOperator,
    authorizationReference: body.authorizationReference, reason: body.reason,
    authorizationExpiresUtc: '2026-10-08T16:00:00Z', targetRelease: null,
    targetMaxConcurrentJobs: body.maxConcurrentJobs ?? null, targetMaxStorageMb: body.maxStorageMb ?? null,
    targetMaxReportSessions: body.maxReportSessions ?? null, receiptHash: 'a8f103b2c9d4e5f6' });
  const overview = () => ({ totalTenants: tenants.length, activeTenants: tenants.filter(t => t.state === 'Active').length,
    provisioningTenants: 0, maintenanceTenants: tenants.filter(t => t.state === 'Maintenance').length,
    quarantinedTenants: tenants.filter(t => t.state === 'Quarantined').length, deletingTenants: 0,
    activeExecutions: tenants.reduce((n, t) => n + t.activeExecutions, 0),
    queuedExecutions: tenants.reduce((n, t) => n + t.queuedExecutions, 0), connectedGateways: tenants.length,
    uniqueGatewayTenants: tenants.length, auditOutboxPending: 0, auditOutboxFailed: 0,
    environment: 'sandbox', capturedAtUtc: now });
  const mock = {
    calls, setFailure(value) { failure = value; },
    pauseNextRead() { pausedRead = true; }, pauseNextMutation() { pausedMutation = true; },
    releaseReads() { pendingReads.splice(0).forEach(release => release()); },
    releaseMutations() { pendingMutations.splice(0).forEach(release => release()); },
    async fetch(url, init = {}) {
      const route = new URL(url, 'http://sandbox.test').pathname;
      const mutation = init.method === 'POST';
      calls.push({ route, method: init.method || 'GET', body: init.body });
      if (failure === 'unauthorized') return response({ error: 'Platform management credentials required.' }, 401);
      if (failure === 'disabled') return response(null, 404);
      if (failure === 'unavailable' && !route.endsWith('/overview')) return response(null, 503);
      let result;
      if (!mutation) {
        result = failure === 'invalid' ? response({ invalid: true }) :
          response(route.endsWith('/overview') ? overview() : route.endsWith('/tenants') ? tenants : audit);
        if (pausedRead && route.endsWith('/overview')) {
          pausedRead = false;
          await new Promise(resolve => pendingReads.push(resolve));
        }
        return result;
      }
      const body = JSON.parse(init.body);
      if (failure === 'invalidReceipt') return response({ operationId: 'untrusted' });
      let target, kind;
      if (route.endsWith('/provision')) {
        if (tenants.some(t => t.tenantId === body.tenantId)) return response({ error: 'Tenant already exists.' }, 409);
        target = tenant(body.tenantId, body.maxConcurrentJobs, 0, 0);
        target.maxStorageMb = body.maxStorageMb;
        target.maxReportSessions = body.maxReportSessions ?? 50;
        tenants.push(target); kind = 'Provision';
      } else {
        target = tenants.find(t => t.tenantId === decodeURIComponent(route.split('/').at(-2)));
        if (!target) return response({ error: 'Tenant not found.' }, 404);
        if (route.endsWith('/quotas')) {
          target.maxConcurrentJobs = body.maxConcurrentJobs;
          target.maxStorageMb = body.maxStorageMb;
          if (body.maxReportSessions != null) target.maxReportSessions = body.maxReportSessions;
          kind = 'UpdateQuotas';
        } else { target.state = body.state; kind = 'SetState:' + body.state; }
      }
      const value = receipt(target.tenantId, kind, body);
      audit.unshift(value); result = response(value);
      if (failure === 'mismatchedReceipt') result = response({ ...value, tenantId: 'another-tenant' });
      if (failure === 'mismatchedQuotas') result = response({ ...value, targetMaxConcurrentJobs: 999 });
      if (failure === 'pendingReceipt') result = response({ ...value, status: 'Pending' });
      if (pausedMutation) {
        pausedMutation = false;
        await new Promise(resolve => pendingMutations.push(resolve));
      }
      return result;
    }
  };
  return mock;
}

export default {
  id: 'control-plane-dashboard', title: 'Control Plane — SaaS Platform Admin', category: 'Control Plane & SaaS',
  fixtures: [
    { id: 'healthy', label: 'Healthy fleet' }, { id: 'noisyNeighbor', label: 'Over quota' },
    { id: 'empty', label: 'Empty fleet' }, { id: 'unavailable', label: 'Sources unavailable' },
    { id: 'invalid', label: 'Malformed responses' }, { id: 'unauthorized', label: 'Platform authority required' },
    { id: 'disabled', label: 'Shared tenancy disabled' }
  ],
  async mount(stage, fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = `Control Plane — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0';
    const page = await fetch('/src/ETL-SQL.Portal/wwwroot/control-plane.html').then(r => r.text());
    const loaded = new Promise(resolve => frame.addEventListener('load', resolve, { once: true }));
    frame.srcdoc = page.replace(/<script\b[^>]*>[\s\S]*?<\/script>/g, '')
      .replace(/(href|src)="\/(css|js)\//g, '$1="/src/ETL-SQL.Portal/wwwroot/$2/')
      .replace('</body>', `<script type="module">
        import { makeControlPlaneSandbox } from '/tools/ui-sandbox/stories/control-plane-dashboard.story.js';
        window.__cpMock = makeControlPlaneSandbox(${JSON.stringify(fixtureId)});
        window.fetch = window.__cpMock.fetch;
        await import('/src/ETL-SQL.Portal/wwwroot/js/feedback.js');
        await import('/src/ETL-SQL.Portal/wwwroot/js/control-plane.js?v=' + Date.now());
      </script></body>`);
    stage.replaceChildren(frame);
    await loaded;
    ctx.stat('Production control-plane page with fixture platform metadata');
    return { dispose() { frame.contentWindow?.ControlPlaneUI?.dispose?.(); frame.remove(); }, resize() {} };
  }
};
