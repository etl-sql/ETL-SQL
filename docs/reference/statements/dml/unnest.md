# UNNEST / FLATTEN
Table-valued functions that expand a list/array value into rows. Each element becomes one row in a single `Value` column.

## Usage
Use in the `FROM` clause or via `CROSS APPLY` (for correlated, per-row expansion):

## Syntax

```sql
-- Standalone
SELECT u.Value FROM UNNEST([10, 20, 30]) AS u;          -- 3 rows: 10, 20, 30

-- Correlated (one expansion per left row)
SELECT t.id, u.Value
FROM #orders t
CROSS APPLY UNNEST(t.tag_list) AS u;
```

`FLATTEN` behaves like `UNNEST` but flattens one level of nested lists:
```sql
SELECT u.Value FROM FLATTEN([[1, 2], [3, 4]]) AS u;     -- 1, 2, 3, 4
```

## Notes
- Output is a single column named `Value`.
- These are **table-valued**. Use them in `FROM`/`CROSS APPLY`, not as a bare scalar in the SELECT list.
- A non-list argument yields a single row with that value; NULL yields no rows.
- List literals use `[a, b, c]`. A single-element `[x]` is parsed as a **quoted identifier**, not a one-element list. Use a 2+ element literal, a list column, or `STRING_SPLIT`.

References:
- [Statements](../README.md)


## References

- [Statements](../README.md)
- [SELECT](select.md)
