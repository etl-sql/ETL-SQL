# LAST_VALUE

Returns the last value in the current window frame.

## Syntax

```sql
LAST_VALUE(expression) OVER (
  [PARTITION BY partition_expression [, ...]]
  ORDER BY sort_expression [ASC|DESC] [, ...]
  [frame_clause]
)
```

## Parameters

- **expression** - Value to return from the last row in the window frame.
- **PARTITION BY** - Optional partition that resets the window per group.
- **ORDER BY** - Required ordering that defines the last row.

## Returns

Returns the same type as `expression`.

## Null Behavior

If the last row's expression value is `NULL`, `LAST_VALUE` returns `NULL`.

## Remarks

- `LAST_VALUE` reads the window frame. With `ORDER BY` and no frame clause the frame is `RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`, so `LAST_VALUE` returns the value of the current row's last peer, which is usually the current row itself.
- To get the last value of the whole partition, write the frame: `ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING`.
- Use deterministic tie-breakers when duplicate sort keys are possible.
- Use [`FIRST_VALUE`](first_value.md) for the first value in the frame.

## Examples

```sql
SELECT
  customer_id,
  order_date,
  LAST_VALUE(order_date) OVER (
    PARTITION BY customer_id
    ORDER BY order_date
    ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING
  ) AS latest_order_date
FROM #orders;
```

## References

- [Functions](../README.md)
- [Window Query Syntax](../../statements/query-syntax/window.md)
- [FIRST_VALUE](first_value.md)
