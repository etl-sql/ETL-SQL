# Phase 2 Reporting & Visuals Baseline Report

> **Timestamp (UTC):** 2026-08-21 12:23:21 | **Branch:** `feat/reporting-phase2-contracts` | **Engine Version:** `0.19.0-phase2`

---

## 1. Browser Runtime Bundle Size Baseline

Measures physical payload sizes of client-side scripts, CSS styles, and library dependencies shipped in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.

| Asset | Raw Size | Gzip Size | Brotli Size |
| :--- | :---: | :---: | :---: |
| `arrow.min.js` | 162.3 KB | 44.8 KB | 44.4 KB |
| `designer/codemirror/codemirror-bundle.min.js` | 371.5 KB | 121.4 KB | 122.3 KB |
| `designer/designer.css` | 75.4 KB | 12.4 KB | 13.5 KB |
| `designer/designer.js` | 277.8 KB | 61.6 KB | 61.9 KB |
| `echarts.min.js` | 1.07 MB | 358.5 KB | 352.2 KB |
| `feedback.js` | 11.3 KB | 3.3 KB | 3.3 KB |
| `report-runtime.css` | 39.4 KB | 8.3 KB | 8.9 KB |
| `report-runtime.js` | 222.3 KB | 47.4 KB | 48.3 KB |
| `tabulator.min.css` | 27.8 KB | 3.9 KB | 4.1 KB |
| `tabulator.min.js` | 432.8 KB | 99.1 KB | 99.4 KB |
| **Total Shared Runtime** | **2.65 MB** | **760.9 KB** | **758.3 KB** |

---

## 2. Representative Visual Fixture Baselines

Measures end-to-end fixture build time, export throughput (Markdown, CSV, SVG), output payload sizes, and process allocations across the named representative fixtures. The first fixture in a fresh test process includes runtime JIT cost. CSV is 0 B for these chart-only fixtures because the CSV renderer exports tabular visuals only.

| Fixture | Visual Type | Fixture Build | Markdown Export | CSV Export | SVG Export | Manifest JSON | Process Allocated |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `bar_category_revenue` | `BAR` | 231.84 ms | 292.873 ms (8.9 KB) | 0.430 ms (0 B) | 3.136 ms (1.8 KB) | 3.7 KB | 7.46 MB |
| `bar_with_goal_rule` | `BAR` | 13.06 ms | 9.700 ms (11.2 KB) | 0.006 ms (0 B) | 0.045 ms (1.9 KB) | 4.7 KB | 6.52 MB |
| `combo_revenue_margin` | `COMBO` | 9.02 ms | 8.599 ms (12.6 KB) | 0.005 ms (0 B) | 0.179 ms (445 B) | 3.9 KB | 6.48 MB |
| `donut_market_share` | `DONUT` | 10.41 ms | 6.418 ms (9.2 KB) | 0.005 ms (0 B) | 3.751 ms (1.1 KB) | 3.7 KB | 6.31 MB |
| `line_timeseries_trend` | `LINE` | 6.57 ms | 5.248 ms (12.2 KB) | 0.003 ms (0 B) | 2.184 ms (2.3 KB) | 3.6 KB | 6.35 MB |
| `scatter_correlation` | `SCATTER` | 6.22 ms | 4.254 ms (12.5 KB) | 0.003 ms (0 B) | 0.006 ms (439 B) | 3.4 KB | 6.40 MB |

### Explicit Client-Side Unsupported Measurements
- **Client Browser Paint / V8 Frame Latency**: `N/A (unsupported: requires headless Chrome CDP profiling in browser test runner)`
- **Client DOM/ECharts Heap Memory**: `N/A (unsupported: requires browser CDP memory heap snapshots)`

---

## 3. Visual Capability Matrix (All 36 Visual Types)

| Visual Type | Category | Browser | Static Export | PDF / Email | Terminal | Interactions | ECharts | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| `BAR` | Cartesian | **TemporaryDependency** — ECharts bar | **Native** — SvgChartRenderer | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, drill, cross-filter, tooltip | Yes | Native static SVG exists; browser rendering still uses ECharts |
| `HBAR` | Cartesian | **TemporaryDependency** — ECharts horizontal bar | **Native** — SvgChartRenderer | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, drill, cross-filter, tooltip | Yes | Native static SVG exists; browser rendering still uses ECharts |
| `LINE` | Cartesian | **TemporaryDependency** — ECharts line | **Native** — SvgChartRenderer | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, zoom/pan, tooltip | Yes | Native static SVG exists; browser rendering still uses ECharts |
| `SCATTER` | Cartesian | **TemporaryDependency** — ECharts scatter | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, brush, zoom/pan, tooltip | Yes | PlotPlan migration target |
| `PIE` | Circular | **TemporaryDependency** — ECharts pie | **Native** — SvgChartRenderer | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Slice select, legend toggle, tooltip | Yes | Native static SVG exists; browser rendering still uses ECharts |
| `DONUT` | Circular | **TemporaryDependency** — ECharts donut | **Native** — SvgChartRenderer | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Slice select, legend toggle, tooltip | Yes | Native static SVG exists; browser rendering still uses ECharts |
| `BOXPLOT` | Statistical | **TemporaryDependency** — ECharts box plot | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Tooltip | Yes | PlotPlan migration target |
| `TREEMAP` | Hierarchical | **TemporaryDependency** — ECharts treemap | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Drill, zoom, breadcrumb | Yes | PlotPlan migration target |
| `HEATMAP` | Matrix / Grid | **TemporaryDependency** — ECharts heat map | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Cell click, visual-map filter, tooltip | Yes | PlotPlan migration target |
| `COMBO` | Layered | **TemporaryDependency** — ECharts bar/line combo | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Click, series toggle, tooltip | Yes | PlotPlan migration target |
| `TABLE` | Tabular | **ThirdPartyDependency** — Tabulator / HTML table | **Native** — Markdown, CSV, and static table exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre table | Sort, filter, pagination, row click | No | Non-ECharts rendering path |
| `CARD` | KPI | **Native** — native DOM card | **Native** — Markdown and static card exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre panel | Click, navigation | No | Non-ECharts rendering path |
| `SLICER` | Filter / Control | **Native** — native DOM control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXT` | Narrative | **Native** — native DOM / Markdown | **Native** — Markdown and HTML | **Native** — static PDF and email attachment formats | **Native** — plain text / Spectre | None | No | Non-ECharts rendering path |
| `GAUGE` | Indicator | **TemporaryDependency** — ECharts gauge | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Tooltip | Yes | PlotPlan migration target |
| `FUNNEL` | Flow | **TemporaryDependency** — ECharts funnel | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Stage select, tooltip | Yes | PlotPlan migration target |
| `WATERFALL` | Variance | **TemporaryDependency** — ECharts waterfall | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, tooltip | Yes | PlotPlan migration target |
| `IMAGE` | Media | **Native** — native img element | **Native** — HTML image reference | **Native** — static PDF and email attachment formats | **SemanticFallback** — text placeholder | Click link | No | Non-ECharts rendering path |
| `BUBBLE` | Cartesian | **TemporaryDependency** — ECharts sized scatter | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, zoom/pan, tooltip | Yes | PlotPlan migration target |
| `RADAR` | Polar | **TemporaryDependency** — ECharts radar | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Hover, legend toggle | Yes | PlotPlan migration target |
| `CANDLESTICK` | Financial | **TemporaryDependency** — ECharts candlestick | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Zoom/pan, tooltip | Yes | PlotPlan migration target |
| `MAP` | Geographic | **TemporaryDependency** — ECharts map / GeoJSON | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Region click, zoom/pan, tooltip | Yes | PlotPlan migration target |
| `GANTT` | Timeline | **TemporaryDependency** — ECharts custom timeline | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Hover, zoom | Yes | PlotPlan migration target |
| `DATEPICKER` | Filter / Control | **Native** — native date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Date selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `RELDATEPICKER` | Filter / Control | **Native** — native relative-date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Preset selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SLIDER` | Filter / Control | **Native** — native range control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Range input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `MULTISELECT` | Filter / Control | **Native** — native multi-select control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SEARCH` | Filter / Control | **Native** — native search control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `CHECKBOX` | Filter / Control | **Native** — native checkbox | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Toggle, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXTBOX` | Filter / Control | **Native** — native text input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `NUMBERBOX` | Filter / Control | **Native** — native number input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Numeric input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SANKEY` | Flow / Network | **TemporaryDependency** — ECharts sankey | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Node/edge highlight, tooltip | Yes | PlotPlan migration target |
| `SUNBURST` | Hierarchical | **TemporaryDependency** — ECharts sunburst | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Drill, tooltip | Yes | PlotPlan migration target |
| `NETWORK` | Graph | **TemporaryDependency** — ECharts force graph | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — textual summary / placeholder | Drag, zoom/pan, click | Yes | PlotPlan migration target |
| `TRELLIS` | Small Multiples | **TemporaryDependency** — ECharts trellis | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Synchronized tooltip | Yes | PlotPlan migration target |
| `MATRIX` | Pivot / Matrix | **Native** — native DOM matrix | **TemporaryDependency** — ECharts SSR matrix (tabular exporters are also available) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre matrix | Expand/collapse, sorting, aggregation | Yes | Browser runtime dispatches MATRIX to renderMatrix; the static ECharts renderer still supports it |
