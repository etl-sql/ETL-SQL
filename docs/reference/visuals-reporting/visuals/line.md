# LINE / AREA
A line chart for trend and time-series data. Set AREA = ON to fill the region below the line. Multiple series are drawn when the SOURCE contains a COLOR grouping column or multiple Y-axis columns.

## Syntax

```sql
CREATE VISUAL VisualName AS LINE (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  ),
  OPTIONS (
    [SYMBOLS = ON|OFF],
    [STACKED = ON|OFF|100PCT],
    [GRID_LINES = ON|OFF],
    [GRID_LINE_COLOR = '#rrggbb'],
    [GRID_LINE_DASH = SOLID|DASHED|DOTTED],
    [GRID_LINE_WIDTH = n],
    [MINOR_GRID_LINES = ON|OFF],
    [ZERO_LINE = ON|OFF],
    [ZERO_LINE_COLOR = '#rrggbb'],
    [ZERO_LINE_DASH = SOLID|DASHED|DOTTED],
    [ZERO_LINE_WIDTH = n],
    [ZOOM_SLIDER = ON|OFF],
    [SERIES_LABELS = ON|OFF WITH (POSITION = START|END)],
    [DATA_LABELS = ON|OFF WITH (
      [POSITION = OUTSIDE_TOP|OUTSIDE_BOTTOM],
      [FONT_SIZE = n],
      [COLOR = '#rrggbb'],
      [LABEL_BACKGROUND = '#rrggbb'],
      [LABEL_BORDER = 'width style #rrggbb']
    )],
    [X_AXIS (LABEL = 'text', MIN = n, MAX = n, INCLUDE_ZERO = ON|OFF, REVERSE = ON|OFF,
      MAJOR_TICK_COUNT = n, TICK_INTERVAL = n, MINOR_TICKS = ON|OFF,
      LABEL_ROTATION = AUTO|0|45|90, LABEL_SKIP = AUTO|n, AXIS_LINE = ON|OFF)],
    [Y_AXIS (...same axis properties...)]
  ),
  [OVERLAYS (
    REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb']),
    REFERENCE_BAND (LOW = n, HIGH = n [, COLOR = '#rrggbb'] [, LABEL = 'text']),
    RUNNING_TOTAL(precomputedCol) AS SOLID|DASHED|DOTTED [WITH (COLOR = '#rrggbb', LABEL = 'text')],
    PERCENT_OF_TOTAL(precomputedCol) AS SOLID|DASHED|DOTTED [WITH (COLOR = '#rrggbb', LABEL = 'text')],
    FORECAST(forecastCol) AS SOLID|DASHED|DOTTED [WITH (
      [CONFIDENCE_LOW = lowCol,]
      [CONFIDENCE_HIGH = highCol,]
      [ANOMALY = anomalyCol,]
      [COLOR = '#rrggbb',]
      [LABEL = 'text']
    )],
    ...
  )]
);
```

## Mappings

- **X** - category or time axis (required)
- **Y** - metric to plot; list multiple columns for multi-series
- **COLOR** - column used to split rows into separate named series

## Options

- **SMOOTH = ON|OFF** - Bezier-smoothed curves instead of straight segments (default OFF)
- **SYMBOLS = ON|OFF** - show data-point markers on the line (default ON)
- **GRID_LINES = ON|OFF** - show background value-axis grid lines (default ON)
- **GRID_LINE_COLOR = '#rrggbb'** - set the major and minor gridline color
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** - set the gridline stroke pattern (default SOLID)
- **GRID_LINE_WIDTH = n** - set gridline width in pixels; the value must be positive (default 1)
- **MINOR_GRID_LINES = ON|OFF** - draw one lighter gridline between each pair of major ticks (default OFF)
- **ZERO_LINE = ON|OFF** - emphasize zero when the value-axis domain contains it (default OFF)
- **ZERO_LINE_COLOR = '#rrggbb'** - set the zero-line color
- **ZERO_LINE_DASH = SOLID|DASHED|DOTTED** - set the zero-line stroke pattern (default SOLID)
- **ZERO_LINE_WIDTH = n** - set zero-line width in pixels; the value must be positive (default 1.5)
- **ZOOM_SLIDER = ON|OFF** - show a browser range selector below the chart (default OFF)
- **SERIES_LABELS = ON|OFF WITH (POSITION = START|END)** — render exactly one series title label per visible series at the first (`START`) or final (`END`) renderable point (default OFF). Gaps and null values are skipped. Deterministically suppresses the data label at that endpoint to prevent collision.
- **DATA_LABELS = ON|OFF WITH (...)** — show and format point values (default OFF). Extended options include:
  - **POSITION = OUTSIDE_TOP|OUTSIDE_BOTTOM** — label placement relative to data points.
  - **FONT_SIZE = n** — label text size in pixels.
  - **COLOR = '#rrggbb'** — label text fill color.
  - **LABEL_BACKGROUND = '#rrggbb'** — padded background rectangle drawn behind the label text.
  - **LABEL_BORDER = 'width style #rrggbb'** — border around the data label background (e.g., `'1px solid #334155'`; style is `solid`, `dashed`, or `dotted`).
- **AREA = ON|OFF** - fill the region below the line (default OFF)
- **STACKED = ON|OFF|100PCT** - draw independent series, cumulative stacks, or normalized 100% stacks (default OFF)
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** - category-axis order; SOURCE preserves query order
- **X_AXIS (...) / Y_AXIS (...)** - axis title, explicit MIN/MAX domain, zero inclusion, reverse direction, major tick count or interval, minor ticks, label rotation, label skipping, and plot-area spine (`AXIS_LINE`). `MAJOR_TICK_COUNT` is 2–100 and `TICK_INTERVAL` must be positive.
- **TITLE = 'text'** - visual title
- **OVERLAYS (...)** - Adds constant reference lines, `REFERENCE_BAND(LOW = n, HIGH = n, ...)`, `RUNNING_TOTAL(field)`, `PERCENT_OF_TOTAL(field)`, and forecast overlays. Calculate table-calculation fields in SQL; the overlay binds and annotates those visible result columns on the primary Y axis.

## Examples

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

-- Time-series forecast with confidence band and anomaly markers
CREATE VISUAL SalesForecast AS LINE (
  SOURCE = #forecast_data,
  MAPPINGS (X = Month, Y = Revenue),
  OPTIONS (TITLE = 'Revenue Forecast (95% CI)'),
  OVERLAYS (
    FORECAST(ForecastRev) AS DASHED WITH (
      CONFIDENCE_LOW = LowBound,
      CONFIDENCE_HIGH = HighBound,
      ANOMALY = AnomalyValue,
      COLOR = '#2563eb',
      LABEL = 'Forecast'
    )
  )
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

-- Multi-series with series labels at endpoints and styled data label badges
CREATE VISUAL RegionalTrendWithSeriesLabels AS LINE (
  SOURCE   = #by_region,
  MAPPINGS (X = date, Y = revenue, COLOR = region),
  OPTIONS  (
    SERIES_LABELS = ON WITH (POSITION = END),
    DATA_LABELS   = ON WITH (
      LABEL_BACKGROUND = '#ffffff',
      LABEL_BORDER     = '1px solid #e2e8f0'
    ),
    TITLE = 'Revenue by Region with Direct Series Labels'
  )
);

-- Analytical overlays derived from the primary Y series in source order
SELECT date,
       revenue,
       SUM(revenue) OVER (ORDER BY date) AS cumulative_revenue,
       revenue * 100.0 / SUM(revenue) OVER () AS daily_share
  INTO #daily_analysis
  FROM #daily;

CREATE VISUAL RevenueAnalysis AS LINE (
  SOURCE = #daily_analysis,
  MAPPINGS (X = date, Y = revenue),
  OVERLAYS (
    REFERENCE_BAND (LOW = 50000, HIGH = 80000, COLOR = '#94a3b8', LABEL = 'Expected range'),
    RUNNING_TOTAL(cumulative_revenue) AS SOLID WITH (COLOR = '#2563eb', LABEL = 'Cumulative revenue'),
    PERCENT_OF_TOTAL(daily_share) AS DOTTED WITH (COLOR = '#dc2626', LABEL = 'Daily share')
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
