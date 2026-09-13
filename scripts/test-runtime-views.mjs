import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Mock DOM element for views picker and presentation state tests
class FakeElement {
    children = []; style = {}; listeners = {}; attributes = {}; classList = {
        _classes: new Set(),
        add(c) { this._classes.add(c); },
        remove(c) { this._classes.delete(c); },
        contains(c) { return this._classes.has(c); },
    };
    isConnected = true;
    type = ''; tabIndex = 0; textContent = ''; hidden = false; title = ''; id = ''; className = '';
    appendChild(child) { this.children.push(child); return child; }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    getAttribute(name) { return this.attributes[name] ?? null; }
    addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
    dispatchEvent(event) { for (const callback of this.listeners[event.type] || []) callback(event); }
    closest(selector) {
        if (selector.startsWith('.') && this.classList.contains(selector.slice(1))) return this;
        return null;
    }
    querySelectorAll(selector) {
        const matches = [];
        const walk = el => {
            for (const child of el.children) {
                if (selector.includes('[role="menuitem"]') && child.attributes.role === 'menuitem') {
                    matches.push(child);
                }
                walk(child);
            }
        };
        walk(this);
        return matches;
    }
    querySelector(selector) {
        return this.querySelectorAll(selector)[0] || null;
    }
    focus() { this._focused = true; }
    contains(other) {
        if (other === this) return true;
        for (const child of this.children) if (child.contains(other)) return true;
        return false;
    }
}

let activePage = 'Summary';
let lastRenderedManifest = null;
let replacedHash = null;
const notified = [];
const uiStates = {};
const params = {};
let currentManifest = {
    parameters: { '@Region': 'North', '@Amount': 100, '@Active': 'True' },
    parameterMetadata: {
        '@Region': { type: 'VARCHAR' },
        '@Amount': { type: 'DECIMAL' },
        '@Active': { type: 'BOOLEAN' },
    },
    bookmarks: [
        {
            name: 'DefaultView',
            title: 'Default View',
            isDefault: true,
            state: {
                activePage: 'Detail',
                parameters: { '@Region': 'South' },
                visible: { 'Chart1': true, 'Table1': false },
                collapsed: { 'Drawer1': true },
            },
        },
    ],
};

const domById = new Map();
const fakeDocument = {
    createElement: () => new FakeElement(),
    getElementById: id => domById.get(id) || null,
    querySelector: selector => {
        const match = selector.match(/\[data-name="([^"]+)"\]/);
        return match ? (domById.get(match[1]) || null) : null;
    },
    addEventListener: () => {},
    activeElement: null,
};

const fakeWindow = {
    __API_BASE__: '/api/reports/42',
    __ETLSNAP__: false,
    history: {
        replaceState: (_data, _title, hash) => { replacedHash = hash; },
    },
};

const dependencies = {
    _uiStates: uiStates,
    apiBase: '/api',
    feedback: {
        notify: (msg, opts) => notified.push({ msg, opts }),
        confirm: async () => true,
        prompt: async () => 'Test View',
    },
    getLastManifest: () => currentManifest,
    isOfflineHost: false,
    isWebMode: false,
    parameters: params,
    vscode: null,
    renderManifest: m => { lastRenderedManifest = m; },
    getActivePageName: () => activePage,
    navigateToPage: page => { activePage = page; },
    _postParametersInternal: async () => null,
    savedViewsRequest: async () => [],
    document: fakeDocument,
    window: fakeWindow,
};

const source = readFileSync(new URL('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-views.js', import.meta.url), 'utf8')
    .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');

const views = new Function(...Object.keys(dependencies), `${source}
return {
    parseStateHash,
    applyBookmark,
    isOfflineSnapshot,
    recordParametersOffline,
    savedViewsBase,
    captureResolvedState,
    buildViewsPicker,
};`)(...Object.values(dependencies));

// 1. parseStateHash
assert.deepEqual(views.parseStateHash('#bookmark=My%20Bookmark'), { bookmark: 'My Bookmark', view: null });
assert.deepEqual(views.parseStateHash('#view=saved-view-123'), { bookmark: null, view: 'saved-view-123' });
assert.deepEqual(views.parseStateHash('&bookmark=Test%201'), { bookmark: 'Test 1', view: null });
assert.deepEqual(views.parseStateHash(''), { bookmark: null, view: null });
assert.deepEqual(views.parseStateHash(null), { bookmark: null, view: null });
assert.deepEqual(views.parseStateHash('#bookmark=%E0%A4%A'), { bookmark: null, view: null }); // malformed URI handling

// 2. savedViewsBase
assert.equal(views.savedViewsBase(), '/api/reports/42/saved-views');
fakeWindow.__API_BASE__ = '/reports/sample/api';
assert.equal(views.savedViewsBase(), null);
fakeWindow.__API_BASE__ = '/api/reports/999';
assert.equal(views.savedViewsBase(), '/api/reports/999/saved-views');

// 3. isOfflineSnapshot
assert.equal(views.isOfflineSnapshot(), false);
fakeWindow.__ETLSNAP__ = true;
assert.equal(views.isOfflineSnapshot(), true);
fakeWindow.__ETLSNAP__ = false;

// 4. recordParametersOffline
assert.equal(views.recordParametersOffline({ '@Year': '2026' }), true);
assert.equal(currentManifest.parameters['@Year'], '2026');
assert.equal(params['@Year'], '2026');

// 5. captureResolvedState
const chartEl = new FakeElement();
const tableEl = new FakeElement();
const drawerEl = new FakeElement();
drawerEl.classList.add('collapsible-drawer');
drawerEl.setAttribute('data-name', 'Drawer1');
domById.set('Chart1', chartEl);
domById.set('Table1', tableEl);
domById.set('Drawer1', drawerEl);
uiStates['Chart1'] = { visible: true };
uiStates['Table1'] = { visible: false };
uiStates['Drawer1'] = { collapsed: true };

const captured = views.captureResolvedState();
assert.equal(captured.schemaVersion, 1);
assert.equal(captured.activePage, 'Summary');
assert.equal(captured.parameters['@Region'], 'North');
assert.equal(captured.parameters['@Amount'], 100);
assert.equal(captured.parameters['@Active'], true);
assert.equal(captured.visible['Chart1'], true);
assert.equal(captured.visible['Table1'], false);
assert.equal(captured.collapsed['Drawer1'], true);

// 6. applyBookmark (client-side / offline mode)
fakeWindow.__ETLSNAP__ = true;
const applied = await views.applyBookmark('DefaultView');
assert.equal(applied, true);
assert.equal(activePage, 'Detail');
assert.equal(chartEl.style.display, '');
assert.equal(tableEl.style.display, 'none');
assert.equal(drawerEl.classList.contains('collapsed'), true);
assert.equal(replacedHash, '#bookmark=DefaultView');
fakeWindow.__ETLSNAP__ = false;

// Non-existent bookmark
const missing = await views.applyBookmark('NonExistent');
assert.equal(missing, false);
assert.equal(notified.at(-1)?.opts?.tone, 'error');

// 7. buildViewsPicker
fakeWindow.__API_BASE__ = null;
const emptyPicker = views.buildViewsPicker({ bookmarks: [] });
assert.equal(emptyPicker, null);

const picker = views.buildViewsPicker(currentManifest);
assert.ok(picker);
assert.equal(picker.className, 'bookmark-picker');
const btn = picker.children[0];
assert.equal(btn.textContent, 'Views');
assert.equal(btn.getAttribute('aria-haspopup'), 'menu');
assert.equal(btn.getAttribute('aria-expanded'), 'false');

console.log('runtime views: hash parsing, endpoint routing, state capture, bookmark replay, and views picker verified');
