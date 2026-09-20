import { readFileSync as readSplitSource } from 'node:fs';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const designerCanvasRenderSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-canvas-render.js', 'utf8');


// The designer is split into focused modules, so each assertion names the module that
// owns the behaviour. Concatenating the modules and grepping the result would keep this script green
// while saying nothing about where anything lives — and the whole point of the split was that the
// formatting inspector is its own unit.
const root = 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer';
const designer = await readFile(`${root}/designer.js`, 'utf8');
const formatInspector = await readFile(`${root}/visual-format-inspector.js`, 'utf8');
const css = await readFile(`${root}/designer.css`, 'utf8');

// The shell owns palette discovery; the canvas controller owns hierarchy and empty states.
assert.match(designer, /id="dsgn-palette-search"/);
assert.match(designer, /function filterPalette\(\)/);
assert.match(designer, /data-search="\$\{type\} \$\{cat\.name\}"/);
assert.match(designerCanvasRenderSource, /Build your first visual/);
assert.match(designerCanvasRenderSource, /No datasets yet/);
assert.match(designerCanvasRenderSource, /No visuals on this page/);
assert.match(designer, /title: 'Save report', label: 'Save', primary: true/);
assert.match(designer, /title: 'Preview report', label: 'Preview'/);

assert.match(css, /@media \(max-width: 1280px\)/);
assert.match(css, /@media \(max-width: 900px\)/);

// Formatting pickers moved to visual-format-inspector.js in the v0.20.0 browser split.
assert.match(formatInspector, /id="pp-fmt-bg-picker"/);
assert.match(formatInspector, /id="pp-fmt-bg-text"/);
assert.match(formatInspector, /class="etlsql-dsgn-swatch-row"/);
assert.match(formatInspector, /id="pp-fmt-radius-slider"/);
assert.match(formatInspector, /id="pp-fmt-font-select"/);
assert.match(formatInspector, /id="pp-fmt-size-select"/);
assert.match(formatInspector, /id="pp-fmt-weight-select"/);
assert.match(formatInspector, /id="pp-fmt-shadow-select"/);
assert.match(formatInspector, /id="pp-fmt-opacity-slider"/);

assert.match(css, /\.etlsql-dsgn-color-picker-row/);
assert.match(css, /\.etlsql-dsgn-swatch-chip/);
assert.match(css, /\.etlsql-dsgn-slider-row/);
assert.match(css, /\.etlsql-dsgn-typography-grid/);

// Native SVG preview contract.
assert.match(designerCanvasRenderSource, /snapshotPackage\.visualSvgs/);

console.log('Designer discovery, hierarchy, empty-state, responsive, formatting pickers, and native SVG preview contract passed.');
