# LATERAL
The ANSI/DuckDB/PostgreSQL spelling of `CROSS APPLY` / `OUTER APPLY`. The right-hand subquery is **correlated**: it may reference columns from the tables to its left and is evaluated once per left row, like a relational for-each.

## Syntax
```sql
FROM <left>
[CROSS] JOIN LATERAL (<subquery>) AS <alias>            -- = CROSS APPLY
FROM <left>, LATERAL (<subquery>) AS <alias>            -- = CROSS APPLY (comma form)
FROM <left>
[INNER] JOIN LATERAL (<subquery>) AS <alias> ON <cond>  -- = CROSS APPLY + ON filter
FROM <left>
LEFT [OUTER] JOIN LATERAL (<subquery>) AS <alias> ON <cond>  -- = OUTER APPLY + ON filter
```

## Mapping to APPLY
| LATERAL form | Equivalent |
| :--- | :--- |
| `CROSS JOIN LATERAL` / `, LATERAL` | `CROSS APPLY` (inner; rows with no right match are dropped) |
| `[INNER] JOIN LATERAL ... ON <cond>` | `CROSS APPLY`, then `<cond>` filters the combined rows |
| `LEFT [OUTER] JOIN LATERAL ... ON <cond>` | `OUTER APPLY`, then `<cond>` filters; unmatched left rows are kept with NULLs |

## Example
```sql
-- Most recent order line per order
SELECT o.OrderId, t.LineItem
FROM Orders AS o
LEFT JOIN LATERAL (
    SELECT TOP 1 * FROM OrderLines WHERE OrderId = o.OrderId ORDER BY CreatedAt DESC
) AS t ON true;
```

## Notes
- The correlation works through the same outer-row mechanism as `CROSS APPLY` / `OUTER APPLY`.
- Unlike `APPLY`, a `LATERAL` join may carry an explicit `ON <condition>`, applied as an additional filter over the correlated rows. The idiomatic outer form is `LEFT JOIN LATERAL (...) ON true`, with the real filtering inside the subquery's `WHERE`.
- `LATERAL` is only valid with `[INNER] JOIN`, `LEFT JOIN`, or `CROSS JOIN`.

References:
- [Statements](../README.md)
