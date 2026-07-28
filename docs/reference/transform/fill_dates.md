# FILL_DATES

Fills missing daily rows in a time-series dataset.

```sql
TRANSFORM #target
FROM #source
USING FILL_DATES (
  DATE_COL = 'date_column',
  GAPS_FILL = default_value,
  BY_GROUP = 'group_column[, ...]'
);
```

- **DATE_COL = 'date_column'** — Name of the column containing dates. Must be present in the source table and contain valid date values.
- **GAPS_FILL = default_value** — The value to populate in missing columns for gap-filled rows. Defaults to `0`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of group columns. When specified, gap filling is performed independently within each partition/group.

## Examples

Fills missing daily sales records per region, defaulting missing quantities to 0:

```sql
TRANSFORM #sales_filled
FROM #sales
USING FILL_DATES (
  DATE_COL = 'OrderDate',
  GAPS_FILL = 0,
  BY_GROUP = 'Region'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
