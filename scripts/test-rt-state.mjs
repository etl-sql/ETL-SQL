import assert from 'node:assert/strict';
import {
    isOfflineHost,
    isWebMode,
    vscode,
    isInteractive,
    safeRequestAnimationFrame,
    feedback,
    apiBase,
    parameters,
    pendingParameters,
    _drillHistory,
    _crossFilterStates,
    _uiStates,
    getBaselineManifest,
    setBaselineManifest,
    getLastManifest,
    setLastManifest,
    getLastActivePage,
    setLastActivePage,
    getRefreshTimers,
    setRefreshTimers,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-state.js';

// ── 1. Host Mode & Environment Detection ─────────────────────────────────────
assert.equal(typeof isOfflineHost, 'boolean');
assert.equal(typeof isWebMode, 'boolean');
assert.equal(Boolean(isInteractive), false);
assert.equal(isInteractive, null);
assert.equal(typeof safeRequestAnimationFrame, 'function');
assert.equal(typeof apiBase, 'string');
assert.ok(!apiBase.endsWith('/'), 'apiBase does not end with a trailing slash');

// In node environment without browser globals injected
assert.equal(vscode, null);
assert.equal(feedback, undefined);

// ── 2. Shared Session Objects ────────────────────────────────────────────────
assert.ok(parameters && typeof parameters === 'object');
assert.ok(pendingParameters && typeof pendingParameters === 'object');
assert.ok(Array.isArray(_drillHistory));
assert.ok(_crossFilterStates && typeof _crossFilterStates === 'object');
assert.ok(_uiStates && typeof _uiStates === 'object');

// Test session state mutation persistence
parameters['region'] = 'North';
assert.equal(parameters['region'], 'North');
delete parameters['region'];

pendingParameters['date_range'] = '2026-Q1';
assert.equal(pendingParameters['date_range'], '2026-Q1');
delete pendingParameters['date_range'];

_drillHistory.push({ page: 'Summary', field: 'Category' });
assert.equal(_drillHistory.length, 1);
assert.deepEqual(_drillHistory.pop(), { page: 'Summary', field: 'Category' });

_crossFilterStates['elem-42'] = { selected: [1, 2, 3] };
assert.deepEqual(_crossFilterStates['elem-42'], { selected: [1, 2, 3] });
delete _crossFilterStates['elem-42'];

_uiStates['sales-table'] = { collapsed: true };
assert.deepEqual(_uiStates['sales-table'], { collapsed: true });
delete _uiStates['sales-table'];

// ── 3. Manifest and Page Lifecycle Accessors ─────────────────────────────────
const initialBaseline = getBaselineManifest();
const testBaseline = { version: '1.0', pages: [{ name: 'Page1' }] };
setBaselineManifest(testBaseline);
assert.equal(getBaselineManifest(), testBaseline);
setBaselineManifest(initialBaseline);

const initialLast = getLastManifest();
const testLast = { version: '1.1', pages: [{ name: 'Page2' }] };
setLastManifest(testLast);
assert.equal(getLastManifest(), testLast);
setLastManifest(initialLast);

const initialPage = getLastActivePage();
setLastActivePage('Dashboard');
assert.equal(getLastActivePage(), 'Dashboard');
setLastActivePage(initialPage);

const initialTimers = getRefreshTimers();
assert.ok(Array.isArray(initialTimers));
const testTimers = [101, 102];
setRefreshTimers(testTimers);
assert.equal(getRefreshTimers(), testTimers);
setRefreshTimers(initialTimers);

// ── 4. safeRequestAnimationFrame Fallback ────────────────────────────────────
let rafCalled = false;
safeRequestAnimationFrame(() => {
    rafCalled = true;
});
await new Promise(resolve => setTimeout(resolve, 50));
assert.equal(rafCalled, true, 'safeRequestAnimationFrame callback executed');

console.log('OK test-rt-state.mjs: all checks passed.');
