import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

// DOM Mock for table rendering
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
    _innerHTML = '';
    type = '';
    value = '';
    placeholder = '';
    colSpan = 1;
    disabled = false;

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

    get innerHTML() { return this._innerHTML; }
    set innerHTML(val) {
        this.children = [];
        this._innerHTML = String(val ?? '');
    }

    get rows() {
        return this.children.filter(c => c.tagName === 'TR');
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

    querySelectorAll(selector) {
        const matches = [];
        const walk = el => {
            for (const child of el.children) {
                if (child instanceof MockElement) {
                    if (selector.startsWith('.') && child.classList.contains(selector.slice(1))) {
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
            if (selector.toUpperCase() === cur.tagName) return cur;
            if (selector.startsWith('.') && cur.classList.contains(selector.slice(1))) return cur;
            cur = cur.parentElement;
        }
        return null;
    }
}

const mockDoc = {
    createElement(tag) { return new MockElement(tag); },
    createTextNode(text) {
        const el = new MockElement('span');
        el.textContent = text;
        return el;
    },
    getElementById() { return null; },
    querySelector() { return null; },
    querySelectorAll() { return []; },
};

globalThis.document = mockDoc;

const dependencies = {
    ...util,
    actionsFor: () => [],
    applyPageCrossFilter: () => {},
    executeAction: () => {},
    showCtxMenu: () => {},
    crossFilterActive: () => false,
    resolveInteraction: () => ({}),
    _uiStates: {},
    renderVisual: () => {},
    findMicroChart: (visual, rIdx, cIdx) => {
        if (cIdx === 1) return { svg: '<svg>spark</svg>', accessibleLabel: 'Trend sparkline' };
        return null;
    },
    document: mockDoc,
};

// ── 1. Contract & Header Assertions ──────────────────────────────────────────
const emitted = readFileSync('src/ETL-SQL.ReportRuntime/Resources/Shared/rt-table.js', 'utf8');
assert.match(emitted, /\/\* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT\./);
assert.match(emitted, /Source: src\/ETL-SQL\.ReportRuntime\/Resources\/TypeScript\/rt-table\.ts/);
assert.match(emitted, /export function renderTable\(/);

const source = emitted
    .replace(/^import .*;\r?\n/gm, '')
    .replace(/^export /gm, '');

const rtTable = new Function(...Object.keys(dependencies), `${source}
return { renderTable };`)(...Object.values(dependencies));

const { renderTable } = rtTable;

// ── 2. No Data Fallback ──────────────────────────────────────────────────────
{
    const container = new MockElement('div');
    renderTable(container, { columns: [] }, null);
    assert.equal(container.children.length, 1);
    assert.equal(container.children[0].className, 'no-data');
    assert.match(container.children[0].textContent, /No data available/);
}

// ── 3. Basic Table Rendering ─────────────────────────────────────────────────
{
    const container = new MockElement('div');
    const visual = {
        name: 'sales_table',
        columns: ['Product', 'Trend', 'Revenue', 'Status'],
        rows: [
            ['Alpha', 10, 5000, 'Active'],
            ['Beta', 20, 12000, 'Pending'],
            ['Gamma', 15, 8500, 'Active'],
        ],
        options: {
            PAGE_SIZE: '10',
            STRIPED: 'ON',
            SEARCH: 'ON',
        },
        columnMeta: [
            { width: 150, freeze: 'left' },
            { cellRenderer: 'sparkline' },
            { dataBar: true, dataBarMax: 15000, dataBarMin: 0, dataBarColor: '#00cc00' },
            { colorScaleFrom: '#ffffff', colorScaleTo: '#0055ff', colorScaleMin: 0, colorScaleMax: 100 },
        ],
        summaryData: {
            totalPosition: 'BOTTOM',
            grandTotals: { Revenue: 25500 },
            aggregates: [{ aggregate: 'SUM', column: 'Revenue', value: 25500 }]
        }
    };

    renderTable(container, visual, null);
    const wrapper = container.querySelector('.table-wrapper');
    assert.ok(wrapper, 'Should mount table-wrapper');

    // Search row
    const searchRow = wrapper.querySelector('.table-search-row');
    assert.ok(searchRow, 'Should render search row');
    const searchInput = searchRow.querySelector('.table-search-input');
    assert.ok(searchInput, 'Should render search input');

    // Table & headers
    const table = wrapper.querySelector('table');
    assert.ok(table, 'Should render table');
    const thead = table.querySelector('thead');
    assert.ok(thead, 'Should render thead');
    const ths = thead.querySelectorAll('th');
    assert.equal(ths.length, 4, 'Should have 4 column headers');
    assert.ok(ths[0].classList.contains('table-cell-frozen-left'), 'First th should be frozen left');

    // Body rows
    const tbody = table.querySelector('tbody');
    assert.ok(tbody, 'Should render tbody');
    const trs = tbody.querySelectorAll('tr');
    assert.equal(trs.length, 3, 'Should render 3 data rows');

    // Striped row check
    assert.ok(trs[1].classList.contains('table-row-alt'), 'Second row should have table-row-alt');

    // Sparkline cell check
    const sparkTd = trs[0].children[1];
    assert.equal(sparkTd.getAttribute('role'), 'img');
    assert.equal(sparkTd.getAttribute('aria-label'), 'Trend sparkline');
    assert.match(sparkTd.innerHTML, /<svg>spark<\/svg>/);

    // Data bar cell check
    const barTd = trs[0].children[2];
    const dataBar = barTd.querySelector('.data-bar-fill');
    assert.ok(dataBar, 'Should render data-bar-fill');
    assert.equal(dataBar.style.backgroundColor, '#00cc00');

    // Summary tfoot check
    const tfoot = table.querySelector('tfoot');
    assert.ok(tfoot, 'Should render tfoot when totalPosition is BOTTOM');
    const summaryRow = tfoot.querySelector('.summary-row');
    assert.ok(summaryRow, 'Should render summary row');
    const aggRow = tfoot.querySelector('.summary-aggregates');
    assert.ok(aggRow, 'Should render aggregates');
    assert.match(aggRow.textContent, /SUM\(Revenue\) = 25500/);
}

// ── 4. Nested Row Detail ─────────────────────────────────────────────────────
{
    const container = new MockElement('div');
    let nestedVisualRendered = false;
    const manifest = {
        visuals: [
            { name: 'order_items', columns: ['OrderID', 'Item'], rows: [['101', 'Widget']] }
        ]
    };

    const visual = {
        name: 'orders_table',
        columns: ['OrderID', 'Customer'],
        rows: [['101', 'Acme Corp']],
        rowDetail: { targetName: 'order_items' },
        rowDetailKeys: [{ OrderID: '101' }]
    };

    renderTable(container, visual, manifest);
    const tbody = container.querySelector('tbody');
    const expBtn = tbody.querySelector('.expand-btn');
    assert.ok(expBtn, 'Should render expand button');
    assert.equal(expBtn.getAttribute('aria-expanded'), 'false');

    // Expand
    expBtn.dispatchEvent({ type: 'click', stopPropagation() {} });
    assert.equal(expBtn.getAttribute('aria-expanded'), 'true');
    const detailRow = tbody.querySelector('.detail-row');
    assert.ok(detailRow, 'Should render detail row on expand');

    // Collapse
    expBtn.dispatchEvent({ type: 'click', stopPropagation() {} });
    assert.equal(expBtn.getAttribute('aria-expanded'), 'false');
    assert.equal(detailRow.style.display, 'none');
}

console.log('test-runtime-table: all assertions passed');
