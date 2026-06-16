Type: BOXPLOT
Shows the statistical distribution of a numeric variable — median, quartiles, whiskers, and outliers. Useful for comparing distributions across categories.

Mappings:
- **X** — category axis (grouping variable)
- **Y** — the numeric values to summarise The engine computes Q1, median, Q3, whiskers, and outliers automatically.

Options:
  ORIENTATION = VERTICAL|HORIZONTAL  (default VERTICAL)
- **SHOW_OUTLIERS = ON|OFF** — render individual outlier points (default ON)
- **WHISKER = 'tukey'** — whisker style: 'tukey' (1.5 × IQR, default) or 'minmax'
- **COLORS** — one colour per category, or a single colour for all boxes

```sql
SELECT region, delivery_days
INTO #delivery
FROM #orders;

CREATE VISUAL DeliveryDist AS BOXPLOT (
  SOURCE   = #delivery,
  MAPPINGS (X = region, Y = delivery_days),
  OPTIONS  (
    SHOW_OUTLIERS = ON,
    WHISKER       = 'tukey',
    TITLE         = 'Delivery Time Distribution by Region'
  )
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
