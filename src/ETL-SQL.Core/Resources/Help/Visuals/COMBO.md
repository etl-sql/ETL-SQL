Type: COMBO
A combined bar + line chart on shared axes. Use when you need to compare a volume metric (bars) with a rate or trend metric (line) — e.g. revenue bars with margin % line.

Mappings:
  X       — shared category / time axis
  Y       — bar metric (left Y axis)
  Y2      — line metric (right Y axis)
  COLOR   — optional series grouping for the bars

Options:
  STACKED    = ON|OFF  — stack the bars (default OFF)
  SMOOTH     = ON|OFF  — smooth the line (default OFF)
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
    Y_AXIS  (LABEL = 'Revenue ($)'),
    Y2_AXIS (LABEL = 'Margin (%)'),
    TITLE   = 'Revenue & Margin Trend'
  )
);
```
