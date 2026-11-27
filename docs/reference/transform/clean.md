# CLEAN

Applies ordered text cleaning and standardization rules across multiple or all string columns in one `TRANSFORM` statement using the same engine as `CLEAN_TEXT` and `STANDARDIZE`.

```sql
TRANSFORM #target
[FROM #source]
USING CLEAN (
  COLS = '*' | 'col1, col2[, ...]',
  RULES = 'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM'
);
```

- **COLS = '*' | 'col1, col2[, ...]'** — Optional column selector. Use `'*'` (default) to clean all string-typed columns while leaving numeric/date columns untouched, or pass a comma-separated list of source column names.
- **RULES = '...'** — Optional comma-separated list of rules applied in order (`TRIM`, `COLLAPSE_WHITESPACE`, `STRIP_CONTROL`, `NULL_IF_EMPTY`, `UPPER`, `LOWER`, `TITLE`, `INITCAP`, `PHONE`, `EMAIL`, `POSTAL_CODE`). Defaults to `'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM'`.
- **Null and schema behavior** — Preserves all source columns and their schema types. `NULL` cells stay `NULL`; `NULL_IF_EMPTY` turns empty strings into `NULL`. Honors `SET WHAT_IF ON` without mutating `#target`.

## Examples

Clean all string columns in a staging table:

```sql
TRANSFORM #customers_clean
FROM #customers_raw
USING CLEAN (
  COLS = '*',
  RULES = 'STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM, NULL_IF_EMPTY'
);
```

Standardize a specific email column in place:

```sql
TRANSFORM #customers_clean
USING CLEAN (
  COLS = 'Email',
  RULES = 'EMAIL'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [CLEAN_TEXT](../functions/string/clean_text.md)
- [STANDARDIZE](../functions/string/standardize.md)
- [Syntax Index](../../syntax-index.md)
