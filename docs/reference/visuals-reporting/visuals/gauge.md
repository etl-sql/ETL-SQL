# GAUGE

A single KPI shown as a progress arc, semicircle, ring, needle dial, or horizontal bar.

## Syntax

```sql
CREATE VISUAL VisualName AS GAUGE (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **VALUE** — Current metric value (required)
- **MIN** — Scale minimum (optional; default 0)
- **MAX** — Scale maximum (optional; default 100)
- **GOAL** — Primary target value rendered as a threshold marker
- **GOAL2** — Secondary target or stretch goal rendered as a secondary threshold marker

## Options

- **FORMAT = 'string'** — .NET format string for value label (e.g., `'0.0%'`, `'C0'`, `'$#,##0'`)
- **GOAL_LABEL = 'text'** — Descriptive label for primary goal marker
- **GOAL2_LABEL = 'text'** — Descriptive label for secondary goal marker
- **LABEL_POSITION = 'CENTER'|'BOTTOM'|'INSIDE'** — Placement of the metric value label
- **GAUGE_STYLE = 'PROGRESS'** — 270-degree progress arc (default; `ARC` is an alias)
- **GAUGE_STYLE = 'SEMI_CIRCLE'** — Half-circle progress gauge
- **GAUGE_STYLE = 'RING'** — Full circular progress ring
- **GAUGE_STYLE = 'NEEDLE'** — Semicircular dial with needle indicator
- **GAUGE_STYLE = 'BAR'** — Horizontal progress bar
- **COLORS = ('range_start%:color', ...)** — Color bands as percent of range (e.g. `('0%:#e74c3c', '60%:#f39c12', '80%:#27ae60')`)
- **TITLE = 'label'** — Text label beneath the gauge

## Examples

```sql
SELECT 73.5 AS score, 0 AS min_val, 100 AS max_val, 70 AS target, 90 AS stretch
INTO #kpi;

CREATE VISUAL SLAGauge AS GAUGE (
  SOURCE   = #kpi,
  MAPPINGS (
    VALUE = score,
    MIN = min_val,
    MAX = max_val,
    GOAL = target,
    GOAL2 = stretch
  ),
  OPTIONS  (
    GAUGE_STYLE = 'SEMI_CIRCLE',
    FORMAT = '0.0%',
    LABEL_POSITION = 'BOTTOM',
    GOAL_LABEL = 'SLA Target',
    GOAL2_LABEL = 'Stretch Target',
    TITLE = 'SLA Compliance'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
