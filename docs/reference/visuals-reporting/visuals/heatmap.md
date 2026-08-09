Type: HEATMAP
A grid where cell colour intensity represents a metric value. Useful for showing patterns across two categorical dimensions (e.g. day-of-week vs. hour, region vs. product).

Mappings:
- **X** - column axis categories
- **Y** - row axis categories
- **VALUE** - the metric that drives cell colour intensity

Options:
- **COLORS** - two-stop or three-stop gradient, e.g. COLORS = ('#eff3ff', '#08519c')
- **SHOW_VALUES = ON|OFF** - display the numeric value inside each cell (default OFF)
  X_AXIS (LABEL = 'text')
  Y_AXIS (LABEL = 'text')

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
    SHOW_VALUES = ON,
    TITLE       = 'Order Volume by Day & Hour'
  )
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
