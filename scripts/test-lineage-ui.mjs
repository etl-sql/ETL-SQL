import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

class ClassList {
  constructor(el) { this.el = el; }
  _set() { return new Set((this.el.className || '').split(/\s+/).filter(Boolean)); }
  _write(set) { this.el.className = [...set].join(' '); }
  add(...names) { const s = this._set(); for (const n of names) s.add(n); this._write(s); }
  remove(...names) { const s = this._set(); for (const n of names) s.delete(n); this._write(s); }
  contains(name) { return this._set().has(name); }
  toggle(name, force) {
    const s = this._set();
    const on = force === undefined ? !s.has(name) : !!force;
    if (on) s.add(name); else s.delete(name);
    this._write(s);
    return on;
  }
}

class Element {
  constructor(tagName) {
    this.tagName = tagName.toUpperCase();
    this.children = [];
    this.parentNode = null;
    this.style = {};
    this.attributes = {};
    this.eventListeners = {};
    this.className = '';
    this.classList = new ClassList(this);
    this.clientWidth = tagName === 'div' ? 900 : 190;
    this.clientHeight = tagName === 'div' ? 600 : 130;
    this._text = '';
    this.value = '';
  }

  appendChild(child) {
    if (child == null) return child;
    if (typeof child === 'string') child = new TextNode(child);
    child.parentNode = this;
    this.children.push(child);
    return child;
  }

  append(...nodes) { for (const n of nodes) this.appendChild(n); }
  replaceChildren(...nodes) { this.children = []; this._text = ''; this.append(...nodes); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  addEventListener(name, fn) { (this.eventListeners[name] ??= []).push(fn); }
  removeEventListener(name, fn) {
    this.eventListeners[name] = (this.eventListeners[name] ?? []).filter(x => x !== fn);
  }
  getBoundingClientRect() { return { left: 0, top: 0, width: this.clientWidth, height: this.clientHeight }; }
  getContext() { return fakeCanvasContext(); }
  querySelector() { return null; }
  closest() { return null; }

  set innerHTML(_) { this.children = []; this._text = ''; }
  get innerHTML() { return this.textContent; }
  set textContent(value) { this._text = String(value ?? ''); this.children = []; }
  get textContent() { return this._text + this.children.map(c => c.textContent ?? '').join(''); }
}

class TextNode {
  constructor(text) { this._text = text; this.parentNode = null; }
  get textContent() { return this._text; }
}

function fakeCanvasContext() {
  return {
    clearRect() {}, fillRect() {}, strokeRect() {}, beginPath() {}, arc() {}, fill() {}, stroke() {},
    set fillStyle(_) {}, set strokeStyle(_) {}, set lineWidth(_) {},
  };
}

function makeChart() {
  return {
    option: { series: [{ zoom: 1, center: [0, 0] }] },
    handlers: {},
    setOption(option) { this.option = option; },
    getOption() { return this.option; },
    getWidth() { return 900; },
    getHeight() { return 600; },
    resize() {},
    dispose() {},
    on(name, fn) { this.handlers[name] = fn; },
    getZr() { return { on() {} }; },
    dispatchAction() {},
    convertFromPixel(_, point) { return point; },
  };
}

globalThis.document = {
  createElement(tag) { return new Element(tag); },
};

globalThis.window = {
  echarts: { init: () => makeChart() },
};

globalThis.clearTimeout = clearTimeout;
globalThis.setTimeout = setTimeout;

const sourcePath = path.resolve('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js');
const tempModule = path.join(os.tmpdir(), `etl-sql-designer-${Date.now()}.mjs`);
await fs.writeFile(tempModule, await fs.readFile(sourcePath, 'utf8'), 'utf8');

try {
  const { renderDag } = await import(pathToFileURL(tempModule).href);
  const root = new Element('div');
  const graph = {
    nodes: [
      {
        id: 'table:edw.Sales',
        label: 'edw.Sales',
        type: 'table',
        meta: {
          columns: ['Amount'],
          columnLineage: {
            Amount: {
              description: 'Sales amount from catalog',
              tags: { db_type: 'decimal', pii: 'true' },
              sources: [],
            },
          },
        },
      },
      {
        id: 'ds:&sales_snap',
        label: '&sales_snap',
        type: 'dataset',
        meta: {
          columns: ['total'],
          columnLineage: {
            total: {
              transform: 'SUM(Amount)',
              description: 'Amount: Sales amount from catalog',
              tags: { pii: 'true' },
              sources: [{ table: 'edw.Sales', column: 'Amount' }],
            },
          },
        },
      },
      {
        id: 'table:#sales',
        label: '#sales',
        type: 'table',
        meta: {
          columns: ['total'],
          columnLineage: {
            total: {
              kind: 'PassThrough',
              sources: [{ table: '&sales_snap', column: 'total' }],
            },
          },
        },
      },
      {
        id: 'vis:salesBar',
        label: 'BAR · salesBar',
        type: 'visual',
        meta: {
          visualType: 'BAR',
          mappings: [{ role: 'YAXIS', column: 'total' }],
        },
      },
    ],
    edges: [
      { source: 'table:edw.Sales', target: 'ds:&sales_snap', label: 'SELECT' },
      { source: 'ds:&sales_snap', target: 'table:#sales', label: 'SELECT' },
      { source: 'table:#sales', target: 'vis:salesBar', label: 'Y: total' },
    ],
  };

  const dag = renderDag(root, graph, { theme: 'portal' });
  dag.showDetail('vis:salesBar');

  const text = root.textContent;
  const expected = ['Fields', 'total', '#sales', '&sales_snap', 'SUM(Amount)', 'edw.Sales', 'Sales amount from catalog', 'pii'];
  const missing = expected.filter(x => !text.includes(x));
  if (missing.length) {
    throw new Error(`Lineage detail panel missing: ${missing.join(', ')}\nRendered text:\n${text}`);
  }

  dag.showDetail('table:edw.Sales');
  const tableText = root.textContent;
  for (const expectedText of ['Columns (1)', 'Amount', 'db_type', 'Sales amount from catalog']) {
    if (!tableText.includes(expectedText)) {
      throw new Error(`Table detail panel missing: ${expectedText}\nRendered text:\n${tableText}`);
    }
  }

  dag.dispose();
  console.log('lineage-ui smoke passed');
} finally {
  await fs.rm(tempModule, { force: true });
}
