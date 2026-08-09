# Report Recipes

Copy-paste-ready dashboard recipes for ETL-SQL. Every recipe is **self-contained** — inline data is
included, so each one runs immediately and can be adapted to a real source.

## Recipes

- [Executive KPI Dashboard](executive-kpi-dashboard.md) — KPI cards, regional bar chart, slicer-filtered detail table. The go-to starting point for any executive summary report.
- [Sales Trend with Forecasting](sales-trend-with-forecasting.md) — A line chart over time with goal line, rolling average, and linear trend overlaid. Add a date-range picker to narrow the window.
- [Year-over-Year Comparison](year-over-year-comparison.md) — Stack current year and prior year on the same chart. A donut shows the full-year share breakdown by product alongside.
- [Master-Detail Drill-Down](master-detail-drill-down.md) — Click a region bar to filter a branch-level detail chart. A reset slicer returns to "all".
- [Cross-Page Filtering with Navigation](cross-page-filtering-with-navigation.md) — A slicer on one page sets a parameter another page also reads, so the filter persists across navigation.
- [Inventory Heatmap & Low-Stock Alerts](inventory-heatmap.md) — A warehouse bin heatmap shows stock intensity at a glance, with items below reorder point highlighted below it.
- [Financial Waterfall, Funnel & Gauge](waterfall-funnel-gauge.md) — A cash-flow waterfall, a sales conversion funnel, and a KPI gauge showing actuals against target.
- [Combo Chart: Revenue + Volume](combo-chart.md) — Revenue bars and unit-volume line on the same axes, for spotting when revenue and volume diverge.
- [Multi-Select + Search Filter Table](multi-select-filter-table.md) — A MULTISELECT narrows a TABLE to chosen categories; a SEARCH box further filters by text.
- [Themed Dashboard with CREATE STYLE](themed-dashboard.md) — Define a shared visual identity once, then apply it across all visuals, pages, and containers.
- [Choropleth Map Charts](choropleth-map-charts.md) — Color-scaled geographic regions driven by a data column. Six bundled maps need no external files.
- [Flow and Hierarchy Analysis](flow-and-hierarchy-analysis.md) — `SANKEY` for weighted flow, `SUNBURST` for hierarchical contribution, and `NETWORK` for connections between entities.

## Tips

**Use `CREATE DATASET` for shared expensive queries** — if multiple visuals query the same data, compute it once as `CREATE DATASET &name` and reference it everywhere. Add `COMPRESS = ON` for large result sets.

**The "All or filtered" pattern** — use `WHERE @param = 'All' OR col = @param` so visuals show full data before the user makes a selection. Pair with a SLICER whose option list includes an `'All'` row.

**Slicer and MULTISELECT require a SOURCE** — the source rows become the dropdown options. Include an `'All'` row via `UNION ALL SELECT 'All' ...` if you want a reset option.

**Page parameters are initialized at load time** — a parameter's `DECLARE @x ... INPUT = <default>` value applies immediately, so every visual has valid data on first render even before any filter interaction.

**TITLE on visuals vs OPTIONS** — the top-level `TITLE = '...'` clause is preferred over `OPTIONS (TITLE = '...')`. Both work but the clause form is cleaner.

See [Report SQL](../../guides/feature-guides/report-sql.md) for the authoring model and
[Visuals Reference](../../reference/visuals-reporting/visuals/README.md) for every visual type and
its options.
