# DONUT

A circular chart with a center hole. It uses PIE semantics and can show a center KPI.

Mappings:
- **VALUE** - numeric metric that determines each slice's area (required)
- **NAME** - label for each slice (required)

Options:
- **INNER_RADIUS = number** - hole size as a fraction from `0` to `0.9`, or as a percentage; the default is `0.45`
- **ROSE_MODE = ON|OFF** - "nightingale" mode: radius also varies with value
- **LEGEND = ON|OFF** - show legend (default ON)
- **CENTER_LABEL = 'text'** - text displayed in the centre hole
- **CENTER_VALUE = 'text'** - prominent value displayed in the centre hole; `{total}` is replaced with the slice total

```sql
SELECT channel, SUM(revenue) AS total
INTO #by_channel
FROM #sales GROUP BY channel;

CREATE VISUAL RevenueDonut AS DONUT (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (
    INNER_RADIUS = 0.55,
    CENTER_VALUE = '{total}',
    CENTER_LABEL = 'Revenue',
    TITLE        = 'Revenue by Channel'
  )
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
