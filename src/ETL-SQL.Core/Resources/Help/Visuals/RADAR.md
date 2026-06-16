Type: RADAR
A spider / radar chart that compares one or more series across multiple axes radiating from a central point. Best for comparing items across 4–12 dimensions.

Data shape:
- **First column** — series name (one row per item being compared)
- **Other columns** — one numeric column per dimension; column headers become axis labels

Options:
- **MIN** — explicit minimum for all axes (default 0)
- **MAX** — explicit maximum for all axes (auto-scaled to 110% of data max if omitted)
  TITLE   = 'text'

Single-series example (one product across performance dimensions):
```sql
SELECT 'ModelA' AS model, 88 AS speed, 92 AS reliability, 75 AS efficiency, 83 AS coverage, 95 AS accuracy
  INTO #perf;

CREATE VISUAL PerfRadar AS RADAR (
  SOURCE   = #perf,
  OPTIONS  (TITLE = 'Model Performance')
);
```

Multi-series example (compare two items across the same dimensions):
```sql
SELECT 'ModelA' AS model, 88 AS speed, 92 AS reliability, 75 AS efficiency
UNION ALL
SELECT 'ModelB',          76,          85,                 91
  INTO #compare;

CREATE VISUAL CompareRadar AS RADAR (
  SOURCE   = #compare,
  OPTIONS  (TITLE = 'Model Comparison', MAX = 100)
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
