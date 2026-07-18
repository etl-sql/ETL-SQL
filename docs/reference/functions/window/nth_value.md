# NTH_VALUE

Returns the value of an expression evaluated at the n-th row of the window frame.

## Syntax

```sql
NTH_VALUE(expression, n) OVER (
    [PARTITION BY col1, col2, ...]
    [ORDER BY colA [ASC|DESC], ...]
    [frame_clause]
)
```

## Parameters

- **expression** - The value to return from the n-th row.
- **n** - A 1-based integer position within the window frame.

## Returns

Returns the value of `expression` at the `n`-th row of the current window frame
(or the whole partition when no frame clause is present). Returns `NULL` when `n`
is outside the frame's row range.

## Null Behavior

If `n` resolves to a position beyond the available rows, the result is `NULL`.
The value returned is whatever `expression` evaluates to at that row, including
`NULL`.

## Remarks

- When a frame clause is present, `n` is counted within the resolved frame; with
  no frame, it is counted across the full partition.
- Use [`FIRST_VALUE`](first_value.md) / [`LAST_VALUE`](last_value.md) for the
  first or last row of the frame, and [`LAG`](lag.md) / [`LEAD`](lead.md) for
  values at a fixed offset from the current row.

## Examples

```sql
-- Second-highest revenue product per category, repeated on every row
SELECT category, product, revenue,
    NTH_VALUE(product, 2) OVER (
        PARTITION BY category ORDER BY revenue DESC
        ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING
    ) AS runner_up
FROM #sales;
```

```sql
SELECT day, price,
    NTH_VALUE(price, 3) OVER (ORDER BY day) AS third_day_price
FROM #prices;
```

## References

- [Functions](../README.md)
- [FIRST_VALUE](first_value.md)
- [LAST_VALUE](last_value.md)
- [LAG](lag.md)
