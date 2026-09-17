import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

class Element {
    children = []; style = {}; attributes = {}; listeners = {}; value = '';
    classList = { add() {}, remove() {} };
    appendChild(child) { this.children.push(child); return child; }
    setAttribute(name, value) { this.attributes[name] = value; }
    addEventListener(name, callback) { (this.listeners[name] ||= []).push(callback); }
    dispatchEvent(event) { for (const callback of this.listeners[event.type] || []) callback(event); }
    focus() {}
}
const posts = [];
const dependencies = {
    ...util, document: { createElement: () => new Element() },
    actionsFor: (visual, trigger) => visual.actions.filter(action => action.trigger === trigger),
    evaluateExpressionAgainstParameters: value => value === 'ON',
    executeAction: () => {}, _uiStates: {}, isWebMode: true, parameters: {}, vscode: null,
    postParameters: async batch => { posts.push(batch); return null; }, renderManifest: () => {}, applyDesignTokens: () => {},
};
const source = readFileSync(new URL('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-controls-input.js', import.meta.url), 'utf8')
    .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');
const controls = new Function(...Object.keys(dependencies), `${source}\nreturn { applyControlState, setParameterAccessibleName, renderCheckbox, renderNumberbox, renderSlider, renderTextbox, renderSearch, renderSlicer };`)(...Object.values(dependencies));
const descendants = element => [element, ...element.children.flatMap(descendants)];
function fixture(renderer, options = {}, value = '') {
    posts.length = 0;
    const root = new Element();
    renderer(root, { name: 'Control', options, actions: [{ trigger: 'ON_CHANGE', type: 'SET_PARAMETER', parameterName: 'Value' }] }, { parameters: { Value: value } });
    return descendants(root);
}
function change(input, value) { input.value = value; input.dispatchEvent({ type: 'change' }); }
const input = new Element();
controls.applyControlState(input, { options: { DISABLED: 'ON', READ_ONLY: 'ON' } }, new Element());
assert.equal(input.disabled, true);
assert.equal(input.readOnly, true);
assert.equal(input.attributes['aria-disabled'], 'true');
controls.setParameterAccessibleName(input, {}, '@Amount', 'minimum');
assert.equal(input.attributes['aria-label'], 'Amount minimum');

let elements = fixture(controls.renderCheckbox, { TRUE_VALUE: 'yes', FALSE_VALUE: 'no' }, 'yes');
let control = elements.find(e => e.type === 'checkbox');
assert.equal(control.checked, true);
control.checked = false;
control.dispatchEvent({ type: 'change' });
assert.deepEqual(posts, [{ Value: 'no' }]);

elements = fixture(controls.renderNumberbox, { MIN: '2', MAX: '8' }, '5');
control = elements.find(e => e.type === 'number');
change(control, '20');
assert.deepEqual(posts.at(-1), { Value: '8' });
change(control, '-4');
assert.deepEqual(posts.at(-1), { Value: '2' });

elements = fixture(controls.renderSlider, { DATA_TICKS: '[0,10,30]', FIRE_ON: 'RELEASE' }, '8');
control = elements.find(e => e.type === 'range');
assert.equal(control.value, '10');
change(control, '24');
assert.deepEqual(posts, [{ Value: '30' }]);

elements = fixture(controls.renderTextbox, { MAX_LENGTH: '12', PATTERN: '^[A-Z]+$' });
control = elements.find(e => e.type === 'text');
assert.equal(control.maxLength, 12);
change(control, '123');
assert.equal(posts.length, 0);
change(control, 'READY');
assert.deepEqual(posts, [{ Value: 'READY' }]);

elements = fixture(controls.renderSearch, { SHOW_CLEAR: 'ON' }, 'Find me');
control = elements.find(e => e.type === 'search');
const clear = elements.find(e => e.className === 'search-clear-button');
assert.equal(control.value, 'Find me');
assert.equal(clear.hidden, false);
assert.equal(clear.attributes['aria-label'], 'Clear Control');
posts.length = 0;
const slicerRoot = new Element();
controls.renderSlicer(slicerRoot, { name: 'Region', visualType: 'SLICER', columns: ['Region'], rows: [['East'], ['West']], options: {}, actions: [{ type: 'SET_PARAMETER', trigger: 'ON_CHANGE', parameterName: 'Value' }] }, { parameters: { Value: 'West' } });
const select = descendants(slicerRoot).find(e => e.attributes['aria-label'] === 'Region');
assert.equal(select.value, 'West');
change(select, 'East');
assert.deepEqual(posts, [{ Value: 'East' }]);
console.log('input controls: accessible state, checkbox values, numeric bounds, slider snapping, textbox validation and slicer selection passed');
