Type: PIE, DONUT
A circular chart divided into slices proportional to each value. DONUT is a PIE with a hollow centre.

Mappings:
  VALUE   — numeric metric that determines each slice's area (required)
  NAME    — label for each slice (required)

Options:
  ROSE_MODE  = ON|OFF  — "nightingale" mode: radius also varies with value, not just angle (default OFF)
  RADIUS = ('inner%', 'outer%')
             — inner radius creates the DONUT hole; '0%' = solid pie (default for PIE)
               DONUT default: ('35%', '70%')
  LEGEND = ON|OFF      — show colour legend (default ON)
  CENTER_LABEL = 'text' — text shown in the centre hole (DONUT only)

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
  OPTIONS  (RADIUS = ('35%', '65%'), CENTER_LABEL = 'Revenue', TITLE = 'Revenue Mix')
);

-- Nightingale / rose chart
CREATE VISUAL SalesRose AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (ROSE_MODE = ON)
);
```
