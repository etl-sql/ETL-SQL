# HEATMAP

A grid where cell colour intensity represents a metric value. Useful for showing patterns across two categorical dimensions (e.g. day-of-week vs. hour, region vs. product).

## Syntax

```sql
CREATE VISUAL VisualName AS HEATMAP (
  SOURCE = #tableName,
  MAPPINGS (
    X = col_x,
    Y = row_y,
    VALUE = metric
  ),
  OPTIONS (
    COLORS = ('#eff3ff', '#08519c'),
    MIDPOINT = 0,
    CELL_BORDER = ON,
    X_SORT = ALPHA,
    Y_SORT = VALUE_DESC
  )
);
```

## Mappings

- **X** — column axis categories (nominal).
- **Y** — row axis categories (nominal).
- **VALUE** — the metric that drives cell colour intensity (quantitative).

## Options

- **COLORS** — Two-stop or three-stop gradient, e.g. `COLORS = ('#eff3ff', '#08519c')` or `COLORS = ('#dc2626', '#ffffff', '#16a34a')`.
- **COLOR_LOW = '#rrggbb'** — Explicit low/min end color of the gradient scale (default `#dbeafe`).
- **COLOR_MID = '#rrggbb'** — Explicit midpoint color for diverging heatmaps (default `#ffffff`).
- **COLOR_HIGH = '#rrggbb'** — Explicit high/max end color of the gradient scale (default `#1d4ed8`).
- **MIDPOINT = n** — Anchor value for the diverging midpoint (e.g. `MIDPOINT = 0` for negative/neutral/positive data).
- **NULL_COLOR = '#rrggbb'** — Color for cells with NULL or missing category intersections (default `#f1f5f9`).
- **CELL_BORDER = ON|OFF** — Toggle grid lines/borders between cells (default ON).
- **CELL_BORDER_COLOR = '#rrggbb'** — Color of the border between cells (default white or gap).
- **CELL_BORDER_WIDTH = n** — Width in pixels of cell borders (default 1).
- **X_SORT = SOURCE|ALPHA|VALUE_DESC|VALUE_ASC** — Sorting order for column axis categories. `VALUE_DESC` clusters columns by total metric sum.
- **Y_SORT = SOURCE|ALPHA|VALUE_DESC|VALUE_ASC** — Sorting order for row axis categories. `VALUE_DESC` clusters rows by total metric sum.
- **DATA_LABELS = ON|OFF WITH (...)** — Shows numeric values inside cells with optional background and border styling.
  - **LABEL_BACKGROUND = '#rrggbb'** — Background color for the value badge (e.g. `'#ffffff'`).
  - **LABEL_BORDER = 'width style color'** — Border for the value badge (e.g. `'1px solid #cbd5e1'`).
- **SHOW_VALUES = ON|OFF** — Alias toggle for cell value display (default OFF).
- **X_AXIS (LABEL = 'text')** — Horizontal axis title.
- **Y_AXIS (LABEL = 'text')** — Vertical axis title.

## Examples

```sql
SELECT
    DATENAME(WEEKDAY, sale_date) AS day_of_week,
    DATEPART(HOUR,   sale_time)  AS hour_of_day,
    SUM(profit_margin)           AS net_margin
INTO #heatmap_data
FROM #sales
GROUP BY DATENAME(WEEKDAY, sale_date), DATEPART(HOUR, sale_time);

CREATE VISUAL ProfitHeatmap AS HEATMAP (
  SOURCE   = #heatmap_data,
  MAPPINGS (X = hour_of_day, Y = day_of_week, VALUE = net_margin),
  OPTIONS  (
    COLOR_LOW         = '#dc2626',
    COLOR_MID         = '#ffffff',
    COLOR_HIGH        = '#16a34a',
    MIDPOINT          = 0,
    NULL_COLOR        = '#f1f5f9',
    CELL_BORDER       = ON,
    CELL_BORDER_COLOR = '#e2e8f0',
    CELL_BORDER_WIDTH = 1,
    X_SORT            = ALPHA,
    Y_SORT            = VALUE_DESC,
    DATA_LABELS       = ON WITH (LABEL_BACKGROUND = '#ffffff', LABEL_BORDER = '1px solid #cbd5e1'),
    TITLE             = 'Profit Margin by Day & Hour'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
