# LEAD

Returns the value of an expression from a subsequent row within the partition.

## Syntax

```sql
LEAD(expression)
LEAD(expression, offset)
LEAD(expression, offset, default)
  OVER (
    [PARTITION BY column_name [, ...]]
    ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Parameters

- **expression** - Column or expression to read from a later row.
- **offset** - Optional number of rows to look forward. Defaults to `1`.
- **default** - Optional value to return when the requested later row does not exist. Defaults to `NULL`.

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
  LEAD(revenue) OVER (ORDER BY sale_date) AS next_revenue
FROM #daily_sales;
```

```sql
SELECT
  sale_date,
  revenue,
  LEAD(revenue, 7, 0) OVER (ORDER BY sale_date) AS revenue_in_seven_rows
FROM #daily_sales;
```

## References

- [Functions](../README.md)
- [LAG](lag.md)
- [LAST_VALUE](last_value.md)
