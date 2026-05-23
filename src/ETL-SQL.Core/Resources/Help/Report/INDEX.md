# Report-SQL

Report-SQL extends ETL-SQL with components for building interactive dashboards — datasets, visuals, pages, navigation, containers, buttons, and styles.

A report is a sequence of CREATE statements. The engine compiles them into a self-contained dashboard served via the Report Portal.

Key components:
  DATASET     — shared cached data source
  VISUAL      — a chart or control bound to a data source
  PAGE        — a grid layout containing visuals, containers, and buttons
  CONTAINER   — nested layout group within a page
  NAVIGATION  — menu or tab strip linking pages
  BUTTON      — interactive back / refresh / link button
  STYLE       — reusable formatting theme

Canonical layout rules:
  STRUCTURE uses CSS grid-template-areas text such as 'A A / B C'
  MAP slots are quoted strings such as MAP ('A' = SalesBar)
  Buttons use CREATE BUTTON ButtonName AS (...) and can be placed in MAP slots

```sql
-- Minimal two-visual report with a slicer
DECLARE @region VARCHAR INPUT = 'All';

SELECT region, product, amount
INTO #orders_raw
FROM dbo.Orders;

CREATE DATASET &orders AS (
  SELECT region, product, SUM(amount) AS revenue
  FROM #orders_raw
  GROUP BY region, product
);

CREATE VISUAL RegionSlicer AS SLICER (
  SOURCE   = (SELECT DISTINCT region FROM #orders_raw),
  MAPPINGS (VALUE = region),
  TITLE    = 'Region',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, value))
);

CREATE VISUAL SalesBar AS BAR (
  SOURCE   = (SELECT product, SUM(revenue) AS revenue
              FROM &orders
              WHERE @region = 'All' OR region = @region
              GROUP BY product),
  MAPPINGS (X = product, Y = revenue)
);

CREATE PAGE Main AS DASHBOARD (
  STRUCTURE = 'S / C',
  MAP ('S' = RegionSlicer, 'C' = SalesBar)
);
```

Use HELP REPORT <component> for details (e.g. HELP REPORT VISUAL, HELP REPORT PAGE).
Use HELP VISUAL <type> for chart-specific options (e.g. HELP VISUAL BAR).
