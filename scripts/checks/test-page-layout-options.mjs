// PAGE OPTIONS and MOBILE_LAYOUT reach the browser inside the manifest. Before this check they
// reached nothing else: the parser stored them, `PageNavigationAndChartGapsTests` asserted they
// were in `page.options`, and `renderLayout` read only `structure` and `slotMap`, so a background
// image, a max width, a centred page and an entire alternate mobile structure were parsed,
// serialized, asserted, and then dropped before they reached a pixel.
//
// The assertions below are on the mechanism, not on the option surviving into a dictionary. The
// helpers are lifted out of the canonical IIFE and run against a small fake element, and the wiring
// that calls them from the page render path is asserted separately — reverting either half goes red.

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

// `getOption` is the case-insensitive lookup the runtime uses for every option bag; the lifted
// helpers depend on it, so it is lifted too rather than reimplemented here.
const evaluated = new Function(
    `const FIT_MIN_HEIGHT_PX = 520; const FIT_COMPACT_ROW_PX = 260; const fitObserved = new WeakSet(); const resizeChartsIn = () => {};\n` +
    `${lift('getOption')}\n${lift('toCssLength')}\n${lift('toPixels')}\n${lift('normalizePageFit')}\n` +
    `${lift('parseRowWeights')}\n${lift('updatePageFit')}\n${lift('measureGridRows')}\n${lift('applyPageFit')}\n${lift('applyPageOptions')}\n` +
    'return { toCssLength, toPixels, applyPageOptions, normalizePageFit, updatePageFit };'
)();
const { toCssLength, toPixels, applyPageOptions, normalizePageFit, updatePageFit } = evaluated;

// ── Lengths ─────────────────────────────────────────────────────────────────

// MAX_WIDTH = 1440 and BREAKPOINT = 768 are written unitless at least as often as '1440px', and
// both arrive as strings. A bare number is pixels; an authored unit is left alone.
assert.equal(toCssLength(1440), '1440px');
assert.equal(toCssLength('1440'), '1440px');
assert.equal(toCssLength('1440px'), '1440px');
assert.equal(toCssLength('80%'), '80%');
assert.equal(toCssLength(''), null);
assert.equal(toCssLength(null), null);
assert.equal(toPixels('768'), 768);
assert.equal(toPixels('768px'), 768);
assert.equal(toPixels(null), 0);

// ── PAGE OPTIONS reach the element ──────────────────────────────────────────

const fakeElement = () => ({ style: {} });

const pageDiv = fakeElement();
const contentDiv = fakeElement();
applyPageOptions(pageDiv, contentDiv, {
    options: {
        BACKGROUND_IMAGE: '/assets/bg.png',
        BACKGROUND_SIZE: 'contain',
        MAX_WIDTH: '1280',
        ALIGN_CONTENT: 'CENTER',
        OVERFLOW: 'SCROLL',
    },
});

assert.equal(pageDiv.style.backgroundImage, 'url("/assets/bg.png")');
assert.equal(pageDiv.style.backgroundSize, 'contain');
assert.equal(pageDiv.style.overflow, 'scroll');
assert.equal(contentDiv.style.maxWidth, '1280px');
// ALIGN_CONTENT = CENTER is the margin pair, not a text alignment: the grid itself is centred in
// the page, which is what constrains a dashboard to a readable column on a wide display.
assert.equal(contentDiv.style.marginLeft, 'auto');
assert.equal(contentDiv.style.marginRight, 'auto');

// The CSS url(...) form is passed through as written rather than wrapped a second time.
const wrapped = fakeElement();
applyPageOptions(wrapped, fakeElement(), { options: { BACKGROUND_IMAGE: "url('/a/b.png')" } });
assert.equal(wrapped.style.backgroundImage, "url('/a/b.png')");

// A bare path cannot close url() and open a second declaration: the result is always exactly one
// url("…") token whose interior holds no quote, parenthesis or semicolon to break out with.
const hostile = fakeElement();
applyPageOptions(hostile, fakeElement(), { options: { BACKGROUND_IMAGE: '/a.png"); background: red; x:("' } });
assert.match(hostile.style.backgroundImage, /^url\("[^"'();\s]*"\)$/);

// BACKGROUND_SIZE without BACKGROUND_IMAGE paints nothing, and a page with no OPTIONS is untouched.
const bare = fakeElement();
const bareContent = fakeElement();
applyPageOptions(bare, bareContent, { options: null });
assert.deepEqual(bare.style, {});
assert.deepEqual(bareContent.style, {});

// ── FIT = 'SCREEN' ──────────────────────────────────────────────────────────

assert.equal(normalizePageFit('screen'), 'SCREEN');
assert.equal(normalizePageFit(' SCREEN '), 'SCREEN');
assert.equal(normalizePageFit('NONE'), 'NONE');
assert.equal(normalizePageFit('bogus'), 'NONE');
assert.equal(normalizePageFit(undefined), 'NONE');

// No FIT leaves the page alone (no data-fit marker, so the CSS never matches).
const unfit = { ...fakeElement(), dataset: {} };
applyPageOptions(unfit, fakeElement(), { options: { MAX_WIDTH: '900' } });
assert.equal(unfit.dataset.fit, undefined);

globalThis.window = { innerHeight: 900, matchMedia: () => ({ matches: false }) };
// An inactive page is not measured: it is display:none and would report a zero rect.
const inactive = {
    dataset: { fit: 'SCREEN' }, style: { height: '999px' },
    classList: { contains: () => false },
};
updatePageFit(inactive, { style: {} });
assert.equal(inactive.style.height, '');

// An active page is sized to the viewport and its rows are split: short rows stay auto,
// tall rows share the rest.
globalThis.window = { innerHeight: 900, matchMedia: () => ({ matches: false }) };
globalThis.requestAnimationFrame = (f) => f();
globalThis.getComputedStyle = () => ({ gridTemplateRows: '90px 400px 400px' });
const active = {
    dataset: { fit: 'SCREEN' }, style: {},
    classList: { contains: () => true },
    getBoundingClientRect: () => ({ top: 100 }),
};
const cell = (area, heavy) => ({ style: { gridArea: area }, querySelector: (sel) => (heavy === sel.split(',')[0].trim() || (heavy === 'table' && sel.startsWith('table')) ? {} : null) });
const grid = { style: { gridTemplateAreas: '"A A" "B C" "D D"', gridTemplateRows: '' },
    children: [cell('A', null), cell('B', '.chart-wrapper'), cell('D', 'table')] };
updatePageFit(active, grid);
assert.equal(active.style.height, '784px');
assert.equal(grid.style.gridTemplateRows, 'auto minmax(0, 2fr) minmax(0, 1fr)');

// A small window never squeezes below the minimum; the page scrolls instead.
globalThis.window.innerHeight = 300;
updatePageFit(active, grid);
assert.equal(active.style.height, '520px');

// MIN_HEIGHT and ROW_WEIGHTS override the default floor and row heuristics.
const customFit = {
    dataset: {},
    style: {},
    classList: { contains: () => true },
    getBoundingClientRect: () => ({ top: 100 }),
};
const customGrid = { style: { gridTemplateAreas: '"A A" "B C" "D D"', gridTemplateRows: '' }, children: [] };
applyPageOptions(customFit, customGrid, {
    options: { FIT: 'SCREEN', MIN_HEIGHT: '680', ROW_WEIGHTS: 'auto, 3, 1.5' },
});
updatePageFit(customFit, customGrid);
assert.equal(customFit.style.height, '680px');
assert.equal(customGrid.style.gridTemplateRows, 'auto minmax(0, 3fr) minmax(0, 1.5fr)');

// A multi-row spanning sidebar does not pull a compact KPI row into 2fr.
globalThis.window.innerHeight = 900;
globalThis.getComputedStyle = () => ({ gridTemplateRows: '80px 400px' });
const spanPage = {
    dataset: { fit: 'SCREEN' }, style: {},
    classList: { contains: () => true },
    getBoundingClientRect: () => ({ top: 100 }),
};
const spanGrid = {
    style: { gridTemplateAreas: '"A S" "B S"', gridTemplateRows: '' },
    children: [cell('A', null), cell('B', '.chart-wrapper'), cell('S', '.chart-wrapper')],
};
updatePageFit(spanPage, spanGrid);
assert.equal(spanGrid.style.gridTemplateRows, 'auto minmax(0, 2fr)');

// ── The render path actually calls them ─────────────────────────────────────

const renderPage = source.slice(source.indexOf('function renderPage('));
const renderPageBody = renderPage.slice(0, renderPage.indexOf('\n    function ', 1));
assert.match(renderPageBody, /applyPageOptions\(/,
    'renderPage no longer applies PAGE OPTIONS — they would reach the manifest and stop there.');
assert.match(renderPageBody, /renderResponsiveLayout\(/,
    'renderPage no longer goes through the responsive layout — MOBILE_LAYOUT would be ignored.');

const responsive = lift('renderResponsiveLayout');
// The breakpoint decides which structure is drawn, and crossing it redraws: the two layouts put
// different visuals in different slots, so a CSS-only reflow cannot express the difference.
assert.match(responsive, /matchMedia\(`\(max-width: \$\{breakpoint\}px\)`\)/);
assert.match(responsive, /mobile\.structure/);
assert.match(responsive, /mobile\.slotMap \|\| page\.slotMap/);
assert.match(responsive, /addEventListener\('change'/);
// A page with no MOBILE_LAYOUT, or one whose breakpoint is unusable, still renders the default
// layout rather than an empty grid.
assert.match(responsive, /if \(!mobile \|\| !mobile\.structure \|\| breakpoint <= 0\)/);

console.log('PAGE OPTIONS and MOBILE_LAYOUT reach the rendered page.');
