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
        [STACKED = ON|OFF],
        [BAND_SIZE = 0.1..1],
        [GRID_LINES = ON|OFF],
        [ZOOM_SLIDER = ON|OFF],
        [LEGEND = ON|OFF],
        [LEGEND_POSITION = TOP|RIGHT|BOTTOM|LEFT],
        [DATA_LABELS = ON|OFF WITH (POSITION = INSIDE_TOP|INSIDE_MIDDLE|INSIDE_BOTTOM|OUTSIDE_TOP|OUTSIDE_MIDDLE|OUTSIDE_BOTTOM)],
        [AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC]
    ),
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
- **STACKED = ON\|OFF** - Enables stacked bars instead of grouped columns when SERIES is mapped. Default is `OFF`.
- **BAND_SIZE = 0.1..1** - Sets bar width as a fraction of its category band. Smaller values add more spacing. Default is `0.75`.
- **GRID_LINES = ON\|OFF** - Shows or hides background value-axis grid lines. Default is `ON`.
- **ZOOM_SLIDER = ON\|OFF** - Shows a browser range selector below the chart. Default is `OFF`.
- **LEGEND = ON\|OFF** - Toggles visual series legend. Default is `ON`.
- **LEGEND_POSITION = TOP\|RIGHT\|BOTTOM\|LEFT** - Places the legend outside the plot. Default is `BOTTOM`.
- **DATA_LABELS = ON\|OFF WITH (...)** - Shows value labels and configures their position, color, font, and numeric format. Default is `OFF`.
- **AXIS_SORT = ASC\|DESC\|SOURCE\|VALUE\|VALUE_DESC** - Category sorting logic. Use `SOURCE` to preserve the query order, or `VALUE_DESC` for ranked bars. Default is `ASC`.

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
