# EXPLAIN ANALYZE
Executes a query and returns its annotated execution plan with observed runtime metrics.

## Syntax
```sql
EXPLAIN ANALYZE SELECT ...;
EXPLAIN ANALYZE SELECT ... INTO #plan;
```

## Examples
```sql
-- Inspect the execution plan for a loaded dataset query
SELECT * INTO #orders FROM SalesDB.dbo.Orders WHERE order_date >= @start;
EXPLAIN ANALYZE
SELECT customer_id, region, SUM(amount) AS total INTO #plan
FROM #orders
GROUP BY customer_id, region;

SELECT * FROM #plan;
```

## Notes
- `EXPLAIN ANALYZE` runs the query; use plain `EXPLAIN` when execution would mutate data or be expensive.
- `INTO #plan` captures the returned plan rows for inspection.
- See: EXPLAIN, SELECT

References:
- [Statements](../README.md)
