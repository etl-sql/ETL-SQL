# Report-SQL

Report-SQL extends ETL-SQL with components for building interactive dashboards — datasets, visuals, pages, navigation, containers, buttons, and styles.

A report is a sequence of CREATE statements. The engine compiles them into a self-contained dashboard served via the Report Portal.

Key components:
  DATASET     — shared cached data source
  VISUAL      — a chart or control bound to a data source
  PAGE        — a grid layout containing visuals
  CONTAINER   — nested layout group within a page
  NAVIGATION  — menu or tab strip linking pages
  BUTTON      — interactive back / refresh / link button
  STYLE       — reusable formatting theme

```sql
-- Minimal two-visual report with a slicer
DECLARE @region VARCHAR INPUT = 'All';

CREATE DATASET #orders AS (
  SELECT region, product, SUM(amount) AS revenue
  FROM dbo.Orders
  WHERE @region = 'All' OR region = @region
  GROUP BY region, product
);

CREATE VISUAL RegionSlicer AS SLICER (
  SOURCE   = (SELECT DISTINCT region FROM dbo.Orders INTO #regions),
  MAPPINGS (VALUE = region),
  OPTIONS  (TITLE = 'Region'),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, value))
);

CREATE VISUAL SalesBar AS BAR (
  SOURCE   = #orders,
  MAPPINGS (X = product, Y = revenue)
);

CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'S / C',
  MAP ('S' = RegionSlicer, 'C' = SalesBar)
);
```

Use HELP REPORT <component> for details (e.g. HELP REPORT VISUAL, HELP REPORT PAGE).
Use HELP VISUAL <type> for chart-specific options (e.g. HELP VISUAL BAR).
