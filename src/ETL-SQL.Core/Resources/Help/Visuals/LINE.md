Type: LINE / AREA
A line chart for trend and time-series data. Set AREA = ON to fill the region below the line. Multiple series are drawn when the SOURCE contains a COLOR grouping column or multiple Y-axis columns.

Mappings:
- **X** — category or time axis (required)
- **Y** — metric to plot; list multiple columns for multi-series
- **COLOR** — column used to split rows into separate named series

Options:
- **SMOOTH  = ON|OFF** — Bezier-smoothed curves instead of straight segments (default OFF)
- **SYMBOLS = ON|OFF** — show data-point markers on the line (default OFF)
- **AREA    = ON|OFF** — fill the region below the line (default OFF)
- **STACKED = ON|OFF** — stack multiple series vertically (default OFF)
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** — category-axis order; SOURCE preserves query order
  TITLE   = 'text'

```sql
-- Single-series daily trend
SELECT order_date AS date, SUM(amount) AS revenue
  INTO #daily
  FROM dbo.Orders
  GROUP BY order_date
  ORDER BY order_date;

CREATE VISUAL RevenueTrend AS LINE (
  SOURCE   = #daily,
  MAPPINGS (X = date, Y = revenue),
  OPTIONS  (SMOOTH = ON, SYMBOLS = ON, AXIS_SORT = SOURCE, TITLE = 'Daily Revenue')
);

-- Multi-series by region using COLOR grouping
SELECT order_date AS date, region, SUM(amount) AS revenue
  INTO #by_region
  FROM dbo.Orders
  GROUP BY order_date, region;

CREATE VISUAL RegionTrend AS LINE (
  SOURCE   = #by_region,
  MAPPINGS (X = date, Y = revenue, COLOR = region),
  OPTIONS  (TITLE = 'Revenue by Region')
);

-- Stacked area chart
CREATE VISUAL StackedArea AS LINE (
  SOURCE   = #by_region,
  MAPPINGS (X = date, Y = revenue, COLOR = region),
  OPTIONS  (AREA = ON, STACKED = ON, TITLE = 'Stacked Revenue')
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
