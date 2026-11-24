# OUTLIERS

Detects numeric outliers per group or across the dataset using `IQR`, `Z_SCORE`, or `PERCENTILE` bounds, and clips, flags, filters, or nulls them while preserving `NULL` semantics.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING OUTLIERS (
    VALUE_COL = 'column_name',
    [METHOD = 'IQR' | 'Z_SCORE' | 'PERCENTILE',]
    [ACTION = 'CLIP' | 'FLAG' | 'FILTER' | 'NULL',]
    [THRESHOLD = 1.5,]
    [LOWER_PCT = 5,]
    [UPPER_PCT = 95,]
    [FLAG_COL = 'col_IsOutlier',]
    [BY_GROUP = 'group_col1, group_col2']
);
```

## Options

- **VALUE_COL** — Numeric column to evaluate (required).
- **METHOD** — `'IQR'` (default, `Q1 - k*IQR .. Q3 + k*IQR`), `'Z_SCORE'` (`mean ± k*stddev`), or `'PERCENTILE'` (`LOWER_PCT .. UPPER_PCT`).
- **ACTION** — `'CLIP'` (default, clamps outliers to the boundary), `'FLAG'` (appends `FLAG_COL` with `1`/`0`/`NULL`), `'FILTER'` (drops outlier rows; keeps `NULL` rows), or `'NULL'` (sets outlier values to `NULL`).
- **THRESHOLD** — Positive multiplier or tail percentile (defaults: `1.5` for `IQR`, `3.0` for `Z_SCORE`, `5.0` for `PERCENTILE`).
- **LOWER_PCT / UPPER_PCT** — Explicit lower/upper percentiles (`0 <= LOWER_PCT < UPPER_PCT <= 100`) for `METHOD = 'PERCENTILE'`.
- **FLAG_COL** — Output flag column name when `ACTION = 'FLAG'` (default: `<VALUE_COL>_IsOutlier`).
- **BY_GROUP** — Optional comma-separated grouping columns; bounds are computed independently per group.

## Examples

```sql
-- Clip extreme values using 1.5x IQR per region
TRANSFORM #clean_sales
FROM #sales
USING OUTLIERS (
    VALUE_COL = 'Amount',
    METHOD = 'IQR',
    ACTION = 'CLIP',
    BY_GROUP = 'Region'
);

-- Flag outliers using Z-score > 2.5
TRANSFORM #flagged_sales
FROM #sales
USING OUTLIERS (
    VALUE_COL = 'Amount',
    METHOD = 'Z_SCORE',
    THRESHOLD = 2.5,
    ACTION = 'FLAG',
    FLAG_COL = 'IsAnomaly'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
