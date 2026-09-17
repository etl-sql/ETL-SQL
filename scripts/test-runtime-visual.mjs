import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';
import * as theme from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-theme.js';

class MockElement {
    tagName = 'DIV';
    children = [];
    style = {
        _props: {},
        getPropertyValue(name) { return this[name] || this._props[name] || ''; },
        setProperty(name, value) { this[name] = value; this._props[name] = value; },
        removeProperty(name) { delete this[name]; delete this._props[name]; },
        length: 0,
    };
    attributes = {};
    listeners = {};
    innerHTML = '';
    textContent = '';
    id = '';
    get className() { return [...this.classList._classes].join(' '); }
    set className(val) {
        this.classList._classes.clear();
        String(val || '').split(/\s+/).filter(Boolean).forEach(c => this.classList.add(c));
    }
    title = '';
    dataset = {};
    parentElement = null;
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

    constructor(tag = 'div') {
        this.tagName = tag.toUpperCase();
    }

    appendChild(child) {
        if (child) {
            child.parentElement = this;
            this.children.push(child);
        }
        return child;
    }

    insertBefore(newNode, refNode) {
        const idx = this.children.indexOf(refNode);
        if (idx >= 0) {
            newNode.parentElement = this;
            this.children.splice(idx, 0, newNode);
        } else {
            this.appendChild(newNode);
        }
        return newNode;
    }

    removeChild(child) {
        const idx = this.children.indexOf(child);
        if (idx >= 0) {
            this.children.splice(idx, 1);
            child.parentElement = null;
        }
        return child;
    }

    replaceChildren(...newChildren) {
        this.children = [];
        for (const c of newChildren) this.appendChild(c);
    }

    setAttribute(name, value) {
        this.attributes[name] = String(value);
        if (name.startsWith('data-')) {
            const key = name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase());
            this.dataset[key] = String(value);
        }
    }

    getAttribute(name) {
        return this.attributes[name] ?? null;
    }

    removeAttribute(name) {
        delete this.attributes[name];
        if (name.startsWith('data-')) {
            const key = name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase());
            delete this.dataset[key];
        }
    }

    hasAttribute(name) {
        return name in this.attributes;
    }

    addEventListener(event, handler) {
        (this.listeners[event] ||= []).push(handler);
    }

    dispatchEvent(evt) {
        const type = typeof evt === 'string' ? evt : evt.type;
        const e = typeof evt === 'string' ? { type } : evt;
        for (const h of this.listeners[type] || []) h(e);
    }

    querySelectorAll(selector) {
        const results = [];
        const match = el => {
            if (selector.startsWith('.') && el.classList.contains(selector.slice(1))) results.push(el);
            else if (selector.startsWith('#') && el.id === selector.slice(1)) results.push(el);
            else if (selector.startsWith('[') && selector.endsWith(']')) {
                const attr = selector.slice(1, -1).split('=')[0];
                if (el.hasAttribute(attr)) results.push(el);
            } else if (el.tagName.toLowerCase() === selector.toLowerCase()) {
                results.push(el);
            }
            for (const child of el.children) match(child);
        };
        for (const child of this.children) match(child);
        return results;
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] || null;
    }

    contains(other) {
        if (other === this) return true;
        for (const c of this.children) if (c.contains(other)) return true;
        return false;
    }

    remove() {
        if (this.parentElement) this.parentElement.removeChild(this);
    }
}

const mockBody = new MockElement('body');
const mockDoc = {
    body: mockBody,
    createElement(tag) { return new MockElement(tag); },
    createTextNode(text) {
        const node = new MockElement('#text');
        node.textContent = text;
        return node;
    },
    addEventListener() {},
    removeEventListener() {},
};

let executedActions = [];
const dependencies = {
    ...util,
    ...theme,
    document: mockDoc,
    window: { addEventListener() {}, removeEventListener() {} },
    actionsFor: (visual, trigger) => (visual.actions || []).filter(a => a.trigger === trigger),
    evaluateExpressionAgainstParameters: () => true,
    executeAction: (action, row, cols, name, visual) => {
        executedActions.push({ action, row, cols, name, visual });
    },
    matchesCondition: () => true,
    parameters: {},
    hasDeferredRows: () => false,
    loadVisualRows: async () => {},
    publishExportState: () => {},
    renderTable: () => {},
    renderCheckbox: () => {},
    renderNumberbox: () => {},
    renderSearch: () => {},
    renderSlicer: () => {},
    renderSlider: () => {},
    renderTextbox: () => {},
    renderDatePicker: () => {},
    renderRelDatePicker: () => {},
    renderMatrix: () => {},
    renderMissingChartPayload: () => {},
    renderNativeSvg: () => {},
    postDrillUp: () => {},
};

const source = readFileSync(new URL('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-visual.js', import.meta.url), 'utf8')
    .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');

const rtVisual = new Function(...Object.keys(dependencies), `${source}
return {
    resizeChartsIn,
    closeMaximizedVisual,
    toggleVisualMaximize,
    addVisualToolbar,
    shouldShowVisualToolbar,
    renderVisual,
    renderCard,
    renderText,
    renderImage,
    isSafeHtmlVisualUrl,
    copyHtmlVisualNode,
};`)(...Object.values(dependencies));

// 1. Maximization state management and toolbar policy
assert.equal(rtVisual.shouldShowVisualToolbar('SLICER', {}), false, 'SLICER hides maximize toolbar by default');
assert.equal(rtVisual.shouldShowVisualToolbar('CARD', {}), true, 'CARD shows maximize toolbar by default');
assert.equal(rtVisual.shouldShowVisualToolbar('SLICER', { ALLOW_MAXIMIZE: 'ON' }), true, 'ALLOW_MAXIMIZE=ON overrides type');
assert.equal(rtVisual.shouldShowVisualToolbar('CARD', { ALLOW_MAXIMIZE: 'OFF' }), false, 'ALLOW_MAXIMIZE=OFF suppresses toolbar');

const card1 = new MockElement('div');
card1.className = 'visual-card';
const maxBtn = new MockElement('button');
maxBtn.className = 'visual-tool-btn';
maxBtn.textContent = '[]';
card1.appendChild(maxBtn);

rtVisual.toggleVisualMaximize(card1, maxBtn);
assert.ok(card1.classList.contains('visual-maximized'), 'Card has visual-maximized class');
assert.ok(mockDoc.body.classList.contains('visual-maximize-active'), 'Body has visual-maximize-active class');
assert.equal(maxBtn.textContent, 'x', 'Button shows x when maximized');

// Close maximization
rtVisual.closeMaximizedVisual();
assert.ok(!card1.classList.contains('visual-maximized'), 'Card lost visual-maximized class after close');
assert.ok(!mockDoc.body.classList.contains('visual-maximize-active'), 'Body lost visual-maximize-active class after close');
assert.equal(maxBtn.textContent, '[]', 'Button restored to [] after close');

// 2. renderVisual card creation
const visualDef = {
    name: 'SalesKPI',
    type: 'CARD',
    rows: [[500]],
    options: {
        TITLE: 'Total Sales',
        SUBTITLE: 'Fiscal Year 2026',
        TAG: 'finance'
    },
    styles: {
        WIDTH: '300px',
        HEIGHT: '200px'
    }
};

const cardContainer = new MockElement('div');
rtVisual.renderVisual(cardContainer, visualDef, null, {});
const visualCard = cardContainer.children[0];
assert.ok(visualCard, 'Card element rendered into container');
assert.equal(visualCard.getAttribute('data-name'), 'SalesKPI');
assert.equal(visualCard.getAttribute('data-visual-name'), 'SalesKPI');
assert.equal(visualCard.getAttribute('data-tag'), 'finance');
assert.equal(visualCard.style.width, '300px');
assert.equal(visualCard.style.height, '200px');
assert.equal(visualCard._visualData, visualDef, '_visualData attached');

const title = visualCard.querySelector('h3');
assert.ok(title, 'Title element exists');
assert.equal(title.textContent, 'Total Sales');

assert.ok(visualCard.classList.contains('has-visual-toolbar'), 'Card has toolbar class');
const cardToolBtn = visualCard.querySelector('.visual-tool-btn');
assert.ok(cardToolBtn, 'Maximize button created in toolbar');
assert.equal(cardToolBtn.getAttribute('aria-label'), 'Maximize visual');

// 3. renderCard (KPI Card)
const container = new MockElement('div');
const cardVisual = {
    name: 'RevenueKPI',
    type: 'CARD',
    columns: ['revenue', 'prev_revenue'],
    rows: [[1250000, 1000000]],
    options: {
        'mapping:value': 'revenue',
        FORMAT: 'C0',
        TITLE: 'Revenue',
        SUBTITLE: 'Q3 Actuals',
        GOAL: '1500000',
        DELTA: '250000',
        DELTA_FORMAT: 'C0',
        SHOW_GOAL: 'ON',
        BADGE: 'On Track',
        TREND: 'UP'
    }
};

rtVisual.renderCard(container, cardVisual);
assert.equal(container.children.length, 1, 'Card element appended');
const kpi = container.children[0];
assert.ok(kpi.innerHTML.includes('$1,250,000'), 'Formatted currency value rendered');
assert.ok(kpi.innerHTML.includes('Revenue'), 'Card title rendered');
assert.ok(kpi.innerHTML.includes('card-status-badge'), 'Status badge rendered');
assert.ok(kpi.innerHTML.includes('⚠'), 'Close status icon rendered');
assert.ok(kpi.innerHTML.includes('Target: $1,500,000'), 'Goal rendered');

// 4. renderText with template interpolation
const textContainer = new MockElement('div');
const textVisual = {
    name: 'GreetingText',
    type: 'TEXT',
    defaultValue: 'Welcome! Total profit is {Profit FORMAT "C2"} across {Units} items.',
    columns: ['Profit', 'Units'],
    rows: [[4567.89, 120]],
    options: {
        ALIGN: 'CENTER',
        MARKDOWN: 'OFF'
    }
};

rtVisual.renderText(textContainer, textVisual);
const textEl = textContainer.children[0];
assert.ok(textEl, 'Text element appended');
assert.ok(textEl.innerHTML.includes('$4,567.89'), 'Interpolated currency with format');
assert.ok(textEl.innerHTML.includes('120 items'), 'Interpolated raw column value');
assert.equal(textEl.style.textAlign, 'center');

// 5. URL validation
assert.equal(rtVisual.isSafeHtmlVisualUrl('https://example.com/image.png'), true);
assert.equal(rtVisual.isSafeHtmlVisualUrl('http://insecure.com/x'), true);
assert.equal(rtVisual.isSafeHtmlVisualUrl('javascript:alert(1)'), false);
assert.equal(rtVisual.isSafeHtmlVisualUrl('java\0script:alert(1)'), false);
assert.equal(rtVisual.isSafeHtmlVisualUrl('data:image/png;base64,iVBORw0KGgo='), true);
assert.equal(rtVisual.isSafeHtmlVisualUrl('data:text/html,<script>alert(1)</script>'), false);

console.log('runtime visual: card creation, maximization lifecycle, KPI formatting, text interpolation, and sanitization passed');
