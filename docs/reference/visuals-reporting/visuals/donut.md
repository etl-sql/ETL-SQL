Type: DONUT  (alias of PIE with RADIUS options set to create the centre hole)
A circular chart with a centre hole. It uses the same semantics as PIE but is cleaner for showing total and allowing a centre-label KPI.

Mappings:
- **VALUE** - numeric metric that determines each slice's area (required)
- **NAME** - label for each slice (required)

Options:
- **RADIUS = ('inner%', 'outer%')** - inner radius creates the hole; default DONUT = ('40%', '70%')
- **ROSE_MODE = ON|OFF** - "nightingale" mode: radius also varies with value
- **LEGEND = ON|OFF** - show legend (default ON)
- **CENTER_LABEL = 'text'** - text displayed in the centre hole

```sql
SELECT channel, SUM(revenue) AS total
INTO #by_channel
FROM #sales GROUP BY channel;

CREATE VISUAL RevenueDonut AS DONUT (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (
    RADIUS       = ('35%', '65%'),
    CENTER_LABEL = 'Revenue',
    TITLE        = 'Revenue by Channel'
  )
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
