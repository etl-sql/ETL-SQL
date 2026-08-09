# Master-Detail Drill-Down

**Pattern**: Click a region bar to filter a branch-level detail chart. The detail chart title updates to show what's selected. Add a reset slicer to return to "all".

**Demonstrates**: `DRILL_DOWN`, `ON_CLICK`, `SET_PARAMETER`, `@param` in inline SELECT, reset via SLICER.

```sql
SET REPORT TITLE = 'Regional Drill-Down';
DECLARE @region VARCHAR INPUT = 'All';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'North' AS region, 'Albany'   AS branch, 28000 AS revenue INTO #branches
UNION ALL SELECT 'North', 'Buffalo',    21000
UNION ALL SELECT 'North', 'Syracuse',   18000
UNION ALL SELECT 'South', 'Atlanta',    34000
UNION ALL SELECT 'South', 'Miami',      29000
UNION ALL SELECT 'South', 'Tampa',      22000
UNION ALL SELECT 'East',  'Boston',     31000
UNION ALL SELECT 'East',  'New York',   52000
UNION ALL SELECT 'East',  'Philly',     24000;

-- ── Region summary — clicking a bar fires DRILL_DOWN ──────────────────────
CREATE VISUAL RegionChart AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue
              FROM #branches GROUP BY region ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region  (click to drill down)',
  MAPPINGS (X = region, Y = revenue),
  ACTIONS  (ON_CLICK = DRILL_DOWN(Target = BranchChart, Key = region)),
  OPTIONS  (
    X_AXIS (LABEL = 'Region'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0),
    COLORS (
      'North' = '#4e79a7',
      'South' = '#f28e2b',
      'East'  = '#59a14f'
    )
  )
);

-- ── Branch detail — filtered by @region set via DRILL_DOWN ───────────────
CREATE VISUAL BranchChart AS BAR (
  SOURCE   = (SELECT branch, revenue
              FROM #branches
              WHERE @region = 'All' OR region = @region
              ORDER BY revenue DESC),
  TITLE    = 'Branch Detail',
  MAPPINGS (X = branch, Y = revenue),
  OPTIONS  (
    X_AXIS (LABEL = 'Branch'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- ── Reset control ─────────────────────────────────────────────────────────
CREATE VISUAL RegionReset AS SLICER (
  SOURCE   = (SELECT 'All' AS region
              UNION ALL SELECT DISTINCT region FROM #branches ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE DrillDown AS DASHBOARD (
  STRUCTURE = 'A A B / C C C',
  MAP (
    'A' = RegionChart,
    'B' = RegionReset,
    'C' = BranchChart
  )
);
```
