# Visual Gallery

Self-contained Report-SQL (`.rptsql`) scripts demonstrating every visual type and interactive control available in ETL-SQL.

Each script generates inline mock data via `#temp` tables, applies mappings and visual configurations, and compiles directly for the terminal, CLI runner, VS Code extension preview, or Portal.

## Charts

| Visual | Script | Key Features |
| :--- | :--- | :--- |
| **Bar** | [`bar_chart.rptsql`](bar_chart.rptsql) | Basic, grouped, stacked, 100% normalized, reference lines, data labels, overlays. |
| **Horizontal Bar** | [`horizontal_bar_chart.rptsql`](horizontal_bar_chart.rptsql) | Horizontal orientation for long categorical labels, ranked bars. |
| **Line** | [`line_chart.rptsql`](line_chart.rptsql) | Time-series, multi-series, step lines, spline curves, target goal overlays. |
| **Scatter** | [`scatter_chart.rptsql`](scatter_chart.rptsql) | 2D correlation, category grouping, reference quadrants. |
| **Bubble** | [`bubble_chart.rptsql`](bubble_chart.rptsql) | 3D metric plots (X, Y, Size), size scaling, threshold color bands. |
| **Pie** | [`pie_chart.rptsql`](pie_chart.rptsql) | Categorical share, slice grouping, custom color palettes. |
| **Donut** | [`donut_chart.rptsql`](donut_chart.rptsql) | Hole-ratio sizing, center metrics, custom legends. |
| **Combo** | [`combo_chart.rptsql`](combo_chart.rptsql) | Dual-axis Bar + Line combinations, volume vs rate trends. |
| **Gauge** | [`gauge_chart.rptsql`](gauge_chart.rptsql) | Radial meter, target bands (Red/Yellow/Green), needle values. |
| **Boxplot** | [`boxplot_chart.rptsql`](boxplot_chart.rptsql) | Statistical distributions, median, quartiles, outlier points. |
| **Treemap** | [`treemap_chart.rptsql`](treemap_chart.rptsql) | Hierarchical rectangular tiling, size and color dimensions. |
| **Heatmap** | [`heatmap_chart.rptsql`](heatmap_chart.rptsql) | 2D density grids, color scales, matrix correlation. |
| **Funnel** | [`funnel_chart.rptsql`](funnel_chart.rptsql) | Linear stage attrition, conversion rates, bottleneck analysis. |
| **Waterfall** | [`waterfall_chart.rptsql`](waterfall_chart.rptsql) | Stepped delta variance, positive/negative subtotals, net totals. |
| **Radar** | [`radar_chart.rptsql`](radar_chart.rptsql) | Multi-variable spider evaluation, performance comparisons. |
| **Candlestick** | [`candlestick_chart.rptsql`](candlestick_chart.rptsql) | Financial OHLC (Open, High, Low, Close) pricing and volume. |
| **Sankey** | [`sankey_diagram.rptsql`](sankey_diagram.rptsql) | Directed flow diagrams, node alignment, multi-hop budget/flow paths. |
| **Gantt** | [`gantt_chart.rptsql`](gantt_chart.rptsql) | Timeline project schedules, start/end dates, milestone markers. |
| **Sunburst** | [`sunburst_radial.rptsql`](sunburst_radial.rptsql) | Concentric multi-level radial hierarchy navigation. |
| **Network** | [`network_graph.rptsql`](network_graph.rptsql) | Node-link relationship graph, directed edges, weightings. |
| **Trellis** | [`trellis_multiples.rptsql`](trellis_multiples.rptsql) | Small multiples repeating a chart across partition facets. |
| **Custom Layers** | [`grammar_of_graphics_custom.rptsql`](grammar_of_graphics_custom.rptsql) | Declarative Grammar of Graphics composite multi-layer charts. |

## Grids & Indicators

| Visual | Script | Key Features |
| :--- | :--- | :--- |
| **Table** | [`table_visual.rptsql`](table_visual.rptsql) | Formatted columns, inline sparklines, search, sorting, pagination. |
| **KPI Card** | [`kpi_card.rptsql`](kpi_card.rptsql) | Single metric callouts, subtitle comparison, variance status badges. |
| **Matrix** | [`matrix_grid.rptsql`](matrix_grid.rptsql) | Pivot table grid, multi-level row/column hierarchies, conditional cell colors. |

## Interactive Controls & Filters

| Control | Script | Description |
| :--- | :--- | :--- |
| **Slicer** | [`slicer_control.rptsql`](slicer_control.rptsql) | Dropdown and list selection filter binding to report parameters. |
| **MultiSelect** | [`multiselect_control.rptsql`](multiselect_control.rptsql) | Multi-tag selection filter. |
| **DatePicker** | [`datepicker_control.rptsql`](datepicker_control.rptsql) | Absolute calendar date picker. |
| **RelDatePicker** | [`reldatepicker_control.rptsql`](reldatepicker_control.rptsql) | Relative date offsets (Today, Last 7 Days, Month-to-Date). |
| **Slider** | [`slider_control.rptsql`](slider_control.rptsql) | Numeric range slider control. |
| **Search** | [`search_control.rptsql`](search_control.rptsql) | Free-text search input. |
| **Checkbox** | [`checkbox_control.rptsql`](checkbox_control.rptsql) | Boolean toggle input. |
| **TextBox** | [`textbox_control.rptsql`](textbox_control.rptsql) | Single-line text input parameter. |
| **NumberBox** | [`numberbox_control.rptsql`](numberbox_control.rptsql) | Numeric input spinner parameter. |
| **Interactions** | [`interactive_actions.rptsql`](interactive_actions.rptsql) | Cross-visual filtering, action triggers, staged apply buttons. |
| **Overlays** | [`chart_overlays.rptsql`](chart_overlays.rptsql) | Moving averages, target goal lines, statistical bands. |

## Presentation & Media

| Visual | Script | Description |
| :--- | :--- | :--- |
| **Text** | [`text_visual.rptsql`](text_visual.rptsql) | Formatted text blocks, titles, markdown notes. |
| **Image** | [`image_visual.rptsql`](image_visual.rptsql) | Image display from URL or local asset. |
| **Map** | [`map_visual.rptsql`](map_visual.rptsql) | Geographic point and region coordinates. |

## Running

Run any script with the CLI:

```bash
dotnet src/ETL-SQL.App/bin/Debug/net10.0/ETL-SQL.dll run samples/visual_gallery/bar_chart.rptsql
```

Or preview interactively in the browser:

```bash
dotnet src/ETL-SQL.App/bin/Debug/net10.0/ETL-SQL.dll serve samples/visual_gallery/bar_chart.rptsql
```
