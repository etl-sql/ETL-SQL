// Run the production page against a deterministic service, including rejected and delayed writes.
export function makeOrchestratorPageSandbox(fixtureId = 'healthy') {
  let jobs = ['daily_sales', 'hourly_orders'].map((name, index) => ({
    id: `job-${index}`, name, script: 'SELECT 1;', interval: 1, unit: 'HOUR',
    isEnabled: true, maxRetries: 0, retryDelaySeconds: 30, hashPolicy: 'Warn',
    version: 3, jobType: 0, createdBy: 'user:operator', lastRun: null, nextRun: null
  }));
  let schedules = [
    { id: 'schedule-0', name: 'nightly_utc', cron: '0 2 * * *', timeZone: 'UTC', isEnabled: true,
      displayName: 'Nightly (UTC)', description: 'Warehouse refresh', createdBy: 'user:operator', version: 2 },
    { id: 'schedule-1', name: 'hourly_us', cron: '0 * * * *', timeZone: 'America/New_York', isEnabled: false,
      displayName: null, description: null, createdBy: 'user:operator', version: 1 }
  ];
  let notifications = [
    { id: 'notification-0', name: 'ops_mail', connectionName: 'smtp_ops', recipient: 'ops@example.test', isEnabled: true,
      displayName: 'Operations mail', description: null, createdBy: 'user:operator', version: 1 },
    { id: 'notification-1', name: 'oncall_chat', connectionName: 'chat_hook', recipient: null, isEnabled: true,
      displayName: null, description: null, createdBy: 'user:operator', version: 4 }
  ];
  const calls = [], pending = [], pendingReads = [];
  let failure = fixtureId, paused = false, readsPaused = false, dispatchOutcome = 'delivered';
  // The Portal relays a refused service write as a JSON string holding the service's JSON.
  const relayed = (error, status) => json(JSON.stringify({ error }), status);
  async function catalogRoute(kind, items, path, method, init) {
    const subject = kind === 'schedules' ? 'Schedules' : 'Notifications';
    const name = path.split('/')[4] ? decodeURIComponent(path.split('/')[4]) : null;
    if (method === 'GET') {
      if (readsPaused) await new Promise(resolve => pendingReads.push(resolve));
      if (failure === 'catalog-unavailable') return json({ error: `${subject} are temporarily unavailable. Please retry.` }, 503);
      if (failure === 'catalog-denied') return json({ error: `${subject} are not available to this account.` }, 403);
      if (failure === 'catalog-malformed') return json([{ name: '', isEnabled: 'yes' }]);
      return json(items);
    }
    if (paused) await new Promise(resolve => pending.push(resolve));
    if (failure === 'denied') return new Response(null, { status: 403 });
    if (failure === 'failed') return json({ error: 'Orchestrator service unavailable.' }, 503);
    if (failure === 'unexpected') return new Response(null, { status: 206 });
    if (path.endsWith('/dispatch')) {
      if (dispatchOutcome === 'skipped') return json({ notificationName: name, delivered: false, skipped: true, message: 'Notification endpoint is disabled.' }, 202);
      if (dispatchOutcome === 'failed') return json({ notificationName: name, delivered: false, skipped: false, message: 'SMTP relay refused the message.' }, 502);
      return json({ notificationName: name, delivered: true, skipped: false, message: null });
    }
    const body = typeof init.body === 'string' ? JSON.parse(init.body) : {};
    if (method === 'POST') {
      if (items.some(item => item.name === body.name)) return relayed(`'${body.name}' already exists.`, 409);
      items.push({ id: `${kind}-${items.length}`, createdBy: 'user:sandbox-admin', version: 1, recipient: null, ...body });
      return new Response(null, { status: 201 });
    }
    const item = items.find(candidate => candidate.name === name);
    if (!item) return relayed(`'${name}' not found.`, 404);
    if (method === 'DELETE') {
      if (failure === 'in-use') return relayed(`Cannot delete schedule '${name}' because it is attached to 1 job(s).`, 409);
      items.splice(items.indexOf(item), 1);
      return new Response(null, { status: 200 });
    }
    Object.assign(item, body, { version: item.version + 1 });
    return new Response(null, { status: 200 });
  }
  const json = (value, status = 200) => new Response(JSON.stringify(value), {
    status, headers: { 'Content-Type': 'application/json' }
  });
  const mock = {
    calls, setFailure(value) { failure = value; },
    pauseWrites() { paused = true; }, releaseWrites() { paused = false; pending.splice(0).forEach(resolve => resolve()); },
    get pendingWrites() { return pending.length; },
    pauseReads() { readsPaused = true; }, releaseReads() { readsPaused = false; pendingReads.splice(0).forEach(resolve => resolve()); },
    get pendingReads() { return pendingReads.length; },
    setDispatch(value) { dispatchOutcome = value; },
    async fetch(input, init = {}) {
      const url = new URL(input instanceof Request ? input.url : String(input), location.href);
      const path = url.pathname, method = init.method || 'GET';
      const headers = new Headers(init.headers);
      calls.push({ path, method, ifMatch: headers.get('If-Match'), authorization: headers.get('Authorization'),
        body: typeof init.body === 'string' ? JSON.parse(init.body) : init.body });
      if (path === '/api/portal/branding') return json({ displayName: 'ETL-SQL Portal', footerText: 'Sandbox', logoUrl: null });
      if (path === '/api/portal/navigation') return json([{ id: 'navOrchestrator', visible: true, reason: null }]);
      if (path === '/api/orchestrator/status') return json({ online: true, status: 'Online' });
      if (path === '/api/orchestrator/metrics') return json({ online: true, metrics: { activeJobs: 0, queuedJobs: 0, completedToday: 0, failedToday: 0 } });
      if (path === '/api/orchestrator/jobs' && method === 'GET') return json(jobs);
      if (path === '/api/orchestrator/jobs/activity') {
        const limit = Number(url.searchParams.get('limit')), offset = Number(url.searchParams.get('offset'));
        const matching = jobs.filter(job => job.name.includes(url.searchParams.get('search') || '')
          && (url.searchParams.get('status') === 'all' || job.isEnabled === (url.searchParams.get('status') === 'enabled')));
        return json({ asOfUtc: '2026-10-09T12:00:00Z', dayStartUtc: '2026-10-09T00:00:00Z',
          counts: { active: 0, queued: 0, completedToday: 0, failedToday: 0 },
          total: matching.length, limit, offset,
          items: matching.slice(offset, offset + limit).map(job => ({ job, runState: 'Idle', completedToday: false, failedToday: false })) });
      }
      if (path === '/api/orchestrator/triage') return json({ generatedAt: '2026-10-09T12:00:00Z', lookbackHours: 24,
        failureCount: 0, incidentCount: 0, runningCount: 0, missedCount: 0, truncated: false, incidents: [], running: [], missed: [] });
      if (/\/jobs\/[^/]+\/(history|schedules|notifications|state)$/.test(path)) return json([]);
      if (/\/authorization\/Job\//.test(path)) return json([]);
      const catalog = /^\/api\/orchestrator\/(schedules|notifications)(\/|$)/.exec(path);
      if (catalog) return catalogRoute(catalog[1], catalog[1] === 'schedules' ? schedules : notifications, path, method, init);
      if (/\/(scripts|bundles)$/.test(path) && method === 'GET') return json([]);
      if (path.startsWith('/api/orchestrator/') && method !== 'GET') {
        if (paused) await new Promise(resolve => pending.push(resolve));
        if (failure === 'denied') return json({ error: 'Job management is denied.' }, 403);
        if (failure === 'failed') return json({ error: 'Service rejected the write.' }, 503);
        if (failure === 'conflict') return json({ error: 'The resource changed after it was read. Refresh it and retry.' }, 409);
        if (failure === 'unexpected') return new Response(null, { status: 206 });
        const name = decodeURIComponent(path.split('/')[4]);
        const job = jobs.find(item => item.name === name);
        if (method === 'PUT' || method === 'DELETE') {
          if (headers.get('If-Match') !== `"${job?.version}"`) return json({ error: 'This mutation requires the current resource version in the If-Match header.' }, 428);
          if (method === 'DELETE') jobs = jobs.filter(item => item !== job);
          else { Object.assign(job, JSON.parse(init.body)); job.isEnabled = JSON.parse(init.body).isEnabled ?? JSON.parse(init.body).IsEnabled ?? job.isEnabled; job.version++; }
          return new Response(null, { status: method === 'PUT' ? 204 : 200 });
        }
        if (path.endsWith('/trigger')) return new Response(null, { status: 202 });
        return new Response(null, { status: 200 });
      }
      return json({ error: `Unexpected sandbox route: ${method} ${path}` }, 404);
    }
  };
  return mock;
}

export async function mountOrchestratorPageFrame(fixtureId) {
  const html = await fetch('/src/ETL-SQL.Portal/wwwroot/orchestrator.html').then(response => response.text());
  document.documentElement.innerHTML = html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/g, '')
    .replace(/(href|src)="\/(css|js|designer)\//g, '$1="/src/ETL-SQL.Portal/wwwroot/$2/');
  const payload = { sub: '1', unique_name: 'sandbox-admin', role: 'Admin', exp: 4102444800 };
  sessionStorage.setItem('etlsql_token', 'sandbox.' + btoa(JSON.stringify(payload)) + '.sandbox');
  sessionStorage.removeItem('etlsql_refresh');
  window.__orchestratorMock = makeOrchestratorPageSandbox(fixtureId);
  window.fetch = window.__orchestratorMock.fetch;
  await import('/src/ETL-SQL.Portal/wwwroot/js/feedback.js');
  await import('/src/ETL-SQL.Portal/wwwroot/js/native-charts.js');
  window.__orchestratorMessages = [];
  const feedback = window.ETLSQLFeedback;
  window.ETLSQLFeedback = { ...feedback, notify(message, options) {
    window.__orchestratorMessages.push({ message, options }); return feedback.notify(message, options);
  } };
  await import('/src/ETL-SQL.Portal/wwwroot/js/pages/orchestrator.js?v=' + Date.now());
  document.body.dataset.orchestratorSandboxReady = 'true';
}

export default {
  id: 'orchestrator-page', title: 'Orchestrator — production page',
  fixtures: [{ id: 'healthy', label: 'Versioned jobs and run overrides' }, { id: 'denied', label: 'Writes denied' },
    { id: 'failed', label: 'Service rejects writes' }, { id: 'conflict', label: 'Opened job version changed' },
    { id: 'catalog-unavailable', label: 'Schedule and notification catalogs unavailable' }],
  async mount(stage, fixtureId, ctx) {
    const token = sessionStorage.getItem('etlsql_token'), refresh = sessionStorage.getItem('etlsql_refresh');
    const frame = document.createElement('iframe');
    frame.title = `Orchestrator — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0';
    const loaded = new Promise(resolve => frame.addEventListener('load', resolve, { once: true }));
    frame.src = `/tools/ui-sandbox/orchestrator-page-frame.html?fixture=${encodeURIComponent(fixtureId)}&v=${Date.now()}`;
    stage.replaceChildren(frame); await loaded;
    ctx.stat('Production Orchestrator page with versioned service fixtures');
    return { dispose() {
      frame.contentWindow?.dispatchEvent(new Event('pagehide')); frame.remove();
      if (token === null) sessionStorage.removeItem('etlsql_token'); else sessionStorage.setItem('etlsql_token', token);
      if (refresh === null) sessionStorage.removeItem('etlsql_refresh'); else sessionStorage.setItem('etlsql_refresh', refresh);
    }, resize() {} };
  }
};
