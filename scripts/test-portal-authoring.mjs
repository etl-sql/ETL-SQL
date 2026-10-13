import assert from 'node:assert/strict';
import path from 'node:path';
import { importSourceModule } from './lib/import-source-module.mjs';

const responseModule = await importSourceModule(path.resolve('src/ETL-SQL.Portal/wwwroot/js/authoring-responses.js'));
const hostModule = await importSourceModule(path.resolve('src/ETL-SQL.Portal/wwwroot/js/portal-studio-host.js'));
try {
    const responses = responseModule.module;
    const date = '2026-10-07T12:00:00Z';
    const session = { mode: 'Standalone', capabilities: ['ScriptRead', 'ScriptSave'], sourceControlEnabled: false, draftRecovery: true };
    const report = { id: 7, folderId: 3, folderPath: '/Authors', name: 'Report', description: null, updatedAt: date, version: 4, kind: 'Report' };
    const script = { scriptText: '-- preserve authored SQL\nSELECT 1;', version: 4, sourceRevision: null, sourceControlEnabled: false };
    const lease = { acquired: true, owner: 'author', expiresAt: date };
    const draft = { content: 'SELECT 2;', baseVersion: 4, baseSourceRevision: null, updatedAt: date };
    const state = { pages: [{ id: 'p1', name: 'Main', mode: 'Dashboard', visuals: [{
        id: 'v1', name: 'Revenue', type: 'BAR', gridCol: 1, gridRow: 1, gridColSpan: 6, gridRowSpan: 4,
        mappings: { X: 'Region', Y: 'Revenue' }, options: {}, formatting: { title: { text: null },
            fields: { Revenue: { dataBar: true, format: null } } }
    }] }], datasets: [{ id: 'd1', name: 'data', query: 'SELECT 1;' }] };
    const snapshot = { reportName: 'Report', builtAt: date, sampleRows: { Revenue: [['North', '120'], ['West', null]] },
        columns: { Revenue: ['Region', 'Revenue'] }, metadata: { isSampled: false, rlsEnforced: false, totalRows: 2, returnedRows: 2 } };
    assert.deepEqual(responses.readStudioSession(session), { ...session, privateDrafts: false });
    assert.equal(responses.readStudioSession({ ...session, privateDrafts: true }).privateDrafts, true);
    assert.deepEqual(responses.readStudioReports([report]), [report]);
    assert.deepEqual(responses.readStudioFolders([{ id: 3, path: '/Authors', name: 'Authors' }]), [{ id: 3, path: '/Authors', name: 'Authors' }]);
    assert.deepEqual(responses.readScriptContent(script), script);
    assert.deepEqual(responses.readDesignerLease(lease), lease);
    assert.deepEqual(responses.readStudioDraft(draft), draft);
    const normalized = responses.readDesignerParse({ designState: state, error: null });
    assert.equal(normalized.pages[0].visuals[0].formatting.title.text, undefined);
    assert.equal(normalized.pages[0].visuals[0].formatting.fields.Revenue.format, undefined);
    assert.deepEqual(responses.readDesignerSnapshot(snapshot).columnsByVisual, snapshot.columns);
    assert.deepEqual(responses.readDesignerSnapshot(snapshot).sampleRows, snapshot.sampleRows);
    assert.throws(() => responses.readDesignerParse({ error: 'Syntax failed', designState: null }), /Syntax failed/);
    const invalid = [
        ['readStudioSession', { ...session, capabilities: [42] }],
        ['readStudioReports', [{ ...report, version: '4' }]],
        ['readStudioFolders', [{ id: 3, path: null, name: 'Authors' }]],
        ['readScriptContent', { ...script, scriptText: null }],
        ['readDesignerLease', { ...lease, acquired: 'true' }],
        ['readDesignerSave', { version: 0 }],
        ['readStudioDraft', { ...draft, updatedAt: 'not a date' }],
        ['readDesignerParse', { designState: { ...state, pages: [{ ...state.pages[0], visuals: [{ ...state.pages[0].visuals[0], options: { TITLE: {} } }] }] } }],
        ['readDesignerSnapshot', { ...snapshot, columns: { Revenue: 'wrong' } }],
        ['readDesignerSnapshot', { ...snapshot, sampleRows: { Revenue: [['North', undefined]] } }]
    ];
    for (const [name, value] of invalid) assert.throws(() => responses[name](value), /Invalid/, name);

    const requests = [];
    let leaseStatus = 200, savePayload = { version: 5, sourceRevision: 'new-revision' }, releaseStatus = 204;
    const fetch = async (url, options = {}) => {
        requests.push({ url, ...options });
        if (url.endsWith('/script-content')) return Response.json(script);
        if (url === '/api/designer/lease') return leaseStatus === 200 ? Response.json(lease)
            : Response.json({ error: 'Already leased', owner: 'Another author', expiresAt: date }, { status: leaseStatus });
        if (url === '/api/designer/save') return Response.json(savePayload);
        if (url.startsWith('/api/designer/lease/')) return new Response(null, { status: releaseStatus });
        if (url.startsWith('/api/studio/drafts/')) return options.method === 'PUT'
            ? new Response(null, { status: 422 }) : Response.json(draft);
        throw new Error('Unexpected request: ' + url);
    };
    const host = hostModule.module.createPortalStudioHost(session, fetch, async request => ({ ...report, name: request.name }));
    const opened = await host.onOpenDocument(report);
    assert.equal(opened.content, script.scriptText);
    assert.equal(opened.path, '/Authors/Report.rptsql');
    assert.equal(opened.canSave, true);
    assert.deepEqual(await host.onSave('SELECT 2;', opened.path, opened), savePayload);
    const save = requests.find(request => request.url === '/api/designer/save');
    assert.equal(save.headers['If-Match'], '"4"');
    assert.deepEqual(JSON.parse(save.body), { reportId: 7, scriptText: 'SELECT 2;', baseRevision: null });
    savePayload = { version: 'wrong' };
    await assert.rejects(host.onSave('SELECT 3;', opened.path, opened), /Invalid report save/);
    await assert.rejects(host.onSave('SELECT 3;', '', { reportId: 7, version: 4, lease: { acquired: false } }), /active edit lease/);
    assert.deepEqual(await host.onLoadDraft(opened), draft);
    assert.equal(await host.onSaveDraft(opened, draft, { keepalive: true }), 'refused');
    assert.equal(requests.find(request => request.method === 'PUT').keepalive, true);
    leaseStatus = 409;
    const conflict = await host.onOpenDocument(report);
    assert.equal(conflict.canSave, false);
    assert.match(conflict.readOnlyReason, /Another author is editing/);
    leaseStatus = 200;
    assert.equal((await host.onReacquireDocument(opened)).content, script.scriptText);
    releaseStatus = 503;
    await assert.rejects(host.onCloseDocument(opened), /could not be released/);
    assert.equal(opened.lease.acquired, true);
    releaseStatus = 204;
    await host.onCloseDocument(opened);
    assert.equal(opened.lease.acquired, false);
    const readOnlyHost = hostModule.module.createPortalStudioHost({ ...session, capabilities: ['ScriptRead'], draftRecovery: false }, fetch, async () => report);
    const before = requests.length;
    assert.equal((await readOnlyHost.onOpenDocument(report)).canSave, false);
    assert.equal(requests.length, before + 1, 'A read-only author must not request an edit lease.');
    assert.equal('onLoadDraft' in readOnlyHost, false);
    await assert.rejects(host.onCreateDocument({ folderId: 'bad', name: 'Report', scriptText: '' }), /Invalid/);
    assert.equal((await host.onCreateDocument({ folderId: '3', name: 'New', scriptText: 'SELECT 1;' })).name, 'New');
    console.log('Portal authoring: DTO validation, snapshot mappings/null cells, source preservation, version fencing, leases, and recovery callbacks passed.');
} finally {
    await responseModule.cleanup();
    await hostModule.cleanup();
}
