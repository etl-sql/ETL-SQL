# FIRST_VALUE

Returns the first value in the current window frame.

## Syntax

```sql
FIRST_VALUE(expression) OVER (
  [PARTITION BY partition_expression [, ...]]
  ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Parameters

- **expression** - Value to return from the first row in the window frame.
- **PARTITION BY** - Optional partition that resets the window per group.
- **ORDER BY** - Required ordering that defines the first row.

## Returns

Returns the same type as `expression`.

## Null Behavior

If the first row's expression value is `NULL`, `FIRST_VALUE` returns `NULL`.

## Remarks

- `FIRST_VALUE` depends on the window ordering. Use deterministic tie-breakers when duplicate sort keys are possible.
- Use [`LAST_VALUE`](last_value.md) for the final value in the frame.

## Examples

```sql
SELECT
  customer_id,
  order_date,
  FIRST_VALUE(order_date) OVER (
    PARTITION BY customer_id
    ORDER BY order_date
  ) AS first_order_date
FROM #orders;
```

## References

- [Standard Library](../standard-library.md)
- [Window Query Syntax](../../statements/query-syntax/window.md)
- [LAST_VALUE](last_value.md)
