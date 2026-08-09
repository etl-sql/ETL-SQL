Type: COMBO
A combined bar + line chart on shared axes. Use it to compare a volume metric (bars) with a rate or trend metric (line), such as revenue bars with margin percent line.

Mappings:
- **X** - shared category / time axis
- **Y** - bar metric (left Y axis)
- **Y2** - line metric (right Y axis)
- **COLOR** - optional series grouping for the bars

Options:
- **STACKED = ON|OFF** - stack the bars (default OFF)
- **SMOOTH = ON|OFF** - smooth the line (default OFF)
- **AXIS_SORT = ASC|DESC|SOURCE|VALUE|VALUE_DESC** - category-axis order; SOURCE preserves query order
  Y_AXIS  (LABEL = 'left axis label')
  Y2_AXIS (LABEL = 'right axis label')
  LEGEND  = ON|OFF

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
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
