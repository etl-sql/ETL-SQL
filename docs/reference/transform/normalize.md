# NORMALIZE

Scales numeric columns to a standard range or distribution.

```sql
TRANSFORM #target
FROM #source
USING NORMALIZE (
  VALUE_COL = 'value_column',
  METHOD = 'MIN_MAX' | 'Z_SCORE',
  BY_GROUP = 'group_column[, ...]',
  NORM_COL = 'normalized_column_name'
);
```

- **VALUE_COL = 'value_column'** — The numeric column to scale.
- **METHOD = 'method'** — The normalization method:
  - `MIN_MAX` (default) — Scales values linearly to the range `[0, 1]` using `(x - min) / (max - min)`.
  - `Z_SCORE` — Standardizes values to have a mean of `0` and standard deviation of `1` using `(x - mean) / stddev`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. Scaling statistics are computed independently within each group.
- **NORM_COL = 'normalized_column_name'** — Output column name. Defaults to `'{VALUE_COL}_Normalized'`.

## Examples

Normalizes player scores between 0 and 1:

```sql
TRANSFORM #normalized_scores
FROM #raw_scores
USING NORMALIZE (
  VALUE_COL = 'Score',
  METHOD = 'MIN_MAX'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
