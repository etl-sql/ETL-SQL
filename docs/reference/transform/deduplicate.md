# DEDUPLICATE

Removes duplicate rows based on key columns, with optional sorting to control which row is kept.

```sql
TRANSFORM #target
FROM #source
USING DEDUPLICATE (
  KEY_COLS = 'key_column[, ...]',
  ORDER_BY = 'order_expression',
  KEEP = 'FIRST' | 'LAST'
);
```

- **KEY_COLS = 'key_column[, ...]'** — Comma-separated list of columns used to identify duplicate rows.
- **ORDER_BY = 'order_expression'** — Optional order-by clause expression (e.g. `'Priority DESC, Id ASC'`) used to sort rows within duplicate groups before deciding which row to keep.
- **KEEP = 'FIRST' | 'LAST'** — Which row in the sorted duplicate group to keep. Defaults to `FIRST`.

## Examples

Keeps only the customer record with the highest Priority:

```sql
TRANSFORM #deduped_customers
FROM #customers
USING DEDUPLICATE (
  KEY_COLS = 'CustomerId',
  ORDER_BY = 'Priority DESC',
  KEEP = 'FIRST'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
