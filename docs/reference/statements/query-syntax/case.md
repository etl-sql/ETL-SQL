# CASE

Conditional value expression; usable anywhere an expression is valid (SELECT, WHERE, SET, etc.).

## Simple Form - Matches a single value

```sql
SELECT
  OrderId,
  CASE Status
    WHEN 'P' THEN 'Pending'
    WHEN 'S' THEN 'Shipped'
    WHEN 'C' THEN 'Cancelled'
    ELSE 'Unknown'
  END AS StatusLabel
FROM #orders;
```

## Searched Form - Evaluates boolean conditions

```sql
SELECT
  OrderId,
  CASE
    WHEN Total > 1000 THEN 'High'
    WHEN Total > 250  THEN 'Medium'
    ELSE 'Low'
  END AS ValueBand
FROM #orders;
```

## Arrow Conditional Shorthand (`=>` and `:`)

ETL-SQL supports a compact arrow conditional shorthand that compiles directly to a `CASE` expression at parse time:

```sql
-- Two-branch ternary
SELECT OrderId, Status = 'P' => 'Pending' : 'Completed' AS StatusLabel
FROM #orders;

-- Multi-branch chaining (flattens into one CASE expression)
SELECT
  OrderId,
  Total > 1000 => 'High'
: Total > 250  => 'Medium'
: 'Low' AS ValueBand
FROM #orders;
```

## Remarks

- `ELSE` is optional in standard `CASE ... END` (omitting it defaults to `NULL`); however, the trailing `: else` branch is **required** when using the `=>` arrow shorthand.
- `CASE` expressions can be nested.
- Works in `SELECT` columns, `WHERE` clauses, `ORDER BY`, `GROUP BY`, and `SET @var = CASE ... END`.
- The result type is inferred from the `THEN`/`ELSE` branches; mixed numeric types promote to `DECIMAL`.

## References

- [Statements](../README.md)
- [Expressions and Operators](../expressions-and-operators.md)
- [IIF Function](../../functions/conversion/iif.md)
- [COALESCE Function](../../functions/null-handler/coalesce.md)
