import assert from 'node:assert/strict';

// Set minimal browser globals before importing modules that attach hooks to window/document
const originalWindow = globalThis.window;
const originalDocument = globalThis.document;
const originalFetch = globalThis.fetch;
const originalCustomEvent = globalThis.CustomEvent;

if (!globalThis.CustomEvent) {
    globalThis.CustomEvent = class CustomEvent {
        constructor(type, init = {}) {
            this.type = type;
            this.detail = init.detail;
        }
    };
}

let dispatchedEvents = [];

globalThis.window = {
    location: { protocol: 'http:' },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent(evt) {
        dispatchedEvents.push(evt);
        return true;
    },
    __IS_WEB__: true,
};

globalThis.document = {
    readyState: 'loading',
    images: [],
    head: { appendChild() {} },
    createElement(tag) {
        return { tag, src: '', onload: null, onerror: null };
    },
    addEventListener() {},
    removeEventListener() {},
};

const {
    publishExportState,
    markExportNotReady,
    markExportReady,
    hasDeferredRows,
    loadVisualRows,
    getExportReadyPromise,
    setExportReadyPromise,
    getPendingLazyRows,
    setPendingLazyRows,
} = await import('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-data.js');

try {
    // ── 1. Function Exports Verification ─────────────────────────────────────────
    assert.equal(typeof publishExportState, 'function');
    assert.equal(typeof markExportNotReady, 'function');
    assert.equal(typeof markExportReady, 'function');
    assert.equal(typeof hasDeferredRows, 'function');
    assert.equal(typeof loadVisualRows, 'function');
    assert.equal(typeof getExportReadyPromise, 'function');
    assert.equal(typeof setExportReadyPromise, 'function');
    assert.equal(typeof getPendingLazyRows, 'function');
    assert.equal(typeof setPendingLazyRows, 'function');

    // ── 2. publishExportState Contract ───────────────────────────────────────────
    dispatchedEvents = [];
    const stateRendering = publishExportState('rendering', { reason: 'test-init' });
    assert.equal(stateRendering.status, 'rendering');
    assert.equal(stateRendering.ready, false);
    assert.equal(stateRendering.reason, 'test-init');
    assert.equal(globalThis.window.__etlSqlReportExportReady, false);
    assert.equal(globalThis.window.__etlSqlReportExportState, stateRendering);
    assert.equal(dispatchedEvents.length, 1);
    assert.equal(dispatchedEvents[0].type, 'etl-sql-report-export-state');
    assert.deepEqual(dispatchedEvents[0].detail, stateRendering);

    dispatchedEvents = [];
    const stateReady = publishExportState('ready', { pageCount: 3, visualCount: 7 });
    assert.equal(stateReady.status, 'ready');
    assert.equal(stateReady.ready, true);
    assert.equal(stateReady.pageCount, 3);
    assert.equal(stateReady.visualCount, 7);
    assert.equal(globalThis.window.__etlSqlReportExportReady, true);
    assert.equal(globalThis.window.__etlSqlReportExportState, stateReady);
    assert.equal(dispatchedEvents.length, 2);
    assert.equal(dispatchedEvents[0].type, 'etl-sql-report-export-state');
    assert.equal(dispatchedEvents[1].type, 'etl-sql-report-export-ready');

    // ── 3. markExportNotReady Contract ───────────────────────────────────────────
    markExportNotReady('loading-extra', { count: 1 });
    assert.equal(globalThis.window.__etlSqlReportExportReady, false);
    const pendingPromise = getExportReadyPromise();
    assert.ok(pendingPromise instanceof Promise);

    // ── 4. hasDeferredRows Predicate Contract ────────────────────────────────────
    assert.equal(hasDeferredRows(null), false);
    assert.equal(hasDeferredRows(undefined), false);
    assert.equal(hasDeferredRows({}), false);
    assert.equal(hasDeferredRows({ rowsSource: null }), false);
    assert.equal(hasDeferredRows({ rowsSource: {} }), false);
    assert.equal(hasDeferredRows({ rowsSource: { url: '/api/deferred' } }), true);
    assert.equal(hasDeferredRows({ rowsSource: { url: '/api/deferred' }, rows: [] }), true);
    assert.equal(hasDeferredRows({ rowsSource: { url: '/api/deferred' }, rows: [['val1']] }), false);

    // ── 5. loadVisualRows Immediate Resolution ───────────────────────────────────
    const immediateVisual = { name: 'non-deferred', rows: [['1', '2']] };
    const loadedImmediate = await loadVisualRows(immediateVisual);
    assert.equal(loadedImmediate, immediateVisual);

    // ── 6. loadVisualRows JSON Fetch Contract ────────────────────────────────────
    globalThis.fetch = async (url) => {
        assert.equal(url, '/api/data/v1');
        return {
            ok: true,
            json: async () => ({
                columns: ['id', 'metric'],
                rows: [['row1', '100'], ['row2', '200']],
            }),
        };
    };

    const deferredVisual = {
        name: 'test_visual',
        rowsSource: { url: '/api/data/v1' },
        rows: [],
    };
    const loadedVisual = await loadVisualRows(deferredVisual);
    assert.equal(loadedVisual.name, 'test_visual');
    assert.deepEqual(loadedVisual.columns, ['id', 'metric']);
    assert.deepEqual(loadedVisual.rows, [['row1', '100'], ['row2', '200']]);
    assert.equal(loadedVisual.rowsSource, null);
    assert.equal(loadedVisual.__rowsLoaded, true);

    // ── 7. loadVisualRows Arrow IPC Contract ─────────────────────────────────────
    globalThis.window.arrow = {
        tableFromIPC: () => ({
            schema: { fields: [{ name: 'colA' }, { name: 'colB' }] },
            numRows: 2,
            numCols: 2,
            getChildAt: (colIdx) => ({
                get: (rowIdx) => (colIdx === 0 ? `a_${rowIdx}` : rowIdx * 10),
            }),
        }),
    };

    globalThis.fetch = async (url) => {
        if (url === '/api/data/arrow') {
            return {
                ok: true,
                arrayBuffer: async () => new ArrayBuffer(32),
            };
        }
        throw new Error('Unexpected URL: ' + url);
    };

    const arrowVisual = {
        name: 'test_arrow_visual',
        rowsSource: { url: '/api/data/json', arrowUrl: '/api/data/arrow' },
        rows: [],
    };
    const loadedArrow = await loadVisualRows(arrowVisual);
    assert.equal(loadedArrow.name, 'test_arrow_visual');
    assert.deepEqual(loadedArrow.columns, ['colA', 'colB']);
    assert.deepEqual(loadedArrow.rows, [['a_0', '0'], ['a_1', '10']]);
    assert.equal(loadedArrow.rowsSource, null);
    assert.equal(loadedArrow.__rowsLoaded, true);

    // ── 8. Lazy Rows Counter & Promise Accessors ─────────────────────────────────
    setPendingLazyRows(10);
    assert.equal(getPendingLazyRows(), 10);
    setPendingLazyRows(0);
    assert.equal(getPendingLazyRows(), 0);

    const testPromise = Promise.resolve({ done: true });
    setExportReadyPromise(testPromise);
    assert.equal(getExportReadyPromise(), testPromise);

    console.log('OK test-rt-data.mjs: all checks passed.');
} finally {
    globalThis.window = originalWindow;
    globalThis.document = originalDocument;
    globalThis.fetch = originalFetch;
    globalThis.CustomEvent = originalCustomEvent;
}
