import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Mock DOM and window before importing runtime modules
globalThis.window = {
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() {},
    location: { protocol: 'http:' },
};

// DOM Mock for matrix rendering
class MockNode {
    children = [];
    parentElement = null;
    appendChild(child) {
        if (child) {
            child.parentElement = this;
            this.children.push(child);
        }
        return child;
    }
}

class MockTextNode extends MockNode {
    constructor(text) {
        super();
        this.nodeValue = String(text ?? '');
        this.textContent = this.nodeValue;
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
    colSpan = 1;
    rowSpan = 1;
    type = '';
    _textContent = '';

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
}

globalThis.document = {
    createElement(tag) { return new MockElement(tag); },
    createTextNode(text) { return new MockTextNode(text); },
    getElementById() { return new MockElement('div'); },
    querySelector() { return null; },
    querySelectorAll() { return []; },
    addEventListener() {},
    removeEventListener() {},
};

const { findMicroChart, renderMatrix } = await import('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-matrix.js');

// Verify generated output contract
const emitted = readFileSync('src/ETL-SQL.ReportRuntime/Resources/Shared/rt-matrix.js', 'utf8');
assert.match(emitted, /\/\* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT\./);
assert.match(emitted, /Source: src\/ETL-SQL\.ReportRuntime\/Resources\/TypeScript\/rt-matrix\.ts/);
assert.match(emitted, /export function renderMatrix\(/);
assert.match(emitted, /export function findMicroChart\(/);

// ── Test findMicroChart ──────────────────────────────────────────────────
{
    assert.equal(findMicroChart({}, 0, 0, 'test'), null);
    assert.equal(findMicroChart({ microCharts: [] }, 0, 0, 'test'), null);

    const visual = {
        microCharts: [
            { role: 'table.cell', columnIndex: 2, rowIndex: 1, svg: '<svg>match-coord</svg>' },
            { role: 'table.cell', columnIndex: 3, sourceValue: 'Sales', svg: '<svg>match-value</svg>' },
            { role: 'other.role', columnIndex: 2, rowIndex: 1, svg: '<svg>ignored</svg>' },
        ]
    };

    const coordMatch = findMicroChart(visual, 1, 2, 'anything');
    assert.equal(coordMatch?.svg, '<svg>match-coord</svg>');

    const valueMatch = findMicroChart(visual, 99, 3, 'Sales');
    assert.equal(valueMatch?.svg, '<svg>match-value</svg>');

    const noMatch = findMicroChart(visual, 99, 99, 'None');
    assert.equal(noMatch, null);
}

// ── Test renderMatrix with missing or invalid payload ────────────────────
{
    const container = new MockElement('div');
    renderMatrix(container, {});
    assert.equal(container.children.length, 1);
    assert.equal(container.children[0].className, 'no-data');
    assert.match(container.children[0].textContent, /No pivot data available/);
}

// ── Test renderMatrix basic pivot structure ──────────────────────────────
{
    const container = new MockElement('div');
    const visual = {
        name: 'test_matrix_basic',
        chartConfig: JSON.stringify({
            __matrix: true,
            rowHeaders: ['Region'],
            colHeaders: ['Category'],
            colParts: [['North'], ['South']],
            rows: [
                ['Electronics', 100, 200],
                ['Apparel', 300, 400],
            ],
            grandTotals: ['Grand Total', 400, 600],
            aggregate: 'SUM',
            columnTotalsEnabled: true,
            rowTotalsEnabled: true,
        })
    };

    renderMatrix(container, visual);
    const wrapper = container.querySelector('.table-wrapper');
    assert.ok(wrapper, 'Should render table-wrapper');
    const table = wrapper.querySelector('.matrix-table');
    assert.ok(table, 'Should render matrix-table');

    const headers = table.querySelectorAll('.matrix-val-header');
    assert.ok(headers.length >= 2, 'Should have column headers');

    const grandTotalRow = table.querySelector('.matrix-grand-total');
    assert.ok(grandTotalRow, 'Should render grand total row');
    const totalVals = grandTotalRow.querySelectorAll('.matrix-total-val');
    assert.ok(totalVals.length >= 2, 'Should have total values');
    assert.equal(totalVals[0].textContent, '400');
    assert.equal(totalVals[1].textContent, '600');
}

// ── Test renderMatrix with MIN, MAX, AVG aggregations ───────────────────
{
    for (const agg of ['MIN', 'MAX', 'AVG']) {
        const container = new MockElement('div');
        const visual = {
            name: `test_matrix_${agg.toLowerCase()}`,
            chartConfig: JSON.stringify({
                __matrix: true,
                rowHeaders: ['Department'],
                colParts: [['Q1']],
                rows: [
                    ['Engineering', 10],
                    ['Design', 20],
                ],
                aggregate: agg,
                rowTotalsEnabled: true,
            })
        };
        renderMatrix(container, visual);
        const rowTotals = container.querySelectorAll('.matrix-row-total');
        assert.ok(rowTotals.length > 0, `Row total should exist for ${agg}`);
    }
}

// ── Test renderMatrix with Data Bars and Formatting Rules ───────────────
{
    const container = new MockElement('div');
    const visual = {
        name: 'test_matrix_formatting',
        chartConfig: JSON.stringify({
            __matrix: true,
            rowHeaders: ['Product'],
            colParts: [['Jan']],
            rows: [
                ['Widget A', 50],
                ['Widget B', 90],
            ],
            dataBar: true,
            dataBarMin: 0,
            dataBarMax: 100,
            dataBarColor: '#00ff00',
            formattingRules: [
                { condition: '>= 90', color: '#ff0000', fontColor: '#ffffff' }
            ]
        })
    };

    renderMatrix(container, visual);
    const bars = container.querySelectorAll('.data-bar-fill');
    assert.ok(bars.length >= 2, 'Should render data bar fill divs');
    assert.equal(bars[0].style.width, '50.0%');
    assert.equal(bars[1].style.width, '90.0%');
    assert.equal(bars[0].style.backgroundColor, '#00ff00');

    const valueCells = container.querySelectorAll('.matrix-val');
    assert.equal(valueCells[1].style.backgroundColor, '#ff0000');
    assert.equal(valueCells[1].style.color, '#ffffff');
}

console.log('test-runtime-matrix: all assertions passed');
