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
    this.dataset = {};
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
  querySelectorAll() { return []; }
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

globalThis.document = {
  createElement(tag) { return new Element(tag); },
  createElementNS(_ns, tag) { return new Element(tag); },
  createTextNode(text) { return new TextNode(String(text ?? '')); },
  getElementById() { return null; },
  addEventListener() {},
  removeEventListener() {},
};

globalThis.window = {};

globalThis.clearTimeout = clearTimeout;
globalThis.setTimeout = setTimeout;
// Connection drawing is scheduled via rAF; the assertions check card/detail text, not drawn
// SVG edges, so a no-op keeps the render synchronous and avoids post-assertion async throws.
globalThis.requestAnimationFrame = () => 0;
globalThis.cancelAnimationFrame = () => {};

const sourcePath = path.resolve('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js');
const tempModule = path.join(os.tmpdir(), `etl-sql-designer-${Date.now()}.mjs`);
await fs.writeFile(tempModule, await fs.readFile(sourcePath, 'utf8'), 'utf8');

async function importTempModule(sourceFile, prefix) {
  const temp = path.join(os.tmpdir(), `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2)}.mjs`);
  await fs.writeFile(temp, await fs.readFile(sourceFile, 'utf8'), 'utf8');
  return {
    href: pathToFileURL(temp).href,
    cleanup: () => fs.rm(temp, { force: true }),
  };
}

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

  // The Card-based lineage designer renders the node detail panel as Title / Type / Metadata
  // (scalar meta) / Columns / Mappings. Visual node: BAR type, YAXIS->total mapping.
  const text = root.textContent;
  const expected = ['BAR · salesBar', 'Type: visual', 'Metadata', 'visualType', 'BAR', 'Mappings', 'YAXIS', 'total'];
  const missing = expected.filter(x => !text.includes(x));
  if (missing.length) {
    throw new Error(`Lineage detail panel missing: ${missing.join(', ')}\nRendered text:\n${text}`);
  }

  // Table node: Type: table, its column list, and no visual mappings.
  dag.showDetail('table:edw.Sales');
  const tableText = root.textContent;
  for (const expectedText of ['edw.Sales', 'Type: table', 'Columns', 'Amount', 'No visual mappings captured.']) {
    if (!tableText.includes(expectedText)) {
      throw new Error(`Table detail panel missing: ${expectedText}\nRendered text:\n${tableText}`);
    }
  }

  dag.dispose();

  const lineageUiTemp = await importTempModule(path.resolve('src/ETL-SQL.Portal/wwwroot/js/lineage-ui.js'), 'etl-sql-lineage-ui');
  try {
    const { lineageRowsToCsv, renderDependencies, renderLineageRow } = await import(lineageUiTemp.href);
    const row = {
      runAt: '2026-05-30T14:15:00Z',
      jobName: 'nightly_sales_refresh',
      reportId: 42,
      reportName: 'Executive Sales',
      folderPath: '/Finance',
      targetTable: 'mart.SalesSummary',
      targetColumn: 'total_revenue',
      operation: 'SELECT',
      transformationKind: 'Aggregation',
      transformationExpression: 'SUM(Amount)',
      functionsApplied: ['SUM'],
      sourceTables: ['edw.Sales'],
      sourceColumns: ['Amount'],
      derivedFromDescriptions: 'Sales amount from catalog',
      tags: { pii: 'true', classification: 'confidential', owner: 'finance' },
      sourceFile: 'samples/integration/sales.rptsql',
      line: 18,
    };
    const rowHtml = renderLineageRow(row, { timeAgo: () => 'just now', formatBuiltAt: () => 'May 30, 2026' });
    for (const expectedText of ['mart.SalesSummary.total_revenue', 'SUM', 'edw.Sales.Amount', 'Sales amount from catalog', 'pii: true', 'classification: confidential', 'Executive Sales']) {
      if (!rowHtml.includes(expectedText)) {
        throw new Error(`Lineage row missing: ${expectedText}\nRendered HTML:\n${rowHtml}`);
      }
    }

    const dependenciesHtml = renderDependencies({
      report: { name: 'Executive Sales', folderPath: '/Finance' },
      snapshot: { builtAt: '2026-05-30T14:15:00Z' },
      manifestDatasets: [{ tempTableName: '#sales', rowCount: 1280, refreshInterval: 'Manual', ttl: 'None' }],
      registeredDatasets: [{ name: '&sales_snap', folderPath: '/Shared', accessLevel: 'Read', rowCount: 1280, sources: [{ name: 'edw.Sales' }] }],
      refreshJobs: [{ orchestratorJobName: 'nightly_sales_refresh', refreshInterval: 'Daily', lastRefreshedAt: '2026-05-30T14:15:00Z' }],
      sources: [{ connection: 'warehouse', objectName: 'edw.Sales', kind: 'TABLE' }],
      lineageEntries: [{
        target: 'mart.SalesSummary',
        targetColumn: 'YAXIS',
        operation: 'SELECT',
        transformationKind: 'Aggregation',
        transformationExpression: 'SUM(Amount)',
        functionsApplied: ['SUM'],
        sources: ['edw.Sales'],
        sourceColumns: ['Amount'],
        derivedFromDescriptions: 'Sales amount from catalog',
        tags: { pii: 'true', classification: 'confidential', owner: 'finance' },
      }],
    }, [{ reportId: 73, reportName: 'Revenue QA', folderPath: '/Audit', runCount: 3, lastSeen: '2026-05-31T09:00:00Z' }], { formatBuiltAt: () => 'May 30, 2026' });
    for (const expectedText of ['Lineage and Tags', 'YAXIS', 'edw.Sales.Amount', 'Sales amount from catalog', 'PII', 'confidential', 'Revenue QA']) {
      if (!dependenciesHtml.includes(expectedText)) {
        throw new Error(`Dependencies view missing: ${expectedText}\nRendered HTML:\n${dependenciesHtml}`);
      }
    }

    const csv = lineageRowsToCsv([row]);
    for (const expectedText of ['RunAt,JobName,Report', 'mart.SalesSummary.total_revenue', 'SUM(Amount)', 'edw.Sales.Amount', 'Sales amount from catalog', 'pii: true; classification: confidential; owner: finance']) {
      if (!csv.includes(expectedText)) {
        throw new Error(`Lineage CSV missing: ${expectedText}\nCSV:\n${csv}`);
      }
    }
  } finally {
    await lineageUiTemp.cleanup();
  }

  const vscodeStoryTemp = await importTempModule(path.resolve('tools/ui-sandbox/stories/vscode-webviews.story.js'), 'etl-sql-vscode-webviews-story');
  try {
    const { default: story } = await import(vscodeStoryTemp.href);
    const fixtureIds = (story.fixtures || []).map(f => f.id).join(',');
    for (const expectedText of ['results', 'preview', 'designer']) {
      if (!fixtureIds.includes(expectedText)) {
        throw new Error(`VS Code webview story missing fixture: ${expectedText}`);
      }
    }
  } finally {
    await vscodeStoryTemp.cleanup();
  }

  const lineageCatalogStory = await fs.readFile(path.resolve('tools/ui-sandbox/stories/lineage-catalog.story.js'), 'utf8');
  for (const expectedText of ["{ id: 'impact'", "async function impact", "catalogApi: { lineage, impact"]) {
    if (!lineageCatalogStory.includes(expectedText)) {
      throw new Error(`Lineage catalog story missing impact coverage marker: ${expectedText}`);
    }
  }

  const portalIndex = await fs.readFile(path.resolve('src/ETL-SQL.Portal/wwwroot/index.html'), 'utf8');
  for (const expectedText of ['#governance/overview', '#governance/lineage', 'createGovernancePortal', 'showGovernanceCatalog']) {
    if (!portalIndex.includes(expectedText)) {
      throw new Error(`Portal governance routing missing: ${expectedText}`);
    }
  }
  const lineageCatalog = await fs.readFile(path.resolve('src/ETL-SQL.Portal/wwwroot/js/lineage-catalog.js'), 'utf8');
  for (const expectedText of ['allowAudit = true', 'onModeChange(state.mode)', 'allowAudit ? `<button']) {
    if (!lineageCatalog.includes(expectedText)) {
      throw new Error(`Lineage catalog route/role contract missing: ${expectedText}`);
    }
  }

  console.log('lineage-ui smoke passed');
} finally {
  await fs.rm(tempModule, { force: true });
}
