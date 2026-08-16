# QUERY-SYNTAX Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ASOF JOIN](asof-join.md) | A nearest-match join designed for temporal and fuzzy continuous alignments. For each row on the left side, `ASOF JOIN` returns the single closest r... |
| [CASE](case.md) | Conditional value expression that evaluates a list of conditions and returns one of multiple possible result expressions. Usable anywhere an expres... |
| [FILTER](filter.md) | Restricts the rows considered by an aggregate function or aggregate window function. |
| [GROUP BY ALL & Positional References](group-by-all.md) | Two ergonomic `GROUP BY` / `ORDER BY` conveniences. |
| [ILIKE](ilike.md) | Performs case-insensitive pattern matching. |
| [IS [NOT] DISTINCT FROM](is-distinct-from.md) | Null-safe comparison operator. Treats `NULL` as an ordinary comparable value instead of producing `UNKNOWN`, so it never yields `NULL`. |
| [LATERAL](lateral.md) | Correlated subquery join. Allows the right-hand subquery or table-valued expression to reference columns from preceding tables on its left. Evaluat... |
| [MATCH_RECOGNIZE](match-recognize.md) | Row pattern recognition for complex event processing (CEP) over ordered datasets. Identifies sequential patterns across time-series, log streams, a... |
| [PIVOT / UNPIVOT](pivot.md) | Rotates rows into columns (`PIVOT`) for cross-tab analytical matrix reporting, or transposes columns into rows (`UNPIVOT`) to normalize wide datase... |
| [QUALIFY](qualify.md) | Filters the results of window functions directly within the current query block. Evaluated after window calculations are computed, eliminating the ... |
| [SELECT Modifiers & Ergonomic Conveniences](select-modifiers.md) | Modern query ergonomics inspired by DuckDB and Snowflake. Includes inline wildcard projection modifiers (`EXCLUDE`, `REPLACE`, `RENAME`), left-to-r... |
| [Set Operations](set-operations.md) | - Statement Reference |
| [WATERMARK](watermark.md) | Declarative incremental change tracking attached directly to table references in `SELECT` queries via `WITH (WATERMARK = ...)`. The engine automati... |
| [WINDOW](window.md) | WINDOW defines reusable named window specifications for analytic functions in a `SELECT` query. Named windows avoid repeating the same `PARTITION B... |
| [WITH (CTE)](with.md) | Defines one or more Common Table Expressions (CTEs) scoped to the following SELECT statement. |
