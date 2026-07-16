# ANALYZE
Collects statistics about a temp table or result set, including row count, column cardinality, null ratios, and min/max values.

## Syntax
```sql
-- Analyze all columns
ANALYZE #table_name;

-- Analyze specific columns only
ANALYZE #table_name (col1, col2, ...);

-- Capture statistics into a temp table
ANALYZE #table_name INTO #stats;
```

## Examples
```sql
-- Collect full statistics on a loaded dataset
SELECT * FROM SalesDB.dbo.Orders WHERE order_date >= @start INTO #orders;
ANALYZE #orders;

-- Analyze only the columns involved in a join and GROUP BY
ANALYZE #orders (customer_id, region, amount);

-- Capture statistics for review before a complex join
ANALYZE #orders INTO #order_stats;
SELECT column_name, distinct_values, null_ratio, min_value, max_value
  FROM #order_stats
  ORDER BY distinct_values DESC;
```

## Notes
- Output columns include: `column_name`, `row_count`, `distinct_values`, `null_ratio`, `min_value`, `max_value`.
- Statistics are advisory. They inform the query planner but do not change data.
- ANALYZE is most useful on large temp tables before complex joins or GROUP BY operations.
- Run ANALYZE before EXPLAIN to give the optimizer accurate cardinality data.
- Only applies to `#temp` tables in the current session. ANALYZE cannot be run against remote connector tables directly.
- See: EXPLAIN, SELECT, CREATE

References:
- [Statements](../README.md)
