// Mount the production page; fixtures supply API replies.
export function makeAdminPageSandbox(fixtureId = 'healthy') {
  const roles = ['Admin', 'Viewer', 'Publisher', 'OrchestratorViewer', 'OrchestratorManager',
    'FleetReader', 'StewardshipViewer', 'DataSteward', 'StewardshipManager'];
  const user = (id, username, role = 'Viewer') => ({ id, username, email: `${username}@example.test`,
    firstName: null, lastName: null, isActive: true, mustChangePassword: false,
    createdAt: '2026-10-08T12:00:00Z', roles: role ? [role] : [], groups: [], provider: 'Local', version: 1 });
  let users = fixtureId === 'empty' ? [] : [user(1, 'sandbox-admin', 'Admin'), user(2, 'data-steward', 'DataSteward'), user(3, 'unassigned', '')];
  let groups = [{ id: 10, name: 'Finance', description: 'Finance team', memberCount: 1, provider: 'Local', adGroup: null, version: 1 },
    { id: 11, name: 'Operations', description: 'Operations team', memberCount: 1, provider: 'Local', adGroup: null, version: 1 }];
  const memberships = new Map([[10, [2]], [11, [3]]]);
  const folders = [
    { id: 20, parentId: null, name: 'Reports', path: '/Reports', version: 1, children: [
      { id: 21, parentId: 20, name: 'Child', path: '/Reports/Child', version: 1, children: [] }
    ] },
    { id: 30, parentId: null, name: 'Operations', path: '/Operations', version: 1, children: [] }
  ];
  const folderAcls = new Map([[20, [{ groupId: 10, groupName: 'Finance', permission: 2 }]],
    [30, [{ groupId: 11, groupName: 'Operations', permission: 0 }]], [21, []]]);
  const permissionOptions = [{ permission: 0, name: 'Read', rank: 0 }, { permission: 1, name: 'Execute', rank: 1 },
    { permission: 3, name: 'Author', rank: 2 }, { permission: 2, name: 'Manage', rank: 3 }];
  const folderRows = () => {
    const rows = [], pending = [...folders];
    while (pending.length) { const folder = pending.pop(); rows.push(folder); pending.push(...folder.children); }
    return rows;
  };
  const detachFolder = folder => {
    const siblings = folder.parentId === null ? folders : folderRows().find(item => item.id === folder.parentId).children;
    siblings.splice(siblings.indexOf(folder), 1);
  };
  const updateFolderPaths = folder => {
    for (const child of folder.children) { child.path = `${folder.path}/${child.name}`; child.version++; updateFolderPaths(child); }
  };
  let nextFolderId = 31;
  let reports = [{ id: 40, name: 'Finance report', description: 'Finance description', scriptPath: 'reports/finance.rptsql',
    folderId: 20, folderName: 'Reports', folderPath: '/Reports', createdAt: '2026-10-09T12:00:00Z', updatedAt: '2026-10-09T12:00:00Z', version: 1 },
    { id: 41, name: 'Operations report', description: null, scriptPath: 'reports/operations.rptsql',
      folderId: 30, folderName: 'Operations', folderPath: '/Operations', createdAt: '2026-10-09T12:00:00Z', updatedAt: '2026-10-09T12:00:00Z', version: 1 }];
  const reportDto = report => ({ ...report, owner: null, contact: null, tags: null, category: null,
    domain: null, steward: null, certification: null, metadata: {}, scriptLastModified: report.updatedAt,
    hasSnapshot: false, snapshotBuiltAt: null, lastViewedAt: null, lastRefreshStartedAt: null,
    lastRefreshCompletedAt: null, lastRefreshStatus: null, lastRefreshError: null, lastRefreshDurationMs: null,
    isFavorite: false, isStale: false, scriptChanged: false });
  let pauseReports = false, pauseValidation = false, pauseScripts = false, nextReportId = 42;
  let pauseAudit = false, pauseExport = false;
  const auditReads = [], exportReads = [], downloads = [], createdUrls = [], revokedUrls = [];
  let auditRows = Array.from({ length: 525 }, (_, index) => ({ id: 525 - index, userId: index === 0 ? null : 2,
    username: index === 0 ? null : 'data-steward', action: index % 2 ? 'RUN_REPORT' : 'UPDATE_USER',
    resourceType: 'Fixture', resourceId: String(525 - index), timestamp: '2026-10-09T12:00:00Z',
    detail: `Audit evidence ${525 - index}`, correlationId: null, studioCapability: null }));
  const reportReads = [], validations = [], scriptReads = [];
  const subscriptionRows = [60, 61].map((id, index) => ({ id, reportId: 40 + index,
    reportName: index ? 'Operations report' : 'Finance report', name: index ? 'Operations delivery' : 'Finance delivery',
    schedule: 'Daily', atTime: null, deliverOnRefresh: false, format: 'PDF', smtpAlias: 'mail',
    recipients: index ? 'operations@example.test' : 'finance@example.test', lastSentAt: null, nextRunAt: null,
    failCount: 0, isActive: true, parameters: { '@Region': index ? 'South' : 'North' },
    parameterSummary: '@Region=' + (index ? 'South' : 'North'), version: 1 }));
  let pauseParameters = false, pauseSubscriptionHistory = false, pauseSubscriptions = false;
  let parameterDefinitions = [{ name: '@Region', type: 'STRING', default: null, required: true, description: 'Delivery region' }];
  const parameterReads = [], subscriptionHistoryReads = [], subscriptionReads = [];
  let orchestratorSettings = { apiUrl: 'https://orchestrator.example.test', hasApiKey: true };
  let brandingSettings = { displayName: 'ETL-SQL Portal', footerText: 'Sandbox footer', logoUrl: null };
  let pauseSettings = fixtureId === 'settingsLateRead', pauseBranding = false, pauseStatus = false;
  const settingsReads = [], brandingReads = [], statusReads = [];
  let pausePublicBranding = fixtureId === 'settingsLatePublicBranding';
  const publicBrandingReads = [];
  let studioSession = { mode: fixtureId.startsWith('reports') ? 'SourceControlled' : 'CatalogOnly',
    capabilities: fixtureId.startsWith('reports') ? ['StudioAccess', 'ScriptRead', 'ScriptPreview', 'ReportPublish'] : [],
    sourceControlEnabled: false, draftRecovery: false };
  let pauseAcl = false;
  const aclReads = [];
  let nextGroupId = 12, pauseMembers = false;
  const memberReads = [];
  let failure = fixtureId, pauseWrite = false, pauseRead = false, nextId = 4;
  const writes = [], reads = [], calls = [];
  const json = (body, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' }
  });
  const mock = {
    calls, setFailure(value) { failure = value; },
    pauseNextSettingsRead() { pauseSettings = true; }, pauseNextBrandingRead() { pauseBranding = true; },
    pauseNextStatusRead() { pauseStatus = true; },
    releaseSettings() { settingsReads.splice(0).forEach(resolve => resolve()); },
    releaseBranding() { brandingReads.splice(0).forEach(resolve => resolve()); },
    releaseStatus() { statusReads.splice(0).forEach(resolve => resolve()); },
    get pendingSettings() { return settingsReads.length; }, get pendingBranding() { return brandingReads.length; },
    get pendingStatus() { return statusReads.length; },
    releasePublicBranding() { publicBrandingReads.splice(0).forEach(resolve => resolve()); },
    get pendingPublicBranding() { return publicBrandingReads.length; },
    downloads, createdUrls, revokedUrls, recordDownload(value) { downloads.push(value); },
    pauseNextAuditRead() { pauseAudit = true; }, pauseNextAuditExport() { pauseExport = true; },
    setAuditLabel(value) { auditRows = auditRows.map(row => ({ ...row, detail: `${value} ${row.id}` })); },
    releaseAuditReads() { auditReads.splice(0).forEach(resolve => resolve()); },
    releaseAuditExports() { exportReads.splice(0).forEach(resolve => resolve()); },
    get pendingAuditReads() { return auditReads.length; }, get pendingAuditExports() { return exportReads.length; },
    pauseNextParameterRead() { pauseParameters = true; }, pauseNextSubscriptionHistory() { pauseSubscriptionHistory = true; },
    releaseParameters() { pauseParameters = false; parameterReads.splice(0).forEach(resolve => resolve()); },
    releaseSubscriptionHistories() { subscriptionHistoryReads.splice(0).forEach(resolve => resolve()); },
    get pendingParameters() { return parameterReads.length; }, get pendingSubscriptionHistories() { return subscriptionHistoryReads.length; },
    pauseNextSubscriptionRead() { pauseSubscriptions = true; },
    releaseSubscriptions() { subscriptionReads.splice(0).forEach(resolve => resolve()); },
    get pendingSubscriptions() { return subscriptionReads.length; },
    setSubscriptionVersion(id, version) { subscriptionRows.find(row => row.id === id).version = version; },
    setParameterDefinitions(value) { parameterDefinitions = structuredClone(value); },
    addSubscriptions(count) { for (let index = 0; index < count; ++index) subscriptionRows.push({ ...structuredClone(subscriptionRows[0]), id: 100 + index, name: 'Additional delivery ' + index }); },
    setUserRoles(id, value) { users.find(item => item.id === id).roles = [...value]; },
    pauseNextWrite() { pauseWrite = true; }, pauseNextRead() { pauseRead = true; },
    pauseNextMemberRead() { pauseMembers = true; },
    pauseNextAclRead() { pauseAcl = true; },
    pauseNextReportRead() { pauseReports = true; }, pauseNextValidation() { pauseValidation = true; },
    pauseNextScriptRead() { pauseScripts = true; },
    setStudioSession(mode, capabilities) { studioSession = { ...studioSession, mode, capabilities }; },
    setReportVersion(id, version, name) { const report = reports.find(item => item.id === id); report.version = version; if (name) report.name = name; },
    releaseReports() { reportReads.splice(0).forEach(resolve => resolve()); },
    releaseValidations() { validations.splice(0).forEach(resolve => resolve()); },
    releaseScripts() { scriptReads.splice(0).forEach(resolve => resolve()); },
    releaseAcls() { aclReads.splice(0).forEach(resolve => resolve()); },
    releaseMembers() { memberReads.splice(0).forEach(resolve => resolve()); },
    releaseWrites() { writes.splice(0).forEach(resolve => resolve()); },
    releaseReads() { reads.splice(0).forEach(resolve => resolve()); },
    get pendingWrites() { return writes.length; }, get pendingReads() { return reads.length; },
    get pendingMembers() { return memberReads.length; },
    get pendingAcls() { return aclReads.length; },
    get pendingReports() { return reportReads.length; }, get pendingValidations() { return validations.length; },
    get pendingScripts() { return scriptReads.length; },
    async fetch(input, init = {}) {
      const url = new URL(input instanceof Request ? input.url : input, 'http://sandbox.test');
      const route = url.pathname, method = init.method || 'GET';
      const body = typeof init.body === 'string' ? JSON.parse(init.body) : null;
      calls.push({ route, method, body, query: url.search, ifMatch: new Headers(init.headers).get('If-Match') });
      if (route === '/api/branding') {
        const result = structuredClone(brandingSettings);
        if (pausePublicBranding) { pausePublicBranding = false; await new Promise(resolve => publicBrandingReads.push(resolve)); }
        return json(result);
      }
      if (route === '/api/admin/settings/orchestrator') {
        if (method === 'GET') {
          const result = structuredClone(orchestratorSettings);
          if (pauseSettings) { pauseSettings = false; await new Promise(resolve => settingsReads.push(resolve)); }
          if (failure === 'settingsUnavailable') return json({ error: 'Settings unavailable.' }, 503);
          return json(failure === 'settingsMalformed' ? { apiUrl: 123, hasApiKey: 'yes' } : result);
        }
        if (pauseWrite) { pauseWrite = false; await new Promise(resolve => writes.push(resolve)); }
        orchestratorSettings = { apiUrl: body.apiUrl, hasApiKey: body.apiKey === null ? orchestratorSettings.hasApiKey : body.apiKey !== '' };
        if (failure === 'settingsWriteUnavailable') return json({ error: 'Outcome unavailable.' }, 503);
        return new Response(null, { status: failure === 'settingsWriteUnconfirmed' ? 202 : 204 });
      }
      if (route === '/api/admin/settings/branding') {
        if (method === 'GET') {
          const result = structuredClone(brandingSettings);
          if (pauseBranding) { pauseBranding = false; await new Promise(resolve => brandingReads.push(resolve)); }
          if (failure === 'brandingUnavailable') return json({ error: 'Branding unavailable.' }, 503);
          return json(failure === 'brandingMalformed' ? { displayName: [] } : result);
        }
        if (pauseWrite) { pauseWrite = false; await new Promise(resolve => writes.push(resolve)); }
        brandingSettings = structuredClone(body);
        if (failure === 'brandingWriteUnavailable') return json({ error: 'Outcome unavailable.' }, 503);
        return new Response(null, { status: failure === 'brandingWriteUnconfirmed' ? 202 : 204 });
      }
      if (route === '/api/orchestrator/status') {
        if (pauseStatus) { pauseStatus = false; await new Promise(resolve => statusReads.push(resolve)); }
        if (failure === 'settingsStatusForbidden') return json({ error: 'Access denied.' }, 403);
        return json(failure === 'settingsStatusMalformed' ? {} : { online: false, status: 'Offline' });
      }
      if (route === '/api/portal/navigation') return json([]);
      if (route === '/api/admin/subscriptions/catalog' && method === 'GET') {
        const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 25);
        const query = (url.searchParams.get('q') || '').toLowerCase(), status = url.searchParams.get('status');
        const matched = subscriptionRows.filter(row => (!query || [row.name, row.reportName, row.recipients].some(value => value.toLowerCase().includes(query)))
          && (!status || status === 'active' && row.isActive || status === 'paused' && !row.isActive || status === 'failed' && row.failCount > 0));
        const result = structuredClone({ items: matched.slice((page - 1) * pageSize, page * pageSize), total: matched.length, page, pageSize });
        if (pauseSubscriptions) { pauseSubscriptions = false; await new Promise(resolve => subscriptionReads.push(resolve)); }
        if (failure === 'subscriptionsCatalogUnavailable') return json({ error: 'Subscription catalog unavailable.' }, 503);
        return json(failure === 'subscriptionsCatalogMalformed' ? { ...result, items: undefined } : result);
      }
      if (/^\/api\/reports\/\d+\/parameters$/.test(route)) {
        const result = structuredClone(parameterDefinitions);
        if (pauseParameters) { pauseParameters = false; await new Promise(resolve => parameterReads.push(resolve)); }
        if (failure === 'subscriptionsParametersUnavailable') return json({ error: 'Parameter definitions unavailable.' }, 503);
        return json(failure === 'subscriptionsParametersMalformed' ? {} : result);
      }
      const historyId = route.match(/^\/api\/subscriptions\/(\d+)\/history$/);
      if (historyId) {
        const id = Number(historyId[1]);
        const result = [{ id, jobId: 'delivery-' + id, jobName: 'Delivery ' + id, status: 'SUCCESS',
          startTime: '2026-10-09T12:00:00Z', endTime: '2026-10-09T12:00:01Z', rowsProcessed: id * 10,
          errorMessage: null, peakMemoryBytes: 0, cpuTimeSeconds: 0, scriptHashAtRunTime: null,
          hashMatched: null, rowsQuarantined: 0, rowsWarned: 0, dataQualityFailures: null,
          checkpointLabel: null, tenantId: 'portal-host', hasResumeSession: false }];
        if (pauseSubscriptionHistory) { pauseSubscriptionHistory = false; await new Promise(resolve => subscriptionHistoryReads.push(resolve)); }
        if (failure === 'subscriptionsHistoryUnavailable') return json({ error: 'History unavailable.' }, 503);
        return json(failure === 'subscriptionsHistoryMalformed' ? [{}] : result);
      }
      const subscriptionId = route.match(/^\/api\/subscriptions\/(\d+)$/);
      if (subscriptionId && method === 'GET') {
        const row = subscriptionRows.find(item => item.id === Number(subscriptionId[1]));
        return row ? json(row) : json({ error: 'Subscription not found.' }, 404);
      }
      if (subscriptionId && method === 'DELETE') {
        const index = subscriptionRows.findIndex(item => item.id === Number(subscriptionId[1]));
        if (index < 0) return json({ error: 'Subscription not found.' }, 404);
        if (new Headers(init.headers).get('If-Match') !== `"${subscriptionRows[index].version}"`) return json({ error: 'Subscription changed.' }, 409);
        subscriptionRows.splice(index, 1);
        return new Response(null, { status: failure === 'subscriptionsDeleteUnconfirmed' ? 202 : 204 });
      }
      if (route === '/api/admin/subscriptions/bulk-status' && method === 'POST') {
        const results = body.subscriptions.map(target => {
          const row = subscriptionRows.find(item => item.id === target.id);
          if (!row) return { id: target.id, status: 'NotFound', currentVersion: null, error: null };
          if (row.version !== target.version || failure === 'subscriptionsBulkPartial' && row.id === 61)
            return { id: row.id, status: 'Conflict', currentVersion: row.version, error: null };
          row.isActive = body.isActive; ++row.version;
          return { id: row.id, status: 'Updated', currentVersion: row.version, error: null };
        });
        return json({ updated: results.filter(result => result.status === 'Updated').length, results });
      }
      if (subscriptionId && method === 'PUT') {
        const row = subscriptionRows.find(item => item.id === Number(subscriptionId[1]));
        if (!row) return json({ error: 'Subscription not found.' }, 404);
        if (pauseWrite) { pauseWrite = false; await new Promise(resolve => writes.push(resolve)); }
        if (new Headers(init.headers).get('If-Match') !== `"${row.version}"`) return json({ error: 'Subscription changed.', currentVersion: row.version }, 409);
        const changes = { ...body };
        if (changes.parameters == null) delete changes.parameters;
        else if (!Object.keys(changes.parameters).length) changes.parameters = null;
        Object.assign(row, changes, { version: row.version + 1 });
        row.parameterSummary = Object.entries(row.parameters || {}).map(([key, value]) => `${key}=${value}`).join(', ') || null;
        if (failure === 'subscriptionsWriteUnavailable') return json({ error: 'Response lost after saving.' }, 500);
        return json(failure === 'subscriptionsReceiptMalformed' ? { ...row, id: 999 } : row);
      }
      if (route === '/api/admin/audit' && method === 'GET') {
        const requestedPage = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 50);
        const action = url.searchParams.get('action'), matched = auditRows.filter(row => !action || row.action === action);
        const page = Math.min(Math.max(1, requestedPage), Math.max(1, Math.ceil(matched.length / pageSize)));
        const result = structuredClone({ items: matched.slice((page - 1) * pageSize, page * pageSize), total: matched.length, page, pageSize });
        if (pauseAudit) { pauseAudit = false; await new Promise(resolve => auditReads.push(resolve)); }
        return failure === 'auditFailed' ? json({ error: 'Audit catalog unavailable.' }, 503)
          : json(failure === 'auditInvalid' ? { ...result, items: [{ id: 525 }] } : result);
      }
      if (route === '/api/admin/audit/export/csv' && method === 'GET') {
        if (pauseExport) { pauseExport = false; await new Promise(resolve => exportReads.push(resolve)); }
        if (failure === 'auditExportFailed') return json({ error: 'Audit export unavailable.' }, 503);
        if (failure === 'auditExportInvalid') return json({ error: 'Unexpected export body.' });
        if (failure === 'auditExportEmpty') return new Response('', { headers: { 'Content-Type': 'text/csv' } });
        const action = url.searchParams.get('action'), matched = auditRows.filter(row => !action || row.action === action);
        return new Response('Id,Timestamp,UserId,Username,Action,ResourceType,ResourceId,Detail,CorrelationId\r\n'
          + matched.map(row => [row.id, row.timestamp, row.userId ?? '', row.username ?? '', row.action,
            row.resourceType, row.resourceId, row.detail, ''].join(',')).join('\r\n'),
        { headers: { 'Content-Type': 'text/csv; charset=utf-8' } });
      }
      if (route === '/api/studio/session') return failure === 'sessionFailed' ? json({ error: 'Studio policy unavailable.' }, 503)
        : json(failure === 'sessionInvalid' ? { mode: 'Unexpected' } : studioSession);
      if (route === '/api/admin/reports' && method === 'GET') {
        const result = structuredClone(reports);
        if (pauseReports) { pauseReports = false; await new Promise(resolve => reportReads.push(resolve)); }
        return failure === 'reportsFailed' ? json({ error: 'Report catalog unavailable.' }, 503)
          : failure === 'reportsInvalid' ? json([{ id: 40 }]) : json(result);
      }
      if (route === '/api/reports/available-scripts' && method === 'GET') {
        if (pauseScripts) { pauseScripts = false; await new Promise(resolve => scriptReads.push(resolve)); }
        return failure === 'scriptsFailed' ? json({ error: 'Script catalog unavailable.' }, 503)
          : json(failure === 'scriptsInvalid' ? [42] : ['reports/finance.rptsql', 'reports/operations.rptsql']);
      }
      if (route === '/api/reports/validate' && method === 'POST') {
        const result = { isValid: failure !== 'validationFailed', scriptPath: body.scriptPath,
          hash: 'sha256:' + (body.scriptPath.includes('finance') ? 'a' : 'b').repeat(64), lastModified: '2026-10-09T12:00:00Z',
          metadata: {}, parameters: [], errors: failure === 'validationFailed' ? ['The script is invalid.'] : [], impact: null };
        if (pauseValidation) { pauseValidation = false; await new Promise(resolve => validations.push(resolve)); }
        return json(result, result.isValid ? 200 : 400);
      }
      const reportRoute = route.match(/^\/api\/reports\/(\d+)$/);
      if (reportRoute && method === 'GET') {
        const report = reports.find(item => item.id === Number(reportRoute[1]));
        return report ? json(reportDto(report)) : json({ error: 'Report not found.' }, 404);
      }
      if (route === '/api/admin/roles') return failure === 'rolesFailed' ? json({ error: 'Role catalog unavailable.' }, 503)
        : failure === 'rolesInvalid' ? json([{ name: 42 }]) : json(roles.map(name => ({ name })));
      if (route === '/api/folders' && method === 'GET') return failure === 'foldersFailed'
        ? json({ error: 'Folder catalog unavailable.' }, 503) : json(failure === 'foldersInvalid' ? [{ id: 20 }] : folders);
      const folderRoute = route.match(/^\/api\/folders\/(\d+)$/);
      if (folderRoute && method === 'GET') {
        const folder = folderRows().find(item => item.id === Number(folderRoute[1]));
        return folder ? json({ ...folder, children: [] }) : json({ error: 'Folder not found.' }, 404);
      }
      const aclRoute = route.match(/^\/api\/folders\/(\d+)\/acl(?:\/catalog)?$/);
      if (aclRoute && method === 'GET') {
        const target = folderRows().find(folder => folder.id === Number(aclRoute[1]));
        if (!target) return json({ error: 'Folder not found.' }, 404);
        const entries = folderAcls.get(target.id) || [];
        const result = structuredClone(route.endsWith('/catalog')
          ? { folderId: target.id, folderName: target.name, version: target.version, items: entries, permissions: permissionOptions } : entries);
        if (pauseAcl) { pauseAcl = false; await new Promise(resolve => aclReads.push(resolve)); }
        return failure === 'aclFailed' ? json({ error: 'Folder permissions unavailable.' }, 503)
          : failure === 'aclInvalid' ? json({ ...result, items: [{ groupId: 10 }] }) : json(result);
      }
      if (method === 'GET' && (route === '/api/admin/users' || route === '/api/admin/users/catalog')) {
        const q = (url.searchParams.get('q') || '').toLowerCase();
        const status = url.searchParams.get('status');
        const matched = users.filter(item => item.username.toLowerCase().includes(q)
          && (status !== 'active' || item.isActive) && (status !== 'inactive' || !item.isActive));
        const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 25);
        const result = failure === 'usersInvalid' ? { items: [{ id: 2 }], total: 1, page, pageSize }
          : { items: structuredClone(matched.slice((page - 1) * pageSize, page * pageSize)), total: matched.length, page, pageSize };
        if (pauseRead) { pauseRead = false; await new Promise(resolve => reads.push(resolve)); }
        return failure === 'usersFailed' ? json({ error: 'User catalog unavailable.' }, 503) : json(result);
      }
      if (method === 'GET' && (route === '/api/admin/groups' || route === '/api/admin/groups/catalog')) {
        if (failure === 'groupsFailed') return json({ error: 'Group catalog unavailable.' }, 503);
        const matched = groups.filter(item => item.name.toLowerCase().includes((url.searchParams.get('q') || '').toLowerCase()));
        const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 25);
        const result = structuredClone(route.endsWith('/catalog')
          ? { items: matched.slice((page - 1) * pageSize, page * pageSize), total: matched.length, page, pageSize } : matched);
        if (pauseRead) { pauseRead = false; await new Promise(resolve => reads.push(resolve)); }
        return json(result);
      }
      const groupRoute = route.match(/^\/api\/admin\/groups\/(\d+)(.*)$/);
      const group = groupRoute ? groups.find(item => item.id === Number(groupRoute[1])) : null;
      if (groupRoute && method === 'GET') {
        if (!group) return json({ error: 'Group not found.' }, 404);
        if (!groupRoute[2]) return json(group);
        const matched = users.filter(item => memberships.get(group.id)?.includes(item.id)
          && item.username.includes(url.searchParams.get('q') || ''));
        const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 25);
        const result = { groupId: group.id, version: group.version, items: matched.slice((page - 1) * pageSize, page * pageSize)
          .map(item => ({ id: item.id, username: item.username, email: item.email, isActive: item.isActive })), total: matched.length, page, pageSize };
        const snapshot = structuredClone(result);
        if (pauseMembers) { pauseMembers = false; await new Promise(resolve => memberReads.push(resolve)); }
        return failure === 'membersFailed' ? json({ error: 'Membership service unavailable.' }, 503)
          : failure === 'membersInvalid' ? json({ ...snapshot, items: [{ id: 2 }] }) : json(snapshot);
      }
      if (pauseWrite) { pauseWrite = false; await new Promise(resolve => writes.push(resolve)); }
      if (failure === 'writeFailed' || (failure === 'resetFailed' && route.endsWith('/reset-password')))
        return json({ error: 'The requested change was rejected.' }, 400);
      if (route === '/api/reports' && method === 'POST') {
        const folder = folderRows().find(item => item.id === body.folderId);
        if (!folder) return json({ error: 'Folder not found.' }, 404);
        const created = { ...body, id: nextReportId++, folderName: folder.name, folderPath: folder.path,
          createdAt: '2026-10-09T12:00:00Z', updatedAt: '2026-10-09T12:00:00Z', version: 1 };
        reports.push(created); return json(failure === 'reportsInvalidReceipt' ? { ...reportDto(created), name: 'Unexpected report' } : reportDto(created), 201);
      }
      if (reportRoute) {
        const report = reports.find(item => item.id === Number(reportRoute[1]));
        if (!report) return json({ error: 'Report not found.' }, 404);
        if (new Headers(init.headers).get('If-Match') !== `"${report.version}"`) return json({ error: 'The report has changed.' }, 409);
        if (method === 'PUT') {
          Object.assign(report, body); report.version++;
          const folder = folderRows().find(item => item.id === report.folderId);
          report.folderName = folder.name; report.folderPath = folder.path;
          return failure === 'reportsServerFailure' ? json({ error: 'Response was lost after saving.' }, 500)
            : json(failure === 'reportsInvalidReceipt' ? { ...reportDto(report), id: 999 } : reportDto(report));
        }
        if (method === 'DELETE') { reports = reports.filter(item => item !== report); return new Response(null, { status: failure === 'reportsDeleteUnconfirmed' ? 202 : 204 }); }
      }
      if (route === '/api/folders' && method === 'POST') {
        const parent = body.parentId === null ? null : folderRows().find(item => item.id === body.parentId);
        if (body.parentId !== null && !parent) return json({ error: 'Parent folder not found.' }, 404);
        const created = { id: nextFolderId++, parentId: body.parentId, name: body.name,
          path: `${parent?.path || ''}/${body.name}`, version: 1, children: [] };
        (parent ? parent.children : folders).push(created);
        return json(failure === 'foldersInvalidReceipt' ? { ...created, name: 'Unexpected folder' } : created, 201);
      }
      const folderMutation = route.match(/^\/api\/folders\/(\d+)(?:\/acl(?:\/(\d+))?)?$/);
      if (folderMutation) {
        const folder = folderRows().find(item => item.id === Number(folderMutation[1]));
        if (!folder) return json({ error: 'Folder not found.' }, 404);
        if (new Headers(init.headers).get('If-Match') !== `"${folder.version}"`) return json({ error: 'The folder has changed.' }, 409);
        if (route.includes('/acl')) {
          const entries = folderAcls.get(folder.id) || [], groupId = body?.groupId ?? Number(folderMutation[2]);
          const group = groups.find(item => item.id === groupId), entry = entries.find(item => item.groupId === groupId);
          if (!group || (method === 'DELETE' && !entry)) return json({ error: 'Group permission not found.' }, 404);
          if (method === 'POST' && !permissionOptions.some(option => option.permission === body.permission)) return json({ error: 'Unknown permission.' }, 400);
          folder.version++;
          if (method === 'POST') {
            if (entry) entry.permission = body.permission; else entries.push({ groupId, groupName: group.name, permission: body.permission });
            folderAcls.set(folder.id, entries);
          } else folderAcls.set(folder.id, entries.filter(item => item !== entry));
          return json({ folderId: failure === 'foldersInvalidReceipt' ? 999 : folder.id, groupId,
            ...(method === 'POST' ? { permission: body.permission } : {}), version: folder.version });
        }
        if (method === 'PUT') {
          const parent = body.parentId === null ? null : folderRows().find(item => item.id === body.parentId);
          if (body.parentId !== null && !parent) return json({ error: 'Parent folder not found.' }, 404);
          if (parent && (parent === folder || parent.path.startsWith(folder.path + '/'))) return json({ error: 'A descendant cannot be a parent.' }, 400);
          detachFolder(folder); (parent ? parent.children : folders).push(folder);
          folder.parentId = body.parentId; folder.name = body.name; folder.version++;
          folder.path = `${parent?.path || ''}/${folder.name}`; updateFolderPaths(folder);
          return json({ ...folder, children: [] });
        }
        if (method === 'DELETE') {
          const pending = [folder];
          while (pending.length) { const item = pending.pop(); folderAcls.delete(item.id); pending.push(...item.children); }
          detachFolder(folder);
          return new Response(null, { status: failure === 'foldersDeleteUnconfirmed' ? 202 : 204 });
        }
      }
      if (route === '/api/admin/groups' && method === 'POST') {
        const created = { id: nextGroupId++, name: body.name, description: body.description, memberCount: 0, provider: 'Local', adGroup: null, version: 1 };
        groups.push(created); memberships.set(created.id, []);
        return json(failure === 'groupsInvalidReceipt' ? { ...created, name: 'Unexpected group' } : created, 201);
      }
      if (route === '/api/admin/groups/bulk-delete' && method === 'POST') {
        const results = body.groups.map(item => {
          const target = groups.find(group => group.id === item.id);
          if (!target) return { id: item.id, status: 'NotFound', currentVersion: null, error: null };
          if (target.memberCount) return { id: item.id, status: 'Blocked', currentVersion: target.version, error: 'Group has members or ACL entries.' };
          if (target.version !== item.version) return { id: item.id, status: 'Conflict', currentVersion: target.version, error: null };
          groups = groups.filter(group => group.id !== item.id);
          return { id: item.id, status: 'Deleted', currentVersion: null, error: null };
        });
        return json({ deleted: results.filter(item => item.status === 'Deleted').length, results });
      }
      if (groupRoute && group) {
        if (new Headers(init.headers).get('If-Match') !== `"${group.version}"`) return json({ error: 'The group has changed.' }, 409);
        if (method === 'DELETE' && !groupRoute[2] && group.memberCount) return json({ error: 'Group has members or ACL entries.' }, 409);
        group.version++;
        if (method === 'PUT') { Object.assign(group, body); return json(group); }
        if (groupRoute[2].includes('/members')) {
          const ids = body?.userIds || [Number(groupRoute[2].split('/').at(-1))];
          const before = memberships.get(group.id) || [];
          const adding = groupRoute[2].endsWith('/bulk-add');
          const changed = ids.filter(id => adding ? !before.includes(id) && users.some(item => item.id === id) : before.includes(id));
          memberships.set(group.id, adding ? [...before, ...changed] : before.filter(id => !changed.includes(id)));
          group.memberCount = memberships.get(group.id).length;
          if (method === 'DELETE') return json({ groupId: group.id, userId: ids[0], version: group.version });
          const result = { groupId: group.id, version: group.version, changedUserIds: changed, skippedUserIds: ids.filter(id => !changed.includes(id)),
            ...(adding ? { added: changed.length } : { removed: changed.length }) };
          return json(failure === 'groupsInvalidReceipt' ? { ...result, groupId: 999 } : result);
        }
        if (method === 'DELETE') { groups = groups.filter(item => item !== group); return new Response(null, { status: failure === 'groupsDeleteUnconfirmed' ? 202 : 204 }); }
      }
      if (route === '/api/admin/users' && method === 'POST') {
        if (body.role && !roles.includes(body.role)) return json({ error: 'The requested role does not exist.' }, 400);
        const created = { ...user(nextId++, body.username, body.role), email: body.email, mustChangePassword: true };
        users.push(created);
        return json(failure === 'invalidReceipt' ? { ...created, roles: ['Admin'] } : created, 201);
      }
      if (route === '/api/admin/users/bulk-status') {
        const results = body.users.map(request => {
          const target = users.find(item => item.id === request.id);
          if (!target) return { id: request.id, status: 'NotFound', currentVersion: null, error: null };
          if (request.version !== target.version) return { id: target.id, status: 'Conflict', currentVersion: target.version, error: null };
          target.isActive = body.isActive; target.version++;
          return { id: target.id, status: 'Updated', currentVersion: target.version, error: null };
        });
        return json({ updated: results.filter(item => item.status === 'Updated').length, results });
      }
      const targetId = Number(route.split('/')[4]), target = users.find(item => item.id === targetId);
      if (target && method !== 'GET') {
        if (new Headers(init.headers).get('If-Match') !== `"${target.version}"`) return json({ error: 'The user has changed.' }, 409);
        if (route.endsWith('/reset-password')) { target.version++; target.mustChangePassword = true; return new Response(null, { status: 204 }); }
        if (method === 'DELETE') { users = users.filter(item => item !== target); return new Response(null, { status: 204 }); }
        if (method === 'PUT') {
          Object.assign(target, Object.fromEntries(Object.entries(body).filter(([key]) => key !== 'role')));
          if (body.role != null) target.roles = [body.role];
          target.version++;
          return json(failure === 'invalidReceipt' ? { ...target, id: 999 } : target);
        }
      }
      return json({ error: `Unconfigured admin fixture route: ${method} ${route}` }, 404);
    }
  };
  return mock;
}

export async function mountAdminPageFrame(fixtureId) {
  const page = await fetch('/src/ETL-SQL.Portal/wwwroot/admin.html').then(response => response.text());
  document.documentElement.innerHTML = page.replace(/<script\b[^>]*>[\s\S]*?<\/script>/g, '')
    .replace(/(href|src)="\/(css|js|designer)\//g, '$1="/src/ETL-SQL.Portal/wwwroot/$2/');
  const payload = { sub: '1', unique_name: 'sandbox-admin', role: 'Admin', exp: 4102444800 };
  sessionStorage.setItem('etlsql_token', 'sandbox.' + btoa(JSON.stringify(payload)) + '.sandbox');
  sessionStorage.removeItem('etlsql_refresh');
  window.__adminMock = makeAdminPageSandbox(fixtureId);
  const originalAnchorClick = HTMLAnchorElement.prototype.click;
  const originalCreateUrl = URL.createObjectURL.bind(URL), originalRevokeUrl = URL.revokeObjectURL.bind(URL);
  URL.createObjectURL = blob => {
    const url = originalCreateUrl(blob); window.__adminMock.createdUrls.push(url); return url;
  };
  URL.revokeObjectURL = url => { window.__adminMock.revokedUrls.push(url); originalRevokeUrl(url); };
  HTMLAnchorElement.prototype.click = function () {
    if (this.download && this.href.startsWith('blob:')) { window.__adminMock.recordDownload({ fileName: this.download, href: this.href }); return; }
    return originalAnchorClick.call(this);
  };
  window.fetch = window.__adminMock.fetch;
  await import('/src/ETL-SQL.Portal/wwwroot/js/feedback.js');
  window.__adminFeedbackMessages = [];
  const feedback = window.ETLSQLFeedback;
  window.ETLSQLFeedback = { ...feedback, notify(message, options) {
    window.__adminFeedbackMessages.push({ message, options });
    return feedback.notify(message, options);
  } };
  await import('/src/ETL-SQL.Portal/wwwroot/js/pages/admin.js?v=' + Date.now());
  document.body.dataset.adminSandboxReady = 'true';
  if (fixtureId.startsWith('groups') || fixtureId.startsWith('members')) document.querySelector(".admin-tab[data-tab='groups']").click();
  if (fixtureId.startsWith('folders') || fixtureId.startsWith('acl')) document.querySelector(".admin-tab[data-tab='folders']").click();
  if (fixtureId.startsWith('reports')) document.querySelector(".admin-tab[data-tab='reports']").click();
  if (fixtureId.startsWith('audit')) document.querySelector(".admin-tab[data-tab='audit']").click();
  if (fixtureId.startsWith('subscriptions')) document.querySelector(".admin-tab[data-tab='subscriptions']").click();
  if (fixtureId.startsWith('settings') || fixtureId.startsWith('branding')) document.querySelector(".admin-tab[data-tab='settings']").click();
}

export default {
  id: 'admin-page', title: 'Admin — production page',
  fixtures: [{ id: 'healthy', label: 'Role catalog and users' }, { id: 'empty', label: 'Empty users' },
    { id: 'usersFailed', label: 'Users unavailable' }, { id: 'usersInvalid', label: 'Malformed users' },
    { id: 'rolesFailed', label: 'Roles unavailable' }, { id: 'rolesInvalid', label: 'Malformed roles' },
    { id: 'groups', label: 'Groups and memberships' }, { id: 'groupsFailed', label: 'Groups unavailable' },
    { id: 'folders', label: 'Nested folders and permissions' }, { id: 'foldersFailed', label: 'Folders unavailable' },
    { id: 'reports', label: 'Reports and source publishing' }, { id: 'reportsFailed', label: 'Reports unavailable' },
    { id: 'audit', label: 'Audit catalog beyond ten pages' }, { id: 'auditFailed', label: 'Audit unavailable' },
    { id: 'subscriptions', label: 'Subscription parameters and history' },
    { id: 'settings', label: 'Connection settings and branding' },
    { id: 'settingsLateRead', label: 'Connection settings arriving after edits' },
    { id: 'settingsLatePublicBranding', label: 'Initial branding arriving after a save' }],
  async mount(stage, fixtureId, ctx) {
    const savedToken = sessionStorage.getItem('etlsql_token'), savedRefresh = sessionStorage.getItem('etlsql_refresh');
    const frame = document.createElement('iframe');
    frame.title = `Admin — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0';
    const loaded = new Promise(resolve => frame.addEventListener('load', resolve, { once: true }));
    frame.src = `/tools/ui-sandbox/admin-page-frame.html?fixture=${encodeURIComponent(fixtureId)}&v=${Date.now()}`;
    stage.replaceChildren(frame);
    await loaded;
    ctx.stat('Production admin page with fixture account and role metadata');
    return { dispose() {
      frame.contentWindow?.dispatchEvent(new Event('pagehide'));
      frame.remove();
      if (savedToken === null) sessionStorage.removeItem('etlsql_token'); else sessionStorage.setItem('etlsql_token', savedToken);
      if (savedRefresh === null) sessionStorage.removeItem('etlsql_refresh'); else sessionStorage.setItem('etlsql_refresh', savedRefresh);
    }, resize() {} };
  }
};
