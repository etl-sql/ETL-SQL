# UNPIVOT

Reshapes wide columns into normalized key/value rows (`MELT` is an accepted synonym for `UNPIVOT`).

```sql
TRANSFORM #target
[FROM #source]
USING UNPIVOT (
  ID_COLS = 'id_column[, ...]',
  VALUE_COLS = 'col1, col2[, ...]',
  NAME_COL = 'Name',
  VALUE_COL = 'Value',
  INCLUDE_NULLS = OFF | ON
);
```

- **ID_COLS = 'id_column[, ...]'** — Optional comma-separated list of identifier columns preserved on every output row with their original schema types.
- **VALUE_COLS = 'col1, col2[, ...]'** — Required comma-separated list of source columns to unpivot into key/value rows.
- **NAME_COL = 'Name'** — Output `VARCHAR` column holding the unpivoted source column name. Defaults to `Name`.
- **VALUE_COL = 'Value'** — Output column holding the unpivoted value. Preserves the shared source type when all `VALUE_COLS` match, widens mixed numeric types to `DECIMAL`/`DOUBLE`, and falls back to `VARCHAR` for incompatible types. Defaults to `Value`.
- **INCLUDE_NULLS = OFF | ON** — Whether to emit rows when an unpivoted cell is `NULL`. Defaults to `OFF`.
- **Output names** — `NAME_COL` and `VALUE_COL` must be distinct and must not collide with `ID_COLS`.

## Examples

Reshapes quarterly sales columns into normalized rows, skipping `NULL` quarters:

```sql
TRANSFORM #sales_long
FROM #sales_wide
USING UNPIVOT (
  ID_COLS = 'Region',
  VALUE_COLS = 'Q1, Q2, Q3, Q4',
  NAME_COL = 'Quarter',
  VALUE_COL = 'Sales',
  INCLUDE_NULLS = OFF
);
```

Using `MELT` while retaining `NULL` values:

```sql
TRANSFORM #sales_long_all
FROM #sales_wide
USING MELT (
  ID_COLS = 'Region',
  VALUE_COLS = 'Q1, Q2, Q3, Q4',
  NAME_COL = 'Quarter',
  VALUE_COL = 'Sales',
  INCLUDE_NULLS = ON
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [PIVOT](pivot.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
