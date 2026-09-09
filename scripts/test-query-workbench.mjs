import assert from 'node:assert/strict';
import { connectionPreamble, firstResultSet } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js';

assert.equal(await connectionPreamble(null, 'fixture'), '');
await assert.rejects(connectionPreamble('alias', 'fixture'), /without a parse route/);
const calls = [];
const options = {
    routes: { parse: '/fixture/parse' },
    request: async (route, options) => {
        calls.push({ route, ...options });
        return { designState: { connections: [
            { name: 'unrelated', text: 'unrelated;' },
            { name: '[Sales.+]', text: " declaration 'contains;semicolon';;;  " },
        ] } };
    },
};
assert.equal(await connectionPreamble('sales.+', 'original buffer', options), "declaration 'contains;semicolon';\n");
assert.equal(calls[0].route, '/fixture/parse');
assert.deepEqual(calls[0].body, { script: 'original buffer' });
assert.equal(await connectionPreamble('missing', 'original buffer', options), '');
const before = calls.length;
assert.equal(await connectionPreamble('alias', ' \n ', options), '');
assert.equal(calls.length, before);
await assert.rejects(connectionPreamble('alias', 'fixture', {
    ...options, request: async () => ({ error: 'Fixture parse error' }),
}), /Fixture parse error/);
const failure = new Error('Fixture request error');
await assert.rejects(connectionPreamble('alias', 'fixture', {
    ...options, request: async () => { throw failure; },
}), error => error.cause === failure && /Fixture request error/.test(error.message));

assert.equal(firstResultSet(null), null);
assert.equal(firstResultSet({ rows: [] }), null);
assert.deepEqual(firstResultSet({ columns: ['a'], rows: [{ a: 1 }], rowCount: 0 }),
    { columns: ['a'], rows: [{ a: 1 }], rowCount: 0 });
assert.deepEqual(firstResultSet({ rows: [{ a: 1 }], trace: [{ type: 'resultset', data: { rows: [[2]] } }] }),
    { columns: [], rows: [{ a: 1 }], rowCount: 1 });
assert.deepEqual(firstResultSet({ trace: [
    { type: 'message', data: { rows: [[9]] } }, { type: 'resultset', data: null },
    { type: 'resultset', data: { columns: ['a'], rows: [[2]] } },
    { type: 'resultset', data: { rows: [[3]] } },
] }), { columns: ['a'], rows: [[2]], rowCount: 1 });
assert.deepEqual(firstResultSet({ trace: [{ type: 'resultset', data: {} }] }),
    { columns: [], rows: [], rowCount: 0 });
console.log('Query workbench: parsed connection selection, error propagation, and result-set selection passed.');
