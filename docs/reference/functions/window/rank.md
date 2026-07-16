# RANK

Assigns a rank to each row within a partition. Tied rows receive the same rank; the next rank has a gap.

## Syntax

```sql
RANK() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Returns

Returns a `BIGINT` rank value with gaps after ties.

## Null Behavior

`RANK` does not evaluate input arguments. Null ordering depends on the `ORDER BY` expression semantics.

## Remarks

- `ORDER BY` is required inside the `OVER` clause.
- Use [`DENSE_RANK`](dense_rank.md) when ranks should not have gaps after ties.
- Use [`ROW_NUMBER`](row_number.md) when every row must receive a unique sequence number.

## Examples

```sql
SELECT product, revenue,
    RANK() OVER (PARTITION BY category ORDER BY revenue DESC) AS rank_in_category
FROM #sales;
```

```sql
SELECT salesperson, month, revenue,
    RANK() OVER (PARTITION BY month ORDER BY revenue DESC) AS monthly_rank
FROM #sales_by_month;
```

## References

- [Standard Library](../standard-library.md)
- [DENSE_RANK](dense_rank.md)
- [ROW_NUMBER](row_number.md)
- [NTILE](ntile.md)
