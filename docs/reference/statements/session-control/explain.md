# EXPLAIN
Shows the execution plan for a SELECT or DML statement without running it.

## Syntax
```sql
EXPLAIN <statement>;

EXPLAIN SELECT * FROM SalesDB.dbo.Orders WHERE year = 2024;

EXPLAIN INSERT INTO #summary SELECT region, SUM(amount) FROM #orders GROUP BY region;
```

## Examples
```sql
-- Inspect the plan for a remote query
EXPLAIN
  SELECT order_id, customer, amount
  FROM SalesDB.dbo.Orders
  WHERE order_date >= '2024-01-01';

-- Capture the plan into a temp table for further analysis
EXPLAIN
  SELECT o.order_id, c.name, o.amount
  FROM #orders o
  JOIN #customers c ON o.customer_id = c.id
  WHERE o.amount > 1000
  INTO #plan;

SELECT operation, target, estimated_rows, pushdown
  FROM #plan
  ORDER BY step_id;
```

## Notes
- Returns a result set describing each plan step: `step_id`, `operation`, `target`, `estimated_rows`, `pushdown`, and `notes`.
- Does not execute the statement — no data is read or written.
- Useful for diagnosing slow queries, verifying SQL pushdown to remote connectors, and understanding join algorithm choices (hash join, nested loop, merge join).
- Results can be captured with `INTO #table` for programmatic inspection.
- Pushdown decisions shown in the plan reflect the capabilities of the target connector — some connectors support partial pushdown only.
- See: ANALYZE, SET, SHOW

References:
- [Grammar](../../../guides/getting-started.md)
