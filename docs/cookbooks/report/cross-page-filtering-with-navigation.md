# Cross-Page Filtering with Navigation

**Pattern**: A slicer on Page 1 sets a parameter that Page 2 also reads. Navigation tabs let users switch pages while the filter persists. Both pages share the same `@region` parameter.

**Demonstrates**: `CREATE NAVIGATION`, multi-page `DECLARE @x ... INPUT` parameters, cross-page parameter sharing, `ORIENTATION = HORIZONTAL`.

```sql
SET REPORT TITLE = 'Multi-Page Sales Dashboard';
DECLARE @region VARCHAR INPUT = 'All';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 'North' AS region, 52000 AS revenue, 420 AS units INTO #sales
UNION ALL SELECT 'Jan', 'South', 41000, 310
UNION ALL SELECT 'Jan', 'East',  38000, 290
UNION ALL SELECT 'Feb', 'North', 58000, 470
UNION ALL SELECT 'Feb', 'South', 47000, 355
UNION ALL SELECT 'Feb', 'East',  43000, 320
UNION ALL SELECT 'Mar', 'North', 61000, 490
UNION ALL SELECT 'Mar', 'South', 53000, 400
UNION ALL SELECT 'Mar', 'East',  49000, 370
UNION ALL SELECT 'Apr', 'North', 67000, 535
UNION ALL SELECT 'Apr', 'South', 58000, 440
UNION ALL SELECT 'Apr', 'East',  54000, 405;

-- ── Shared filter — lives on Page 1, persists across pages ───────────────
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT 'All' AS region
              UNION ALL SELECT DISTINCT region FROM #sales ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── Page 1: Overview ──────────────────────────────────────────────────────
CREATE VISUAL TotalRev AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val FROM #sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

CREATE VISUAL RevByRegion AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY region ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS  (Y_AXIS (LABEL = 'Revenue ($)', MIN = 0))
);

-- ── Page 2: Monthly Trends (reads same @region parameter) ────────────────
CREATE VISUAL RevTrend AS LINE (
  SOURCE   = (SELECT month, SUM(revenue) AS revenue FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY month ORDER BY month),
  TITLE    = 'Revenue Trend',
  MAPPINGS (X = month, Y = revenue),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

CREATE VISUAL UnitTrend AS LINE (
  SOURCE   = (SELECT month, SUM(units) AS units FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY month ORDER BY month),
  TITLE    = 'Units Trend',
  MAPPINGS (X = month, Y = units),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Units', MIN = 0)
  )
);

-- ── Pages ─────────────────────────────────────────────────────────────────
CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A B / C C',
  MAP (
    'A' = TotalRev,
    'B' = RegionFilter,
    'C' = RevByRegion
  )
);

CREATE PAGE Trends AS DASHBOARD (
  STRUCTURE = 'A B',
  MAP (
    'A' = RevTrend,
    'B' = UnitTrend
  )
);

-- ── Navigation ────────────────────────────────────────────────────────────
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT     = Overview,
  PAGES (Overview, Trends)
);
```

> **How cross-page filtering works**: When the user changes `RegionFilter` on the Overview page, `SET_PARAMETER` sets `@region` in the dashboard session. The Trends page declares the same `@region` parameter — when the user navigates there, its visuals re-query using the already-set value.
