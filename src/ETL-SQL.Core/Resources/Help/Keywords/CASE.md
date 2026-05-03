# CASE
Conditional value expression; usable anywhere an expression is valid (SELECT, WHERE, SET, etc.).

## Simple form — matches a single value
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

## Searched form — evaluates boolean conditions
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

## Notes
- `ELSE` is optional; omitting it returns NULL when no branch matches.
- CASE expressions can be nested.
- Works in SELECT columns, WHERE clauses, ORDER BY, GROUP BY, and SET @var = CASE ... END.
- The result type is inferred from the THEN/ELSE branches; mixed numeric types promote to DECIMAL.
- See: IF, SET