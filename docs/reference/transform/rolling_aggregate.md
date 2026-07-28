# ROLLING_AGGREGATE

Smooths noisy data trends (e.g. 7-day moving average) or tracks running/cumulative metrics.

```sql
TRANSFORM #target
FROM #source
USING ROLLING_AGGREGATE (
  VALUE_COL = 'value_column',
  ORDER_COL = 'order_column',
  WINDOW_SIZE = window_size,
  AGGREGATE = 'AVG' | 'SUM' | 'MIN' | 'MAX' | 'COUNT',
  BY_GROUP = 'group_column[, ...]',
  ROLLING_COL = 'rolling_column_name'
);
```

- **VALUE_COL = 'value_column'** — The numeric column to aggregate.
- **ORDER_COL = 'order_column'** — The column used to sort the series sequentially before calculating the rolling window.
- **WINDOW_SIZE = window_size** — The integer size of the trailing rolling window.
- **AGGREGATE = 'aggregate_function'** — The aggregation function to apply over the rolling window: `AVG`, `SUM`, `MIN`, `MAX`, or `COUNT`. Defaults to `AVG`.
- **BY_GROUP = 'group_column[, ...]'** — Optional comma-separated list of columns to partition/group by. Rolling metrics are computed independently within each group.
- **ROLLING_COL = 'rolling_column_name'** — Output column name. Defaults to `'{VALUE_COL}_Rolling'`.

## Examples

Computes a 7-day moving average of daily sales:

```sql
TRANSFORM #sales_smoothed
FROM #daily_sales
USING ROLLING_AGGREGATE (
  VALUE_COL = 'Sales',
  ORDER_COL = 'SalesDate',
  WINDOW_SIZE = 7,
  AGGREGATE = 'AVG'
);
```

## References

- [TRANSFORM](../statements/dml/transform.md)
- [Data Prep Helpers](../statements/data-prep.md)
- [Syntax Index](../../syntax-index.md)
