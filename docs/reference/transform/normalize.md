# NORMALIZE

Scales numeric columns to a standard range or distribution.

```sql
TRANSFORM #target
FROM #source
USING NORMALIZE (
  VALUE_COLS = 'value_col1[, value_col2, ...]',
  METHOD = 'MIN_MAX' | 'Z_SCORE',
  BY_GROUP = 'group_column[, ...]',
  NORM_COL = 'normalized_col1[, normalized_col2, ...]'
);
```

- **VALUE_COL = 'value_column'** / **VALUE_COLS = 'col1, col2, ...'** — One or more comma-separated numeric columns to scale. Specify either `VALUE_COL` or `VALUE_COLS`.
- **METHOD = 'method'** — The normalization method:
  - `MIN_MAX` (default) — Scales values linearly to the range `[0, 1]` using `(x - min) / (max - min)`.
  - `Z_SCORE` — Standardizes values to have a mean of `0` and standard deviation of `1` using `(x - mean) / stddev`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. Scaling statistics are computed independently within each group.
- **NORM_COL = 'normalized_column_name[, ...]'** — Output column name(s), matching the count of `VALUE_COL`/`VALUE_COLS`. Defaults to `'{col}_Normalized'` for each input column.

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
