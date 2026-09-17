import assert from 'node:assert/strict';
import { escapeHtml, inlineMarkup, noteMarkup, sqlPreviewMarkup, mutationExplanationMarkup,
    sampleGridMarkup } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-ui.js';

assert.equal(escapeHtml('<tag title="x">&'), '&lt;tag title=&quot;x&quot;&gt;&amp;');
assert.equal(escapeHtml(null), '');
assert.equal(inlineMarkup(['<text>', [{ strong: '&' }, { br: true }], { em: 'em' },
    { code: '<code>' }, { text: 'plain' }, 0, null]),
    '&lt;text&gt;<strong>&amp;</strong><br><em>em</em><code>&lt;code&gt;</code>plain0');
assert.throws(() => inlineMarkup({ html: '<b>raw</b>' }), /unsupported segment/);
assert.equal(noteMarkup('<error>', '"bad'),
    '<div class="etlsql-studio-guided-note is-&quot;bad">&lt;error&gt;</div>');
assert.equal(sqlPreviewMarkup('<sql>', 'Adds <data>', '<heading>'),
    '<div class="etlsql-studio-sql-preview"><span>&lt;heading&gt;</span>'
    + '<p class="etlsql-studio-sql-explains">Adds &lt;data&gt;</p><pre>&lt;sql&gt;</pre></div>');
assert.match(mutationExplanationMarkup('Changes <data>'), /Changes &lt;data&gt;/);
for (const explanation of [undefined, null, '', ' \t ', 7]) {
    assert.throws(() => sqlPreviewMarkup('fixture', explanation), TypeError);
    assert.throws(() => mutationExplanationMarkup(explanation), TypeError);
}

const objectGrid = sampleGridMarkup({ columns: [{ name: '<field>' }, 'second'],
    rows: [{ '<field>': '<value>', second: 0 }] });
const arrayGrid = sampleGridMarkup({ columns: ['<field>', 'second'], rows: [['<value>', 0]] });
assert.equal(objectGrid, arrayGrid);
assert.match(objectGrid, /<th>&lt;field&gt;<\/th>/);
assert.match(objectGrid, /<td>&lt;value&gt;<\/td><td>0<\/td>/);
assert.match(objectGrid, /1 row sampled · 2 fields/);
assert.match(sampleGridMarkup({ rows: [{ inferred: null }] }), /<th>inferred<\/th>/);
assert.match(sampleGridMarkup({ columns: ['empty'], rows: [[null]] }), /<td><\/td>/);
assert.match(sampleGridMarkup(null), /no columns/);
assert.match(sampleGridMarkup({ rows: [[1]] }), /no columns/);
assert.match(sampleGridMarkup({ rows: [] }), /returned 0 rows/);
const limited = sampleGridMarkup({ columns: ['value'], rows: [[1], [2]], rowCount: 100 }, 1);
assert.match(limited, /<td>1<\/td>/);
assert.doesNotMatch(limited, /<td>2<\/td>/);
assert.match(limited, /100 rows sampled · 1 field/);
assert.match(sampleGridMarkup({ columns: ['value'], rows: [[1]], rowCount: 0 }), /0 rows sampled/);
const defaultGrid = sampleGridMarkup({ columns: ['value'], rows: Array.from({ length: 51 }, (_, i) => [i]) });
assert.equal((defaultGrid.match(/<td>/g) || []).length, 50);
assert.match(defaultGrid, /51 rows sampled/);
console.log('Studio authoring UI: escaping, structured notes, explanations, and sample grids passed.');
