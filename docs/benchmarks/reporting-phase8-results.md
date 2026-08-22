# Phase 8 Standard Catalog Migration and Runtime Retirement Results

> Measured 2026-08-22 12:07:46 UTC on `phase8/standard-catalog-native-retirement` with ETL-SQL `0.18.0.0` / .NET `10.0.11`.

These results preserve `reporting-phase8-baselines.*` as the pre-migration record and measure the completed native runtime on the same representative fixture harness. Timings are local-machine observations, not universal performance budgets.

## Footprint

| Metric | Before | After | Change |
| :--- | ---: | ---: | ---: |
| Shared browser assets (raw) | 2.65 MB | 1.56 MB | -41.1% |
| Shared browser assets (gzip) | 760.9 KB | 397.9 KB | -47.7% |
| Shared browser assets (Brotli) | 758.2 KB | 401.7 KB | -47.0% |
| ClearScript multi-RID package estimate | 131.11 MB | 0 B | -100.0% |

## Representative runtime and artifacts

- First fixture build (the harness cold path): **234.04 ms**
- Median fixture build: **16.68 ms**
- Maximum per-fixture managed allocation: **6.80 MB**
- Combined native SVG export time: **0.280 ms**
- Combined native SVG artifact size: **18.2 KB**
- Combined representative manifest size: **155.0 KB**

| Fixture | Type | Build | SVG export | SVG size | Manifest | Allocated |
| :--- | :---: | ---: | ---: | ---: | ---: | ---: |
| `bar_category_revenue` | `BAR` | 234.04 ms | 0.071 ms | 3.1 KB | 21.2 KB | 6.80 MB |
| `bar_with_goal_rule` | `BAR` | 16.68 ms | 0.061 ms | 3.7 KB | 33.1 KB | 6.37 MB |
| `combo_revenue_margin` | `COMBO` | 26.97 ms | 0.040 ms | 3.8 KB | 25.7 KB | 6.28 MB |
| `donut_market_share` | `DONUT` | 5.31 ms | 0.026 ms | 1.2 KB | 15.8 KB | 6.24 MB |
| `line_timeseries_trend` | `LINE` | 4.55 ms | 0.034 ms | 3.2 KB | 27.1 KB | 6.41 MB |
| `scatter_correlation` | `SCATTER` | 6.09 ms | 0.048 ms | 3.2 KB | 32.2 KB | 6.43 MB |

## Capability result

All graphical catalog entries now use the shared renderer-neutral PlotPlan path or an approved focused native SVG layout module. No capability entry requires an external chart runtime.

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
