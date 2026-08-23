# GAUGE

A single KPI shown as a progress arc, semicircle, ring, needle dial, or horizontal bar.

Mappings:
- **VALUE** - the current metric value (required)
- **MIN** - scale minimum (optional; default 0)
- **MAX** - scale maximum (optional; default 100)
- **GOAL** - target value; rendered as a marker on the dial

Options:
- **GAUGE_STYLE = 'PROGRESS'** - 270-degree progress arc (default; `ARC` is an alias)
- **GAUGE_STYLE = 'SEMI_CIRCLE'** - Power BI-style half-circle progress gauge
- **GAUGE_STYLE = 'RING'** - full circular progress ring
- **GAUGE_STYLE = 'NEEDLE'** - semicircular dial with a needle
- **GAUGE_STYLE = 'BAR'** - horizontal progress bar
- **COLORS = ('range_start%:color', ...)** - colour bands as percent of range, e.g. ('0%:#e74c3c', '60%:#f39c12', '80%:#27ae60')
- **TITLE = 'label'** - text label beneath the gauge

```sql
SELECT 73.5 AS score, 0 AS min_val, 100 AS max_val, 80 AS target
INTO #kpi;

CREATE VISUAL SLAGauge AS GAUGE (
  SOURCE   = #kpi,
  MAPPINGS (VALUE = score, MIN = min_val, MAX = max_val, GOAL = target),
  OPTIONS  (
    GAUGE_STYLE = 'SEMI_CIRCLE',
    COLORS (
      low = '#e74c3c',
      medium = '#f39c12',
      high = '#27ae60'
    ),
    TITLE  = 'SLA Compliance %'
  )
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
