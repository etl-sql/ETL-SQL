// A TABLE that spans physical pages is cut into one slice per page. Only `rows` used to be cut,
// so page 2 showed page 1's row colours, detail keys and sparklines against its own rows. The
// slicer is lifted out of the bundle and run directly, and its wiring into renderPhysicalPages is
// asserted separately.

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const runtimePath = 'src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.bundle.js';
const source = await readFile(runtimePath, 'utf8');

function lift(name) {
    const start = source.indexOf(`function ${name}(`);
    assert.notEqual(start, -1, `${runtimePath} no longer defines ${name}.`);
    let depth = 0;
    let seenBody = false;
    for (let i = start; i < source.length; i++) {
        const ch = source[i];
        if (ch === '{') { depth++; seenBody = true; }
        else if (ch === '}') {
            depth--;
            if (seenBody && depth === 0) return source.slice(start, i + 1);
        }
    }
    throw new Error(`Could not find the end of ${name}.`);
}

const { sliceTableVisualRows } = new Function(`${lift('sliceTableVisualRows')}\nreturn { sliceTableVisualRows };`)();

const visual = {
    name: 'orders',
    visualType: 'TABLE',
    columns: ['Id', 'Trend'],
    rows: [[1, 'a'], [2, 'b'], [3, 'c'], [4, 'd']],
    rowStyles: ['red', 'green', 'blue', 'gold'],
    rowFontStyles: ['#100', '#200', '#300', '#400'],
    rowDetailKeys: [{ Id: 1 }, { Id: 2 }, { Id: 3 }, { Id: 4 }],
    microCharts: [
        { role: 'table.cell', rowIndex: 0, columnIndex: 1, svg: 'r0' },
        { role: 'table.cell', rowIndex: 2, columnIndex: 1, svg: 'r2' },
        { role: 'table.cell', rowIndex: 3, columnIndex: 1, svg: 'r3' },
        { role: 'table.cell', columnIndex: 1, sourceValue: 'x', svg: 'by-value' },
    ],
};

const page2 = sliceTableVisualRows(visual, 2, 3);
assert.deepEqual(page2.rows, [[3, 'c'], [4, 'd']]);
assert.deepEqual(page2.rowStyles, ['blue', 'gold'], 'row colours follow their rows');
assert.deepEqual(page2.rowFontStyles, ['#300', '#400']);
assert.deepEqual(page2.rowDetailKeys, [{ Id: 3 }, { Id: 4 }], 'detail keys follow their rows');
assert.deepEqual(page2.microCharts.map(m => [m.svg, m.rowIndex]), [['r2', 0], ['r3', 1], ['by-value', undefined]],
    'micro charts are rebased onto the slice; value-matched ones are kept');
assert.equal(visual.rowStyles.length, 4, 'the source visual is not changed');

const page1 = sliceTableVisualRows(visual, 0, 1);
assert.deepEqual(page1.rowStyles, ['red', 'green']);
assert.deepEqual(page1.microCharts.map(m => m.svg), ['r0', 'by-value']);

assert.match(lift('renderPhysicalPages'), /sliceTableVisualRows\(pv\.visual, pv\.startRowIndex, pv\.endRowIndex\)/,
    'renderPhysicalPages cuts tables with sliceTableVisualRows');

console.log('runtime layout page split: row-indexed table arrays follow their rows across physical pages');
