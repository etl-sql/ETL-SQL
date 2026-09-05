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
    [BAR_MIN_HEIGHT = n],
    [SYNC_AXES = ON|OFF],
    [Y_MARK = BAR|LINE|AREA],
    [Y2_MARK = BAR|LINE|AREA],
    [SYMBOL_SIZE = n],
    [ANIMATION = ON|OFF],
    [ANIMATION_DURATION = n],
    [ANIMATION_EASING = LINEAR|EASE_IN|EASE_OUT|ELASTIC|BOUNCE],
    [UPDATE_ANIMATION = ON|OFF],
    [HOVER_FOCUS = NONE|SELF|SERIES],
    [LINE_WIDTH = n],
    [INTERPOLATION = LINEAR|SMOOTH|STEP_BEFORE|STEP_AFTER],
    [LINE_DASH = SOLID|DASHED|DOTTED],
    [PLOT_BACKGROUND = '#rrggbb'|'transparent'],
    [PLOT_BORDER = 'width style #rrggbb'],
    [AXIS_FONT_SIZE = n],
    [AXIS_FONT_COLOR = '#rrggbb'],
    [AXIS_TITLE_FONT_SIZE = n],
    [SHOW_EXPORT = ON|OFF],
    [SHOW_DATA_VIEW = ON|OFF],
    [ZOOM_GROUP = 'groupName'],
    [SAMPLING = NONE|LTTB|AVERAGE|MAX|MIN],
    [PROGRESSIVE = ON|OFF],
    [PROGRESSIVE_CHUNK = n],
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
    [NULL_HANDLING = CONNECT|GAP|ZERO],
    [TOOLTIP_MODE = ITEM|SHARED],
    [TOOLTIP_POSITION = AUTO|TOP|BOTTOM|LEFT|RIGHT|CURSOR],
    [CROSSHAIR = ON|OFF],
    [CROSSHAIR_AXIS = X|Y|BOTH],
    [CROSSHAIR_COLOR = '#rrggbb'],
    [CROSSHAIR_DASH = 'dash_array'],
    [LINK_TOOLTIP = 'groupName'],
    [SEGMENT_STYLE (
      WHEN condition THEN LINE_DASH = DASHED|DOTTED|SOLID [, COLOR = '#rrggbb']
    )],
    [X_AXIS (TIME_UNIT = AUTO|DAY|WEEK|MONTH|QUARTER|YEAR, TICK_FORMAT = 'format', ...)], [Y_AXIS (...)], [Y2_AXIS (...)]
  ),
  [OVERLAYS (
    REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb']),
    REFERENCE_BAND (LOW = n, HIGH = n [, COLOR = '#rrggbb'] [, LABEL = 'text']),
    FORECAST(forecastCol) AS SOLID|DASHED|DOTTED [WITH (
      [CONFIDENCE_LOW = lowCol,]
      [CONFIDENCE_HIGH = highCol,]
      [ANOMALY = anomalyCol,]
      [COLOR = '#rrggbb',]
      [LABEL = 'text']
    )],
    ...
  )],
  [ANNOTATIONS (
    POINT (SERIES = 'seriesName', TYPE = MAX|MIN|COORD(x, y), LABEL = 'text' [, SYMBOL = 'pin|arrow|circle'] [, COLOR = '#rrggbb']),
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

- **BAR_MIN_HEIGHT = n** - Minimum bar height in pixels so very small non-zero bar values remain visible and hoverable (default 0).
- **SYNC_AXES = ON|OFF** - Synchronizes primary and secondary Y axis domains (`Y` and `Y2`) to share identical min/max scales and matching grid intervals (default OFF).
- **Y_MARK = BAR|LINE|AREA** - Mark type rendered for the primary `Y` axis series (default BAR).
- **Y2_MARK = BAR|LINE|AREA** - Mark type rendered for the secondary `Y2` axis series (default LINE).
- **SYMBOL_SIZE = n** - Size / radius of symbols on line or area series in pixels (default 4).
- **ANIMATION = ON|OFF** - Controls entry animation when the chart first mounts (default OFF for server/PDF, ON for interactive dashboards).
- **ANIMATION_DURATION = n** - Entry animation duration in milliseconds (default 600).
- **ANIMATION_EASING = LINEAR|EASE_IN|EASE_OUT|ELASTIC|BOUNCE** - Animation easing curve (default EASE_OUT).
- **UPDATE_ANIMATION = ON|OFF** - Controls transition animations when data updates (default OFF).
- **HOVER_FOCUS = NONE|SELF|SERIES** - Dimming and emphasis mode on pointer hover (`NONE`, `SELF` to dim other marks, or `SERIES` to highlight the hovered series across all categories). Default is `NONE`.
- **STACKED = ON|OFF** - stack the bars (default OFF)
- **SMOOTH = ON|OFF** - smooth the line (default OFF)
- **LINE_WIDTH = n** - set each line series width from `0.1` through `10` pixels (default `2`)
- **INTERPOLATION = LINEAR|SMOOTH|STEP_BEFORE|STEP_AFTER** - how line series connect their points; `INTERPOLATION` wins over `SMOOTH` when both are present.
- **LINE_DASH = SOLID|DASHED|DOTTED** - stroke pattern for line series (default SOLID).
- **PLOT_BACKGROUND / PLOT_BORDER** - fill and outline the region bounded by the axes, independently of the visual card. `PLOT_BORDER` takes a CSS border shorthand such as `'1px dashed #94a3b8'`.
- **AXIS_FONT_SIZE / AXIS_FONT_COLOR / AXIS_TITLE_FONT_SIZE** - typography for axis tick labels and axis titles (defaults `9`, `#666`/`#444`, and `10`).
- **SHOW_EXPORT = ON|OFF** - adds a per-chart PNG download button (default OFF).
- **SHOW_DATA_VIEW = ON|OFF** - adds a per-chart toggle between the chart and a table of its SOURCE rows (default OFF).
- **ZOOM_GROUP = 'groupName'** - links the range sliders of every chart naming the same group; implies `ZOOM_SLIDER = ON`.
- **SAMPLING = NONE|LTTB|AVERAGE|MAX|MIN** - render-time downsampling for dense series. The plan keeps every row and each bucket contributes a real row, so a sampled mark keeps its tooltip and selection identity (default NONE).
- **PROGRESSIVE = ON|OFF / PROGRESSIVE_CHUNK = n** - reveals marks in `n`-sized batches across animation frames rather than one layout pass (default chunk 200). Browser-only.
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
- **X_AXIS (...) / Y_AXIS (...) / Y2_AXIS (...)** - each accepts `LABEL`, `MIN`, `MAX`, `INCLUDE_ZERO`, `REVERSE`, `MAJOR_TICK_COUNT`, `TICK_INTERVAL`, `MINOR_TICKS`, `LABEL_ROTATION`, `LABEL_SKIP`, `AXIS_LINE`, `TIME_UNIT = AUTO|DAY|WEEK|MONTH|QUARTER|YEAR`, and `TICK_FORMAT = 'format'`. Rotation accepts `AUTO`, `0`, `45`, or `90`; skip accepts `AUTO` or a non-negative integer.
- **ZOOM_SLIDER = ON|OFF** - show interactive axis zoom slider controls (default OFF)
- **LEGEND = ON|OFF** - shows or hides the series legend
- **NULL_HANDLING = CONNECT|GAP|ZERO** - missing data policy for the line series: `CONNECT` connects through null points, `GAP` splits into disconnected segments (default), and `ZERO` plots nulls as 0.
- **TOOLTIP_MODE = ITEM|SHARED** - tooltip card mode: `ITEM` inspects the single hovered data point; `SHARED` presents values across all series for that X coordinate.
- **TOOLTIP_POSITION = AUTO|TOP|BOTTOM|LEFT|RIGHT|CURSOR** - placement of the tooltip card relative to the target or cursor.
- **CROSSHAIR = ON|OFF** - displays cursor-following guideline axes across the plot area (default OFF).
- **CROSSHAIR_AXIS = X|Y|BOTH** - orientation of active crosshair guides (default BOTH).
- **CROSSHAIR_COLOR = '#rrggbb'** - line color for crosshair guides (default '#6b7280').
- **CROSSHAIR_DASH = 'dash_array'** - dash pattern for crosshair lines (e.g. '3,3').
- **LINK_TOOLTIP = 'groupName'** - synchronizes tooltips and crosshairs across all charts with matching group names.
- **SEGMENT_STYLE (WHEN condition THEN LINE_DASH = DASHED|DOTTED|SOLID [, COLOR = '#rrggbb'])** - conditional per-segment line styling applied to connecting segments between consecutive data points of line series in the combo chart. Overrides line dash pattern and stroke color for segments matching the boolean predicate.
- **OVERLAYS (...)** - Adds `REFERENCE_LINE`, shaded `REFERENCE_BAND(LOW = n, HIGH = n, ...)`, and forecast overlays on the primary Y axis, never Y2. Reference bounds and forecast values participate in primary Y domain resolution.
- **ANNOTATIONS (...)** - Adds point annotations (`POINT (SERIES = '...', TYPE = MAX|MIN|COORD(x, y), LABEL = '...', SYMBOL = 'pin|arrow|circle')`) pointing to series extrema or specific plot coordinates with customizable marker shapes.
- **FORMATTING (...)** - conditional mark coloring based on predicate conditions (e.g. `FORMATTING (WHEN Y >= 1000 THEN '#10b981')`).

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
    LINE_WIDTH = 3,
    AXIS_SORT = SOURCE,
    Y_AXIS  (LABEL = 'Revenue ($)'),
    Y2_AXIS (LABEL = 'Margin (%)'),
    TITLE   = 'Revenue & Margin Trend'
  ),
  OVERLAYS (
    REFERENCE_BAND (LOW = 50000, HIGH = 80000, COLOR = '#cbd5e1', LABEL = 'Expected revenue')
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
