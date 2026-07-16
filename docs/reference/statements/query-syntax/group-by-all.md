# GROUP BY ALL & Positional References
Two ergonomic `GROUP BY` / `ORDER BY` conveniences.

## GROUP BY ALL
Groups by **every** expression in the SELECT list that does not contain an aggregate (or window function), so you don't have to restate the non-aggregated columns.

```sql
-- Equivalent to: GROUP BY region, UPPER(category)
SELECT region, UPPER(category) AS cat, SUM(amount) AS total, COUNT(*) AS n
FROM #sales
GROUP BY ALL;
```

### Notes
- The grouping set is resolved at execution time as "all SELECT expressions that are not aggregates and not window functions."
- If every SELECT column is an aggregate, the result is a single group (no grouping columns), exactly like writing an aggregate query with no `GROUP BY`.
- Combines with `HAVING`, `ORDER BY`, and `QUALIFY` like an ordinary `GROUP BY`.

## Positional references (GROUP BY / ORDER BY)
A bare integer refers to the Nth item in the SELECT list (1-based).

```sql
SELECT region, SUM(amount) AS total
FROM #sales
GROUP BY 1        -- group by region (the 1st select item)
ORDER BY 2 DESC;  -- order by total (the 2nd select item)
```

### Notes
- Only a standalone integer literal is positional. A compound expression such as `1 + 1` is evaluated as an ordinary expression, **not** a position.
- A position outside the range `1..<column count>` is a syntax error.
- Positional references cannot be used when the SELECT list contains `*` (the position would be ambiguous); list the columns explicitly instead.

References:
- [Grammar](../../../guides/getting-started.md)
