Type: GAUGE, GAUGE_STYLE (arc | needle | bar)
A single KPI shown as a gauge dial. It is ideal for showing progress toward a target or position within a range.

Mappings:
- **VALUE** - the current metric value (required)
- **MIN** - scale minimum (optional; default 0)
- **MAX** - scale maximum (optional; default 100)
- **GOAL** - target value; rendered as a marker on the dial

Options:
- **GAUGE_STYLE = 'arc'** - circular arc gauge (default)
- **GAUGE_STYLE = 'needle'** - traditional dial with needle
- **GAUGE_STYLE = 'bar'** - horizontal progress-bar style
- **COLORS = ('range_start%:color', ...)** - colour bands as percent of range, e.g. ('0%:#e74c3c', '60%:#f39c12', '80%:#27ae60')
- **TITLE = 'label'** - text label beneath the gauge

```sql
SELECT 73.5 AS score, 0 AS min_val, 100 AS max_val, 80 AS target
INTO #kpi;

CREATE VISUAL SLAGauge AS GAUGE (
  SOURCE   = #kpi,
  MAPPINGS (VALUE = score, MIN = min_val, MAX = max_val, GOAL = target),
  OPTIONS  (
    GAUGE_STYLE = 'arc',
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
