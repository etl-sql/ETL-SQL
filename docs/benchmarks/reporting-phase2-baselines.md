# Phase 2 Reporting & Visuals Baseline Report

> **Timestamp (UTC):** 2026-08-25 16:18:49 | **Branch:** `release/v0.19.0` | **Engine Version:** `0.19.0-phase2`

---

## 1. Browser Runtime Bundle Size Baseline

Measures physical payload sizes of client-side scripts, CSS styles, and library dependencies shipped in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.

| Asset | Raw Size | Gzip Size | Brotli Size |
| :--- | :---: | :---: | :---: |
| `arrow.min.js` | 162.3 KB | 44.8 KB | 44.4 KB |
| `designer/codemirror/codemirror-bundle.min.js` | 371.5 KB | 121.4 KB | 122.3 KB |
| `designer/designer.css` | 75.4 KB | 12.4 KB | 13.5 KB |
| `designer/designer.js` | 269.8 KB | 61.2 KB | 61.5 KB |
| `feedback.js` | 11.3 KB | 3.3 KB | 3.3 KB |
| `report-runtime.css` | 43.8 KB | 9.6 KB | 10.2 KB |
| `report-runtime.js` | 278.8 KB | 60.7 KB | 61.6 KB |
| `tabulator.min.css` | 27.8 KB | 3.9 KB | 4.1 KB |
| `tabulator.min.js` | 432.8 KB | 99.1 KB | 99.4 KB |
| **Total Shared Runtime** | **1.63 MB** | **416.5 KB** | **420.3 KB** |

---

## 2. Representative Visual Fixture Baselines

Measures end-to-end fixture build time, export throughput (Markdown, CSV, SVG), output payload sizes, and process allocations across the named representative fixtures. The first fixture in a fresh test process includes runtime JIT cost. CSV is 0 B for these chart-only fixtures because the CSV renderer exports tabular visuals only.

| Fixture | Visual Type | Fixture Build | Markdown Export | CSV Export | SVG Export | Manifest JSON | Browser Delivery (raw / gzip) | Process Allocated |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `bar_category_revenue` | `BAR` | 239.37 ms | 2.772 ms (5.8 KB) | 0.383 ms (0 B) | 12.832 ms (3.6 KB) | 22.6 KB | 10.1 KB / 2.3 KB | 6.73 MB |
| `bar_with_goal_rule` | `BAR` | 21.43 ms | 0.234 ms (9.1 KB) | 0.002 ms (0 B) | 0.116 ms (5.9 KB) | 37.6 KB | 14.5 KB / 3.0 KB | 6.34 MB |
| `combo_revenue_margin` | `COMBO` | 23.82 ms | 0.137 ms (7.2 KB) | 0.002 ms (0 B) | 0.054 ms (4.6 KB) | 27.8 KB | 12.1 KB / 2.6 KB | 6.20 MB |
| `donut_market_share` | `DONUT` | 7.46 ms | 0.109 ms (4.9 KB) | 0.001 ms (0 B) | 0.060 ms (2.9 KB) | 19.3 KB | 8.3 KB / 2.3 KB | 6.16 MB |
| `line_timeseries_trend` | `LINE` | 6.58 ms | 0.094 ms (6.2 KB) | 0.001 ms (0 B) | 0.055 ms (3.7 KB) | 28.6 KB | 10.3 KB / 2.4 KB | 6.32 MB |
| `scatter_correlation` | `SCATTER` | 5.45 ms | 0.101 ms (6.3 KB) | 0.001 ms (0 B) | 0.057 ms (3.8 KB) | 34.1 KB | 10.6 KB / 2.4 KB | 6.35 MB |

**Combined manifest JSON:** 170.1 KB on the server object, 65.9 KB raw / 15.1 KB gzip delivered to a browser client. End-to-end page weight is the browser figure plus the shared assets above, not shared assets alone.

### End-to-End Page Weight

What a browser downloads to render one report: the shared runtime assets plus that report's delivered manifest. Shared assets are counted once because they are cached across reports in a session; the manifest is per report. Neither half alone is the page weight.

| Fixture | Manifest (raw / gzip) | Shared assets (raw / gzip) | **Page weight (raw / gzip)** |
| :--- | :---: | :---: | :---: |
| `bar_category_revenue` | 10.1 KB / 2.3 KB | 1.63 MB / 416.5 KB | **1.64 MB / 418.8 KB** |
| `bar_with_goal_rule` | 14.5 KB / 3.0 KB | 1.63 MB / 416.5 KB | **1.65 MB / 419.5 KB** |
| `combo_revenue_margin` | 12.1 KB / 2.6 KB | 1.63 MB / 416.5 KB | **1.65 MB / 419.1 KB** |
| `donut_market_share` | 8.3 KB / 2.3 KB | 1.63 MB / 416.5 KB | **1.64 MB / 418.8 KB** |
| `line_timeseries_trend` | 10.3 KB / 2.4 KB | 1.63 MB / 416.5 KB | **1.64 MB / 418.9 KB** |
| `scatter_correlation` | 10.6 KB / 2.4 KB | 1.63 MB / 416.5 KB | **1.64 MB / 418.9 KB** |

**Dominant shared assets:** `tabulator.min.js` (432.8 KB raw / 99.1 KB gzip), `designer/codemirror/codemirror-bundle.min.js` (371.5 KB raw / 121.4 KB gzip), `report-runtime.js` (278.8 KB raw / 60.7 KB gzip), `designer/designer.js` (269.8 KB raw / 61.2 KB gzip) — 81% of the shared raw total.

### Explicit Client-Side Unsupported Measurements
- **Client Browser Paint / V8 Frame Latency**: `N/A (unsupported: requires headless Chrome CDP profiling in browser test runner)`
- **Client DOM/ECharts Heap Memory**: `N/A (unsupported: requires browser CDP memory heap snapshots)`

---

## 3. Visual Capability Matrix (All 37 Visual Types)

| Visual Type | Category | Browser | Static Export | PDF / Email | Terminal | Interactions | External Chart Runtime | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| `BAR` | Cartesian | **Native** — PlotPlan native SVG bar | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, drill, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `HBAR` | Cartesian | **Native** — PlotPlan native SVG horizontal bar | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, drill, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `LINE` | Cartesian | **Native** — PlotPlan native SVG line | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `SCATTER` | Cartesian | **Native** — PlotPlan native SVG scatter | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `PIE` | Circular | **Native** — PlotPlan native SVG pie | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Slice click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `DONUT` | Circular | **Native** — PlotPlan native SVG donut | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Slice click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `BOXPLOT` | Statistical | **Native** — PlotPlan native SVG box plot | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Tooltip | No | Shared renderer-neutral PlotPlan path |
| `TREEMAP` | Hierarchical | **Native** — specialized native SVG treemap | **Native** — specialized native SVG renderer | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **SemanticFallback** — ordered proportional hierarchy | Rect click, drill context, tooltip | No | Approved focused native layout module |
| `HEATMAP` | Matrix / Grid | **Native** — PlotPlan native SVG heat map | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Cell click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `COMBO` | Layered | **Native** — PlotPlan native SVG bar/line combo | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `CUSTOM` | Advanced / Layered | **Native** — PlotPlan native SVG advanced chart | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `TABLE` | Tabular | **ThirdPartyDependency** — Tabulator / HTML table | **Native** — Markdown, CSV, and static table exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre table | Sort, filter, pagination, row click | No | Native rendering path |
| `CARD` | KPI | **Native** — native DOM card | **Native** — Markdown and static card exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre panel | Click, navigation | No | Native rendering path |
| `SLICER` | Filter / Control | **Native** — native DOM control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXT` | Narrative | **Native** — native DOM / Markdown | **Native** — Markdown and HTML | **Native** — static PDF and email attachment formats | **Native** — plain text / Spectre | None | No | Native rendering path |
| `GAUGE` | Indicator | **Native** — PlotPlan native SVG gauge | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Tooltip | No | Shared renderer-neutral PlotPlan path |
| `FUNNEL` | Flow | **Native** — PlotPlan native SVG funnel | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Stage select, tooltip | No | Shared renderer-neutral PlotPlan path |
| `WATERFALL` | Variance | **Native** — PlotPlan native SVG waterfall | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, tooltip | No | Shared renderer-neutral PlotPlan path |
| `IMAGE` | Media | **Native** — native img element | **Native** — HTML image reference | **Native** — static PDF and email attachment formats | **SemanticFallback** — text placeholder | Click link | No | Native rendering path |
| `BUBBLE` | Cartesian | **Native** — PlotPlan native SVG sized scatter | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, cross-filter, tooltip | No | Shared renderer-neutral PlotPlan path |
| `RADAR` | Polar | **Native** — PlotPlan native SVG radar | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, tooltip | No | Shared renderer-neutral PlotPlan path |
| `CANDLESTICK` | Financial | **Native** — PlotPlan native SVG candlestick | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, tooltip | No | Shared renderer-neutral PlotPlan path |
| `MAP` | Geographic | **Native** — specialized native SVG map / GeoJSON | **Native** — specialized native SVG renderer | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **SemanticFallback** — ranked regional breakdown | Region/point click, cross-filter, tooltip | No | Approved focused native layout module |
| `GANTT` | Timeline | **Native** — PlotPlan native SVG timeline | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Task click, tooltip | No | Shared renderer-neutral PlotPlan path |
| `DATEPICKER` | Filter / Control | **Native** — native date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Date selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `RELDATEPICKER` | Filter / Control | **Native** — native relative-date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Preset selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SLIDER` | Filter / Control | **Native** — native range control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Range input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `MULTISELECT` | Filter / Control | **Native** — native multi-select control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SEARCH` | Filter / Control | **Native** — native search control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `CHECKBOX` | Filter / Control | **Native** — native checkbox | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Toggle, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXTBOX` | Filter / Control | **Native** — native text input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `NUMBERBOX` | Filter / Control | **Native** — native number input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Numeric input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SANKEY` | Flow / Network | **Native** — specialized native SVG sankey | **Native** — specialized native SVG renderer | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **SemanticFallback** — transition and source drop-off table | Link click, tooltip | No | Approved focused native layout module |
| `SUNBURST` | Hierarchical | **Native** — specialized native SVG sunburst | **Native** — specialized native SVG renderer | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **SemanticFallback** — ordered proportional hierarchy | Arc click, drill context, tooltip | No | Approved focused native layout module |
| `NETWORK` | Graph | **Native** — specialized native SVG network | **Native** — specialized native SVG renderer | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **SemanticFallback** — node-degree and connection summary | Link click, tooltip | No | Approved focused native layout module |
| `TRELLIS` | Small Multiples | **Native** — PlotPlan native SVG trellis | **Native** — PlotPlan native SVG | **Native** — native SVG to static PDF; email attaches PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Mark click, tooltip | No | Shared renderer-neutral PlotPlan path |
| `MATRIX` | Pivot / Matrix | **Native** — native SVG matrix | **Native** — native SVG matrix and tabular exporters | **Native** — native SVG to PDF; email attaches PDF/CSV/Markdown | **Native** — Spectre matrix | Row click | No | Native SVG matrix with semantic table fallbacks |
