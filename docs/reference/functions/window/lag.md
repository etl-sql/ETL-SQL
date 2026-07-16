# LAG

Returns the value of an expression from a previous row within the partition.

## Syntax

```sql
LAG(expression)
LAG(expression, offset)
LAG(expression, offset, default)
  OVER (
    [PARTITION BY column_name [, ...]]
    ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Parameters

- **expression** - Column or expression to read from a prior row.
- **offset** - Optional number of rows to look back. Defaults to `1`.
- **default** - Optional value to return when the requested prior row does not exist. Defaults to `NULL`.

## Returns

Returns the same type as `expression`.

## Null Behavior

Returns `default` when the target row is outside the partition. If `default` is omitted, returns `NULL`.

## Remarks

- `ORDER BY` inside `OVER (...)` is required for deterministic results.
- `PARTITION BY` restarts the offset calculation for each partition.

## Examples

```sql
SELECT
  sale_date,
  revenue,
  LAG(revenue) OVER (ORDER BY sale_date) AS previous_revenue
FROM #daily_sales;
```

```sql
SELECT
  sale_date,
  revenue,
  revenue - LAG(revenue, 1, 0) OVER (ORDER BY sale_date) AS revenue_change
FROM #daily_sales;
```

## References

- [Standard Library](../standard-library.md)
- [LEAD](lead.md)
- [FIRST_VALUE](first_value.md)
