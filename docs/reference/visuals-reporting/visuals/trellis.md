# TRELLIS
A small-multiples (faceted) chart that repeats the same chart type for each distinct value of a FACET column. All panels share layout and, by default, the same Y axis, making cross-facet comparisons honest.

## Syntax

```sql
CREATE VISUAL VisualName AS TRELLIS (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  ),
  OPTIONS (
    [COLUMNS = 1..6],
    [SHARED_AXIS = ON|OFF],
    [SHARED_X = ON|OFF],
    [SHARED_Y = ON|OFF],
    [SHARED_COLOR = ON|OFF],
    [GRID_LINES = ON|OFF],
    [GRID_LINE_COLOR = '#rrggbb'],
    [GRID_LINE_DASH = SOLID|DASHED|DOTTED],
    [GRID_LINE_WIDTH = n],
    [MINOR_GRID_LINES = ON|OFF],
    [X_AXIS (...)],
    [Y_AXIS (...)]
  )
);
```

## Mappings

- **X** - category axis (BAR/LINE) or horizontal numeric axis (SCATTER) (required)
- **Y** - measure axis (required)
- **FACET** - column whose unique values each produce one panel (required)

## Options

- **TITLE = 'text'** - Sets the visual title.
- **CHART_TYPE = BAR|LINE|SCATTER** - Selects the repeated chart type. Default is `BAR`.
- **COLUMNS = 1..6** - Sets the number of panels per row. Default is `3`.
- **SHARED_AXIS = ON|OFF** - Uses one shared Y range across all panels when `ON` (default `ON`). Synonym for `SHARED_Y`.
- **SHARED_X = ON|OFF** - Uses one shared X scale/range across all panels when `ON` (default `ON`). When `OFF`, each panel auto-scales its X axis independently.
- **SHARED_Y = ON|OFF** - Uses one shared Y scale/range across all panels when `ON` (default `ON`). When `OFF`, each panel auto-scales its Y axis independently.
- **SHARED_COLOR = ON|OFF** - Uses one shared color scale across panels when `ON` (default `ON`). When `OFF`, each panel resolves colors independently.
- **GRID_LINES = ON|OFF** - Shows or hides background value-axis grid lines. Default is `ON`.
- **GRID_LINE_COLOR = '#rrggbb'** - Sets the major and minor gridline color.
- **GRID_LINE_DASH = SOLID|DASHED|DOTTED** - Sets the gridline stroke pattern. Default is `SOLID`.
- **GRID_LINE_WIDTH = n** - Sets gridline width in pixels. The value must be positive; default is `1`.
- **MINOR_GRID_LINES = ON|OFF** - Draws one lighter gridline between each pair of major ticks. Default is `OFF`.

Note: With `SHARED_Y = ON` (or `SHARED_AXIS = ON`), panels with a narrow data range still show the global scale. This prevents misleading comparisons but may compress low-variance panels visually. Setting `SHARED_Y = OFF` or `SHARED_X = OFF` allows each panel to focus on its local variance.

- **X_AXIS (...) / Y_AXIS (...)** - accepts `LABEL`, `MIN`, `MAX`, `INCLUDE_ZERO`, `REVERSE`, `MAJOR_TICK_COUNT`, `TICK_INTERVAL`, `MINOR_TICKS`, `LABEL_ROTATION`, `LABEL_SKIP`, and `AXIS_LINE` for the repeated Cartesian axes.

## Examples

```sql
-- Revenue by category, one bar chart per region
SELECT Region, Category, SUM(Revenue) AS Revenue
  INTO #trellis
  FROM dbo.Sales
  GROUP BY Region, Category;

CREATE VISUAL TrellisRevByRegion AS TRELLIS (
  SOURCE   = #trellis,
  TITLE    = 'Revenue by Category Faceted by Region',
  MAPPINGS (
    X     = Category,
    Y     = Revenue,
    FACET = Region
  ),
  OPTIONS  (CHART_TYPE = BAR, COLUMNS = 2, SHARED_AXIS = ON)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
