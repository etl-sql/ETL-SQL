# FILTER
Restricts the rows considered by an aggregate function or aggregate window function.

## Syntax
```sql
AGGREGATE_FUNCTION(column) FILTER (WHERE condition)
```

## Example
Compute total sales and large sales in a single query:
```sql
SELECT department,
       SUM(sales) AS total_sales,
       SUM(sales) FILTER (WHERE sales > 1000) AS large_sales,
       COUNT(*) FILTER (WHERE status = 'Open') AS open_orders
FROM sales_data
GROUP BY department;
```

## Notes
- Evaluated as part of the aggregation phase.
- Supported for both grouped aggregates and window aggregates (e.g. `SUM(amount) FILTER (WHERE amount > 100) OVER (...)`).
- Avoids multiple subqueries or complex `CASE WHEN` logic when computing multiple conditional metrics from the same table.

References:
- [Grammar](../../../guides/getting-started.md#5101-filter-conditional-aggregation)
