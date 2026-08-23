# Authoring Dashboards in Report-SQL

Report-SQL extends ETL-SQL with declarative statements for assembling interactive data dashboards: `CREATE DATASET`, `CREATE VISUAL`, `CREATE CONTAINER`, `CREATE PAGE`, `CREATE BUTTON`, and `CREATE NAVIGATION`. 

Dashboards run identically across the **CLI**, the **Report Player**, the **VS Code Extension**, and the **Web Portal**.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS). Dashboards are source-controlled `.rptsql` files.

## The Three-Tier Logic Model

To ensure dashboards load quickly and remain responsive to user interactions, Report-SQL uses a three-tier architecture that separates heavy data ingestion from fast interactive queries:

```
┌────────────────────────────────────────────────────────────────────────┐
│ Tier 1: Ingestion (Build / Refresh Time)                               │
│ - CREATE CONNECTION, RUN SCRIPT                                        │
│ - Pulls remote data once per refresh                                   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│ Tier 2: Preparation (Build / Refresh Time)                             │
│ - SELECT ... INTO #staged / CREATE DATASET &summary AS (...)           │
│ - Heavy joins, aggregations, data cleansing                            │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│ Tier 3: Presentation (Interactive Runtime)                             │
│ - CREATE VISUAL ... SOURCE = (SELECT ... WHERE @filter = val)          │
│ - Re-evaluated instantly when user moves a slicer or datepicker        │
└────────────────────────────────────────────────────────────────────────┘
```

> [!IMPORTANT]
> **Avoid the "Tier 2 Trap"**: Never put interactive `@parameters` inside a `SELECT ... INTO #temp` or base `CREATE DATASET` query. Base tables are evaluated **once** during report build. Apply interactive parameter filters in the `SOURCE = (SELECT ... WHERE ...)` clause of individual visuals.

---

## Example 1: Basic Single-Page Dashboard

This example extracts sales data into an engine `#temp` table, builds a bar chart and summary card, and places them onto a single dashboard page using CSS grid areas.

```sql
SET REPORT TITLE = 'Sales Overview';
SET REPORT DESCRIPTION = 'Executive revenue summary by region';

-- Tier 1 & 2: Ingestion & Preparation
CREATE CONNECTION src AS FLATFILE('data/sales.csv');

SELECT region, SUM(revenue) AS revenue, SUM(units) AS units
INTO #sales_summary
FROM src
GROUP BY region;

-- Tier 3: Visuals
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE   = #sales_summary,
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS  (GRID = ON)
);

CREATE VISUAL TotalRevenue AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS total, 'Total Revenue' AS lbl FROM #sales_summary),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = total, LABEL = lbl),
  OPTIONS  (FORMAT = 'C0')
);

-- Page Layout (Structure uses CSS grid-template-areas)
CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP (
      'A' = TotalRevenue,
      'B' = RevenueByRegion
    ),
    GAP = '16px'
  )
);
```

---

## Example 2: Interactive Multi-Visual Dashboard with Slicer

This dashboard defines an interactive `@region` filter that updates a KPI card and a bar chart in real time without re-running data extraction.

```sql
SET REPORT TITLE = 'Regional Sales Explorer';

DECLARE @region VARCHAR INPUT = 'All';

CREATE CONNECTION db AS MOCKDB();

SELECT OrderId, Region, Category, Amount
INTO #orders
FROM db.Orders;

-- Slicer Filter Visual
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT Region FROM #orders ORDER BY Region),
  MAPPINGS (VALUE = Region),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, Region))
);

-- KPI Card filtering dynamically on @region
CREATE VISUAL KpiRevenue AS CARD (
  SOURCE   = (SELECT SUM(Amount) AS Total, 'Filtered Revenue' AS Label
              FROM #orders
              WHERE @region = 'All' OR Region = @region),
  MAPPINGS (VALUE = Total, LABEL = Label),
  OPTIONS  (FORMAT = 'C0')
);

-- Bar Chart filtering dynamically on @region
CREATE VISUAL SalesByCategory AS BAR (
  SOURCE   = (SELECT Category, SUM(Amount) AS Total
              FROM #orders
              WHERE @region = 'All' OR Region = @region
              GROUP BY Category),
  TITLE    = 'Sales by Category',
  MAPPINGS (X = Category, Y = Total)
);

-- 2-Row Page Layout: Slicer on top, Card and Chart side-by-side below
CREATE PAGE ExplorerPage AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A A / B C',
    MAP (
      'A' = RegionFilter,
      'B' = KpiRevenue,
      'C' = SalesByCategory
    ),
    GAP = '12px'
  )
);
```

---

## Example 3: Multi-Page Dashboard with Tabs & Containers

Group visuals into logical sections with `CREATE CONTAINER` and provide multi-page navigation using `CREATE NAVIGATION`.

```sql
SET REPORT TITLE = 'Operations Command Center';

CREATE CONNECTION demo AS MOCKDB();

SELECT MetricName, MetricValue, Department
INTO #kpis
FROM demo.Metrics;

CREATE VISUAL DeptBar AS BAR (
  SOURCE   = #kpis,
  MAPPINGS (X = Department, Y = MetricValue)
);

CREATE VISUAL DetailTable AS TABLE (
  SOURCE   = #kpis,
  SUMMARY  (GRAND_TOTAL = ON, SUM(MetricValue) AS 'Total')
);

-- Container grouping
CREATE CONTAINER ChartBox AS BOX (
  TITLE  = 'Department Metrics',
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = DeptBar)
  )
);

-- Pages
CREATE PAGE SummaryPage AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = ChartBox)
  )
);

CREATE PAGE TablePage AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = DetailTable)
  )
);

-- Multi-page Tab Navigation (Defined after pages)
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT     = SummaryPage,
  PAGES       (SummaryPage, TablePage)
);
```

---

## Common Pitfalls

- **Placing parameters in `#temp` tables**: If a slicer parameter `@p` is used in `SELECT ... INTO #temp WHERE col = @p`, the query runs only once on initial load. Slicer changes will not update the `#temp` table.
- **Unquoted MAP slots**: In `MAP ('A' = VisualName)`, the grid slot identifiers (`'A'`, `'B'`) must be single-quoted strings.
- **Navigation layer order**: Define `CREATE NAVIGATION` *after* all referenced `CREATE PAGE` statements in your script to satisfy the LayerOrder linter rule.

---

## Related Topics

- [Report Parameters and Filters](report-parameters-and-filters.md) — Connect inputs, dates, and slicers.
- [Cascading Slicers](cascading-slicers.md) — Configure dependent parent-child filter hierarchies.
- [Visual Report Builder Guide](../tooling/report-builder.md) — Design layouts visually on a 12-column grid.
- [Report-SQL Reference](../../reference/visuals-reporting/README.md) — Complete visual and page syntax index.
