import assert from 'node:assert/strict';
import { buildSideBySideDiff } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-git-diff.js';

assert.deepEqual(buildSideBySideDiff('a\r\nb\r', 'a\nb\n'), [
    { kind: 'equal', leftNumber: 1, rightNumber: 1, leftText: 'a', rightText: 'a' },
    { kind: 'equal', leftNumber: 2, rightNumber: 2, leftText: 'b', rightText: 'b' },
    { kind: 'equal', leftNumber: 3, rightNumber: 3, leftText: '', rightText: '' },
]);
assert.deepEqual(buildSideBySideDiff('a\nold\nz', 'a\nnew\nextra\nz'), [
    { kind: 'equal', leftNumber: 1, rightNumber: 1, leftText: 'a', rightText: 'a' },
    { kind: 'change', leftNumber: 2, rightNumber: 2, leftText: 'old', rightText: 'new' },
    { kind: 'add', leftNumber: null, rightNumber: 3, leftText: '', rightText: 'extra' },
    { kind: 'equal', leftNumber: 3, rightNumber: 4, leftText: 'z', rightText: 'z' },
]);
assert.equal(buildSideBySideDiff('a\nx\nb', 'a\nb')[1].kind, 'delete');
assert.deepEqual(buildSideBySideDiff(null, undefined), [
    { kind: 'equal', leftNumber: 1, rightNumber: 1, leftText: '', rightText: '' },
]);

// Both the LCS path and large-file fallback must preserve all text and line numbers.
for (const count of [12, 634]) {
    const left = Array.from({ length: count }, (_, i) => `line ${i}`);
    const right = [...left.slice(0, 3), 'inserted', ...left.slice(3, -2), 'replacement'];
    const rows = buildSideBySideDiff(left.join('\n'), right.join('\n'));
    assert.deepEqual(rows.filter(r => r.leftNumber !== null).map(r => r.leftText), left);
    assert.deepEqual(rows.filter(r => r.rightNumber !== null).map(r => r.rightText), right);
    assert.deepEqual(rows.filter(r => r.leftNumber !== null).map(r => r.leftNumber), left.map((_, i) => i + 1));
    assert.deepEqual(rows.filter(r => r.rightNumber !== null).map(r => r.rightNumber), right.map((_, i) => i + 1));
}
console.log('Studio Git diff: alignment, normalization, numbering, and large-file fallback passed');
