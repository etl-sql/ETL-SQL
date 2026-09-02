# HBAR

Renders a horizontal category-based bar chart. Use `HBAR` when category labels are long, rankings matter, or readers need to compare values across many groups.

## Syntax

```sql
CREATE VISUAL VisualName AS HBAR (
  SOURCE = #tableName,
  MAPPINGS (
    X = categoryColumn,
    Y = metricColumn,
    [SERIES = seriesColumn]
  ),
  OPTIONS (
    [STACKED = ON|OFF|100PCT],
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
    [LEGEND = ON|OFF],
    [LABEL_POSITION = INSIDE|OUTSIDE|NONE],
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

## Mappings

- **X** - Category or group column. In `HBAR`, these categories render on the vertical axis.
- **Y** - Numeric measure column. In `HBAR`, this value controls horizontal bar length.
- **SERIES** - Optional series breakdown for grouped or stacked bars.

## Options

- **STACKED = ON|OFF|100PCT** - Uses grouped bars, cumulative stacking, or normalized 100% stacking. Default is `OFF`.
- **SERIES_GAP = 0..1** - Sets the gap between bars in a grouped cluster as a fraction of bar width.
- **OUTER_PADDING = 0..1** - Adds category-band padding before the first and after the last bar. Default is `0`.
- **GRID_LINES = ON|OFF** - Shows or hides background value-axis grid lines. Default is `ON`.
- **GRID_LINE_COLOR = '#rrggbb'** - Sets the major and minor gridline color.
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** - Sets the gridline stroke pattern. Default is `SOLID`.
- **GRID_LINE_WIDTH = n** - Sets gridline width in pixels. The value must be positive; default is `1`.
- **MINOR_GRID_LINES = ON|OFF** - Draws one lighter gridline between each pair of major ticks. Default is `OFF`.
- **ZERO_LINE = ON|OFF** - Emphasizes zero when the horizontal value-axis domain contains it. Default is `OFF`.
- **ZERO_LINE_COLOR = '#rrggbb'** - Sets the zero-line color.
- **ZERO_LINE_DASH = SOLID|DASHED|DOTTED** - Sets the zero-line stroke pattern. Default is `SOLID`.
- **ZERO_LINE_WIDTH = n** - Sets zero-line width in pixels. The value must be positive; default is `1.5`.
- **LEGEND = ON|OFF** - Shows or hides the series legend. Default `ON`.
- **LABEL_POSITION = INSIDE|OUTSIDE|NONE** - Shows and positions value labels. Default `NONE`.
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** - Sorts categories by label, source order, or measure value. Default `ASC`.
- **X_AXIS (...) / Y_AXIS (...)** - Configures axis titles, explicit MIN/MAX domains, zero inclusion, reverse direction, ticks, label rotation, label skipping, and plot-area spines (`AXIS_LINE`). For `HBAR`, X is the category axis and Y is the horizontal value scale in the authoring model.
- **OVERLAYS (...)** - Adds constant target or threshold reference lines (`REFERENCE_LINE(VALUE = n, ...)`), rendered as vertical plot-spanning lines across the primary value axis. Participates in automatic value-axis domain resolution.

## Actions

- **ON_CLICK = DRILL_IN(HIERARCHY = (...))** - Enables hierarchical drill-in behavior.
- **ON_CLICK = SET_PARAMETER(@parameter, column)** - Updates a report parameter from the clicked category or series value.
- **ON_CLICK = RUN_SCRIPT(...)** - Runs a script action from the clicked bar.

## Examples

```sql
CREATE VISUAL TopRegions AS HBAR (
  SOURCE = (
    SELECT Region, SUM(Revenue) AS Revenue
    FROM #sales
    GROUP BY Region
  ),
  MAPPINGS (X = Region, Y = Revenue),
  OPTIONS (AXIS_SORT = VALUE_DESC, LABEL_POSITION = OUTSIDE)
);
```

```sql
CREATE VISUAL RevenueByRegionAndChannel AS HBAR (
  SOURCE = #sales_by_channel,
  MAPPINGS (X = Region, Y = Revenue, SERIES = Channel),
  OPTIONS (STACKED = ON, LEGEND = ON)
);
```

## References

- [BAR](bar.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
