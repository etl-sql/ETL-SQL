# Report-SQL Scripting Guide
<!-- CreateTemplateStatement -->
<!-- CreateThemeStatement -->
<!-- SetTemplatePathStatement -->

Report-SQL extends ETL-SQL with native statements for building interactive dashboards, operational reports, and printable layouts using plain text scripts: `SET REPORT TITLE`, `CREATE DATASET`, `CREATE VISUAL`, `CREATE PAGE`, `CREATE CONTAINER`, `CREATE NAVIGATION`, `CREATE BUTTON`, and `CREATE STYLE`.

## Architecture Overview

```
┌─────────────────────┐    build / serve      ┌─────────────────────┐
│  (report script)    │                       │  (etl-sql-report)   │
│ your_report.rptsql  │ ──────────────────▶   etl-sql-report CLI   │
└─────────────────────┘                       └──────────┬──────────┘
                                                         │ evaluates script
                                                         ▼
                                              ┌─────────────────────┐
                                              │  ETL-SQL Engine     │
                                              │  (Evaluator)        │
                                              └──────────┬──────────┘
                                                         │ builds ReportManifest
                                                         ▼
                                      ┌──────────────────────────────┐
                                      │       ManifestBuilder        │
                                      │  ┌──────────────────────── ┐ │
                                      │  │ VisualManifest (x N)    │ │
                                      │  │ PageManifest   (x N)    │ │
                                      │  │ DatasetManifest (x N)   │ │
                                      │  └─────────────────────────┘ │
                                      └──────────┬───────────────────┘
                                                 │
                              ┌──────────────────┴──────────────────┐
                              │                                     │
                              ▼                                     ▼
                  ┌───────────────────────┐           ┌──────────────────────┐
                  │  MarkdownRenderer     │           │  ReportPlayer        │
                  │  → .report.md         │           │  (ASP.NET Kestrel)   │
                  │  → .etlsnap           │           │  http://localhost:5200│
                  └───────────────────────┘           └──────────────────────┘
```

A `.rptsql` file is a standard ETL-SQL script containing Report-SQL layout declarations. After evaluation, the engine builds a `ReportManifest` and creates a snapshot package (`.etlsnap`) containing layout definitions and high-performance Arrow IPC files for browser rendering.

`etl-sql-report offline <script>` turns that package into a single self-contained HTML file for
readers who cannot reach a server. Pages, bookmarks, and detail popovers replay from the captured
manifest; the figures are frozen at capture time and the page says so when a bookmark is applied.
See [Report CLI](../../reference/visuals-reporting/report-cli.md#etl-sql-report-offline) for what
does and does not work without a server.

## The Three-Tier Logic Model

To ensure fast interactive filtering when users adjust slicers or date pickers, author scripts following the three-tier logic separation:

| Tier | Layer | Purpose | Execution Timing | Best Practices |
| :--- | :--- | :--- | :--- | :--- |
| **Tier 1** | **Ingestion** | Connecting to remote databases, flat files, or APIs. | Initial Build / Scheduled Refresh | Use `CREATE CONNECTION` and parameterized remote queries. |
| **Tier 2** | **Preparation** | Heavy data transformations, joins, and aggregations into `#temp` tables. | Initial Build / Scheduled Refresh | Stage complete, wide datasets into `#temp` tables. Avoid slicer parameters in Tier 2. |
| **Tier 3** | **Presentation** | Interactive filtering, slicing, and visual rendering. | **User Interaction** | Bind visual `SOURCE` queries to `#temp` tables filtering on `@parameter` variables. |

## Minimal Report Lifecycle Example

```sql
-- 1. Report Metadata
SET REPORT TITLE = 'Executive Sales Dashboard';

-- 2. Tier 2 Data Preparation
SELECT Region, Category, SUM(Amount) AS TotalSales, COUNT(*) AS OrderCount
INTO #sales_summary
FROM #raw_orders
GROUP BY Region, Category;

-- 3. Visual Declarations
CREATE VISUAL RegionalSalesChart AS BAR (
  SOURCE = (SELECT Region, TotalSales FROM #sales_summary),
  MAPPINGS (X = Region, Y = TotalSales),
  OPTIONS (TITLE = 'Sales by Region')
);

CREATE VISUAL OrdersTable AS TABLE (
  SOURCE = (SELECT * FROM #sales_summary),
  OPTIONS (PAGE_SIZE = 10, SORTABLE = TRUE)
);

-- 4. Page Layout
CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A B',
  MAP (
    'A' = RegionalSalesChart,
    'B' = OrdersTable
  )
);
```

## Focused Reporting Guides

- [Authoring Dashboards](../reporting/authoring-dashboards.md) — Grid layout composition, visual positioning, and responsive design.
- [Report Parameters & Filters](../reporting/report-parameters-and-filters.md) — Interactive dropdowns, search boxes, and date pickers.
- [Cascading Slicers](../reporting/cascading-slicers.md) — Configuring multi-level dependent filter hierarchies.
- [Row-Level Security (RLS)](../reporting/report-row-level-security.md) — User and group-based data partition enforcement.
- [Paginated & Print-Ready Reports](../reporting/paginated-and-print-reports.md) — Multi-page layout, headers, footers, and PDF rendering.
- [Micro-Charts & KPI Cards](../reporting/micro-charts-and-kpis.md) — Sparklines, bullet graphs, and KPI summary displays.
- [Custom Theming & Branding](../reporting/custom-theming-and-branding.md) — Color palettes, typography, and organization themes.
- [Report Badges & Trust](../reporting/report-badges-and-trust.md) — Freshness indicators and data certification badges.

## Reference Documentation

- [Visual Types Reference](../../reference/visuals-reporting/visuals/README.md) — Full catalog of 38 visual types including BAR, LINE, PIE, GAUGE, SANKEY, and CHART.
- [Report Statements Reference](../../reference/visuals-reporting/report/README.md) — Statement reference for CREATE VISUAL, CREATE PAGE, CREATE DATASET, and CREATE CONTAINER.
- [Report-SQL CLI Reference](../../reference/visuals-reporting/report-cli.md) — `etl-sql-report` build, test, and serve CLI options.
