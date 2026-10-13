import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

class Element {
    constructor(dataset = {}, parent = null) { this.dataset = dataset; this.parent = parent; }
    closest() { return this.dataset.index === undefined ? this.parent?.closest() : this; }
}
const window = {};
vm.runInNewContext(fs.readFileSync('src/ETL-SQL.Portal/wwwroot/js/native-charts.js', 'utf8'), { window, Element });
const listeners = new Map();
const host = { clientWidth: 600, clientHeight: 240, dataset: {}, innerHTML: '',
    addEventListener: (type, callback) => listeners.set(type, callback),
    removeEventListener: (type, callback) => { if (listeners.get(type) === callback) listeners.delete(type); },
    replaceChildren() { this.innerHTML = ''; },
};
const chart = window.nativeCharts.init(host);
assert.equal(window.nativeCharts.getInstanceByDom(host), chart);
chart.setOption({ title: { text: 'Run <script>alert(1)</script>' }, series: [{ data: [1, 2, [3, 4], { value: 5 }] }] });
assert.match(host.innerHTML, /role="img"/);
assert.match(host.innerHTML, /aria-label="Run &lt;script&gt;alert\(1\)&lt;\/script&gt;"/);
assert.equal((host.innerHTML.match(/<rect /g) || []).length, 4);
assert.doesNotMatch(host.innerHTML, /<script>|NaN/);
chart.setOption({ series: [{ type: 'graph', data: [{ name: 'one' }, { name: '<two>' }], links: [{ source: 'one', target: '<two>' }] }] });
assert.equal((host.innerHTML.match(/<circle /g) || []).length, 2);
assert.match(host.innerHTML, /<line /);
assert.match(host.innerHTML, /data-name="&lt;two&gt;"/);
let click;
chart.on('click', event => { click = event; });
const mark = new Element({ index: '1', name: '<two>' });
const event = { target: new Element({}, mark) };
listeners.get('click')(event);
assert.equal(click.name, '<two>', 'Clicks on a node child identify its enclosing graph mark.');
assert.equal(click.dataIndex, 1);
assert.equal(click.event.event, event);
chart.resize();
chart.dispatchAction({ type: 'highlight' });
chart.setOption(null);
assert.match(host.innerHTML, /Operational chart/);
chart.dispose();
assert.equal(host.innerHTML, '');
assert.equal(window.nativeCharts.getInstanceByDom(host), undefined);
assert.equal(listeners.size, 0, 'Disposed adapters must release their host event handlers.');
console.log('Portal operational charts: accessible labels, escaping, graph/bar rendering, nested clicks, and disposal passed.');
