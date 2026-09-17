import assert from 'node:assert/strict';
import {
    redactSecrets,
    normalizeRunTrace,
    createScriptResultsPanel,
    MAX_RENDERED_ROWS,
    resultRenderWindow,
    filterRows,
    toCsv,
    formatResultCell,
    buildDataPreviewPayload,
    editLeaseRetryDelay,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/run-results.js';

// ── 1. redactSecrets ──────────────────────────────────────────────────────────
assert.equal(redactSecrets(null), null);
assert.equal(redactSecrets(undefined), undefined);
assert.equal(redactSecrets(12345), 12345);

// Passwords and keys
assert.equal(redactSecrets("USE PASSWORD 'my_secret_pwd';"), "USE PASSWORD '********';");
assert.equal(redactSecrets('PASSWORD = "secret"'), 'PASSWORD = "********"');
assert.equal(redactSecrets('PWD=secret123'), 'PWD = ********');
assert.equal(redactSecrets('SECRET_KEY = "key_val"'), 'SECRET_KEY = "********"');
assert.equal(redactSecrets('API_KEY = abc_xyz_123'), 'API_KEY = ********');
assert.equal(redactSecrets('TOKEN=tok123;'), 'TOKEN = ********;');
assert.equal(redactSecrets('KEY_FILE = "C:\\path\\key.pem"'), 'KEY_FILE = "********"');

// Encrypted and vault references
assert.equal(redactSecrets('ENC:AQAAANCMnd8BFdERjHoAwE='), 'ENC:********');
assert.equal(redactSecrets('DPAPI-M:abc123=='), 'DPAPI-M:********');
assert.equal(redactSecrets('DPAPI:def456=='), 'DPAPI:********');
assert.equal(redactSecrets('SECRET:vault-key-name'), 'SECRET:********');
assert.equal(redactSecrets('CAPABILITY:cap_read'), 'CAPABILITY:********');
assert.equal(redactSecrets('SHARED:cluster_token'), 'SHARED:********');

// Bearer tokens
assert.equal(redactSecrets('Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'), 'Bearer ********');

// ── 2. normalizeRunTrace ──────────────────────────────────────────────────────
// Pass-through existing trace array
const existingTrace = [{ type: 'custom_event', data: 42 }];
assert.equal(normalizeRunTrace({ trace: existingTrace }), existingTrace);

// Success run normalization
const successResult = {
    success: true,
    rows: [{ id: 1, name: 'Alice' }, { id: 2, name: 'Bob' }],
    columns: ['id', 'name'],
    elapsedMs: 150,
    messages: ['Query completed successfully', { text: 'PASSWORD = "plain" in log' }],
    diagnostics: [{ code: 'WARN01', line: 3, message: 'Unused column', severity: 'Warning' }],
    lineage: [{ sourceTable: 'users', targetTable: 'RESULTSET', targetColumn: 'id' }],
};
const successTrace = normalizeRunTrace(successResult, 'SELECT id, name FROM users;');
assert.ok(Array.isArray(successTrace));
assert.equal(successTrace[0].type, 'clear');
assert.equal(successTrace[1].type, 'status');
assert.equal(successTrace[1].status, 'running');
assert.equal(successTrace[2].type, 'message');
assert.equal(successTrace[2].level, 'sys');

// Diagnostics converted to messages
const diagMsg = successTrace.find(t => t.type === 'message' && t.text.includes('[WARN01]'));
assert.ok(diagMsg);
assert.equal(diagMsg.level, 'warn');
assert.ok(diagMsg.text.includes('Line 3'));

// Redacted message
const redactedMsg = successTrace.find(t => t.type === 'message' && t.text.includes('PASSWORD = "********"'));
assert.ok(redactedMsg);

// Progress event with pipeline
const progressEvent = successTrace.find(t => t.type === 'progress');
assert.ok(progressEvent);
assert.ok(Array.isArray(progressEvent.data));

// Lineage event
const lineageEvent = successTrace.find(t => t.type === 'lineage');
assert.ok(lineageEvent);
assert.equal(lineageEvent.data.length, 1);

// Results event
const resultsEvent = successTrace.find(t => t.type === 'results');
assert.ok(resultsEvent);
assert.deepEqual(resultsEvent.columns, ['id', 'name']);
assert.equal(resultsEvent.rows.length, 2);

// Performance event
const perfEvent = successTrace.find(t => t.type === 'performance');
assert.ok(perfEvent);
assert.equal(perfEvent.metrics.executionMs, 150);
assert.equal(perfEvent.metrics.rowsProcessed, 2);

// Done event
const doneEvent = successTrace.find(t => t.type === 'done');
assert.ok(doneEvent);
assert.equal(doneEvent.exitCode, 0);

// Failure run normalization
const failResult = {
    success: false,
    message: 'Error: table not found',
    diagnostics: [{ code: 'ERR01', line: 10, message: 'Table not found', severity: 'Error' }],
};
const failTrace = normalizeRunTrace(failResult, 'SELECT * FROM missing;');
assert.equal(failTrace[1].status, 'failed');
const failDone = failTrace.find(t => t.type === 'done');
assert.ok(failDone);
assert.equal(failDone.exitCode, 1);

// ── 3. MAX_RENDERED_ROWS and resultRenderWindow ─────────────────────────────
assert.equal(MAX_RENDERED_ROWS, 5000);

// Total <= cap
const rw1 = resultRenderWindow([{ a: 1 }], 1, false, 10);
assert.equal(rw1.visible.length, 1);
assert.equal(rw1.truncated, false);
assert.equal(rw1.label, '1 row');

// Plural rows
const rw2 = resultRenderWindow([{ a: 1 }, { a: 2 }], 2, false, 10);
assert.equal(rw2.label, '2 rows');

// Filtered without truncation
const rw3 = resultRenderWindow([{ a: 1 }], 5, true, 10);
assert.equal(rw3.truncated, false);
assert.equal(rw3.label, '1 of 5 rows');

// Truncated unfiltered
const rw4 = resultRenderWindow([1, 2, 3, 4, 5], 5, false, 3);
assert.equal(rw4.visible.length, 3);
assert.equal(rw4.truncated, true);
assert.equal(rw4.label, 'showing first 3 rows of 5 rows');

// Truncated filtered
const rw5 = resultRenderWindow([1, 2, 3, 4], 10, true, 2);
assert.equal(rw5.visible.length, 2);
assert.equal(rw5.truncated, true);
assert.equal(rw5.label, 'showing first 2 rows of 4 matched (10 rows total)');

// Null/empty fallbacks
const rwEmpty = resultRenderWindow(null, null, null, 10);
assert.equal(rwEmpty.visible.length, 0);
assert.equal(rwEmpty.truncated, false);
assert.equal(rwEmpty.label, '0 rows');

// ── 4. filterRows, toCsv, formatResultCell ────────────────────────────────────
const testRows = [
    { id: 10, dept: 'Engineering', active: true, info: { role: 'lead' } },
    { id: 20, dept: 'Marketing', active: false, info: null },
];
const testCols = ['id', 'dept', 'active', 'info'];

// filterRows
assert.equal(filterRows(testRows, testCols, '').length, 2);
assert.equal(filterRows(testRows, testCols, 'ENGINEERING').length, 1);
assert.equal(filterRows(testRows, testCols, 'lead').length, 1);
assert.equal(filterRows(testRows, testCols, 'nonexistent').length, 0);

// formatResultCell
assert.equal(formatResultCell(null), '');
assert.equal(formatResultCell(undefined), '');
assert.equal(formatResultCell(0), '0');
assert.equal(formatResultCell(false), 'false');
assert.equal(formatResultCell('test'), 'test');
assert.equal(formatResultCell({ k: 'v' }), '{"k":"v"}');

// toCsv
const csvOut = toCsv(['id', 'dept'], [{ id: 1, dept: 'Sales, North' }, { id: 2, dept: 'He said "Hello"' }]);
const csvLines = csvOut.split('\r\n');
assert.equal(csvLines[0], 'id,dept');
assert.equal(csvLines[1], '1,"Sales, North"');
assert.equal(csvLines[2], '2,"He said ""Hello"""');

// ── 5. buildDataPreviewPayload ───────────────────────────────────────────────
const tempPayload = buildDataPreviewPayload({ sourceKind: 'temp', name: '#staging' }, 'SELECT 1;', 'doc://1');
assert.equal(tempPayload.sourceKind, 'temp');
assert.equal(tempPayload.script, 'SELECT 1;');
assert.equal(tempPayload.documentUri, 'doc://1');

const tablePayload = buildDataPreviewPayload({ sourceKind: 'table', name: 'users' }, 'SELECT 1;', null);
assert.equal(tablePayload.sourceKind, 'table');
assert.equal(tablePayload.script, null);
assert.equal(tablePayload.documentUri, 'portal-designer');

// ── 6. editLeaseRetryDelay ───────────────────────────────────────────────────
const now = 1700000000000;
// Normal expiry in 15 seconds -> delay = 15000 + 1000 = 16000
assert.equal(editLeaseRetryDelay(now + 15000, now), 16000);
// Immediate or past expiry -> clamped to min 5000
assert.equal(editLeaseRetryDelay(now - 1000, now), 5000);
// Distant expiry (> 60s) -> clamped to max 60000
assert.equal(editLeaseRetryDelay(now + 120000, now), 60000);
// Invalid date -> defaults to 30000
assert.equal(editLeaseRetryDelay('invalid-date', now), 30000);

// ── 7. createScriptResultsPanel (Mock DOM) ───────────────────────────────────
function createMockElement(tag = 'div') {
    const listeners = {};
    const dataset = {};
    const classList = {
        classes: new Set(),
        add(c) { this.classes.add(c); },
        remove(c) { this.classes.delete(c); },
        toggle(c, force) { if (force !== undefined) { if (force) this.classes.add(c); else this.classes.delete(c); } else { if (this.classes.has(c)) this.classes.delete(c); else this.classes.add(c); } },
        contains(c) { return this.classes.has(c); },
    };
    const children = [];
    return {
        tagName: tag.toUpperCase(),
        className: '',
        innerHTML: '',
        textContent: '',
        value: '',
        hidden: false,
        dataset,
        classList,
        addEventListener(event, fn) {
            listeners[event] = listeners[event] || [];
            listeners[event].push(fn);
        },
        removeEventListener(event, fn) {
            if (!listeners[event]) return;
            listeners[event] = listeners[event].filter(f => f !== fn);
        },
        trigger(event, payload = {}) {
            for (const fn of (listeners[event] || [])) fn({ type: event, ...payload, target: this });
        },
        querySelector(selector) {
            if (selector.includes('[data-body]')) return createMockElement('div');
            if (selector.includes('[data-status]')) return createMockElement('span');
            if (selector.includes('[data-result-filter]')) return createMockElement('input');
            if (selector.includes('[data-result-tools]')) return createMockElement('span');
            if (selector.includes('[data-tab="messages"]')) return createMockElement('button');
            return null;
        },
        querySelectorAll(selector) {
            if (selector.includes('[data-tab]')) {
                const b1 = createMockElement('button');
                b1.dataset.tab = 'results';
                const b2 = createMockElement('button');
                b2.dataset.tab = 'messages';
                return [b1, b2];
            }
            if (selector.includes('[data-export]')) {
                const e1 = createMockElement('button');
                e1.dataset.export = 'csv';
                return [e1];
            }
            return [];
        },
        replaceChildren() {
            children.length = 0;
        },
    };
}

// Set up mock window if not present
const hadWindow = typeof globalThis.window !== 'undefined';
if (!hadWindow) {
    globalThis.window = {
        addEventListener() {},
        removeEventListener() {},
    };
}

const mockContainer = createMockElement('div');
let navLine = 0;
let navCol = 0;
const panel = createScriptResultsPanel(mockContainer, {
    onNavigate(line, col) {
        navLine = line;
        navCol = col;
    },
});

assert.ok(panel);
assert.equal(typeof panel.replay, 'function');
assert.equal(typeof panel.setDiagnostics, 'function');
assert.equal(typeof panel.startElapsed, 'function');
assert.equal(typeof panel.stopElapsed, 'function');
assert.equal(typeof panel.clear, 'function');
assert.equal(typeof panel.setNavigate, 'function');
assert.equal(typeof panel.setApplyFix, 'function');
assert.equal(typeof panel.dispose, 'function');

// Replay trace into panel
panel.replay([
    { type: 'clear' },
    { type: 'status', status: 'running' },
    { type: 'results', columns: ['x'], rows: [{ x: 10 }] },
    { type: 'done', exitCode: 0 },
]);

// Set diagnostics
panel.setDiagnostics([
    { code: 'SYNTAX', startLine: 4, startColumn: 2, message: 'Expected semicolon', severity: 'Error' },
]);

// Start and stop elapsed timers
panel.startElapsed();
panel.stopElapsed();

// Custom navigation and applyFix handlers
panel.setNavigate((l, c) => { navLine = l * 10; navCol = c * 10; });
panel.setApplyFix(() => {});

// Dispose
panel.dispose();

if (!hadWindow) {
    delete globalThis.window;
}

console.log('test-run-results: all checks passed');
