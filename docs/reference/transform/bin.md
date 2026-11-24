# BIN

Buckets numeric values into equal-width, quantile, or explicit threshold intervals and emits `Bin_Start`, `Bin_End`, and `Bin_Label` columns ready for ranged-rect histograms. `BUCKETIZE` is a synonym for `BIN`.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING BIN (
    VALUE_COL = 'column_name',
    [METHOD = 'EQUAL_WIDTH' | 'QUANTILE' | 'THRESHOLDS',]
    [BINS = 5,]
    [THRESHOLDS = '0, 50, 100',]
    [BY_GROUP = 'group_col1, group_col2',]
    [BIN_START_COL = 'Bin_Start',]
    [BIN_END_COL = 'Bin_End',]
    [BIN_LABEL_COL = 'Bin_Label']
);
```

## Options

- **VALUE_COL** — Numeric column to bucketize (required).
- **METHOD** — `'EQUAL_WIDTH'` (default), `'QUANTILE'`, or `'THRESHOLDS'`.
- **BINS** — Positive integer number of bins for `'EQUAL_WIDTH'` / `'QUANTILE'` (default: `5`), or comma-separated ascending numeric cutpoints when `METHOD = 'THRESHOLDS'`.
- **THRESHOLDS** — Comma-separated strictly ascending numeric edges for `METHOD = 'THRESHOLDS'` (at least two values).
- **BY_GROUP** — Optional comma-separated grouping columns; bin edges are computed per group.
- **BIN_START_COL / BIN_END_COL / BIN_LABEL_COL** — Optional output column names (defaults: `Bin_Start`, `Bin_End`, `Bin_Label`). `NULL` inputs emit `NULL` in all three columns.

## Examples

```sql
TRANSFORM #binned
FROM #orders
USING BIN (
    VALUE_COL = 'Amount',
    METHOD = 'EQUAL_WIDTH',
    BINS = 4
);

TRANSFORM #tiers
FROM #orders
USING BUCKETIZE (
    VALUE_COL = 'Amount',
    METHOD = 'THRESHOLDS',
    THRESHOLDS = '0, 100, 500, 1000'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
