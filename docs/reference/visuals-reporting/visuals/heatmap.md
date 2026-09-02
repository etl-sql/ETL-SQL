# HEATMAP
A grid where cell colour intensity represents a metric value. Useful for showing patterns across two categorical dimensions (e.g. day-of-week vs. hour, region vs. product).

## Syntax

```sql
CREATE VISUAL VisualName AS HEATMAP (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **X** - column axis categories
- **Y** - row axis categories
- **VALUE** - the metric that drives cell colour intensity

## Options

- **COLORS** — Two-stop or three-stop gradient, e.g. `COLORS = ('#eff3ff', '#08519c')`.
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
    COUNT(*)                     AS orders
INTO #heatmap_data
FROM #sales
GROUP BY DATENAME(WEEKDAY, sale_date), DATEPART(HOUR, sale_time);

CREATE VISUAL OrderHeatmap AS HEATMAP (
  SOURCE   = #heatmap_data,
  MAPPINGS (X = hour_of_day, Y = day_of_week, VALUE = orders),
  OPTIONS  (
    COLORS      = ('#f7fbff', '#08306b'),
    DATA_LABELS = ON WITH (LABEL_BACKGROUND = '#ffffff', LABEL_BORDER = '1px solid #cbd5e1'),
    TITLE       = 'Order Volume by Day & Hour'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
