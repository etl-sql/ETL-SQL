# RADAR

A spider or radar chart that compares one or more series across multiple categorical or dimensional axes radiating from a central point. Useful for comparing performance, competency matrices, or multi-attribute benchmarks.

## Syntax

```sql
CREATE VISUAL VisualName AS RADAR (
  SOURCE = #tableName,
  OPTIONS (
    TITLE = 'Product Performance Benchmark',
    SHAPE = POLYGON,
    FILL_OPACITY = 0.25,
    INDEPENDENT_AXES = OFF,
    MIN = 0,
    MAX = 100
  )
);
```

## Mappings

Data is structured in wide format by default:

- **Series Column** — The first column defines the series name (one row per item or group being compared).
- **Dimension Columns** — Remaining columns must be numeric; each column name becomes an axis radiating from the center.

Alternatively, explicit mappings may be provided:

- **SERIES** — Column identifying the series/legend category (or `COLOR`).
- **DIMENSION** — Numeric column representing a radar axis (can be repeated for multiple dimensions via `METRIC` or `DETAIL`).

## Options

- **SHAPE = POLYGON|CIRCLE** — Determines whether the background grid lines form nested polygons or concentric circles (default `POLYGON`).
- **FILL_OPACITY = 0.0..1.0** — Opacity for series polygon fills, making overlapping series distinguishable (default `0.18`).
- **INDEPENDENT_AXES = ON|OFF** — When `ON`, each axis auto-scales independently based on its own dimension min and max instead of sharing a global scale (default `OFF`).
- **FILL = ON|OFF** — Controls whether the radar series area is filled (default `ON`).
- **DATA_LABELS = ON|OFF** — Renders data values at each radar vertex marker (default `OFF`).
- **MIN = number** — Explicit minimum value for all shared axes (default `0`).
- **MAX = number** — Explicit maximum value for all shared axes (auto-scaled to 110% of data maximum if omitted).
- **TITLE = 'text'** — Visual title displayed above the chart.

## Examples

### Concentric Circular Grid with Custom Opacity

```sql
SELECT 'Model A' AS Model, 88 AS Speed, 92 AS Reliability, 75 AS Efficiency, 83 AS Coverage, 95 AS Accuracy INTO #models
UNION ALL SELECT 'Model B', 76, 85, 91, 70, 80;

CREATE VISUAL CircularRadar AS RADAR (
  SOURCE = #models,
  OPTIONS (
    TITLE = 'Model Comparison (Circular Grid)',
    SHAPE = CIRCLE,
    FILL_OPACITY = 0.35,
    MAX = 100
  )
);
```

### Independent Dimension Axes for Differing Units

```sql
SELECT 'Server 1' AS Server, 1200 AS RequestsPerSec, 45 AS LatencyMs, 99.9 AS UptimePct, 16 AS MemoryGB INTO #metrics
UNION ALL SELECT 'Server 2', 2800, 22, 98.5, 32
UNION ALL SELECT 'Server 3', 850, 68, 99.99, 8;

CREATE VISUAL ServerBenchmark AS RADAR (
  SOURCE = #metrics,
  OPTIONS (
    TITLE = 'Multi-Metric System Health',
    INDEPENDENT_AXES = ON,
    FILL_OPACITY = 0.20
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
