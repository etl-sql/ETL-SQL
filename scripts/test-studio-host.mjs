import assert from 'node:assert/strict';
import { createStudioHostAdapter } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-host.js';

const desktop = createStudioHostAdapter();
assert.equal(desktop.hasWorkspaceHost, true);
assert.equal(desktop.hasGitHost, false);
assert.equal(desktop.apiBase, '');
assert.equal(desktop.hasCapability({ deploymentMode: 'Desktop', capabilities: new Set() }, 'Report.Edit'), true);
const portal = createStudioHostAdapter({ deploymentMode: 'Portal', apiBase: '/tenant/api' });
assert.equal(portal.hasWorkspaceHost, false);
assert.equal(portal.apiBase, '/tenant/api');
assert.equal(portal.hasCapability({ deploymentMode: 'Portal', capabilities: new Set(['Report.Read']) }, 'Report.Edit'), false);
assert.equal(portal.hasCapability({ deploymentMode: 'Portal', capabilities: new Set(['Report.Edit']) }, 'Report.Edit'), true);
assert.equal(createStudioHostAdapter({ hasWorkspaceHost: false }).hasWorkspaceHost, false);
assert.equal(createStudioHostAdapter({ deploymentMode: 'Portal', hasWorkspaceHost: true }).hasWorkspaceHost, true);

const callback = async () => [];
assert.equal(createStudioHostAdapter({ onLoadGitStatus: callback, onLoadGitHistory: callback }).hasGitHost, false);
assert.equal(createStudioHostAdapter({ onLoadGitStatus: callback, onLoadGitHistory: callback, onLoadGitDiff: callback }).hasGitHost, true);
assert.equal(createStudioHostAdapter({ onLoadGitStatus: callback, onLoadGitHistory: callback, onLoadGitDiff: {} }).hasGitHost, false);

const originalFetch = globalThis.fetch;
const calls = [];
const response = new Response('ok');
globalThis.fetch = async (...args) => { calls.push(args); return response; };
try {
    const adapter = createStudioHostAdapter({ headers: { 'X-Host': 'fixture', Shared: 'host' } });
    const controller = new AbortController();
    assert.equal(await adapter.authFetch('/api/example', {
        method: 'POST', body: 'body', signal: controller.signal, headers: { Shared: 'request' },
    }), response);
    assert.deepEqual(calls[0], ['/api/example', {
        method: 'POST', body: 'body', signal: controller.signal, headers: { 'X-Host': 'fixture', Shared: 'request' },
    }]);
    await adapter.authFetch('/api/default');
    assert.deepEqual(calls[1], ['/api/default', { headers: { 'X-Host': 'fixture', Shared: 'host' } }]);
    const customFetch = async () => response;
    assert.equal(createStudioHostAdapter({ authFetch: customFetch }).authFetch, customFetch);
} finally { globalThis.fetch = originalFetch; }
console.log('Studio host: defaults, capability checks, Git availability, fetch override and header precedence passed');
