# PIE

A circular chart divided into slices proportional to each value. Use `DONUT` when the visual needs a hollow center.

## Syntax

```sql
CREATE VISUAL VisualName AS PIE (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **VALUE** - numeric metric that determines each slice's area (required)
- **NAME** - label for each slice (required)

## Options

- **ROSE_MODE = ON|OFF** - "nightingale" mode: radius also varies with value, not just angle (default OFF)
- **INNER_RADIUS = number** - DONUT hole as a fraction from `0` to `0.9`, or as a percentage; the default is `0.45`
- **LEGEND = ON|OFF** - show colour legend (default ON)
- **CENTER_LABEL = 'text'** - text shown in the centre hole (DONUT only)
- **CENTER_VALUE = 'text'** - prominent value shown in the centre hole (DONUT only); `{total}` is replaced with the slice total

## Examples

```sql
SELECT channel, SUM(revenue) AS total
INTO #by_channel
FROM #sales GROUP BY channel;

-- Pie chart
CREATE VISUAL RevenueByChannel AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (LEGEND = ON, TITLE = 'Revenue Mix')
);

-- Donut with centre label
CREATE VISUAL RevenueDonut AS DONUT (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (INNER_RADIUS = 0.55, CENTER_VALUE = '{total}', CENTER_LABEL = 'Revenue', TITLE = 'Revenue Mix')
);

-- Nightingale / rose chart
CREATE VISUAL SalesRose AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (ROSE_MODE = ON)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
