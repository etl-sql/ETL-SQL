import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

// Mock DOM classes for SVG and chart rendering
class MockNode {
    children = [];
    parentElement = null;
    parentNode = null;

    appendChild(child) {
        if (child) {
            child.parentElement = this;
            child.parentNode = this;
            this.children.push(child);
        }
        return child;
    }

    append(...children) {
        for (const c of children) this.appendChild(c);
    }

    replaceChild(newChild, oldChild) {
        const idx = this.children.indexOf(oldChild);
        if (idx >= 0) {
            newChild.parentElement = this;
            newChild.parentNode = this;
            this.children[idx] = newChild;
            oldChild.parentElement = null;
            oldChild.parentNode = null;
        } else {
            this.appendChild(newChild);
        }
        return oldChild;
    }

    insertBefore(newNode, refNode) {
        const idx = this.children.indexOf(refNode);
        if (idx >= 0) {
            newNode.parentElement = this;
            newNode.parentNode = this;
            this.children.splice(idx, 0, newNode);
        } else {
            this.appendChild(newNode);
        }
        return newNode;
    }

    cloneNode(deep = false) {
        const copy = new MockElement(this.tagName ? this.tagName.toLowerCase() : 'div');
        copy.attributes = { ...this.attributes };
        copy.dataset = { ...this.dataset };
        copy.style = { ...this.style };
        copy._classes = new Set(this.classList._classes);
        if (deep) {
            for (const child of this.children) {
                if (child.cloneNode) copy.appendChild(child.cloneNode(true));
            }
        }
        return copy;
    }
}

class MockElement extends MockNode {
    style = {
        setProperty(k, v) { this[k] = String(v); },
        getPropertyValue(k) { return this[k] || ''; }
    };
    attributes = {};
    listeners = {};
    classList = {
        _classes: new Set(),
        add(...tokens) { tokens.forEach(t => this._classes.add(t)); },
        remove(...tokens) { tokens.forEach(t => this._classes.delete(t)); },
        contains(token) { return this._classes.has(token); },
        toggle(token, force) {
            if (force === undefined) force = !this.contains(token);
            if (force) this.add(token); else this.remove(token);
            return force;
        }
    };
    dataset = {};
    tagName = 'DIV';
    _textContent = '';
    _innerHTML = '';
    hidden = false;
    type = '';
    value = '';

    constructor(tag = 'div') {
        super();
        this.tagName = tag.toUpperCase();
        this.nodeName = this.tagName;
    }

    get className() { return [...this.classList._classes].join(' '); }
    set className(val) {
        this.classList._classes.clear();
        String(val || '').split(/\s+/).filter(Boolean).forEach(c => this.classList.add(c));
    }

    get textContent() {
        if (this.children.length === 0) return this._textContent;
        return this.children.map(c => c.textContent || '').join('');
    }
    set textContent(val) {
        this.children = [];
        this._textContent = String(val ?? '');
    }

    setAttribute(name, val) {
        this.attributes[name] = String(val);
        if (name.startsWith('data-')) {
            const prop = name.slice(5).replace(/-([a-z])/g, (_, l) => l.toUpperCase());
            this.dataset[prop] = String(val);
        }
    }
    getAttribute(name) { return this.attributes[name] ?? null; }
    removeAttribute(name) {
        delete this.attributes[name];
        if (name.startsWith('data-')) {
            const prop = name.slice(5).replace(/-([a-z])/g, (_, l) => l.toUpperCase());
            delete this.dataset[prop];
        }
    }
    addEventListener(event, fn) { (this.listeners[event] ||= []).push(fn); }
    dispatchEvent(event) {
        const list = this.listeners[event.type] || [];
        for (const fn of list) fn(event);
    }

    querySelectorAll(selector) {
        const matches = [];
        const walk = el => {
            for (const child of el.children) {
                if (child instanceof MockElement) {
                    if (selector.startsWith('.') && child.classList.contains(selector.slice(1))) {
                        matches.push(child);
                    } else if (selector.toUpperCase() === child.tagName) {
                        matches.push(child);
                    } else if (selector.startsWith('[') && selector.endsWith(']')) {
                        const attrName = selector.slice(1, -1).split('=')[0].trim();
                        if (child.getAttribute(attrName) != null) matches.push(child);
                    }
                    walk(child);
                }
            }
        };
        walk(this);
        return matches;
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] || null;
    }

    closest(selector) {
        let cur = this;
        while (cur) {
            if (selector.toUpperCase() === cur.tagName) return cur;
            if (selector.startsWith('.') && cur.classList.contains(selector.slice(1))) return cur;
            cur = cur.parentElement;
        }
        return null;
    }
}

class MockDOMParser {
    parseFromString(str, type) {
        if (!str || str.includes('parsererror')) {
            const errDoc = {
                documentElement: null,
                querySelector: s => s === 'parsererror' ? new MockElement('parsererror') : null
            };
            return errDoc;
        }
        const svg = new MockElement('svg');
        svg.setAttribute('viewBox', '0 0 600 400');
        svg.setAttribute('width', '600');
        svg.setAttribute('height', '400');
        return {
            documentElement: svg,
            querySelector: () => null
        };
    }
}

const mockDoc = {
    createElement(tag) { return new MockElement(tag); },
    createElementNS(ns, tag) { return new MockElement(tag); },
    createTextNode(text) {
        const el = new MockElement('span');
        el.textContent = text;
        return el;
    },
    importNode(node) { return node.cloneNode ? node.cloneNode(true) : node; },
    getElementById() { return null; },
    querySelector() { return null; },
    querySelectorAll() { return []; },
};

globalThis.document = mockDoc;
globalThis.DOMParser = MockDOMParser;
globalThis.ResizeObserver = class {
    observe() {}
    disconnect() {}
};

const dependencies = {
    ...util,
    actionsFor: () => [],
    applyPageCrossFilter: () => {},
    executeAction: () => {},
    showCtxMenu: () => {},
    appendDetailStaticNote: () => {},
    attachDetailSurface: () => ({ destroy: () => {} }),
    apiBase: '/api',
    getLastManifest: () => null,
    isWebMode: true,
    vscode: null,
    isOfflineSnapshot: () => false,
    renderManifest: () => {},
    document: mockDoc,
    DOMParser: MockDOMParser,
};

// ── 1. Contract & Header Assertions ──────────────────────────────────────────
const emitted = readFileSync('src/ETL-SQL.ReportRuntime/Resources/Shared/rt-charts.js', 'utf8');
assert.match(emitted, /\/\* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT\./);
assert.match(emitted, /Source: src\/ETL-SQL\.ReportRuntime\/Resources\/TypeScript\/rt-charts\.ts/);
assert.match(emitted, /export function renderMissingChartPayload\(/);
assert.match(emitted, /export function resolveInteraction\(/);
assert.match(emitted, /export function crossFilterActive\(/);
assert.match(emitted, /export function renderNativeSvg\(/);
assert.match(emitted, /export function nativeLayoutTier\(/);
assert.match(emitted, /export function observeNativeLayout\(/);
assert.match(emitted, /export function getNativeLayoutObservers\(/);
assert.match(emitted, /export function setNativeLayoutObservers\(/);
assert.match(emitted, /export const _nativeLayoutTimers =/);

const source = emitted
    .replace(/^import .*;\r?\n/gm, '')
    .replace(/^export /gm, '');

const rtCharts = new Function(...Object.keys(dependencies), `${source}
return {
    renderMissingChartPayload,
    resolveInteraction,
    crossFilterActive,
    renderNativeSvg,
    nativeLayoutTier,
    observeNativeLayout,
    getNativeLayoutObservers,
    setNativeLayoutObservers,
    _nativeLayoutTimers
};`)(...Object.values(dependencies));

const {
    renderMissingChartPayload,
    resolveInteraction,
    crossFilterActive,
    renderNativeSvg,
    nativeLayoutTier,
    observeNativeLayout,
    getNativeLayoutObservers,
    setNativeLayoutObservers,
    _nativeLayoutTimers
} = rtCharts;

// ── 2. Missing Chart Payload Fallback ─────────────────────────────────────────
{
    const container = new MockElement('div');
    const visual = { visualType: 'scatter', title: 'Customer Churn', name: 'vChurn' };
    const el = renderMissingChartPayload(container, visual);
    assert.equal(el.getAttribute('role'), 'status');
    assert.equal(el.classList.contains('missing-chart-payload'), true);
    assert.match(el.textContent, /Customer Churn/);
    assert.match(el.textContent, /SCATTER/);
}

// ── 3. Interaction Resolution ────────────────────────────────────────────────
{
    // Modern resolved manifest
    const modern = resolveInteraction({
        interaction: {
            key: 'Category',
            valueKey: 'Revenue',
            select: 'single',
            effect: 'filter',
            highlight: 'categorical'
        }
    });
    assert.equal(modern.key, 'Category');
    assert.equal(modern.valueKey, 'Revenue');
    assert.equal(modern.select, 'SINGLE');
    assert.equal(modern.effect, 'FILTER');
    assert.equal(modern.highlight, 'CATEGORICAL');
    assert.equal(crossFilterActive(modern), true);

    // Legacy fallback
    const legacyBar = resolveInteraction({
        visualType: 'bar',
        interactions: { ON_SELECT: 'FILTER', MATCHING: 'Region' },
        options: { 'mapping:y': 'Sales' }
    });
    assert.equal(legacyBar.key, 'Region');
    assert.equal(legacyBar.valueKey, 'Sales');
    assert.equal(legacyBar.select, 'MULTIPLE');
    assert.equal(legacyBar.effect, 'FILTER');
    assert.equal(legacyBar.highlight, 'PROPORTIONAL');
    assert.deepEqual(legacyBar.extent, { axis: 'y', anchor: 'end' });
    assert.equal(crossFilterActive(legacyBar), true);

    // Inactive interaction
    const inactive = resolveInteraction(null);
    assert.equal(inactive.select, 'NONE');
    assert.equal(crossFilterActive(inactive), false);
}

// ── 4. Native Layout Tiers ───────────────────────────────────────────────────
{
    const layout = {
        compactMaxWidth: 400,
        standardMaxWidth: 800
    };
    assert.equal(nativeLayoutTier(layout, 350), 'COMPACT');
    assert.equal(nativeLayoutTier(layout, 400), 'COMPACT');
    assert.equal(nativeLayoutTier(layout, 650), 'STANDARD');
    assert.equal(nativeLayoutTier(layout, 800), 'STANDARD');
    assert.equal(nativeLayoutTier(layout, 1200), 'WIDE');

    // Invalid widths or layout
    assert.equal(nativeLayoutTier(null, 500), null);
    assert.equal(nativeLayoutTier(layout, 0), null);
    assert.equal(nativeLayoutTier(layout, -50), null);
}

// ── 5. Native SVG Chart Rendering ────────────────────────────────────────────
{
    const container = new MockElement('div');
    const visual = {
        name: 'SalesChart',
        nativeSvg: '<svg viewBox="0 0 600 400"><rect data-row-index="0" x="10" y="20" width="50" height="100"/></svg>',
        layout: { tier: 'standard' },
        options: {
            SHOW_EXPORT: 'ON',
            SHOW_DATA_VIEW: 'ON',
            ZOOM_SLIDER: 'ON'
        },
        columns: ['Category', 'Sales'],
        rows: [
            ['Electronics', 50000],
            ['Apparel', 32000]
        ]
    };

    renderNativeSvg(container, visual, { visuals: [visual] }, null);
    const wrapper = container.querySelector('.native-chart-wrapper');
    assert.ok(wrapper, 'wrapper should be created');
    assert.equal(wrapper.dataset.layoutTier, 'STANDARD');

    // Toolbox presence
    const toolbox = container.querySelector('.native-chart-toolbox');
    assert.ok(toolbox, 'toolbox should be attached');
    const tools = toolbox.querySelectorAll('button');
    assert.equal(tools.length, 2, 'save-image and data-view buttons should be created');

    // Zoom slider presence
    const zoomSlider = container.querySelector('.native-chart-zoom-slider');
    assert.ok(zoomSlider, 'zoom slider controls should be attached');

    // Data table presence
    const dataTable = container.querySelector('.native-chart-data-view');
    assert.ok(dataTable, 'data view table should be attached');
}

// ── 6. Layout Observer Accessors ─────────────────────────────────────────────
{
    const obs = [new globalThis.ResizeObserver()];
    setNativeLayoutObservers(obs);
    assert.equal(getNativeLayoutObservers(), obs);
    setNativeLayoutObservers([]);
    assert.equal(getNativeLayoutObservers().length, 0);
    assert.ok(_nativeLayoutTimers instanceof Map);
}

console.log('runtime charts: all contract, rendering, interaction, and layout checks passed');
