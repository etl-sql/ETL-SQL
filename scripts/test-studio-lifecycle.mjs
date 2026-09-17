import assert from 'node:assert/strict';
import {
    createStudioLeaseLifecycle,
    isDraftStoragePermitted,
    saveDraftRecord,
    getRecoverableDraftRecord,
    removeDraftRecord
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js';

const originalWindow = globalThis.window;
const originalLocalStorage = globalThis.localStorage;

const mockStorage = new Map();
globalThis.localStorage = {
    getItem(key) { return mockStorage.get(key) ?? null; },
    setItem(key, value) { mockStorage.set(key, String(value)); },
    removeItem(key) { mockStorage.delete(key); },
    clear() { mockStorage.clear(); }
};

const listeners = new Map();
let timer, interval, cleared;
globalThis.window = {
    setInterval(callback, delay) { timer = callback; interval = delay; return 1; },
    clearInterval(id) { cleared = id; },
    addEventListener(name, callback) { listeners.set(name, callback); },
    removeEventListener(name, callback) { if (listeners.get(name) === callback) listeners.delete(name); },
};

try {
    // 1. Lease renewal & loss lifecycle
    const saved = { id: 'doc1', reportId: 101, lease: { acquired: true, token: 'fixture' }, canSave: true, version: 1, isDirty: false, content: 'SELECT 1;' };
    const lost = { id: 'doc2', reportId: 102, lease: { acquired: true }, canSave: true, version: 1, isDirty: true, content: 'SELECT 2;' };
    const unopened = { id: 'doc3', reportId: 103, lease: { acquired: false }, canSave: false };
    const documents = [saved, lost, unopened];
    const contexts = new Map(documents.map(d => [d, { previewAbort: new AbortController(), dagAbort: new AbortController() }]));
    const renewed = [], released = [], notices = [];

    let reacquireCallCount = 0;
    const lifecycle = createStudioLeaseLifecycle({
        state: { documents },
        options: {
            onRenewDocument: async d => {
                renewed.push(d);
                if (d === lost) throw new Error('Lease revoked');
                return { expires: 'later' };
            },
            onReacquireDocument: async d => {
                reacquireCallCount++;
                return { lease: { acquired: true, token: 'reacquired' }, version: 2, content: 'SELECT 200;' };
            },
            onCloseDocument: (document, options) => released.push({ document, ...options }),
        },
        documentContext: d => contexts.get(d),
        feedback: { notify: (...args) => notices.push(args) },
    });

    assert.equal(interval, 240000);
    await timer();
    assert.deepEqual(renewed, [saved, lost]);
    assert.deepEqual(saved.lease, { acquired: true, token: 'fixture', expires: 'later' });
    assert.equal(lost.lease.acquired, false);
    assert.equal(lost.canSave, false);
    assert.equal(lost.readOnlyReason, 'Lease revoked');

    // Notification on lease loss includes Reacquire action
    assert.equal(notices.length, 1);
    assert.equal(notices[0][0], 'Lease revoked');
    assert.equal(notices[0][1].title, 'Edit Lease Lost');
    assert.equal(notices[0][1].tone, 'warning');
    assert.equal(typeof notices[0][1].action?.onSelect, 'function');

    // Trigger reacquire via action
    await notices[0][1].action.onSelect();
    assert.equal(reacquireCallCount, 1);
    assert.equal(lost.lease.acquired, true);
    assert.equal(lost.canSave, true);
    assert.equal(lost.readOnlyReason, null);
    // Since lost doc isDirty, local content was NOT overwritten by server content, and warning was posted
    assert.equal(lost.content, 'SELECT 2;');
    assert.ok(notices.some(n => n[1]?.title === 'Server Revision Conflict'));

    // Test reacquire on clean doc: server content is adopted cleanly
    saved.isDirty = false;
    saved.version = 1;
    saved.content = 'SELECT 1;';
    await lifecycle.reacquire(saved, { silent: true });
    assert.equal(saved.version, 2);
    assert.equal(saved.content, 'SELECT 200;');

    // 2. Draft storage and Zero-Trust credential protection
    const dirtyDoc = { id: 'doc-edit', path: 'pipeline.etlsql', content: 'SELECT * FROM source;', isDirty: true, version: 5 };
    assert.equal(isDraftStoragePermitted(dirtyDoc.content), true);
    assert.equal(lifecycle.saveDraft(dirtyDoc), true);

    // When document buffer is reloaded without the draft changes (e.g. server content):
    const reloadedDoc = { id: 'doc-edit', path: 'pipeline.etlsql', content: '', isDirty: false };
    const draft = lifecycle.getRecoverableDraft(reloadedDoc);
    assert.ok(draft);
    assert.equal(draft.content, 'SELECT * FROM source;');
    assert.equal(draft.version, 5);

    // When recovered content matches current document content, returns null and cleans storage:
    const appliedDoc = { id: 'doc-edit', path: 'pipeline.etlsql', content: 'SELECT * FROM source;', isDirty: true };
    const recovered = lifecycle.getRecoverableDraft(appliedDoc);
    assert.equal(recovered, null);
    assert.equal(lifecycle.getRecoverableDraft(reloadedDoc), null);

    // Plaintext secrets MUST NOT be persisted to localStorage
    const secretDoc = { id: 'secret-doc', path: 'secret.etlsql', content: "CREATE CONNECTION x TYPE mssql (PASSWORD = 'mySecret123');", isDirty: true };
    assert.equal(isDraftStoragePermitted(secretDoc.content), false);
    assert.equal(lifecycle.saveDraft(secretDoc), false);
    assert.equal(lifecycle.getRecoverableDraft({ ...secretDoc, content: '' }), null);

    // Strict / ZeroTrust deployment modes MUST NOT persist drafts
    assert.equal(isDraftStoragePermitted('SELECT 1;', { deploymentMode: 'Strict' }), false);
    assert.equal(isDraftStoragePermitted('SELECT 1;', { deploymentMode: 'ZeroTrust' }), false);

    // Draft removal
    lifecycle.removeDraft(dirtyDoc);
    assert.equal(lifecycle.getRecoverableDraft(reloadedDoc), null);

    // 3. Online/offline listeners
    listeners.get('offline')();
    assert.ok(notices.some(n => n[1]?.title === 'Offline Mode'));

    const reacquireCallsBefore = reacquireCallCount;
    listeners.get('online')();
    assert.ok(notices.some(n => n[1]?.title === 'Back Online'));

    // 4. pagehide saves dirty drafts
    lost.isDirty = true;
    lost.content = 'SELECT 999;';
    listeners.get('pagehide')();
    const pagehideDraft = lifecycle.getRecoverableDraft({ reportId: 102, content: '' });
    assert.ok(pagehideDraft);
    assert.equal(pagehideDraft.content, 'SELECT 999;');

    // 5. Cleanup and dispose
    lifecycle.dispose();
    assert.deepEqual(released, [{ document: saved, keepalive: true }, { document: lost, keepalive: true }, { document: saved, keepalive: false }, { document: lost, keepalive: false }]);
    assert.equal(cleared, 1);
    assert.equal(listeners.size, 0);
    for (const context of contexts.values()) {
        assert.equal(context.previewAbort.signal.aborted, true);
        assert.equal(context.dagAbort.signal.aborted, true);
    }

    console.log('Studio lifecycle: lease renewal, loss, conflict-aware reacquisition, recoverable drafts, Zero-Trust guards, and online/offline handling all passed');
} finally {
    if (originalWindow === undefined) delete globalThis.window;
    else globalThis.window = originalWindow;
    if (originalLocalStorage === undefined) delete globalThis.localStorage;
    else globalThis.localStorage = originalLocalStorage;
}
