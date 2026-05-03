Type: RADAR
A spider / radar chart comparing values across multiple axes radiating from a central point. Best for comparing items across 4–12 dimensions.

Mappings:
  NAME    — the axis label (one row per axis, or one row per series if multi-series)
  VALUE   — numeric value for this axis/series combination
  COLOR   — optional series identifier for multi-series charts

Options:
  FILL    = ON|OFF    — shade the area inside the radar polygon (default ON)
  SMOOTH  = ON|OFF    — round polygon corners (default OFF)
  MIN     — explicit minimum for all axes (default 0)
  MAX     — explicit maximum for all axes (auto if omitted)

Single-series example (one item across multiple dimensions):
```sql
SELECT 'Speed'       AS dimension, 88 AS score UNION ALL
SELECT 'Reliability',              92           UNION ALL
SELECT 'Efficiency',               75           UNION ALL
SELECT 'Coverage',                 83           UNION ALL
SELECT 'Accuracy',                 95
INTO #perf;

CREATE VISUAL PerfRadar AS RADAR (
  SOURCE   = #perf,
  MAPPINGS (NAME = dimension, VALUE = score),
  OPTIONS  (FILL = ON, TITLE = 'Model Performance')
);
```

Multi-series example (compare two items):
```sql
SELECT 'Speed', 88, 'ModelA' UNION ALL SELECT 'Speed', 76, 'ModelB'
UNION ALL SELECT 'Accuracy', 95, 'ModelA' UNION ALL SELECT 'Accuracy', 90, 'ModelB'
INTO #compare (dimension, score, model);

CREATE VISUAL CompareRadar AS RADAR (
  SOURCE   = #compare,
  MAPPINGS (NAME = dimension, VALUE = score, COLOR = model),
  OPTIONS  (FILL = ON)
);
```
