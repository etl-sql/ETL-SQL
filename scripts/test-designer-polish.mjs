import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const js = await readFile('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js', 'utf8');
const css = await readFile('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css', 'utf8');

assert.match(js, /id="dsgn-palette-search"/);
assert.match(js, /function filterPalette\(\)/);
assert.match(js, /data-search="\$\{type\} \$\{cat\.name\}"/);
assert.match(js, /Build your first visual/);
assert.match(js, /No datasets yet/);
assert.match(js, /No visuals on this page/);
assert.match(js, /title: 'Save report', label: 'Save', primary: true/);
assert.match(js, /title: 'Preview report', label: 'Preview'/);
assert.match(css, /@media \(max-width: 1280px\)/);
assert.match(css, /@media \(max-width: 900px\)/);
assert.match(css, /grid-template-areas: "topbar topbar" "sidebar canvas" "sidebar props"/);
assert.match(css, /\.etlsql-dsgn-palette-dot/);

console.log('Designer discovery, hierarchy, empty-state, and responsive contract passed.');
