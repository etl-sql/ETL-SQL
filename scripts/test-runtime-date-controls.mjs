import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { getOption, getParam, parseMultiParameter } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

// Execute generated control code with a small DOM and transport fixture; no runtime boot or network.
class Element {
    children = []; style = {}; listeners = {}; attributes = {}; value = '';
    classList = { add() {}, remove() {} };
    appendChild(child) { this.children.push(child); return child; }
    setAttribute(name, value) { this.attributes[name] = value; }
    addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
    dispatchEvent(event) { for (const callback of this.listeners[event.type] || []) callback(event); }
}
const posts = [];
const source = readFileSync(new URL('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-controls-date.js', import.meta.url), 'utf8')
    .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');
const controls = new Function('document', 'actionsFor', 'getOption', 'getParam', 'parseMultiParameter',
    'applyControlState', 'setParameterAccessibleName', 'isWebMode', 'postParameters', 'renderManifest',
    `${source}\nreturn { renderDatePicker, renderRelDatePicker };`)(
    { createElement: () => new Element() }, visual => visual.actions, getOption, getParam, parseMultiParameter,
    () => {}, () => {}, true, async batch => { posts.push(batch); return null; }, () => {});
const descendants = element => [element, ...element.children.flatMap(descendants)];
function fixture(renderer, options = {}, parameters = {}) {
    posts.length = 0;
    const root = new Element();
    renderer(root, { options, actions: [{ type: 'SET_PARAMETER', parameterName: 'Start', secondaryParameterName: 'End' }] }, { parameters });
    const elements = descendants(root);
    return { inputs: elements.filter(e => e.type === 'text'), error: elements.find(e => e.className === 'filter-error'), elements };
}
function change(input, value) { input.value = value; input.dispatchEvent({ type: 'change' }); }

let view = fixture(controls.renderDatePicker, { DISABLED_DATES: '["2026-09-14"]', DISABLED_DAYS: 'SUN' });
change(view.inputs[0], '2026-09-14');
assert.equal(posts.length, 0);
assert.equal(view.error.textContent, 'Selected date is disabled');
change(view.inputs[0], '2026-09-13');
assert.equal(posts.length, 0);
change(view.inputs[0], '2026-09-15');
assert.deepEqual(posts, [{ Start: '2026-09-15' }]);
assert.equal(view.error.style.display, 'none');

view = fixture(controls.renderDatePicker, { MODE: 'RANGE' }, { Start: '2026-09-10', End: '2026-09-12' });
assert.deepEqual(view.inputs.map(e => e.value), ['2026-09-10', '2026-09-12']);
change(view.inputs[0], '2026-09-20');
assert.equal(posts.length, 0);
assert.equal(view.error.textContent, 'Start date cannot be after end date');
change(view.inputs[1], '2026-09-21');
assert.deepEqual(posts, [{ Start: '2026-09-20', End: '2026-09-21' }]);

view = fixture(controls.renderRelDatePicker);
change(view.inputs[0], 'invalid');
assert.equal(posts.length, 0);
assert.equal(view.error.style.display, 'block');
change(view.inputs[0], 'FQS-1');
assert.deepEqual(posts, [{ Start: 'FQS-1' }]);
const quickPick = view.elements.find(e => e.type === 'button' && e.textContent === 'D-1');
assert.ok(quickPick);
quickPick.dispatchEvent({ type: 'click' });
assert.deepEqual(posts.at(-1), { Start: 'D-1' });

view = fixture(controls.renderRelDatePicker, { MODE: 'RANGE' }, { Start: 'D-7', End: 'D-0' });
change(view.inputs[1], 'bad');
assert.equal(posts.length, 0);
change(view.inputs[1], '2026-09-13');
assert.deepEqual(posts, [{ Start: 'D-7', End: '2026-09-13' }]);
console.log('date controls: disabled dates/days, range ordering, relative expressions, quick picks and parameter batches passed');
