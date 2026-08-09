# Sales Trend with Forecasting

**Pattern**: A line chart over time with goal line, rolling average, and linear trend overlaid. Add a date-range picker to narrow the window.

**Demonstrates**: `LINE`, `OVERLAYS`, `GOAL`, `AVERAGE`, `MOVING_AVG`, `LINEAR`, `SMOOTH`, `DATEPICKER`, typed `DECLARE @x DATE INPUT` parameters.

```sql
SET REPORT TITLE = 'Monthly Sales Trend & Forecast';
DECLARE @start DATE INPUT = '2025-01-01';
DECLARE @end   DATE INPUT = '2025-12-31';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT '2025-01-01' AS sale_date, 42000 AS revenue INTO #monthly
UNION ALL SELECT '2025-02-01',  38000
UNION ALL SELECT '2025-03-01',  51000
UNION ALL SELECT '2025-04-01',  47000
UNION ALL SELECT '2025-05-01',  55000
UNION ALL SELECT '2025-06-01',  62000
UNION ALL SELECT '2025-07-01',  58000
UNION ALL SELECT '2025-08-01',  71000
UNION ALL SELECT '2025-09-01',  66000
UNION ALL SELECT '2025-10-01',  74000
UNION ALL SELECT '2025-11-01',  80000
UNION ALL SELECT '2025-12-01',  88000;

-- ── Date filter controls ──────────────────────────────────────────────────
CREATE VISUAL StartPicker AS DATEPICKER (
  TITLE   = 'From',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start, value))
);

CREATE VISUAL EndPicker AS DATEPICKER (
  TITLE   = 'To',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@end, value))
);

-- ── Trend chart with overlays ─────────────────────────────────────────────
CREATE VISUAL RevenueTrend AS LINE (
  SOURCE   = (SELECT sale_date AS month, revenue
              FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end
              ORDER BY sale_date),
  TITLE    = 'Monthly Revenue with Trend',
  MAPPINGS (X = month, Y = revenue),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0),
    LEGEND_POSITION = BOTTOM
  ),
  OVERLAYS (
    GOAL(75000)   AS DASHED WITH (COLOR = '#e74c3c', LABEL = 'Annual Target / Month'),
    AVERAGE       AS DOTTED WITH (COLOR = '#3498db', LABEL = 'Period Average'),
    MOVING_AVG(3) AS SOLID  WITH (COLOR = '#2ecc71', LABEL = '3-Month Moving Avg'),
    LINEAR        AS DASHED WITH (COLOR = '#9b59b6', LABEL = 'Linear Trend')
  )
);

-- ── Summary card ──────────────────────────────────────────────────────────
CREATE VISUAL PeriodTotal AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end),
  TITLE    = 'Period Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Trends AS DASHBOARD (
  STRUCTURE = 'A B C / D D D',
  MAP (
    'A' = PeriodTotal,
    'B' = StartPicker,
    'C' = EndPicker,
    'D' = RevenueTrend
  )
);
```
