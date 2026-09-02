# BAR

Renders a category-based bar or column chart to visualize metrics across different groups or series.

## Syntax

```sql
CREATE VISUAL VisualName AS BAR (
    SOURCE = #tableName,
    MAPPINGS (
        X = categoryColumn,
        Y = metricColumn,
        [SERIES = seriesColumn]
    ),
    OPTIONS (
        [STACKED = ON|OFF|100PCT],
        [BAND_SIZE = 0.1..1],
        [SERIES_GAP = 0..1],
        [OUTER_PADDING = 0..1],
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
        [LEGEND = ON|OFF],
        [LEGEND_POSITION = TOP|RIGHT|BOTTOM|LEFT],
        [DATA_LABELS = ON|OFF WITH (
          [POSITION = INSIDE_TOP|INSIDE_MIDDLE|INSIDE_BOTTOM|OUTSIDE_TOP|OUTSIDE_MIDDLE|OUTSIDE_BOTTOM],
          [FONT_SIZE = n],
          [COLOR = '#rrggbb'],
          [LABEL_BACKGROUND = '#rrggbb'],
          [LABEL_BORDER = 'width style #rrggbb']
        )],
        [AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC],
        [X_AXIS (LABEL = 'text', MIN = n, MAX = n, INCLUDE_ZERO = ON|OFF, REVERSE = ON|OFF,
          MAJOR_TICK_COUNT = n, TICK_INTERVAL = n, MINOR_TICKS = ON|OFF,
          LABEL_ROTATION = AUTO|0|45|90, LABEL_SKIP = AUTO|n, AXIS_LINE = ON|OFF)],
        [Y_AXIS (...same axis properties...)]
    ),
    [OVERLAYS (
        REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb'])
    )],
    ACTIONS (
        [ON_CLICK = DRILL_IN(...) | DRILL_DOWN(...) | SET_PARAMETER(...) | RUN_SCRIPT(...)]
    )
);
```

## Visual Types
- **BAR** - Vertical column chart (default).
- **HBAR** - Horizontal bar chart.

## Mappings
- **X** - The column containing categories/groups for the X-axis (required).
- **Y** - The column containing metrics/numeric values for the Y-axis (required).
- **SERIES** - The column containing series breakdown for multi-series grouping or stacking (optional).

## Options
- **STACKED = ON\|OFF\|100PCT** - Uses grouped bars, cumulative stacking, or normalized 100% stacking. Default is `OFF`.
- **BAND_SIZE = 0.1..1** - Sets bar width as a fraction of its category band. Smaller values add more spacing. Default is `0.75`.
- **SERIES_GAP = 0..1** - Sets the gap between bars in a grouped cluster as a fraction of bar width. Default is renderer spacing.
- **OUTER_PADDING = 0..1** - Adds category-band padding before the first and after the last bar. Default is `0`.
- **GRID_LINES = ON\|OFF** - Shows or hides background value-axis grid lines. Default is `ON`.
- **GRID_LINE_COLOR = '#rrggbb'** - Sets the major and minor gridline color.
- **GRID_LINE_DASH = SOLID\|DASHED\|DOTTED** - Sets the gridline stroke pattern. Default is `SOLID`.
- **GRID_LINE_WIDTH = n** - Sets gridline width in pixels. The value must be positive; default is `1`.
- **MINOR_GRID_LINES = ON\|OFF** - Draws one lighter gridline between each pair of major ticks. Default is `OFF`.
- **ZERO_LINE = ON\|OFF** - Emphasizes zero when the value-axis domain contains it. Default is `OFF`.
- **ZERO_LINE_COLOR = '#rrggbb'** - Sets the zero-line color.
- **ZERO_LINE_DASH = SOLID\|DASHED\|DOTTED** - Sets the zero-line stroke pattern. Default is `SOLID`.
- **ZERO_LINE_WIDTH = n** - Sets zero-line width in pixels. The value must be positive; default is `1.5`.
- **ZOOM_SLIDER = ON\|OFF** - Shows a browser range selector below the chart. Default is `OFF`.
- **LEGEND = ON\|OFF** - Toggles visual series legend. Default is `ON`.
- **LEGEND_POSITION = TOP\|RIGHT\|BOTTOM\|LEFT** - Places the legend outside the plot. Default is `BOTTOM`.
- **DATA_LABELS = ON\|OFF WITH (...)** - Shows value labels and configures their position, color, font, numeric format, background, and border. Default is `OFF`. Extended options:
  - **POSITION = INSIDE_TOP\|...** - Placement relative to bars.
  - **FONT_SIZE = n** - Label text size in pixels.
  - **COLOR = '#rrggbb'** - Label text fill color.
  - **LABEL_BACKGROUND = '#rrggbb'** - Padded background rectangle drawn behind the label text.
  - **LABEL_BORDER = 'width style #rrggbb'** - Border around the data label background (e.g., `'1px solid #334155'`; style is `solid`, `dashed`, or `dotted`).
- **AXIS_SORT = ASC\|DESC\|SOURCE\|VALUE\|VALUE_DESC** - Category sorting logic. Use `SOURCE` to preserve the query order, or `VALUE_DESC` for ranked bars. Default is `ASC`.
- **X_AXIS (...) / Y_AXIS (...)** - Configures the axis title (`LABEL`), explicit domain (`MIN`, `MAX`), zero inclusion, direction, major tick count or interval, minor ticks, label rotation, label skipping, and plot-area spine (`AXIS_LINE`). `MAJOR_TICK_COUNT` is 2–100; `TICK_INTERVAL` must be positive; `LABEL_SKIP = n` hides `n` labels between visible labels.
- **OVERLAYS (...)** - Adds constant target or threshold reference lines (`REFERENCE_LINE(VALUE = n, ...)`), rendered as horizontal plot-spanning lines across the primary value axis. Participates in automatic value-axis domain resolution.

## Actions
- **ON_CLICK = DRILL_IN(HIERARCHY = (...))** - Enables hierarchical drilling, such as Year to Quarter to Month, on click with breadcrumb navigation.
- **ON_CLICK = SET_PARAMETER(...)** - Binds category selection to updates of a query parameter.
- **ON_CLICK = RUN_SCRIPT(...)** - Runs an external script with parameters.

## Examples

```sql
-- Simple ranked bar chart
CREATE VISUAL SalesByRegion AS BAR (
    SOURCE = #data,
    MAPPINGS (X = Region, Y = Sales),
    OPTIONS (AXIS_SORT = VALUE_DESC, BAND_SIZE = 0.65, DATA_LABELS = ON WITH (POSITION = OUTSIDE_TOP))
);
```

```sql
-- Hierarchical drill-down on click
CREATE VISUAL SalesByPeriod AS BAR (
    SOURCE = (SELECT Year, Quarter, Month, SUM(Revenue) AS Revenue FROM #sales GROUP BY Year, Quarter, Month),
    MAPPINGS (X = Year, Y = Revenue),
    ACTIONS (ON_CLICK = DRILL_IN(HIERARCHY = (Year, Quarter, Month)))
);
```

## References
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
