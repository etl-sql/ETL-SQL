# Cookbooks

Cookbooks contain complete, runnable examples. Prefer a cookbook recipe when a reader needs a
production-shaped pattern rather than a small syntax snippet.

## Run These Now (No Database Needed)

These recipes run as-is in VS Code — paste into a file and press **F5** (`.etlsql`) or
**F1 → ETL-SQL: Preview Report** (`.rptsql`). They use inline sample data or the built-in
`MOCKDB()` connector.

| Recipe | What you'll see |
| :--- | :--- |
| [Executive KPI Dashboard](report/executive-kpi-dashboard.md) | KPI cards, regional bars, and a slicer-filtered table |
| [Choropleth Map Charts](report/choropleth-map-charts.md) | US states and Minnesota counties shaded by value, plus a city point map |
| [Financial Waterfall, Funnel & Gauge](report/waterfall-funnel-gauge.md) | Cash-flow waterfall, sales funnel, and a target gauge |
| [Flow and Hierarchy Analysis](report/flow-and-hierarchy-analysis.md) | Sankey flow and sunburst hierarchy |
| [Sales Trend with Forecasting](report/sales-trend-with-forecasting.md) | Trend line with goal, rolling average, and linear forecast |
| [Data Quality Gate](etl/data-quality-gate.md) | In-stream `EXPECT` rules quarantining bad rows |
| [Dead-Letter Queue](etl/dead-letter-queue.md) | Bad rows routed aside with a reason while good rows load |
| [Fuzzy Entity Resolution](etl/fuzzy-entity-resolution.md) | Messy vendor names matched to canonical customers |
| [Time Series Gap Filling](etl/time-series-gap-filling.md) | Missing days filled with zero, per region |
| [Incremental Load with High-Water Mark](etl/incremental-load-with-high-water-mark.md) | Only rows newer than the watermark are loaded, then the watermark advances |
| [Staged Ingestion](etl/staged-ingestion.md) | Stage, cleanse, and `MERGE` — updates and inserts in one statement |
| [Full Refresh](etl/full-refresh.md) | Truncate-and-reload guarded against an empty source |
| [Financial Reporting (PIVOT)](etl/financial-reporting-pivot.md) | Revenue rotated into one column per region |

Every [report recipe](report/README.md) uses inline sample data, so all of them preview as-is.

## Recipe Collections

- [ETL Recipes](etl/README.md) — extract, stage, validate, transform, merge, cleanup, notify, and govern data movement. 30 recipes.
- [Report Recipes](report/README.md) — report authoring, datasets, visuals, filters, export, and portal publishing patterns. 17 recipes.

Each recipe is its own file, so it can be linked to directly, revised without touching its
neighbours, and found by filename.

## Recipe Standard

Every recipe should include:

- Goal and scenario.
- Required connectors and assumptions.
- Complete script.
- Validation step.
- Cleanup or rollback guidance.
- Operational notes for scheduling, secrets, lineage, and WHAT_IF behavior where relevant.

Use [Cookbook Recipe Template](../templates/cookbook-recipe-template.md) for new recipes. Add the
new file to the collection's `README.md` — that index is the only place a recipe is announced, so a
recipe missing from it is invisible.
