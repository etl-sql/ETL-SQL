# CUME_DIST

Returns the cumulative distribution of the current row within its partition — the fraction of rows at or before the current row's position.

## Syntax

```sql
CUME_DIST() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Parameters

None. `CUME_DIST` takes no arguments; it operates over the ordered rows of the
window.

## Returns

Returns a numeric value in the range `(0, 1]`, computed as
`rows_at_or_before / total_rows`, where `rows_at_or_before` counts every row up
to and including the last row of the current peer group (rows that tie on the
`ORDER BY` values), and `total_rows` is the number of rows in the partition. Tied
rows share the same value; the last peer group always returns `1`.

## Null Behavior

`CUME_DIST` does not evaluate input arguments. Ordering of `NULL`s follows the
`ORDER BY` expression semantics.

## Remarks

- `ORDER BY` is **required** inside the `OVER` clause; omitting it raises an error.
- Use [`PERCENT_RANK`](percent_rank.md) for relative rank, or
  [`NTILE`](ntile.md) to bucket rows into groups.

## Examples

```sql
SELECT student, score,
    CUME_DIST() OVER (ORDER BY score) AS score_cume_dist
FROM #exam;
```

```sql
SELECT region, salesperson, revenue,
    CUME_DIST() OVER (PARTITION BY region ORDER BY revenue DESC) AS revenue_cume_dist
FROM #sales;
```

## References

- [Functions](../README.md)
- [PERCENT_RANK](percent_rank.md)
- [NTILE](ntile.md)
- [RANK](rank.md)
