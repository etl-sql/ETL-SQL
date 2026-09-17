import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

// DOM Mock for detail surface rendering
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

    remove() {
        if (this.parentNode) {
            const idx = this.parentNode.children.indexOf(this);
            if (idx >= 0) this.parentNode.children.splice(idx, 1);
            this.parentElement = null;
            this.parentNode = null;
        }
    }

    replaceChildren(...newChildren) {
        for (const child of [...this.children]) {
            child.remove();
        }
        for (const child of newChildren) {
            this.appendChild(child);
        }
    }
}

class MockElement extends MockNode {
    style = {};
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
    _focused = false;

    constructor(tag = 'div') {
        super();
        this.tagName = tag.toUpperCase();
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

    setAttribute(name, val) { this.attributes[name] = String(val); }
    getAttribute(name) { return this.attributes[name] ?? null; }
    removeAttribute(name) { delete this.attributes[name]; }
    addEventListener(event, fn) { (this.listeners[event] ||= []).push(fn); }
    removeEventListener(event, fn) {
        if (!this.listeners[event]) return;
        this.listeners[event] = this.listeners[event].filter(f => f !== fn);
    }
    dispatchEvent(event) {
        const list = this.listeners[event.type] || [];
        for (const fn of list) fn(event);
    }
    focus() { this._focused = true; }
    blur() { this._focused = false; }

    contains(el) {
        let cur = el;
        while (cur) {
            if (cur === this) return true;
            cur = cur.parentElement;
        }
        return false;
    }

    querySelectorAll(selector) {
        const matches = [];
        const walk = el => {
            for (const child of el.children) {
                if (child instanceof MockElement) {
                    if (selector.startsWith('.') && child.classList.contains(selector.slice(1))) {
                        matches.push(child);
                    } else if (selector.startsWith('[data-row-index]') && ('rowIndex' in child.dataset || child.getAttribute('data-row-index') !== null)) {
                        matches.push(child);
                    } else if (selector.toUpperCase() === child.tagName) {
                        matches.push(child);
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
            if (selector === '[data-row-index]' && ('rowIndex' in cur.dataset || cur.getAttribute('data-row-index') !== null)) {
                return cur;
            }
            cur = cur.parentElement;
        }
        return null;
    }
}

const mockBody = new MockElement('body');
const mockDoc = {
    body: mockBody,
    createElement(tag) { return new MockElement(tag); },
    createTextNode(text) {
        const el = new MockElement('span');
        el.textContent = text;
        return el;
    },
    getElementById() { return null; },
    querySelector() { return null; },
    querySelectorAll() { return []; },
    addEventListener() {},
    removeEventListener() {},
    contains(el) { return mockBody.contains(el); },
};

const mockWindow = {
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() {},
    location: { protocol: 'http:' },
    innerWidth: 1000,
    innerHeight: 800,
};

let postedParameters = [];
const dependencies = {
    ...util,
    renderContainer: () => {},
    renderVisual: () => {},
    postParameters: async (...args) => { postedParameters.push(args); return null; },
    document: mockDoc,
    window: mockWindow,
    ResizeObserver: class {
        observe() {}
        unobserve() {}
        disconnect() {}
    },
};

// ── 1. Contract & Header Assertions ──────────────────────────────────────────
const emitted = readFileSync('src/ETL-SQL.ReportRuntime/Resources/Shared/rt-detail.js', 'utf8');
assert.match(emitted, /\/\* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT\./);
assert.match(emitted, /Source: src\/ETL-SQL\.ReportRuntime\/Resources\/TypeScript\/rt-detail\.ts/);
assert.match(emitted, /export function computeDetailPlacement\(/);
assert.match(emitted, /export function closeOpenDetail\(/);
assert.match(emitted, /export function appendDetailStaticNote\(/);
assert.match(emitted, /export function destroyDetailSurfaces\(/);
assert.match(emitted, /export function attachDetailSurface\(/);

const source = emitted
    .replace(/^import .*;\r?\n/gm, '')
    .replace(/^export /gm, '');

const rtDetail = new Function(...Object.keys(dependencies), `${source}
return {
    computeDetailPlacement,
    closeOpenDetail,
    appendDetailStaticNote,
    destroyDetailSurfaces,
    attachDetailSurface,
    getOpenDetail,
    setOpenDetail,
    DETAIL_VIEWPORT_MARGIN,
    DETAIL_ANCHOR_GAP,
    DETAIL_PREFERRED_SIDE,
};`)(...Object.values(dependencies));

const {
    computeDetailPlacement,
    closeOpenDetail,
    appendDetailStaticNote,
    destroyDetailSurfaces,
    attachDetailSurface,
    getOpenDetail,
    setOpenDetail,
    DETAIL_VIEWPORT_MARGIN,
    DETAIL_ANCHOR_GAP,
    DETAIL_PREFERRED_SIDE,
} = rtDetail;

// ── 2. Pure Geometry Tests: computeDetailPlacement ───────────────────────────
{
    // Preferred side 'top' with ample space above:
    // Anchor at top: 300, bottom: 350. Size height: 100.
    // Candidate top: 300 - 100 - 10 = 190 >= 8 (margin) -> fits 'top'
    const anchor = { left: 100, top: 300, right: 200, bottom: 350 };
    const size = { width: 150, height: 100 };
    const viewport = { width: 1000, height: 800 };

    const topResult = computeDetailPlacement(anchor, size, viewport);
    assert.equal(topResult.side, 'top');
    assert.equal(topResult.top, 190);
    assert.equal(topResult.left, 100);
    assert.equal(topResult.flipped, false);
    assert.equal(topResult.shifted, false);

    // Preferred side 'top' with tight space above (anchor.top = 20):
    // 20 - 100 - 10 = -90 < 8 -> does NOT fit top.
    // Flips to next in flip order: 'bottom'
    // Candidate bottom: anchor.bottom (70) + 10 = 80. 80 + 100 = 180 <= 792 -> fits 'bottom'
    const tightAnchor = { left: 100, top: 20, right: 200, bottom: 70 };
    const flipResult = computeDetailPlacement(tightAnchor, size, viewport);
    assert.equal(flipResult.side, 'bottom');
    assert.equal(flipResult.top, 80);
    assert.equal(flipResult.flipped, true);

    // RTL alignment: leading edge should align to anchor.right - size.width
    const rtlResult = computeDetailPlacement(anchor, size, viewport, { rtl: true });
    assert.equal(rtlResult.left, 200 - 150); // 50

    // Preferred side 'right'
    const rightAnchor = { left: 100, top: 200, right: 200, bottom: 260 };
    const rightResult = computeDetailPlacement(rightAnchor, size, viewport, { preferredSide: 'right' });
    assert.equal(rightResult.side, 'right');
    assert.equal(rightResult.left, 200 + 10); // 210
    // cross-axis alignTop: 200 + (260 - 200)/2 - 100/2 = 200 + 30 - 50 = 180
    assert.equal(rightResult.top, 180);

    // Clamping to margin when oversized
    const hugeSize = { width: 1200, height: 900 };
    const clamped = computeDetailPlacement(anchor, hugeSize, viewport);
    assert.equal(clamped.left, DETAIL_VIEWPORT_MARGIN);
    assert.equal(clamped.top, DETAIL_VIEWPORT_MARGIN);
    assert.equal(clamped.shifted, true);
}

// ── 3. appendDetailStaticNote ────────────────────────────────────────────────
{
    const container = new MockElement('div');
    appendDetailStaticNote(container, { name: 'test_no_tooltip' });
    assert.equal(container.children.length, 0);

    appendDetailStaticNote(container, {
        name: 'test_with_tooltip',
        tooltip: { staticSummary: 'Summary of 42 records across regions' }
    });
    assert.equal(container.children.length, 1);
    const note = container.children[0];
    assert.equal(note.tagName, 'P');
    assert.equal(note.className, 'report-detail-static-note');
    assert.equal(note.textContent, 'Summary of 42 records across regions');
}

// ── 4. destroyDetailSurfaces ─────────────────────────────────────────────────
{
    const scope = new MockElement('div');
    const chartWrapper = new MockElement('div');
    chartWrapper.classList.add('chart-wrapper');
    let destroyed = false;
    chartWrapper._detailSurface = {
        destroy: () => { destroyed = true; }
    };
    scope.appendChild(chartWrapper);

    destroyDetailSurfaces(scope);
    assert.equal(destroyed, true);
    assert.equal(chartWrapper._detailSurface, null);

    // Null/undefined safety
    destroyDetailSurfaces(null);
    destroyDetailSurfaces(undefined);
}

// ── 5. attachDetailSurface Lifecycle ─────────────────────────────────────────
{
    const wrapper = new MockElement('div');
    const mark = new MockElement('span');
    mark.dataset.rowIndex = '0';
    mark.setAttribute('data-row-index', '0');
    wrapper.appendChild(mark);

    const visual = {
        name: 'sales_by_region',
        title: 'Regional Sales',
        columns: ['Region', 'Amount'],
        rows: [['North', 1500], ['South', 2300]],
        tooltip: {
            mode: 'popover',
            type: 'inline',
            visuals: ['detail_sub_chart']
        }
    };

    const handle = attachDetailSurface(wrapper, visual, {}, 'light', 'Region');
    assert.ok(handle && typeof handle.destroy === 'function');

    // Mark should be initialized with accessibility attributes
    assert.equal(mark.getAttribute('tabindex'), '0');
    assert.equal(mark.getAttribute('role'), 'button');
    assert.equal(mark.getAttribute('aria-haspopup'), 'dialog');
    assert.equal(mark.dataset.detailReady, '1');

    // Teardown
    handle.destroy();
}

console.log('test-runtime-detail: all assertions passed');
