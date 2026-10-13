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
    8: [{ name: 'Product', type: 'TEXT', default: null, required: true, description: null }],
    9: []
  };
  // The folder tree is nested, as the server sends it: Finance contains Quarterly.
  const folders = [{ id: 1, parentId: null, name: 'Finance', path: '/Finance', version: 1, children: [
    { id: 2, parentId: 1, name: 'Quarterly', path: '/Finance/Quarterly', version: 1, children: [] }] },
  { id: 3, parentId: null, name: 'Operations', path: '/Operations', version: 1, children: [] }];
  const favorites = new Set([9]);
  const listed = (id, folderId, name, extra = {}) => ({ id, folderId, name, description: `${name} report`, path: `/Finance/${name}`,
    isFavorite: favorites.has(id), isStale: false, scriptChanged: false, snapshotBuiltAt: '2026-10-09T05:00:00Z', lastViewedAt: null,
    lastRefreshStatus: null, lastRefreshError: null, lastRefreshCompletedAt: null, ...extra });
  const library = () => [listed(7, 1, 'Regional sales', { isStale: true, snapshotBuiltAt: '2026-10-01T05:00:00Z' }),
    listed(8, 1, 'Margin by product', { snapshotBuiltAt: null, lastRefreshStatus: 'Failed', lastRefreshError: 'Timeout', lastRefreshCompletedAt: '2026-10-09T06:00:00Z' }),
    listed(10, 1, 'Accounts <i>aging</i>', { description: 'Open receivables by age', perViewer: true }), listed(9, 2, 'Inventory aging')];
  // Report 7 has a snapshot; report 8 has none until a run completes. Report 9 has no parameters.
  const snapshots = new Set([7, 9]);
  let runState = 'Completed', runs = 0, polls = 0;
  const calls = [], pendingWrites = [], pendingReads = [];
  let failure = fixtureId, writesPaused = false, readsPaused = null, nextId = 20;
  const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  const mock = {
    calls, setFailure(value) { failure = value; },
    /** What the next polls of a run report: Running, Completed, Failed, Cancelled, or unreachable. */
    setRun(value) { runState = value; }, get polls() { return polls; },
    pauseWrites() { writesPaused = true; }, releaseWrites() { writesPaused = false; pendingWrites.splice(0).forEach(resolve => resolve()); },
    get pendingWrites() { return pendingWrites.length; },
    /** Holds GET requests whose path contains `fragment` until released. */
    pauseReads(fragment) { readsPaused = fragment; }, releaseReads() { readsPaused = null; pendingReads.splice(0).forEach(resolve => resolve()); },
    get pendingReads() { return pendingReads.length; },
    async fetch(input, init = {}) {
      const url = new URL(input instanceof Request ? input.url : String(input), location.href);
      const path = url.pathname, method = init.method || 'GET', headers = new Headers(init.headers);
      const body = typeof init.body === 'string' ? JSON.parse(init.body) : undefined;
      calls.push({ path, method, query: url.searchParams, ifMatch: headers.get('If-Match'), body });
      if (method === 'GET' && readsPaused && path.includes(readsPaused)) await new Promise(resolve => pendingReads.push(resolve));
      if (path === '/api/portal/branding') return json({ displayName: 'ETL-SQL Portal', footerText: 'Sandbox', logoUrl: null });
      if (path === '/api/portal/navigation') return json([]);
      if (path === '/api/folders') return failure === 'folders-unavailable' ? json({ error: 'The folder index is rebuilding.' }, 503) : json(folders);
      const folderReports = /^\/api\/folders\/(\d+)\/reports$/.exec(path);
      if (folderReports) return failure === 'folder-unavailable' ? json({ error: 'Folder permissions are being rebuilt.' }, 503)
        : json(library().filter(item => item.folderId === Number(folderReports[1])));
      if (path === '/api/catalog/consumer-home') return failure === 'home-unavailable' ? json({ error: 'The catalog index is offline.' }, 503)
        : json({ favorites: library().filter(item => item.isFavorite), recent: library().filter(item => item.id === 7), featured: [], popular: [] });
      if (path === '/api/catalog/favorites') return json(library().filter(item => item.isFavorite));
      if (path === '/api/catalog/recent') return json(library().filter(item => item.id === 7 || item.id === 9));
      if (path === '/api/catalog/search') {
        if (failure === 'search-unavailable') return json({ error: 'The search index is offline.' }, 503);
        const wanted = (url.searchParams.get('q') || '').toLowerCase();
        return json([...folders.flatMap(folder => [folder, ...folder.children]).filter(folder => folder.name.toLowerCase().includes(wanted))
          .map(folder => ({ type: 'Folder', id: folder.id, name: folder.name, path: folder.path })),
        ...library().filter(item => item.name.toLowerCase().includes(wanted)).map(item => ({ type: 'Report', ...item }))]);
      }
      const favorite = /^\/api\/reports\/(\d+)\/favorite$/.exec(path);
      if (favorite) {
        if (writesPaused) await new Promise(resolve => pendingWrites.push(resolve));
        if (failure === 'favorite-failed') return json({ error: 'Favorites are read-only during maintenance.' }, 503);
        if (method === 'POST') favorites.add(Number(favorite[1])); else favorites.delete(Number(favorite[1]));
        return new Response(null, { status: 204 });
      }
      if (path === '/api/catalog/lineage/downstream') {
        if (url.searchParams.get('table') === 'customers' && failure === 'downstream-partial') return json({ error: 'Lineage catalog is rebuilding.' }, 503);
        return json(url.searchParams.get('table') === 'orders' ? [{ reportId: 9, reportName: 'Inventory aging', lastSeen: '2026-10-09T05:00:00Z' }] : []);
      }
      if (path === '/api/smtp-aliases') return failure === 'smtp-unavailable' ? json({ error: 'Connection catalog is offline.' }, 503)
        : json(failure === 'no-smtp' ? [] : ['smtp_ops', 'smtp_backup']);
      const job = /^\/api\/jobs\/(.+)$/.exec(path);
      if (job) {
        polls++;
        if (runState === 'unreachable') return json({ error: 'The job service is restarting.' }, 503);
        const reportId = Number(job[1].split('-')[1]);
        if (runState === 'Completed') snapshots.add(reportId);
        return json({ jobId: job[1], status: runState, createdAt: '2026-10-10T09:00:00Z', rowsProcessed: 0, peakMemoryBytes: 0, cpuTimeSeconds: 0,
          error: runState === 'Failed' ? 'Division by zero in visual Sales.' : null });
      }
      const report = /^\/api\/reports\/(\d+)(\/(snapshot|parameters|execute|structure|dependencies|history|export\/\w+))?$/.exec(path);
      if (report) {
        const id = Number(report[1]);
        if (report[3] === 'execute') return failure === 'run-rejected' ? json({ error: 'Report execution capacity is exhausted.' }, 429)
          : json({ jobId: `run-${id}-${++runs}` }, 202);
        if (report[3] === 'structure') return failure === 'dialogs-unavailable' ? json({ error: 'The script could not be parsed.' }, 503)
          : json({ nodes: [], edges: [] });
        if (report[3] === 'history') return failure === 'dialogs-unavailable' ? json({ error: 'History store is offline.' }, 503)
          : json({ report: { name: 'Regional sales', folderPath: '/reports' }, scriptChanged: true, publishedScriptHash: 'abc123', currentScriptHash: 'def456',
            snapshots: [{ builtAt: '2026-10-09T05:00:00Z', builtBy: 'reader', hashMatched: false, scriptHashAtRunTime: 'abc123' }],
            changes: [{ timestamp: '2026-10-08T05:00:00Z', action: 'PUBLISH_REPORT', userId: 1, detail: '<script>document.title = 1</script>' }] });
        if (report[3] === 'dependencies') return json({ report: { name: 'Regional sales', folderPath: '/reports' }, snapshot: { builtAt: '2026-10-09T05:00:00Z' },
          manifestDatasets: [], registeredDatasets: [], refreshJobs: [], lineageEntries: [],
          sources: [{ connection: 'warehouse', name: 'orders', kind: 'Table' }, { connection: 'warehouse', objectName: 'customers', kind: 'Table' }] });
        if (report[3]?.startsWith('export/')) return failure === 'export-failed' ? json({ error: 'The export renderer is unavailable.' }, 503)
          : new Response('a,b\n1,2\n', { status: 200, headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename="regional.csv"' } });
        if (report[3] === 'snapshot' && !snapshots.has(id)) return json({ error: 'No snapshot' }, 404);
        if (!report[3] && failure === 'report-unavailable') return json({ error: 'The catalog is offline.' }, 503);
        if (report[3] === 'parameters') return failure === 'parameters-unavailable' ? json({ error: 'The report script could not be inspected.' }, 503)
          : json(parameters[id] ?? []);
        if (report[3] === 'snapshot') return json({ reportId: id, builtAt: '2026-10-09T05:00:00Z', isStale: false, pages: [] });
        return json({ id, name: id === 9 ? 'Inventory aging' : id === 8 ? 'Margin by product' : 'Regional sales', description: 'Sandbox report', folderId: 1,
          folderPath: '/reports', scriptPath: 'reports/sandbox.rptsql', hasSnapshot: snapshots.has(id), version: 1 });
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
