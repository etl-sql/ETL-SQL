import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const runtimeSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js'), 'utf8');

// Verify intra-import contract required for offline snapshot single-script inlining
const INTRA_IMPORT = /^import\s*\{\s*([\w$]+(?:\s*,\s*[\w$]+)*\s*,?)\s*\}\s*from\s*'\.\/((?:rt-[a-z-]+|report-runtime)\.js)';\s*$/;
const importLines = runtimeSource.split('\n').filter(l => l.startsWith('import '));
assert.ok(importLines.length >= 10, 'Expected at least 10 intra-module import statements');
for (const line of importLines) {
    assert.ok(INTRA_IMPORT.test(line), `Import violates single-line INTRA_IMPORT pattern: ${line}`);
}

// Verify export declaration pattern
const exportLines = runtimeSource.split('\n').filter(l => l.startsWith('export '));
assert.equal(exportLines.length, 1, 'Expected exactly 1 export statement (renderManifest)');
assert.ok(exportLines[0].includes('function renderManifest'), 'Expected export function renderManifest');

// Set up mock DOM environment
class MockElement {
    constructor(tagName) {
        this.tagName = tagName.toUpperCase();
        this.children = [];
        this.parentElement = null;
        this.parentNode = null;
        this.attributes = {};
        this.style = {
            display: '',
            setProperty(k, v) { this[k] = String(v); },
            removeProperty(k) { delete this[k]; },
            getPropertyValue(k) { return this[k] || ''; }
        };
        this.classList = {
            _set: new Set(),
            add(...c) { c.forEach(x => this._set.add(x)); },
            remove(...c) { c.forEach(x => this._set.delete(x)); },
            contains(c) { return this._set.has(c); }
        };
        this.innerHTML = '';
        this.value = '';
        this.options = [];
    }
    appendChild(child) {
        child.parentElement = this;
        child.parentNode = this;
        this.children.push(child);
        return child;
    }
    replaceChildren(...newChildren) {
        this.children = [];
        for (const c of newChildren) this.appendChild(c);
    }
    setAttribute(name, val) { this.attributes[name] = String(val); }
    getAttribute(name) { return this.attributes[name] ?? null; }
    hasAttribute(name) { return name in this.attributes; }
    querySelector() { return null; }
    querySelectorAll() { return []; }
    addEventListener() {}
    removeEventListener() {}
}

const rootEl = new MockElement('div');
rootEl.setAttribute('id', 'root');
const bodyEl = new MockElement('body');

const doc = {
    readyState: 'complete',
    body: bodyEl,
    getElementById(id) {
        if (id === 'root') return rootEl;
        return null;
    },
    createElement(tag) { return new MockElement(tag); },
    createDocumentFragment() { return new MockElement('fragment'); },
    querySelectorAll() { return []; },
    addEventListener() {},
    removeEventListener() {}
};

const win = {
    document: doc,
    location: { search: '', hash: '', protocol: 'http:' },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() { return true; },
    __IS_WEB__: true
};

globalThis.window = win;
globalThis.document = doc;
globalThis.CustomEvent = class CustomEvent {
    constructor(type, init) {
        this.type = type;
        this.detail = init?.detail;
    }
};
globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
};

// Import the runtime module
const runtime = await import('../src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js');

// 1. Verify exported function
assert.equal(typeof runtime.renderManifest, 'function', 'renderManifest must be an exported function');

// 2. Verify window.__etlSqlReportWhenExportReady is installed
assert.equal(typeof win.__etlSqlReportWhenExportReady, 'function', '__etlSqlReportWhenExportReady must be installed');

// 3. Verify window.__ETLSQL_DETAIL__
assert.ok(win.__ETLSQL_DETAIL__, '__ETLSQL_DETAIL__ must be set');
assert.equal(typeof win.__ETLSQL_DETAIL__.computeDetailPlacement, 'function');
assert.equal(typeof win.__ETLSQL_DETAIL__.destroyIn, 'function');
assert.equal(win.__ETLSQL_DETAIL__.preferredSide, 'top');
assert.equal(win.__ETLSQL_DETAIL__.viewportMargin, 8);
assert.equal(win.__ETLSQL_DETAIL__.anchorGap, 10);

// 4. Verify window.__reportRuntime__ pure test escape hatches
assert.ok(win.__reportRuntime__, '__reportRuntime__ must be set');
assert.equal(typeof win.__reportRuntime__.isOn, 'function');
assert.equal(typeof win.__reportRuntime__.renderCard, 'function');
assert.equal(typeof win.__reportRuntime__.renderDatePicker, 'function');
assert.equal(typeof win.__reportRuntime__.renderNativeSvg, 'function');
assert.equal(typeof win.__reportRuntime__.nativeLayoutTier, 'function');
assert.equal(typeof win.__reportRuntime__.abbreviateNumber, 'function');
assert.equal(typeof win.__reportRuntime__.savedViewsBase, 'function');
assert.equal(typeof win.__reportRuntime__.parseStateHash, 'function');
assert.equal(typeof win.__reportRuntime__.applyBookmark, 'function');
assert.equal(typeof win.__reportRuntime__.resolveDesignTokens, 'function');
assert.ok(win.__reportRuntime__.DESIGN_TOKENS);

// 5. Verify renderManifest with minimal manifest
runtime.renderManifest({
    title: 'Test Report',
    pages: [],
    visuals: []
});
assert.ok(win.__CURRENT_MANIFEST__, 'renderManifest should store manifest in window.__CURRENT_MANIFEST__');
assert.equal(win.__CURRENT_MANIFEST__.title, 'Test Report');

// 6. Verify export readiness promise
const readyPromise = win.__etlSqlReportWhenExportReady(100);
assert.ok(readyPromise instanceof Promise, 'WhenExportReady should return a Promise');

console.log('test-runtime-main: all assertions passed.');
