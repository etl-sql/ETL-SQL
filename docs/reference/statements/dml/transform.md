# TRANSFORM

Applies a table-level transformation algorithm to a source table, writing the output to a target table.

```sql
TRANSFORM #target
FROM #source
USING algorithm_name (
  parameter = value,
  ...
);
```

- **#target** — The target temp table to write the output rows into.
- **FROM #source** — The source temp table or dataset containing the input rows.
- **USING algorithm_name** — The transformation algorithm to apply (e.g., `FILL_DATES`).
- **parameter = value** — Algorithm-specific options passed inside parentheses.

## Examples

### FILL_DATES

Fills missing daily rows in a date series, optionally partitioned by groups:

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

- [Data Prep Helpers](../data-prep.md)
- [Syntax Index](../../../syntax-index.md)
