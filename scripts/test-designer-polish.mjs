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
assert.match(js, /id="pp-fmt-bg-picker"/);
assert.match(js, /id="pp-fmt-bg-text"/);
assert.match(js, /class="etlsql-dsgn-swatch-row"/);
assert.match(js, /id="pp-fmt-radius-slider"/);
assert.match(js, /id="pp-fmt-font-select"/);
assert.match(js, /id="pp-fmt-size-select"/);
assert.match(js, /id="pp-fmt-weight-select"/);
assert.match(js, /id="pp-fmt-shadow-select"/);
assert.match(js, /id="pp-fmt-opacity-slider"/);
assert.match(css, /\.etlsql-dsgn-color-picker-row/);
assert.match(css, /\.etlsql-dsgn-swatch-chip/);
assert.match(css, /\.etlsql-dsgn-slider-row/);
assert.match(css, /\.etlsql-dsgn-typography-grid/);
assert.match(js, /snapshotPackage\.visualSvgs/);

console.log('Designer discovery, hierarchy, empty-state, responsive, formatting pickers, and native SVG preview contract passed.');
