// Unit tests for the script workbench result grid's behaviour: what the filter box matches, how a
// value becomes display text, and what CSV export writes.
//
// These are the parts of "result-grid interaction" that carry logic. They are pure functions in the
// canonical designer.js, so they run with no DOM and no npm dependency — the scripts/*.mjs tests are
// invoked as bare `node scripts/<file>` with no package.json, which is why the sibling tests
// hand-roll a DOM rather than reaching for jsdom.

import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const sourcePath = path.resolve('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js');
const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'etlsql-result-grid-'));
const tempModule = path.join(tempDir, 'designer.mjs');
await fs.writeFile(tempModule, await fs.readFile(sourcePath, 'utf8'), 'utf8');

const { filterRows, toCsv, formatResultCell, resultRenderWindow, MAX_RENDERED_ROWS } =
    await import(pathToFileURL(tempModule).href);

const columns = ['Id', 'Name', 'Notes'];
const rows = [
    { Id: 1, Name: 'Alice', Notes: null },
    { Id: 2, Name: 'Bob', Notes: 'follow up' },
    { Id: 30, Name: 'Carol', Notes: 'FOLLOW UP twice' },
];

// ── formatResultCell ────────────────────────────────────────────────────────

// A null cell renders as empty rather than the string "null", which would be indistinguishable
// from a literal value.
assert.equal(formatResultCell(null), '');
assert.equal(formatResultCell(undefined), '');

// Zero and false are values, not absence — they must survive.
assert.equal(formatResultCell(0), '0');
assert.equal(formatResultCell(false), 'false');
assert.equal(formatResultCell(''), '');

assert.equal(formatResultCell(42), '42');
assert.equal(formatResultCell('text'), 'text');
assert.equal(formatResultCell({ a: 1 }), '{"a":1}');
assert.equal(formatResultCell([1, 2]), '[1,2]');

// ── filterRows ──────────────────────────────────────────────────────────────

// No filter returns the original array, not a copy — the grid renders every row.
assert.equal(filterRows(rows, columns, ''), rows);
assert.equal(filterRows(rows, columns, '   '), rows);
assert.equal(filterRows(rows, columns, null), rows);

// Matching is case-insensitive and spans every column.
assert.deepEqual(filterRows(rows, columns, 'alice').map(r => r.Id), [1]);
assert.deepEqual(filterRows(rows, columns, 'FOLLOW').map(r => r.Id), [2, 30]);
assert.deepEqual(filterRows(rows, columns, 'follow up').map(r => r.Id), [2, 30]);

// Numeric cells are matched on their display text, so a substring of a number matches.
assert.deepEqual(filterRows(rows, columns, '30').map(r => r.Id), [30]);

// A term matching nothing yields an empty grid rather than everything.
assert.deepEqual(filterRows(rows, columns, 'nothing-matches-this'), []);

// A null cell must not match a non-empty term, and must not throw.
assert.deepEqual(filterRows(rows, columns, 'null'), []);

// A missing row object must not throw — the grid is fed whatever the run returned.
assert.deepEqual(filterRows([null, undefined, ...rows], columns, 'alice').map(r => r.Id), [1]);

// ── toCsv ───────────────────────────────────────────────────────────────────

const csv = toCsv(columns, rows);
const lines = csv.split('\r\n');

// Header first, then one line per row, CRLF-delimited.
assert.equal(lines[0], 'Id,Name,Notes');
assert.equal(lines.length, 4);
assert.equal(lines[1], '1,Alice,');

// A value containing a comma must be quoted or it would shift every later column.
assert.equal(toCsv(['A'], [{ A: 'x,y' }]).split('\r\n')[1], '"x,y"');

// An embedded double quote is doubled, per RFC 4180 — otherwise the field terminates early.
assert.equal(toCsv(['A'], [{ A: 'say "hi"' }]).split('\r\n')[1], '"say ""hi"""');

// Newlines inside a value must be quoted too, or the row would split.
assert.equal(toCsv(['A'], [{ A: 'line1\nline2' }]).split('\r\n')[0 + 1], '"line1\nline2"');

// A column name needing escaping is escaped in the header as well as the body.
assert.equal(toCsv(['we,ird'], [{ 'we,ird': 1 }]).split('\r\n')[0], '"we,ird"');

// A formula-looking value is written verbatim: CSV injection is a spreadsheet concern, and silently
// mangling data would be worse. Pinned so a future change to this is a deliberate one.
assert.equal(toCsv(['A'], [{ A: '=1+1' }]).split('\r\n')[1], '=1+1');

// ── resultRenderWindow ──────────────────────────────────────────────────────
//
// Not every producer bounds its rows: the Workstation and Portal run paths cap at 100/1000, but the
// VS Code REPL streams whatever the CLI evaluated. Rendering that whole is what hangs the panel.

// A small result is drawn entirely and reads naturally.
{
    const { visible, truncated, label } = resultRenderWindow(rows, rows.length, false);
    assert.equal(visible.length, 3);
    assert.equal(truncated, false);
    assert.equal(label, '3 rows');
}

// Singular is not "1 rows".
assert.equal(resultRenderWindow([rows[0]], 1, false).label, '1 row');

// An oversized result is capped, and the label says so rather than quietly showing fewer.
{
    const many = Array.from({ length: MAX_RENDERED_ROWS + 250 }, (_, i) => ({ Id: i }));
    const { visible, truncated, label } = resultRenderWindow(many, many.length, false);
    assert.equal(visible.length, MAX_RENDERED_ROWS);
    assert.equal(truncated, true);
    assert.match(label, /showing first/);
    // The true total stays visible — a truncated grid must not misreport how much data there is.
    assert.match(label, new RegExp(many.length.toLocaleString().replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
}

// When filtered and truncated, both the matched count and the true total survive.
{
    const many = Array.from({ length: MAX_RENDERED_ROWS + 10 }, (_, i) => ({ Id: i }));
    const { label } = resultRenderWindow(many, 99999, true);
    assert.match(label, /matched/);
    assert.match(label, /total/);
}

// Filtered but small: the "x of y" form, not a truncation notice.
assert.equal(resultRenderWindow([rows[0]], 3, true).label, '1 of 3 rows');

// Degenerate input must not throw — the panel renders whatever a producer sent.
assert.equal(resultRenderWindow(null, 0, false).visible.length, 0);
assert.equal(resultRenderWindow(undefined, undefined, false).visible.length, 0);

// The cap bounds the DOM but never the data: export reads the filtered rows, not what was drawn,
// so a truncated grid still exports in full.
{
    const many = Array.from({ length: MAX_RENDERED_ROWS + 5 }, (_, i) => ({ A: i }));
    const exported = toCsv(['A'], many).split('\r\n');
    assert.equal(exported.length, many.length + 1); // header + every row
}

await fs.rm(tempDir, { recursive: true, force: true });
console.log('result-grid-ui smoke passed');
