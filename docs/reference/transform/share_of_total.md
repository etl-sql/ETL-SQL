# SHARE_OF_TOTAL

Computes the percentage contribution of a row's numeric value relative to the group or grand total.

```sql
TRANSFORM #target
FROM #source
USING SHARE_OF_TOTAL (
  VALUE_COL = 'value_column',
  BY_GROUP = 'group_column[, ...]',
  SHARE_COL = 'share_column_name'
);
```

- **VALUE_COL = 'value_column'** — The numeric column containing values to compute shares for.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. If specified, shares are computed relative to the total of the corresponding partition group. If omitted, shares are computed relative to the grand total.
- **SHARE_COL = 'share_column_name'** — Output column name. Defaults to `'{VALUE_COL}_Share'`.

## Examples

Calculates sales share of each category relative to its regional total:

```sql
TRANSFORM #category_regional_shares
FROM #category_sales
USING SHARE_OF_TOTAL (
  VALUE_COL = 'Sales',
  BY_GROUP = 'Region',
  SHARE_COL = 'RegionalSalesShare'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
