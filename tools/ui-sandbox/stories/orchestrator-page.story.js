// Run the production page against a deterministic service, including rejected and delayed writes.
export function makeOrchestratorPageSandbox(fixtureId = 'healthy') {
  let jobs = ['daily_sales', 'hourly_orders'].map((name, index) => ({
    id: `job-${index}`, name, script: 'SELECT 1;', interval: 1, unit: 'HOUR',
    isEnabled: true, maxRetries: 0, retryDelaySeconds: 30, hashPolicy: 'Warn',
    version: 3, jobType: 0, createdBy: 'user:operator', lastRun: null, nextRun: null
  }));
  // One job is due later today (local time), so the day strip has a run to place.
  const dueToday = new Date(); dueToday.setHours(18, 30, 0, 0);
  jobs[0].nextRun = dueToday.toISOString();
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
  // Detail state is kept per job so a response drawn under the wrong job is visible as wrong.
  const details = {
    daily_sales: {
      schedules: [{ jobId: 'job-0', scheduleId: 'schedule-0', scheduleName: 'nightly_utc', jobName: 'daily_sales', lastRun: null, nextRun: '2026-10-10T02:00:00Z' }],
      notifications: [{ jobId: 'job-0', notificationId: 'notification-0', notificationName: 'ops_mail', jobName: 'daily_sales', trigger: 'Failure' }],
      state: [{ jobName: 'daily_sales', stateKey: 'last_order_id', stateValue: '100520', updatedAt: '2026-10-09T11:00:00Z' }],
      history: [
        { id: 42, jobName: 'daily_sales', startTime: '2026-10-09T02:00:00Z', endTime: '2026-10-09T02:00:41Z', status: 'Failed', errorMessage: 'Timeout',
          rowsProcessed: 1200, peakMemoryBytes: 0, cpuTimeSeconds: 0, rowsQuarantined: 0, rowsWarned: 0, checkpointLabel: 'load_orders', hasResumeSession: true },
        { id: 41, jobName: 'daily_sales', startTime: '2026-10-08T02:00:00Z', endTime: '2026-10-08T02:00:30Z', status: 'Success', errorMessage: null,
          rowsProcessed: 5000, peakMemoryBytes: 0, cpuTimeSeconds: 0, rowsQuarantined: 0, rowsWarned: 0, checkpointLabel: null, hasResumeSession: false }
      ],
      grants: [{ objectId: 'job-0', objectKind: 'JOB', principalKind: 'GROUP', principalId: 'finance-ops', permission: 'EXECUTE', grantedBy: 'user:operator', version: 1 }]
    },
    hourly_orders: { schedules: [], notifications: [], state: [], history: [], grants: [] }
  };
  // Objects nobody is accountable for; adopting assigns one owner to all of them.
  let unowned = [{ objectId: 'job-1', kind: 'JOB', name: 'hourly_orders' }, { objectId: 'schedule-1', kind: 'SCHEDULE', name: 'hourly_us' }];
  const detailWaiters = [];
  let detailPausedFor = null;
  async function detailRoute(path, method, init) {
    const parts = path.split('/').map(decodeURIComponent);
    const authorization = parts[3] === 'authorization';
    const name = authorization ? parts[5] : parts[4], kind = authorization ? 'grants' : parts[5];
    const detail = details[name];
    if (method === 'GET') {
      if (detailPausedFor === name) await new Promise(resolve => detailWaiters.push(resolve));
      if (!detail) return json({ error: 'The job no longer exists.' }, 404);
      if (failure === 'detail-unavailable') return json({ error: 'Orchestrator job details are temporarily unavailable. Please retry.' }, 503);
      if (failure === 'detail-denied' && authorization) return new Response(null, { status: 403 });
      if (failure === 'detail-malformed') return json([{ unexpected: true }]);
      if (kind === 'dag') return json({ nodes: [], edges: [] });
      if (kind === 'audit') return json([{ id: 1, tenantId: 'portal-host', actorType: 'User', actorId: 'operator', action: 'JobScriptEdited',
        resourceType: 'Job', resourceId: name, timestamp: '2026-10-09T10:00:00Z', detail: `edited ${name}`, outboxMessages: [] }]);
      if (kind === 'dependencies') return json({ jobName: name, upstream: name === 'daily_sales' ? ['hourly_orders'] : [], downstream: [], sharedSchedules: [],
        nodes: jobs.filter(job => job.name === name || name === 'daily_sales').map(job => ({ id: job.name, name: job.name, displayName: null,
          isEnabled: job.isEnabled, lastRun: null, nextRun: null, isCurrent: job.name === name })),
        edges: name === 'daily_sales' ? [{ from: 'hourly_orders', to: 'daily_sales', type: 'Data', detail: 'Writes orders' }] : [] });
      return json(detail[kind] ?? []);
    }
    if (paused) await new Promise(resolve => pending.push(resolve));
    if (failure === 'denied') return new Response(null, { status: 403 });
    if (failure === 'failed') return json({ error: 'Orchestrator service unavailable.' }, 503);
    if (failure === 'unexpected') return new Response(null, { status: 206 });
    const body = typeof init.body === 'string' ? JSON.parse(init.body) : {};
    if (authorization) {
      if (parts[6] === 'owner') {
        jobs.find(job => job.name === name).createdBy = `${body.principalKind.toLowerCase()}:${body.principalId}`;
        return json({ kind: 'JOB', name, owner: body.principalId });
      }
      const [principalKind, principalId] = [parts[6], parts[7]];
      detail.grants = detail.grants.filter(grant => !(grant.principalKind === principalKind && grant.principalId === principalId));
      if (method === 'DELETE') return new Response(null, { status: 204 });
      detail.grants.push({ objectId: name, objectKind: 'JOB', principalKind, principalId, permission: body.permission, grantedBy: 'user:sandbox-admin', version: 1 });
      return json(detail.grants.at(-1));
    }
    const target = parts[6];
    if (kind === 'schedules' && failure === 'attach-refused') return new Response(null, { status: 403 });
    if (kind === 'schedules') {
      if (method === 'POST' && !schedules.some(item => item.name === target)) return relayed(`Schedule '${target}' not found.`, 404);
      detail.schedules = detail.schedules.filter(link => link.scheduleName !== target);
      if (method === 'POST') detail.schedules.push({ jobId: name, scheduleId: target, scheduleName: target, jobName: name, lastRun: null, nextRun: null });
      return json(method === 'POST' ? { attached: true } : { detached: true });
    }
    if (kind === 'notifications') {
      const trigger = method === 'POST' ? body.trigger : new URL(path + (init.query || ''), location.href).searchParams.get('trigger');
      detail.notifications = detail.notifications.filter(link => !(link.notificationName === target && (!trigger || link.trigger === trigger)));
      if (method === 'POST') detail.notifications.push({ jobId: name, notificationId: target, notificationName: target, jobName: name, trigger });
      return json(method === 'POST' ? { attached: true } : { detached: true });
    }
    detail.state = detail.state.filter(entry => entry.stateKey !== target);
    if (method === 'PUT') detail.state.push({ jobName: name, stateKey: target, stateValue: body.value, updatedAt: '2026-10-10T09:00:00Z' });
    return json({ key: target });
  }
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
    pauseDetail(name) { detailPausedFor = name; }, releaseDetail() { detailPausedFor = null; detailWaiters.splice(0).forEach(resolve => resolve()); },
    get pendingDetailReads() { return detailWaiters.length; },
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
      if (/^\/api\/orchestrator\/jobs\/[^/]+\/(history|schedules|notifications|state|dag|audit|dependencies)(\/|$)/.test(path)
        || path.startsWith('/api/orchestrator/authorization/Job/')) return detailRoute(path, method, { ...init, query: url.search });
      if (path === '/api/orchestrator/authorization/unowned') return failure === 'legacy'
        ? json({ error: 'Per-object grants and ownership require federated identity.' }, 409) : json(unowned);
      if (path === '/api/orchestrator/authorization/adopt') {
        if (paused) await new Promise(resolve => pending.push(resolve));
        if (failure === 'failed') return json({ error: 'Orchestrator service unavailable.' }, 503);
        const body = JSON.parse(init.body), owner = `${body.principalKind.toLowerCase()}:${body.principalId}`;
        const adopted = failure === 'adopt-partial' ? unowned.slice(0, 1) : unowned;
        unowned = unowned.filter(item => !adopted.includes(item));
        for (const item of adopted) { const job = jobs.find(candidate => candidate.name === item.name); if (job) job.createdBy = owner; }
        return json(failure === 'adopt-unconfirmed' ? { owner: 'user:someone-else', count: adopted.length, adopted }
          : { owner, count: adopted.length, adopted });
      }
      if (/^\/api\/orchestrator\/runs\/\d+\/resume$/.test(path)) {
        if (paused) await new Promise(resolve => pending.push(resolve));
        return failure === 'failed' ? json({ error: 'Resume failed: the checkpoint session expired.' }, 409) : new Response(null, { status: 202 });
      }
      if (path === '/api/orchestrator/data-quality/status') return failure === 'quality-unavailable' ? json({ error: 'Quality status is unavailable.' }, 503)
        : json([{ runId: '42', jobName: 'daily_sales', startTime: '2026-10-09T02:00:00Z', endTime: null, status: 'Failed', rowsProcessed: 1200,
          rowsWarned: 7, rowsQuarantined: 3, failedRuleCount: 1, freshestValueUtc: null, freshnessState: 'Fresh', errorSummary: null },
        { runId: '9', jobName: 'hourly_orders', startTime: '2026-10-09T03:00:00Z', endTime: null, status: 'Success', rowsProcessed: 10,
          rowsWarned: 99, rowsQuarantined: 99, failedRuleCount: 9, freshestValueUtc: null, freshnessState: 'Stale', errorSummary: null }]);
      if (path === '/api/orchestrator/data-quality/failures') return json([{ runId: 42, jobName: 'daily_sales', startTime: '2026-10-09T02:00:00Z', endTime: null,
        status: 'Failed', targetTable: 'orders', columnName: 'amount', rule: 'NOT NULL', action: 'QUARANTINE', failureCount: 3, owner: null }]);
      const catalog = /^\/api\/orchestrator\/(schedules|notifications)(\/|$)/.exec(path);
      if (catalog) return catalogRoute(catalog[1], catalog[1] === 'schedules' ? schedules : notifications, path, method, init);
      if (path === '/api/orchestrator/scripts') return failure === 'pickers-unavailable' ? json({ error: 'Script unavailable.' }, 503)
        : json({ files: ['etl/daily_sales.etlsql', 'etl/hourly_orders.etlsql'] });
      if (path === '/api/orchestrator/bundles') return json([{ bundleName: 'sales_bundle', version: 1 }, { bundleName: 'sales_bundle', version: 2 }]);
      if (path === '/api/orchestrator/service/stop') {
        if (paused) await new Promise(resolve => pending.push(resolve));
        return failure === 'failed' ? json({ error: 'The Orchestrator service refused the stop request.' }, 500)
          : failure === 'denied' ? new Response(null, { status: 403 }) : json({ message: 'Stop signal sent.' });
      }
      if (path === '/api/orchestrator/jobs' && method === 'POST') {
        if (paused) await new Promise(resolve => pending.push(resolve));
        const body = JSON.parse(init.body);
        if (failure === 'failed') return json({ error: 'Orchestrator service unavailable.' }, 503);
        if (jobs.some(item => item.name === body.name)) return relayed(`Job '${body.name}' already exists.`, 409);
        jobs.push({ id: `job-${jobs.length}`, name: body.name, script: '', interval: body.interval ?? 1, unit: body.unit ?? 'HOUR', isEnabled: true,
          maxRetries: body.maxRetries, retryDelaySeconds: body.retryDelaySeconds, hashPolicy: body.hashPolicy, version: 1, jobType: 0,
          targetPath: body.targetPath, displayName: body.displayName, createdBy: 'user:sandbox-admin', lastRun: null, nextRun: null });
        details[body.name] = { schedules: [], notifications: [], state: [], history: [], grants: [] };
        return new Response(null, { status: 201 });
      }
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
  const role = new URLSearchParams(location.search).get('role') || 'Admin';
  const payload = { sub: '1', unique_name: 'sandbox-admin', role, exp: 4102444800 };
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
