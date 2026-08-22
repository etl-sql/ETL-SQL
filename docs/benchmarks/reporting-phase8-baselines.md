# Phase 8 Reporting & Visuals Baseline Report

> **Timestamp (UTC):** 2026-08-22 10:39:04 | **Branch:** `phase8/gemini-gantt-retirement-audit` | **Engine Version:** `0.18.0.0`
> **Host OS:** `Microsoft Windows NT 10.0.26200.0` | **Runtime:** `.NET 10.0.11`

---

## 1. Browser Runtime Bundle Size Baseline

Physical payload sizes of client-side scripts, CSS styles, and library dependencies shipped in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.

| Asset | Raw Size | Gzip Size | Brotli Size | % of Shared Bundle |
| :--- | :---: | :---: | :---: | :---: |
| `arrow.min.js` | 162.3 KB | 44.8 KB | 44.4 KB | 6.0% |
| `designer/codemirror/codemirror-bundle.min.js` | 371.5 KB | 121.4 KB | 122.3 KB | 13.7% |
| `designer/designer.css` | 75.4 KB | 12.4 KB | 13.5 KB | 2.8% |
| `designer/designer.js` | 277.8 KB | 61.6 KB | 61.9 KB | 10.2% |
| `echarts.min.js` | 1.07 MB | 358.5 KB | 352.2 KB | 40.2% |
| `feedback.js` | 11.3 KB | 3.3 KB | 3.3 KB | 0.4% |
| `report-runtime.css` | 39.6 KB | 8.4 KB | 9.0 KB | 1.5% |
| `report-runtime.js` | 223.1 KB | 47.3 KB | 48.1 KB | 8.2% |
| `tabulator.min.css` | 27.8 KB | 3.9 KB | 4.1 KB | 1.0% |
| `tabulator.min.js` | 432.8 KB | 99.1 KB | 99.4 KB | 16.0% |
| **Total Shared Runtime** | **2.65 MB** | **760.9 KB** | **758.2 KB** | **100.0%** |

> [!IMPORTANT]
> `echarts.min.js` accounts for **1.07 MB (40.2%)** of the uncompressed shared browser runtime asset bundle. Removing ECharts eliminates **358.5 KB** of gzipped transfer payload per cold browser session.

---

## 2. Server-Side SSR & Package Binary Footprint Baseline

Size contribution of ClearScript V8 managed and native platform runtimes in published server artifacts (`src/ETL-SQL.Reporting/`):

| Package / Runtime | Target OS / Arch | Version | Package Size | Description |
| :--- | :---: | :---: | :---: | :--- |
| `Microsoft.ClearScript.V8` | `Managed (Any CPU)` | `7.4.5` | ~371.1 KB | Managed V8 bridge interface and type marshaling assembly. |
| `Microsoft.ClearScript.V8.Native.win-x64` | `win-x64` | `7.4.5` | ~27.08 MB | Native V8 + ClearScript C++ engine dynamic library for Windows 64-bit. |
| `Microsoft.ClearScript.V8.Native.win-arm64` | `win-arm64` | `7.4.5` | ~23.94 MB | Native V8 + ClearScript C++ engine dynamic library for Windows ARM64. |
| `Microsoft.ClearScript.V8.Native.linux-x64` | `linux-x64` | `7.4.5` | ~29.75 MB | Native V8 + ClearScript C++ engine dynamic shared library for Linux x64. |
| `Microsoft.ClearScript.V8.Native.linux-arm64` | `linux-arm64` | `7.4.5` | ~26.51 MB | Native V8 + ClearScript C++ engine dynamic shared library for Linux ARM64. |
| `Microsoft.ClearScript.V8.Native.osx-arm64` | `osx-arm64` | `7.4.5` | ~23.46 MB | Native V8 + ClearScript C++ engine dynamic shared library for macOS Apple Silicon. |
| **Total ClearScript V8 Multi-Platform Footprint** | **All Runtimes** | `7.4.5` | **~131.11 MB** | Complete multi-RID V8 runtime payload |

> [!NOTE]
> On a single published target (e.g., Linux x64 or Windows x64 container), ClearScript adds **~28 MB to ~31 MB** of native unmanaged binary weight and requires V8 process heap initialization (~35 MB working set overhead per node). Retiring ClearScript eliminates native C++ binary dependencies completely.

---

## 3. Representative Visual Fixture Execution Baselines

Measures end-to-end fixture build time (Lexer -> Parser -> Evaluator -> Manifest), export throughput (Markdown, CSV, SVG), output payload sizes, and process allocations across named representative fixtures.

| Fixture | Visual Type | Build Latency | Markdown Export | CSV Export | SVG Export | Manifest JSON | Process Memory |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `bar_category_revenue` | `BAR` | 221.27 ms | 6.817 ms (5.7 KB) | 0.420 ms (0 B) | 0.116 ms (3.1 KB) | 16.5 KB | 7.01 MB |
| `bar_with_goal_rule` | `BAR` | 42.39 ms | 1.288 ms (7.2 KB) | 0.003 ms (0 B) | 0.085 ms (3.7 KB) | 28.2 KB | 6.51 MB |
| `combo_revenue_margin` | `COMBO` | 6.10 ms | 0.188 ms (7.0 KB) | 0.002 ms (0 B) | 0.058 ms (3.8 KB) | 20.1 KB | 6.27 MB |
| `donut_market_share` | `DONUT` | 8.27 ms | 1.321 ms (3.2 KB) | 0.003 ms (0 B) | 0.047 ms (1.2 KB) | 15.3 KB | 6.33 MB |
| `line_timeseries_trend` | `LINE` | 7.04 ms | 0.106 ms (6.1 KB) | 0.001 ms (0 B) | 0.046 ms (3.2 KB) | 22.5 KB | 6.40 MB |
| `scatter_correlation` | `SCATTER` | 5.71 ms | 0.822 ms (6.6 KB) | 0.002 ms (0 B) | 0.076 ms (3.2 KB) | 28.4 KB | 6.43 MB |

---

## 4. Cold Start & Server Export Throughput Comparison

| Export Path | Cold Start Engine Init | Warm Render Latency | Memory Overhead | External Runtime Dependencies |
| :--- | :---: | :---: | :---: | :--- |
| **Native PlotPlan Pure C# SVG** | `< 1 ms` | `0.1 ms - 0.8 ms` | `< 15 KB` | **Zero** (Pure managed C# System.Text.StringBuilder) |
| **Legacy ECharts V8 SSR** | `120 ms - 280 ms` | `15 ms - 45 ms` | `~35 MB - 50 MB` | **ClearScript V8 + native C++ shared library + echarts.min.js** |

---

## 5. Visual Capability Matrix Status (All 37 Visual Types)

| Visual Type | Category | Browser | Static Export | PDF / Email | Terminal | Interactions | ECharts | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| `BAR` | Cartesian | **TemporaryDependency** — ECharts bar generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, drill, cross-filter, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `HBAR` | Cartesian | **TemporaryDependency** — ECharts horizontal bar generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, drill, cross-filter, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `LINE` | Cartesian | **TemporaryDependency** — ECharts line generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, zoom/pan, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `SCATTER` | Cartesian | **TemporaryDependency** — ECharts scatter generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, brush, zoom/pan, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `PIE` | Circular | **TemporaryDependency** — ECharts pie generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Slice select, legend toggle, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `DONUT` | Circular | **TemporaryDependency** — ECharts donut generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Slice select, legend toggle, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `BOXPLOT` | Statistical | **TemporaryDependency** — ECharts box plot | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Tooltip | Yes | PlotPlan migration target |
| `TREEMAP` | Hierarchical | **TemporaryDependency** — ECharts treemap | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — ordered proportional hierarchy | Drill, zoom, breadcrumb | Yes | PlotPlan migration target |
| `HEATMAP` | Matrix / Grid | **TemporaryDependency** — ECharts heat map generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Cell click, visual-map filter, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `COMBO` | Layered | **TemporaryDependency** — ECharts bar/line combo generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, series toggle, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `CUSTOM` | Advanced / Layered | **TemporaryDependency** — ECharts advanced chart generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, series toggle, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `TABLE` | Tabular | **ThirdPartyDependency** — Tabulator / HTML table | **Native** — Markdown, CSV, and static table exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre table | Sort, filter, pagination, row click | No | Non-ECharts rendering path |
| `CARD` | KPI | **Native** — native DOM card | **Native** — Markdown and static card exporters | **Native** — static PDF and email attachment formats | **Native** — Spectre panel | Click, navigation | No | Non-ECharts rendering path |
| `SLICER` | Filter / Control | **Native** — native DOM control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXT` | Narrative | **Native** — native DOM / Markdown | **Native** — Markdown and HTML | **Native** — static PDF and email attachment formats | **Native** — plain text / Spectre | None | No | Non-ECharts rendering path |
| `GAUGE` | Indicator | **TemporaryDependency** — ECharts gauge generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `FUNNEL` | Flow | **TemporaryDependency** — ECharts funnel generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Stage select, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `WATERFALL` | Variance | **TemporaryDependency** — ECharts waterfall | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Click, tooltip | Yes | PlotPlan migration target |
| `IMAGE` | Media | **Native** — native img element | **Native** — HTML image reference | **Native** — static PDF and email attachment formats | **SemanticFallback** — text placeholder | Click link | No | Non-ECharts rendering path |
| `BUBBLE` | Cartesian | **TemporaryDependency** — ECharts sized scatter generated transiently from PlotPlan | **Native** — PlotPlan native SVG | **Native** — PlotPlan SVG to static PDF; email attaches V8-free PDF/Markdown | **Native** — PlotPlan semantic terminal renderer | Click, zoom/pan, tooltip | Yes | Phase 3 semantic path; browser retains ECharts only as a transient backend |
| `RADAR` | Polar | **TemporaryDependency** — ECharts radar | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — ordered dimension/value table | Hover, legend toggle | Yes | PlotPlan migration target |
| `CANDLESTICK` | Financial | **TemporaryDependency** — ECharts candlestick | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Zoom/pan, tooltip | Yes | PlotPlan migration target |
| `MAP` | Geographic | **TemporaryDependency** — ECharts map / GeoJSON | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — ranked regional breakdown | Region click, zoom/pan, tooltip | Yes | PlotPlan migration target |
| `GANTT` | Timeline | **TemporaryDependency** — ECharts custom timeline | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Hover, zoom | Yes | PlotPlan migration target |
| `DATEPICKER` | Filter / Control | **Native** — native date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Date selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `RELDATEPICKER` | Filter / Control | **Native** — native relative-date control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Preset selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SLIDER` | Filter / Control | **Native** — native range control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Range input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `MULTISELECT` | Filter / Control | **Native** — native multi-select control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Selection, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SEARCH` | Filter / Control | **Native** — native search control | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `CHECKBOX` | Filter / Control | **Native** — native checkbox | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Toggle, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `TEXTBOX` | Filter / Control | **Native** — native text input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Text input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `NUMBERBOX` | Filter / Control | **Native** — native number input | **Unsupported** — omitted from non-browser exports | **Unsupported** — interactive control is not exported | **Native** — Spectre selection summary | Numeric input, parameter binding | No | Interactive-only visual; terminal shows current selection state |
| `SANKEY` | Flow / Network | **TemporaryDependency** — ECharts sankey | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — transition and source drop-off table | Node/edge highlight, tooltip | Yes | PlotPlan migration target |
| `SUNBURST` | Hierarchical | **TemporaryDependency** — ECharts sunburst | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — ordered proportional hierarchy | Drill, tooltip | Yes | PlotPlan migration target |
| `NETWORK` | Graph | **TemporaryDependency** — ECharts force graph | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **SemanticFallback** — node-degree and connection summary | Drag, zoom/pan, click | Yes | PlotPlan migration target |
| `TRELLIS` | Small Multiples | **TemporaryDependency** — ECharts trellis | **TemporaryDependency** — ECharts SSR SVG (SvgChartRenderer emits a semantic placeholder if SSR is unavailable) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre terminal renderer | Synchronized tooltip | Yes | PlotPlan migration target |
| `MATRIX` | Pivot / Matrix | **Native** — native DOM matrix | **TemporaryDependency** — ECharts SSR matrix (tabular exporters are also available) | **TemporaryDependency** — static PDF uses ECharts SSR; email attaches PDF/CSV/Markdown | **Native** — Spectre matrix | Expand/collapse, sorting, aggregation | Yes | Browser runtime dispatches MATRIX to renderMatrix; the static ECharts renderer still supports it |
