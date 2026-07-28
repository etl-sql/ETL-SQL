# PIVOT

Rotates rows into columns to build cross-tabulation summaries.

```sql
TRANSFORM #target
FROM #source
USING PIVOT (
  ROW_FIELDS = 'row_column[, ...]',
  PIVOT_FIELD = 'pivot_column',
  VALUE_FIELD = 'value_column',
  AGGREGATE = 'SUM' | 'AVG' | 'COUNT' | 'MIN' | 'MAX'
);
```

- **ROW_FIELDS = 'row_column[, ...]'** — Comma-separated list of columns to group by on the left side of the output table.
- **PIVOT_FIELD = 'pivot_column'** — The column whose distinct values will rotate to become new columns.
- **VALUE_FIELD = 'value_column'** — The column containing numeric or comparable values to aggregate.
- **AGGREGATE = 'SUM' | 'AVG' | 'COUNT' | 'MIN' | 'MAX'** — The aggregation function to apply. Defaults to `SUM`.

## Examples

Pivots annual sales figures by region:

```sql
TRANSFORM #sales_matrix
FROM #sales_summary
USING PIVOT (
  ROW_FIELDS = 'Region',
  PIVOT_FIELD = 'Year',
  VALUE_FIELD = 'SalesAmount',
  AGGREGATE = 'SUM'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
