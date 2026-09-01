# Sales Trend with Forecasting

**Pattern**: A line chart over time with historical actuals, goal line, and pre-computed forecast projection with 95% confidence intervals and anomaly flags. Add a date-range picker to narrow the window.

**Demonstrates**: `LINE`, `OVERLAYS`, `FORECAST`, `CONFIDENCE_LOW`, `CONFIDENCE_HIGH`, `ANOMALY`, `GOAL`, `AVERAGE`, `SMOOTH`, `DATEPICKER`, typed `DECLARE @x DATE INPUT` parameters.

```sql
SET REPORT TITLE = 'Monthly Sales Trend & Forecast';
DECLARE @start DATE INPUT = '2025-01-01';
DECLARE @end   DATE INPUT = '2025-12-31';

-- ── Inline sample data: Actuals for Q1-Q3, forecast projection for Q4 ──────────
SELECT '2025-01-01' AS sale_date, 42000 AS revenue, CAST(NULL AS INT) AS forecast_rev,
       CAST(NULL AS INT) AS conf_low, CAST(NULL AS INT) AS conf_high, CAST(NULL AS INT) AS anomaly
INTO #monthly
UNION ALL SELECT '2025-02-01', 38000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-03-01', 51000, NULL, NULL, NULL, 51000
UNION ALL SELECT '2025-04-01', 47000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-05-01', 55000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-06-01', 62000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-07-01', 58000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-08-01', 71000, NULL, NULL, NULL, NULL
UNION ALL SELECT '2025-09-01', 66000, 66000, 63000, 69000, NULL
UNION ALL SELECT '2025-10-01', NULL,  74000, 70000, 78000, NULL
UNION ALL SELECT '2025-11-01', NULL,  80000, 75000, 85000, NULL
UNION ALL SELECT '2025-12-01', NULL,  88000, 81000, 95000, 88000;

-- ── Date filter controls ──────────────────────────────────────────────────
CREATE VISUAL StartPicker AS DATEPICKER (
  TITLE   = 'From',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start, value))
);

CREATE VISUAL EndPicker AS DATEPICKER (
  TITLE   = 'To',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@end, value))
);

-- ── Trend chart with forecast and goal overlays ───────────────────────────
CREATE VISUAL RevenueTrend AS LINE (
  SOURCE   = (SELECT sale_date AS month, revenue, forecast_rev, conf_low, conf_high, anomaly
              FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end
              ORDER BY sale_date),
  TITLE    = 'Monthly Revenue with Forecast & Target',
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
    FORECAST(forecast_rev) AS DASHED WITH (
      CONFIDENCE_LOW = conf_low,
      CONFIDENCE_HIGH = conf_high,
      ANOMALY = anomaly,
      COLOR = '#2563eb',
      LABEL = 'Q4 Forecast'
    )
  )
);

-- ── Summary card ──────────────────────────────────────────────────────────
CREATE VISUAL PeriodTotal AS CARD (
  SOURCE   = (SELECT SUM(COALESCE(revenue, forecast_rev)) AS val FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end),
  TITLE    = 'Projected Year Total',
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
