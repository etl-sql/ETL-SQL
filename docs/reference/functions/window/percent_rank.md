# PERCENT_RANK

Returns the relative rank of the current row within its partition, as a value between `0` and `1`.

## Syntax

```sql
PERCENT_RANK() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Parameters

None. `PERCENT_RANK` takes no arguments; it operates over the ordered rows of the
window.

## Returns

Returns a numeric value computed as `(rank - 1) / (row_count - 1)`, where `rank`
is the row's [`RANK`](rank.md) within the partition and `row_count` is the number
of rows in the partition. The first row is always `0`; a partition containing a
single row returns `0`.

## Null Behavior

`PERCENT_RANK` does not evaluate input arguments. Ordering of `NULL`s follows the
`ORDER BY` expression semantics.

## Remarks

- `ORDER BY` is **required** inside the `OVER` clause; omitting it raises an error.
- Use [`CUME_DIST`](cume_dist.md) for cumulative distribution, or
  [`RANK`](rank.md) / [`ROW_NUMBER`](row_number.md) for integer positions.

## Examples

```sql
SELECT product, revenue,
    PERCENT_RANK() OVER (ORDER BY revenue) AS revenue_percentile
FROM #sales;
```

```sql
SELECT category, product, revenue,
    PERCENT_RANK() OVER (PARTITION BY category ORDER BY revenue DESC) AS pct_rank_in_category
FROM #sales;
```

## References

- [Functions](../README.md)
- [CUME_DIST](cume_dist.md)
- [RANK](rank.md)
- [NTILE](ntile.md)
