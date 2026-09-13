import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as util from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';

const posts = [], internalPosts = [], refreshes = [], runs = [], notifications = [];
const pendingParameters = {};
const dependencies = {
    ...util, _crossFilterStates: {}, _drillHistory: [], _uiStates: {}, apiBase: '/api',
    feedback: { notify: message => notifications.push(message) },
    getBaselineManifest: () => ({ parameters: { '@Region': 'North', '@Year': '2026' } }),
    getLastManifest: () => null, isInteractive: true, parameters: {}, pendingParameters,
    setLastActivePage: () => {}, vscode: null,
    postParameters: async (...args) => { posts.push(args); return null; },
    _postParametersInternal: async (...args) => { internalPosts.push(args); return null; },
    postRefreshVisuals: async targets => { refreshes.push(targets); return null; },
    postRunScript: async (...args) => { runs.push(args); return { message: 'Done', refresh: true }; },
    postDrillIn: async () => null, renderManifest: () => {}, resizeChartsIn: () => {},
    hideModalDialog: () => {}, showModalDialog: () => {}, updateStagedUI: () => {}, applyBookmark: () => {},
    document: { querySelectorAll: () => [], getElementById: () => null, addEventListener: () => {} },
};
const source = readFileSync(new URL('../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-actions.js', import.meta.url), 'utf8')
    .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');
const actions = new Function(...Object.keys(dependencies), `${source}\nreturn { actionsFor, evaluateExpressionAgainstParameters, executeAction, matchesCondition };`)(...Object.values(dependencies));
const evaluate = actions.evaluateExpressionAgainstParameters;
assert.equal(evaluate('@amount >= 10', { '@Amount': '12' }), true);
assert.equal(evaluate("@region = 'NORTH'", { '@Region': 'North' }), true);
assert.equal(evaluate('@missing', {}), false);
assert.equal(evaluate('@enabled', { '@enabled': 'OFF' }), false);
assert.equal(evaluate('@amount != 12', { '@amount': '12' }), false);
for (const [operator, equal, less, greater] of [
    ['<>', false, true, true], ['!=', false, true, true],
    ['=', true, false, false], ['==', true, false, false],
    ['<', false, true, false], ['<=', true, true, false],
    ['>', false, false, true], ['>=', true, false, true],
]) {
    for (const [value, expected] of [[12, equal], [11, less], [13, greater]]) {
        assert.equal(evaluate(`@amount ${operator} 12`, { '@amount': String(value) }), expected, `parameter ${value} ${operator} 12`);
        assert.equal(actions.matchesCondition(`Amount ${operator} 12`, value, 'Amount'), expected, `column ${value} ${operator} 12`);
        assert.equal(actions.matchesCondition(`${operator} 12`, value, 'Amount'), expected, `bare ${value} ${operator} 12`);
    }
}
assert.equal(evaluate("@region <> 'NORTH'", { '@region': 'North' }), false);
assert.equal(evaluate("@region <> 'SOUTH'", { '@region': 'North' }), true);
assert.equal(evaluate('not an expression', {}), false);
const click = { trigger: 'ON_CLICK', type: 'SET_PARAMETER' };
assert.deepEqual(actions.actionsFor({ actions: [click, { trigger: 'ON_CHANGE' }] }, 'ON_CLICK'), [click]);
assert.deepEqual(actions.actionsFor({}, 'ON_CLICK'), []);

function execute(action, row = [], columns = []) { actions.executeAction(action, row, columns, 'Test', {}); }
execute({ type: 'SET_PARAMETER', parameterName: '@Region', valueSource: 'COLUMN', valueColumn: 'region' }, ['West'], ['Region']);
assert.deepEqual(posts.at(-1)[0], { '@Region': 'West' });
execute({ type: 'SET_PARAMETER', parameterName: '@Count', valueSource: 'LITERAL', literalValue: 0 });
assert.deepEqual(posts.at(-1)[0], { '@Count': '0' });
pendingParameters['@Region'] = 'South';
execute({ type: 'APPLY_PARAMETERS' });
assert.deepEqual(internalPosts.at(-1)[0], { '@Region': 'South' });
assert.deepEqual(pendingParameters, {});
execute({ type: 'RESET_PARAMETERS', resetParameters: ['region'] });
assert.deepEqual(internalPosts.at(-1)[0], { '@region': 'North' });
execute({ type: 'REFRESH_VISUALS', targets: ['Chart', '', 'Table'] });
assert.deepEqual(refreshes, [['Chart', 'Table']]);
execute({ type: 'RUN_SCRIPT', scriptPath: 'example.etlsql', parameterColumns: { Region: 'region' }, literalParameters: { Limit: 5 } }, ['East'], ['Region']);
await Promise.resolve();
assert.deepEqual(runs, [['example.etlsql', { Region: 'East', Limit: '5' }]]);
assert.deepEqual(notifications, ['Done']);
assert.deepEqual(posts.at(-1), [{}, true]);
console.log('runtime actions: expression evaluation, triggers, parameter resolution, staging/reset, refresh and script continuation passed');
