# Visual Analytics

High-impact analytical SQL query patterns paired directly with Report-SQL (`.rptsql`) visual dashboards.

Each sample answers a concrete business or data engineering question by staging inline data in `#temp` tables, executing an analytical query (window functions, running totals, cohort partitions, aging intervals), and rendering the result into interactive visuals.

## Sample Inventory

| Scenario | Script | Query Logic | Visuals |
| :--- | :--- | :--- | :--- |
| **Sales Conversion Funnel** | [`sales_funnel_conversion.rptsql`](sales_funnel_conversion.rptsql) | Multi-stage pipeline count aggregation and stage-over-stage conversion % calculation. | `FUNNEL`, `CARD`, `TABLE` |
| **Cohort Retention Matrix** | [`cohort_retention_heatmap.rptsql`](cohort_retention_heatmap.rptsql) | Monthly acquisition cohort lifecycle tracking and retention percentage decay calculation. | `HEATMAP`, `LINE`, `CARD` |
| **Pareto Principle (80/20 Rule)** | [`pareto_distribution_80_20.rptsql`](pareto_distribution_80_20.rptsql) | Cumulative running sum using window functions and catalog revenue share classification. | `COMBO` (Bar + Line), `CARD`, `TABLE` |
| **Moving Average Smoothing** | [`moving_average_trend.rptsql`](moving_average_trend.rptsql) | Rolling 7-day moving window average calculation smoothing daily weekend transaction dips. | `LINE` + `OVERLAYS`, `CARD`, `TABLE` |
| **Executive Performance Scorecard** | [`executive_kpi_scorecard.rptsql`](executive_kpi_scorecard.rptsql) | Multi-metric health rollup comparing actual vs target values across business units. | `GAUGE`, `CARD`, `HBAR` |
| **Inventory Aging Buckets** | [`inventory_aging_buckets.rptsql`](inventory_aging_buckets.rptsql) | Conditional `CASE` aging interval classification (`<30d`, `31-60d`, `61-90d`, `90d+`). | `BAR` (Stacked), `CARD`, `TABLE` |
| **Budget Variance Waterfall** | [`budget_variance_waterfall.rptsql`](budget_variance_waterfall.rptsql) | Stepped P&L financial reconciliation from Gross Revenue down to Net Operating Profit. | `WATERFALL`, `CARD`, `TABLE` |

## Running

Run any script with the CLI:

```bash
dotnet src/ETL-SQL.App/bin/Debug/net10.0/ETL-SQL.dll run samples/visual_analytics/sales_funnel_conversion.rptsql
```

Or preview interactively in the browser:

```bash
dotnet src/ETL-SQL.App/bin/Debug/net10.0/ETL-SQL.dll serve samples/visual_analytics/sales_funnel_conversion.rptsql
```
