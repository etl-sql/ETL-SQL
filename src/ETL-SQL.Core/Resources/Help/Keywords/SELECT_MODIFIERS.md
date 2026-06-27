# SELECT conveniences (star modifiers, ORDER BY ALL, count(), separators, trailing commas)
Modern, DuckDB/Snowflake-style ergonomics for the SELECT statement.

## Star modifiers
Adjust a `*` projection inline. Modifiers apply in the order `EXCLUDE` → `REPLACE` → `RENAME`.

```sql
-- Drop columns from the wildcard
SELECT * EXCLUDE (password, internal_notes) FROM users;

-- Keep every column but substitute an expression for one of them
SELECT * REPLACE (UPPER(name) AS name) FROM users;

-- Keep every column but rename one
SELECT * RENAME (id AS user_id) FROM users;

-- Combined
SELECT * EXCLUDE (secret) RENAME (id AS user_id) FROM users;
```

## ORDER BY ALL
Order by every output column, left to right. Add `DESC` to reverse all of them.
```sql
SELECT region, product, total FROM #sales ORDER BY ALL;
SELECT region, product, total FROM #sales ORDER BY ALL DESC;
```

## count() shorthand
A zero-argument `COUNT()` is treated as `COUNT(*)`.
```sql
SELECT count() FROM #orders;          -- same as COUNT(*)
```

## Underscore digit separators
`_` may separate digits inside a numeric literal for readability; it is ignored.
```sql
SELECT 1_000_000 AS one_million, 3_14 AS pi_ish;
```

## Trailing commas
An optional trailing comma is tolerated at the end of `SELECT`, `GROUP BY`, and `ORDER BY` lists, and in function-argument lists.
```sql
SELECT region, total,
FROM #sales
GROUP BY region,
ORDER BY region,;
```

## Notes
- Star modifiers and `ORDER BY ALL` are resolved locally and are not pushed down to remote sources.
- Star modifiers currently apply to an unqualified `*` (and `t.*` parses, but modifiers target the unqualified form).
- `EXCLUDE`/`REPLACE`/`RENAME` match column names case-insensitively, by base name.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md#511-modern-select-conveniences)
