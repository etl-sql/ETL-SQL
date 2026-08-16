# QUERY-SYNTAX Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ASOF JOIN](asof-join.md) | A nearest-match join. For each left row it returns the single closest right row satisfying one inequality after any equality keys. It is ideal for ... |
| [CASE](case.md) | Conditional value expression; usable anywhere an expression is valid (SELECT, WHERE, SET, etc.). |
| [FILTER](filter.md) | Restricts the rows considered by an aggregate function or aggregate window function. |
| [GROUP BY ALL & Positional References](group-by-all.md) | Two ergonomic `GROUP BY` / `ORDER BY` conveniences. |
| [ILIKE](ilike.md) | Performs case-insensitive pattern matching. |
| [IS [NOT] DISTINCT FROM](is-distinct-from.md) | Null-safe comparison operator. Treats `NULL` as an ordinary comparable value instead of producing `UNKNOWN`, so it never yields `NULL`. |
| [LATERAL](lateral.md) | The ANSI/DuckDB/PostgreSQL spelling of `CROSS APPLY` / `OUTER APPLY`. The right-hand subquery is **correlated**: it may reference columns from the ... |
| [MATCH_RECOGNIZE](match-recognize.md) | Identifies sequences of rows that match a pattern, similar to a regex applied to ordered result sets. |
| [PIVOT / UNPIVOT](pivot.md) | Rotates rows into columns (PIVOT) or columns into rows (UNPIVOT). |
| [QUALIFY](qualify.md) | Filters the results of window functions. Evaluated after window functions have been computed. |
| [SELECT conveniences (star modifiers, ORDER BY ALL, count(), separators, trailing commas)](select-modifiers.md) | Modern, DuckDB/Snowflake-style ergonomics for the SELECT statement. |
| [Set Operations](set-operations.md) | - Statement Reference |
| [WATERMARK](watermark.md) | Declarative incremental watermarking syntax attached to table references in `SELECT` queries (`WITH (WATERMARK = ...)`). Automatically retrieves th... |
| [WINDOW](window.md) | WINDOW defines reusable named window specifications for analytic functions in a `SELECT` query. Named windows avoid repeating the same `PARTITION B... |
| [WITH (CTE)](with.md) | Defines one or more Common Table Expressions (CTEs) scoped to the following SELECT statement. |
