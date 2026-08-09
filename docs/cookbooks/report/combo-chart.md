# Combo Chart: Revenue + Volume

**Pattern**: Revenue bars and unit-volume line on the same axes — the classic dual-metric chart for spotting when revenue and volume diverge.

**Demonstrates**: `COMBO`, `SERIES (BAR col, LINE col)`, `STACKED = ON` on a separate grouped bar example.

```sql
SET REPORT TITLE = 'Revenue & Volume Combined';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 52000 AS revenue, 420 AS units INTO #metrics
UNION ALL SELECT 'Feb', 47000, 385
UNION ALL SELECT 'Mar', 61000, 490
UNION ALL SELECT 'Apr', 58000, 465
UNION ALL SELECT 'May', 67000, 535
UNION ALL SELECT 'Jun', 72000, 580
UNION ALL SELECT 'Jul', 69000, 550
UNION ALL SELECT 'Aug', 78000, 625
UNION ALL SELECT 'Sep', 74000, 590
UNION ALL SELECT 'Oct', 83000, 665
UNION ALL SELECT 'Nov', 91000, 730
UNION ALL SELECT 'Dec', 88000, 705;

-- ── Combo: bars for revenue, line for units ────────────────────────────────
CREATE VISUAL RevUnitsCombo AS COMBO (
  SOURCE   = (SELECT month, revenue, units FROM #metrics ORDER BY month),
  TITLE    = 'Revenue ($) vs Units Sold',
  MAPPINGS (X = month),
  SERIES   (BAR revenue, LINE units),
  OPTIONS  (
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Value'),
    LEGEND_POSITION = BOTTOM
  )
);

-- ── Stacked bar variant: revenue by product per month ────────────────────
SELECT 'Jan' AS month, 'Widget A' AS product, 31000 AS revenue INTO #by_product
UNION ALL SELECT 'Jan', 'Widget B', 21000
UNION ALL SELECT 'Feb', 'Widget A', 28000
UNION ALL SELECT 'Feb', 'Widget B', 19000
UNION ALL SELECT 'Mar', 'Widget A', 37000
UNION ALL SELECT 'Mar', 'Widget B', 24000
UNION ALL SELECT 'Apr', 'Widget A', 35000
UNION ALL SELECT 'Apr', 'Widget B', 23000;

CREATE VISUAL StackedRevenue AS BAR (
  SOURCE   = (SELECT month, revenue, product FROM #by_product ORDER BY month),
  TITLE    = 'Revenue by Product (Stacked)',
  MAPPINGS (X = month, Y = revenue, SERIES = product),
  OPTIONS  (
    STACKED = ON,
    X_AXIS  (LABEL = 'Month'),
    Y_AXIS  (LABEL = 'Revenue ($)', MIN = 0),
    LEGEND_POSITION = TOP,
    COLORS ('Widget A' = '#0d6efd', 'Widget B' = '#fd7e14')
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Combo AS DASHBOARD (
  STRUCTURE = 'A A / B B',
  MAP (
    'A' = RevUnitsCombo,
    'B' = StackedRevenue
  )
);
```
