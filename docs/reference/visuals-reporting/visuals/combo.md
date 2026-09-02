# COMBO
A combined bar + line chart on shared axes. Use it to compare a volume metric (bars) with a rate or trend metric (line), such as revenue bars with margin percent line.

## Syntax

```sql
CREATE VISUAL VisualName AS COMBO (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  ),
  OPTIONS (
    [GRID_LINES = ON|OFF],
    [GRID_LINE_COLOR = '#rrggbb'],
    [GRID_LINE_DASH = SOLID|DASHED|DOTTED],
    [GRID_LINE_WIDTH = n],
    [MINOR_GRID_LINES = ON|OFF],
    [SERIES_GAP = 0..1],
    [OUTER_PADDING = 0..1],
    [ZERO_LINE = ON|OFF],
    [ZERO_LINE_COLOR = '#rrggbb'],
    [ZERO_LINE_DASH = SOLID|DASHED|DOTTED],
    [ZERO_LINE_WIDTH = n],
    [SERIES_LABELS = ON|OFF WITH (POSITION = START|END)],
    [DATA_LABELS = ON|OFF WITH (
      [FONT_SIZE = n],
      [COLOR = '#rrggbb'],
      [LABEL_BACKGROUND = '#rrggbb'],
      [LABEL_BORDER = 'width style #rrggbb']
    )],
    [X_AXIS (...)], [Y_AXIS (...)], [Y2_AXIS (...)]
  ),
  [OVERLAYS (
    REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb']),
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

- **X** - shared category / time axis
- **Y** - bar metric (left Y axis)
- **Y2** - line metric (right Y axis)
- **COLOR** - optional series grouping for the bars

## Options

- **STACKED = ON|OFF** - stack the bars (default OFF)
- **SMOOTH = ON|OFF** - smooth the line (default OFF)
- **SERIES_GAP = 0..1** - set the gap between grouped bar series as a fraction of bar width
- **OUTER_PADDING = 0..1** - add category-band padding before the first and after the last bar (default 0)
- **GRID_LINES = ON|OFF** - show or hide background value-axis grid lines (default ON)
- **GRID_LINE_COLOR = '#rrggbb'** - set the major and minor gridline color
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** - set the gridline stroke pattern (default SOLID)
- **GRID_LINE_WIDTH = n** - set gridline width in pixels; the value must be positive (default 1)
- **MINOR_GRID_LINES = ON|OFF** - draw one lighter gridline between each pair of major ticks (default OFF)
- **ZERO_LINE = ON|OFF** - emphasize zero when the primary value-axis domain contains it (default OFF)
- **ZERO_LINE_COLOR = '#rrggbb'** - set the zero-line color
- **ZERO_LINE_DASH = SOLID|DASHED|DOTTED** - set the zero-line stroke pattern (default SOLID)
- **ZERO_LINE_WIDTH = n** - set zero-line width in pixels; the value must be positive (default 1.5)
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** - category-axis order; SOURCE preserves query order
- **SERIES_LABELS = ON|OFF WITH (POSITION = START|END)** — render exactly one series title label per visible series at the first (`START`) or final (`END`) renderable point (default OFF). Gaps and null values are skipped. Deterministically suppresses the data label at that endpoint to prevent collision.
- **DATA_LABELS = ON|OFF WITH (...)** — shows and formats data values (default OFF). Extended options:
  - **FONT_SIZE = n** — label text size in pixels.
  - **COLOR = '#rrggbb'** — label text fill color.
  - **LABEL_BACKGROUND = '#rrggbb'** — padded background rectangle drawn behind the label text.
  - **LABEL_BORDER = 'width style #rrggbb'** — border around the data label background (e.g., `'1px solid #334155'`; style is `solid`, `dashed`, or `dotted`).
- **X_AXIS (...) / Y_AXIS (...) / Y2_AXIS (...)** - each accepts `LABEL`, `MIN`, `MAX`, `INCLUDE_ZERO`, `REVERSE`, `MAJOR_TICK_COUNT`, `TICK_INTERVAL`, `MINOR_TICKS`, `LABEL_ROTATION`, `LABEL_SKIP`, and `AXIS_LINE`. Rotation accepts `AUTO`, `0`, `45`, or `90`; skip accepts `AUTO` or a non-negative integer.
- **LEGEND = ON|OFF** - shows or hides the series legend
- **OVERLAYS (...)** - visual overlays including `REFERENCE_LINE(VALUE = n, ...)` (targeting the primary Y axis, never Y2) and `FORECAST(field) AS SOLID|DASHED|DOTTED [WITH (...)]` on the primary quantitative axis. Supports paired `CONFIDENCE_LOW` / `CONFIDENCE_HIGH` quantitative interval ribbon and `ANOMALY` marker glyphs. Reference line values and forecast values participate in primary Y domain resolution.

## Examples

```sql
SELECT
    FORMAT(sale_date, 'yyyy-MM') AS month,
    SUM(revenue)                  AS total_revenue,
    AVG(margin_pct)               AS avg_margin
INTO #monthly
FROM #sales
GROUP BY FORMAT(sale_date, 'yyyy-MM')
ORDER BY month;

CREATE VISUAL RevenueWithMargin AS COMBO (
  SOURCE   = #monthly,
  MAPPINGS (X = month, Y = total_revenue, Y2 = avg_margin),
  OPTIONS  (
    SMOOTH  = ON,
    AXIS_SORT = SOURCE,
    Y_AXIS  (LABEL = 'Revenue ($)'),
    Y2_AXIS (LABEL = 'Margin (%)'),
    TITLE   = 'Revenue & Margin Trend'
  )
);

CREATE VISUAL RevenueWithMarginDirectLabels AS COMBO (
  SOURCE   = #monthly,
  MAPPINGS (X = month, Y = total_revenue, Y2 = avg_margin),
  OPTIONS  (
    SMOOTH        = ON,
    AXIS_SORT     = SOURCE,
    SERIES_LABELS = ON WITH (POSITION = END),
    DATA_LABELS   = ON WITH (
      LABEL_BACKGROUND = '#ffffff',
      LABEL_BORDER     = '1px solid #e2e8f0'
    ),
    Y_AXIS  (LABEL = 'Revenue ($)'),
    Y2_AXIS (LABEL = 'Margin (%)'),
    TITLE   = 'Revenue & Margin Trend with Direct Labels'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
