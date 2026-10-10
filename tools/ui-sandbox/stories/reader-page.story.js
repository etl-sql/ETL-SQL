// Run the production catalog page against a deterministic service so its subscription surfaces
// can be driven the way a reader drives them: the list at #subscriptions, Subscribe from a report.
export function makeReaderPageSandbox(fixtureId = 'healthy') {
  const subscription = (id, reportId, reportName, extra = {}) => ({
    id, reportId, reportName, name: null, schedule: 'Daily', atTime: '06:00', deliverOnRefresh: false, format: 'PDF',
    smtpAlias: 'smtp_ops', recipients: 'reader@example.test', lastSentAt: '2026-10-09T06:00:00Z', nextRunAt: '2026-10-10T06:00:00Z',
    failCount: 0, isActive: true, parameters: { Region: 'North' }, parameterSummary: 'Region=North', version: 2, ...extra });
  let subscriptions = [
    subscription(11, 7, 'Regional sales', { name: 'Morning sales' }),
    subscription(12, 7, 'Regional sales', { isActive: false, failCount: 2, parameters: {}, parameterSummary: null, version: 5 }),
    subscription(13, 9, 'Inventory <b>aging</b>', { format: 'Link', smtpAlias: '', recipients: '', lastSentAt: null, nextRunAt: null })
  ];
  const parameters = {
    7: [{ name: 'Region', type: 'TEXT', default: null, required: true, description: 'Sales region to include' },
      { name: 'AsOf', type: 'RELDATE', default: 'D-1', required: false, description: null }],
    9: []
  };
  const calls = [], pendingWrites = [], pendingReads = [];
  let failure = fixtureId, writesPaused = false, readsPaused = null, nextId = 20;
  const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  const mock = {
    calls, setFailure(value) { failure = value; },
    pauseWrites() { writesPaused = true; }, releaseWrites() { writesPaused = false; pendingWrites.splice(0).forEach(resolve => resolve()); },
    get pendingWrites() { return pendingWrites.length; },
    /** Holds GET requests whose path contains `fragment` until released. */
    pauseReads(fragment) { readsPaused = fragment; }, releaseReads() { readsPaused = null; pendingReads.splice(0).forEach(resolve => resolve()); },
    get pendingReads() { return pendingReads.length; },
    async fetch(input, init = {}) {
      const url = new URL(input instanceof Request ? input.url : String(input), location.href);
      const path = url.pathname, method = init.method || 'GET', headers = new Headers(init.headers);
      const body = typeof init.body === 'string' ? JSON.parse(init.body) : undefined;
      calls.push({ path, method, ifMatch: headers.get('If-Match'), body });
      if (method === 'GET' && readsPaused && path.includes(readsPaused)) await new Promise(resolve => pendingReads.push(resolve));
      if (path === '/api/portal/branding') return json({ displayName: 'ETL-SQL Portal', footerText: 'Sandbox', logoUrl: null });
      if (path === '/api/portal/navigation') return json([]);
      if (path === '/api/folders') return json([]);
      if (path === '/api/catalog/favorites' || path === '/api/catalog/recent') return json([]);
      if (path === '/api/smtp-aliases') return failure === 'smtp-unavailable' ? json({ error: 'Connection catalog is offline.' }, 503)
        : json(failure === 'no-smtp' ? [] : ['smtp_ops', 'smtp_backup']);
      const report = /^\/api\/reports\/(\d+)(\/(snapshot|parameters))?$/.exec(path);
      if (report) {
        const id = Number(report[1]);
        if (report[3] === 'parameters') return failure === 'parameters-unavailable' ? json({ error: 'The report script could not be inspected.' }, 503)
          : json(parameters[id] ?? []);
        if (report[3] === 'snapshot') return json({ reportId: id, builtAt: '2026-10-09T05:00:00Z', isStale: false, pages: [] });
        return json({ id, name: id === 9 ? 'Inventory aging' : 'Regional sales', description: 'Sandbox report', folderId: 1,
          folderPath: '/reports', scriptPath: 'reports/sandbox.rptsql', hasSnapshot: true, version: 1 });
      }
      if (path === '/api/subscriptions' && method === 'GET') {
        if (failure === 'list-unavailable') return json({ error: 'The subscription store is offline.' }, 503);
        if (failure === 'list-malformed') return json([{ id: 11, reportName: 'Regional sales' }]);
        return json(subscriptions);
      }
      const history = /^\/api\/subscriptions\/(\d+)\/history$/.exec(path);
      if (history) return failure === 'history-unavailable'
        ? json({ error: 'Scheduler trigger history is temporarily unavailable. Please retry.' }, 503) : json([]);
      if (path.startsWith('/api/subscriptions')) {
        if (writesPaused) await new Promise(resolve => pendingWrites.push(resolve));
        if (failure === 'failed') return json({ error: 'The subscription store is offline.' }, 503);
        if (method === 'POST') {
          if (failure === 'rejected') return json({ error: 'No recipient email. Supply one or add an email to your profile.' }, 400);
          const created = subscription(nextId++, body.reportId, body.reportId === 9 ? 'Inventory aging' : 'Regional sales', {
            name: body.name, schedule: body.schedule, atTime: body.atTime, format: body.format, smtpAlias: body.smtpAlias ?? '',
            recipients: body.recipientEmail ?? '', parameters: body.parameters ?? {}, version: 1 });
          subscriptions.push(created);
          return json(created, 201);
        }
        const id = Number(path.split('/')[3]), current = subscriptions.find(item => item.id === id);
        if (!current) return json({ error: 'Not found' }, 404);
        if (failure === 'conflict' || headers.get('If-Match') !== `"${current.version}"`)
          return json({ error: 'The resource changed after it was read. Refresh it and retry.' }, 409);
        if (method === 'DELETE') { subscriptions = subscriptions.filter(item => item !== current); return new Response(null, { status: 204 }); }
        if (failure === 'unconfirmed') return json(current);
        Object.assign(current, body.isActive == null ? {} : { isActive: body.isActive },
          body.parameters == null ? {} : { parameters: body.parameters, parameterSummary: Object.entries(body.parameters).map(pair => pair.join('=')).join(', ') },
          { version: current.version + 1 });
        return json(current);
      }
      return json({ error: `Unexpected sandbox route: ${method} ${path}` }, 404);
    }
  };
  return mock;
}

export async function mountReaderPageFrame(fixtureId) {
  const html = await fetch('/src/ETL-SQL.Portal/wwwroot/index.html').then(response => response.text());
  document.documentElement.innerHTML = html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/g, '')
    .replace(/(href|src)="\/(css|js|designer)\//g, '$1="/src/ETL-SQL.Portal/wwwroot/$2/');
  const payload = { sub: '1', unique_name: 'sandbox-reader', role: 'Viewer', exp: 4102444800 };
  sessionStorage.setItem('etlsql_token', 'sandbox.' + btoa(JSON.stringify(payload)) + '.sandbox');
  sessionStorage.removeItem('etlsql_refresh');
  window.__readerMock = makeReaderPageSandbox(fixtureId);
  window.fetch = window.__readerMock.fetch;
  await import('/src/ETL-SQL.Portal/wwwroot/js/feedback.js');
  window.__readerMessages = [];
  const feedback = window.ETLSQLFeedback;
  window.ETLSQLFeedback = { ...feedback, notify(message, options) {
    window.__readerMessages.push({ message, title: options?.title, tone: options?.tone }); return feedback.notify(message, options);
  } };
  await import('/src/ETL-SQL.Portal/wwwroot/js/pages/index.js?v=' + Date.now());
  document.body.dataset.readerSandboxReady = 'true';
}

export default {
  id: 'reader-page', title: 'Catalog — production page (subscriptions)',
  fixtures: [{ id: 'healthy', label: 'A reader with three subscriptions' }, { id: 'list-unavailable', label: 'Subscription list unavailable' },
    { id: 'parameters-unavailable', label: 'Report parameters unavailable' }, { id: 'no-smtp', label: 'No delivery connection configured' }],
  async mount(stage, fixtureId, ctx) {
    const token = sessionStorage.getItem('etlsql_token'), refresh = sessionStorage.getItem('etlsql_refresh');
    const frame = document.createElement('iframe');
    frame.title = `Catalog — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0';
    const loaded = new Promise(resolve => frame.addEventListener('load', resolve, { once: true }));
    frame.src = `/tools/ui-sandbox/reader-page-frame.html?fixture=${encodeURIComponent(fixtureId)}&v=${Date.now()}#subscriptions`;
    stage.replaceChildren(frame); await loaded;
    ctx.stat('Production catalog page with a subscription service fixture');
    return { dispose() {
      frame.contentWindow?.dispatchEvent(new Event('pagehide')); frame.remove();
      if (token === null) sessionStorage.removeItem('etlsql_token'); else sessionStorage.setItem('etlsql_token', token);
      if (refresh === null) sessionStorage.removeItem('etlsql_refresh'); else sessionStorage.setItem('etlsql_refresh', refresh);
    }, resize() {} };
  }
};
