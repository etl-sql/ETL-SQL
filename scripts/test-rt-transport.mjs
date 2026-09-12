import assert from 'node:assert/strict';

// Set minimal browser globals before importing modules that attach hooks to window/document
const originalWindow = globalThis.window;
const originalDocument = globalThis.document;

globalThis.window = {
    location: { protocol: 'http:' },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() { return true; },
};

globalThis.document = {
    readyState: 'loading', // prevents report-runtime.js from auto-booting during import
    getElementById() { return null; },
    querySelector() { return null; },
    querySelectorAll() { return []; },
    addEventListener() {},
    removeEventListener() {},
    body: { classList: { add() {}, remove() {}, contains() { return false; } } },
};

const {
    savedViewsRequest,
    postDrillIn,
    postDrillUp,
    postParameters,
    _postParametersInternal,
    postRunScript,
    postRefreshVisuals,
} = await import('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-transport.js');

const { pendingParameters } = await import('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-state.js');

try {
    // ── 1. Function Exports Verification ─────────────────────────────────────────
    assert.equal(typeof savedViewsRequest, 'function');
    assert.equal(typeof postDrillIn, 'function');
    assert.equal(typeof postDrillUp, 'function');
    assert.equal(typeof postParameters, 'function');
    assert.equal(typeof _postParametersInternal, 'function');
    assert.equal(typeof postRunScript, 'function');
    assert.equal(typeof postRefreshVisuals, 'function');

    // ── 2. savedViewsRequest with no base URL returns null ───────────────────────
    const savedViewsResult = await savedViewsRequest('/views');
    assert.equal(savedViewsResult, null);

    // ── 3. postParameters explicit staging contract ──────────────────────────────
    // When stage is explicitly true and not an interaction, parameters stage into pendingParameters
    const testParams = { department: 'Finance', year: '2026' };
    const stagedResult = await postParameters(testParams, false, true);
    assert.equal(stagedResult, null);
    assert.equal(pendingParameters.department, 'Finance');
    assert.equal(pendingParameters.year, '2026');

    // Clean up staged parameters
    delete pendingParameters.department;
    delete pendingParameters.year;

    // ── 4. Error handling contracts for network requests ─────────────────────────
    const originalFetch = globalThis.fetch;
    const originalError = console.error;
    const originalWarn = console.warn;
    console.error = () => {};
    console.warn = () => {};

    try {
        // 4.1 Mock 500 server error
        globalThis.fetch = async () => ({
            ok: false,
            status: 500,
            text: async () => 'Internal Server Error',
            json: async () => ({ error: 'fail' }),
        });

        const runScriptErr = await postRunScript('report.etlsql', {});
        assert.deepEqual(runScriptErr, { message: 'Server error: 500' });

        const refreshErr = await postRefreshVisuals(['chart1']);
        assert.equal(refreshErr, null);

        // 4.2 Mock network exception
        globalThis.fetch = async () => {
            throw new Error('Connection refused');
        };

        const runScriptThrown = await postRunScript('report.etlsql', {});
        assert.deepEqual(runScriptThrown, { message: 'Request failed: Connection refused' });

        const refreshThrown = await postRefreshVisuals(['chart1']);
        assert.equal(refreshThrown, null);

        const paramThrown = await _postParametersInternal({ q: 'test' });
        assert.equal(paramThrown, null);

        // 4.3 Mock 200 OK success
        globalThis.fetch = async () => ({
            ok: true,
            status: 200,
            json: async () => ({ status: 'ok', updated: true }),
        });

        const runScriptOk = await postRunScript('report.etlsql', { p: 1 });
        assert.deepEqual(runScriptOk, { status: 'ok', updated: true });

        const refreshOk = await postRefreshVisuals(['chart1', 'chart2']);
        assert.deepEqual(refreshOk, { status: 'ok', updated: true });
    } finally {
        globalThis.fetch = originalFetch;
        console.error = originalError;
        console.warn = originalWarn;
    }

    console.log('OK test-rt-transport.mjs: all checks passed.');
} finally {
    if (originalWindow === undefined) delete globalThis.window;
    else globalThis.window = originalWindow;

    if (originalDocument === undefined) delete globalThis.document;
    else globalThis.document = originalDocument;
}
