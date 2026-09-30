/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeEach, afterEach, vi } from 'vitest';
import { JSDOM } from 'jsdom';

/**
 * Recovery drafts and lease renewal. Drafts go to the host, never to browser storage; a lease
 * survives a renewal that fails once; and a refusal the host means is not retried.
 */
describe('Studio lease and draft lifecycle', () => {
    let m: any;
    let dom: JSDOM;
    const notes: string[] = [];
    const feedback = { notify: (message: string) => { notes.push(message); } };

    beforeEach(async () => {
        dom = new JSDOM('<!doctype html><html><body></body></html>', { url: 'https://studio.test/' });
        (globalThis as any).window = dom.window;
        (globalThis as any).localStorage = dom.window.localStorage;
        notes.length = 0;
        m = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-lifecycle.js');
    });

    afterEach(() => {
        vi.useRealTimers();
        delete (globalThis as any).window;
        delete (globalThis as any).localStorage;
    });

    const lifecycle = (documents: any[], options: any) =>
        m.createStudioLeaseLifecycle({ state: { documents }, options, documentContext: () => ({}), feedback });

    test('a dirty document is kept on the host, and nothing is written to browser storage', async () => {
        const saved: any[] = [];
        const doc = { id: 'd1', reportId: 7, content: '-- edit', isDirty: true, version: 3, sourceRevision: 'r3' };
        const handle = lifecycle([doc], { onSaveDraft: async (_: any, draft: any, options: any) => { saved.push({ draft, options }); return 'kept'; } });

        expect(await handle.saveDraft(doc)).toBe('kept');
        expect(saved[0].draft).toEqual({ content: '-- edit', baseVersion: 3, baseSourceRevision: 'r3' });
        expect(saved[0].options).toEqual({ keepalive: false });
        expect(dom.window.localStorage.length).toBe(0);
        handle.dispose();
    });

    test('a draft with a plaintext credential never leaves the browser', async () => {
        const onSaveDraft = vi.fn(async () => 'kept');
        const doc = { id: 'd1', reportId: 7, isDirty: true, content: "CREATE CONNECTION db AS MSSQL(PASSWORD = 'hunter2');" };
        const handle = lifecycle([doc], { onSaveDraft });

        expect(await handle.saveDraft(doc)).toBe('refused');
        expect(onSaveDraft).not.toHaveBeenCalled();
        handle.dispose();
    });

    test('with no host draft callbacks nothing is kept, and an unreachable host is not an error', async () => {
        const doc = { id: 'd1', reportId: 7, isDirty: true, content: '-- edit' };
        const none = lifecycle([doc], {});
        expect(none.draftsKept).toBe(false);
        expect(await none.saveDraft(doc)).toBe('unavailable');
        none.dispose();

        const offline = lifecycle([doc], { onSaveDraft: async () => { throw new TypeError('Failed to fetch'); } });
        expect(await offline.saveDraft(doc)).toBe('unavailable');
        offline.dispose();
    });

    test('a draft kept in browser storage before v0.20.0 is offered once and then removed', async () => {
        dom.window.localStorage.setItem('etlsql_studio_draft:report_7', JSON.stringify({ content: '-- old edit', version: 2 }));
        const onRemoveDraft = vi.fn(async () => undefined);
        const doc = { id: 'd1', reportId: 7, content: '-- saved', isDirty: false };
        const handle = lifecycle([doc], { onLoadDraft: async () => null, onRemoveDraft });

        const draft = await handle.getRecoverableDraft(doc);
        expect(draft).toMatchObject({ content: '-- old edit', baseVersion: 2 });
        handle.removeDraft(doc);
        expect(dom.window.localStorage.getItem('etlsql_studio_draft:report_7')).toBeNull();
        expect(onRemoveDraft).toHaveBeenCalled();
        handle.dispose();
    });

    test('a draft identical to the document is not offered, and is cleared', async () => {
        const onRemoveDraft = vi.fn(async () => undefined);
        const doc = { id: 'd1', reportId: 7, content: '-- same' };
        const handle = lifecycle([doc], { onLoadDraft: async () => ({ content: '-- same' }), onRemoveDraft });
        expect(await handle.getRecoverableDraft(doc)).toBeNull();
        expect(onRemoveDraft).toHaveBeenCalled();
        handle.dispose();
    });

    test('a renewal that fails once keeps the lease', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const doc: any = { id: 'd1', reportId: 7, lease: { acquired: true } };
        const handle = lifecycle([doc], {
            leaseRenewIntervalMs: 1000,
            leaseRenewRetryDelaysMs: [10],
            onRenewDocument: async () => {
                calls++;
                if (calls === 1) throw new TypeError('Failed to fetch');
                return { acquired: true };
            }
        });

        await vi.advanceTimersByTimeAsync(1000);
        await vi.advanceTimersByTimeAsync(20);
        expect(calls).toBe(2);
        expect(doc.lease.acquired).toBe(true);
        expect(doc.readOnlyReason).toBeUndefined();
        handle.dispose();
    });

    test('a lease the host gives to someone else is dropped at once, not retried', async () => {
        vi.useFakeTimers();
        let calls = 0;
        const doc: any = { id: 'd1', reportId: 7, lease: { acquired: true } };
        const handle = lifecycle([doc], {
            leaseRenewIntervalMs: 1000,
            leaseRenewRetryDelaysMs: [10, 10],
            onRenewDocument: async () => {
                calls++;
                throw Object.assign(new Error('Another author holds the lease.'), { status: 409 });
            }
        });

        await vi.advanceTimersByTimeAsync(1000);
        await vi.advanceTimersByTimeAsync(50);
        expect(calls).toBe(1);
        expect(doc.lease.acquired).toBe(false);
        expect(notes.some(note => note.includes('Another author holds the lease.'))).toBe(true);
        handle.dispose();
    });
});
