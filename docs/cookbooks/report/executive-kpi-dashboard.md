# Executive KPI Dashboard

**Pattern**: KPI cards, regional bar chart, slicer-filtered detail table. The go-to starting point for any executive summary report.

**Demonstrates**: `CARD` goal/progress/delta options, `BAR` `AXIS_SORT`, `TABLE`, `SLICER`, `DECLARE @x ... INPUT` parameters, `CREATE DATASET`, `FORMAT`, `FORMATTING`, conditional formatting, multi-slot layout.

```sql
SET REPORT TITLE       = 'Executive Sales Dashboard';
DECLARE @region VARCHAR INPUT = 'All';
SET REPORT DESCRIPTION = 'Top-level KPIs with regional breakdown and filterable order detail.';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 'North' AS region, 'Widget A' AS product, 120 AS units, 14400 AS revenue INTO #raw
UNION ALL SELECT 'Jan', 'South', 'Widget A', 95,  11400
UNION ALL SELECT 'Jan', 'East',  'Widget B', 60,   9000
UNION ALL SELECT 'Feb', 'North', 'Widget A', 140, 16800
UNION ALL SELECT 'Feb', 'South', 'Widget B', 110, 16500
UNION ALL SELECT 'Feb', 'East',  'Widget A', 75,   9000
UNION ALL SELECT 'Mar', 'North', 'Widget B', 160, 24000
UNION ALL SELECT 'Mar', 'South', 'Widget A', 130, 15600
UNION ALL SELECT 'Mar', 'East',  'Widget B', 90,  13500
UNION ALL SELECT 'Apr', 'North', 'Widget A', 180, 21600
UNION ALL SELECT 'Apr', 'South', 'Widget B', 145, 21750
UNION ALL SELECT 'Apr', 'East',  'Widget A', 105, 12600;

-- Shared pre-aggregated dataset (swap FLATFILE source here when using real data)
CREATE DATASET &sales
  TTL = '1h'
  COMPRESS = AS AS(SELECT month, region, product,
             SUM(units)   AS units,
             SUM(revenue) AS revenue
      FROM #raw
      GROUP BY month, region, product);

-- ── Filter control ────────────────────────────────────────────────────────
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT region FROM &sales ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── KPI cards ─────────────────────────────────────────────────────────────
CREATE VISUAL TotalRevenue AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val, 125000 AS target, 110000 AS prior_val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val, GOAL = target, DELTA = prior_val),
  OPTIONS  (
    FORMAT               = 'C0',
    ABBREVIATE           = ON,
    SHOW_GOAL            = ON,
    SHOW_PERCENT_OF_GOAL = ON,
    SHOW_PROGRESS        = ON,
    PROGRESS_STYLE       = BAR,
    ICON_SET             = CHECKS,
    DELTA_LABEL          = 'vs target baseline'
  )
);

CREATE VISUAL TotalUnits AS CARD (
  SOURCE   = (SELECT SUM(units) AS val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Units Sold',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'N0')
);

CREATE VISUAL AvgRevenue AS CARD (
  SOURCE   = (SELECT AVG(revenue) AS val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Avg Monthly Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

-- ── Chart ─────────────────────────────────────────────────────────────────
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue FROM &sales
              WHERE @region = 'All' OR region = @region
              GROUP BY region
              ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS  (
    AXIS_SORT = VALUE_DESC,
    X_AXIS (LABEL = 'Region'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- ── Detail table with conditional formatting ──────────────────────────────
CREATE VISUAL SalesDetail AS TABLE (
  SOURCE = (SELECT month, region, product, units, revenue FROM &sales
            WHERE @region = 'All' OR region = @region
            ORDER BY month, region),
  FORMATTING (
    revenue >= 20000 THEN '#d4edda',
    revenue < 10000  THEN '#f8d7da'
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE CONTAINER KpiRow AS BOX (
  LAYOUT (
    STRUCTURE = 'A B C',
    MAP (
      'A' = TotalRevenue,
      'B' = TotalUnits,
      'C' = AvgRevenue
    )
  )
);

CREATE PAGE Main AS DASHBOARD (
  STRUCTURE = 'A A B / C C C / D D D',
  MAP (
    'A' = KpiRow,
    'B' = RegionFilter,
    'C' = RevenueByRegion,
    'D' = SalesDetail
  )
);
```
