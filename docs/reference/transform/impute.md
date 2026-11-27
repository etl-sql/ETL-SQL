# IMPUTE

Replaces missing (`NULL`) values across one or more columns using group or global `MEAN`, `MEDIAN`, `MODE`, or `CONSTANT` statistics while preserving non-null values and `SET WHAT_IF ON` safety.

## Syntax

```sql
TRANSFORM #target [FROM #source]
USING IMPUTE (
    COLS = 'col1, col2',
    [METHOD = 'MEAN' | 'MEDIAN' | 'MODE' | 'CONSTANT',]
    [VALUE = 0,]
    [BY_GROUP = 'group_col1, group_col2']
);
```

## Options

- **COLS** — Comma-separated list of columns to impute (required; `VALUE_COLS` and `VALUE_COL` are also accepted).
- **METHOD** — `'MEAN'` (default), `'MEDIAN'`, `'MODE'` (most frequent non-null value; ties broken by first appearance), or `'CONSTANT'`.
- **VALUE** — Replacement value when `METHOD = 'CONSTANT'` (required for `CONSTANT`).
- **BY_GROUP** — Optional comma-separated grouping columns; fill statistics are computed independently per group. If every value in a group is `NULL`, `NULL` is preserved.

## Examples

```sql
TRANSFORM #filled
FROM #metrics
USING IMPUTE (
    COLS = 'Latency, Score',
    METHOD = 'MEDIAN',
    BY_GROUP = 'Region'
);

TRANSFORM #defaulted
FROM #leads
USING IMPUTE (
    COLS = 'Status',
    METHOD = 'CONSTANT',
    VALUE = 'PENDING'
);
```

## References

- [TRANSFORM Reference](README.md)
- [TRANSFORM Statement](../statements/dml/transform.md)
- [Syntax Index](../../syntax-index.md)
