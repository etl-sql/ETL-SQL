import assert from 'node:assert/strict';
import { createStudioLeaseLifecycle } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js';

const originalWindow = globalThis.window;
const listeners = new Map();
let timer, interval, cleared;
globalThis.window = {
    setInterval(callback, delay) { timer = callback; interval = delay; return 1; },
    clearInterval(id) { cleared = id; },
    addEventListener(name, callback) { listeners.set(name, callback); },
    removeEventListener(name, callback) { if (listeners.get(name) === callback) listeners.delete(name); },
};
try {
    const saved = { lease: { acquired: true, token: 'fixture' }, canSave: true };
    const lost = { lease: { acquired: true }, canSave: true };
    const unopened = { lease: { acquired: false } };
    const documents = [saved, lost, unopened];
    const contexts = new Map(documents.map(d => [d, { previewAbort: new AbortController(), dagAbort: new AbortController() }]));
    const renewed = [], released = [], notices = [];
    const lifecycle = createStudioLeaseLifecycle({
        state: { documents },
        options: {
            onRenewDocument: async d => {
                renewed.push(d);
                if (d === lost) throw new Error('Lease revoked');
                return { expires: 'later' };
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
    assert.deepEqual(notices, [['Lease revoked', { title: 'Edit Lease Lost', tone: 'warning' }]]);
    listeners.get('pagehide')();
    lifecycle.dispose();
    assert.deepEqual(released, [{ document: saved, keepalive: true }, { document: saved, keepalive: false }]);
    assert.equal(cleared, 1);
    assert.equal(listeners.size, 0);
    for (const context of contexts.values()) {
        assert.equal(context.previewAbort.signal.aborted, true);
        assert.equal(context.dagAbort.signal.aborted, true);
    }

    const noMessage = { lease: { acquired: true }, canSave: true };
    const fallback = createStudioLeaseLifecycle({
        state: { documents: [noMessage] },
        options: { leaseRenewIntervalMs: 17, onRenewDocument: async () => { throw null; } },
        documentContext: () => ({}), feedback: { notify() {} },
    });
    assert.equal(interval, 17);
    await timer();
    assert.equal(noMessage.readOnlyReason, 'The edit lease expired. Reopen the report to continue editing.');
    assert.equal(noMessage.canSave, false);
    fallback.dispose();

    timer = undefined;
    const inactive = createStudioLeaseLifecycle({
        state: { documents: [] }, options: {}, documentContext: () => ({}), feedback: { notify() {} },
    });
    assert.equal(timer, undefined);
    assert.equal(listeners.size, 0);
    inactive.dispose();
    console.log('Studio lifecycle: renewal, lease loss, release, timer cleanup and aborts passed');
} finally {
    if (originalWindow === undefined) delete globalThis.window;
    else globalThis.window = originalWindow;
}
