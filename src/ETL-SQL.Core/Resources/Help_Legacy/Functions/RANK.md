# RANK
Assigns a rank to each row within a partition. Tied rows receive the same rank; the next rank has a gap.

**Category:** Window

## Syntax
```sql
RANK() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Returns
`BIGINT` — Rank with gaps for ties (1, 1, 3, 4, …). Requires `ORDER BY`.

## Example
```sql
SELECT product, revenue,
    RANK() OVER (PARTITION BY category ORDER BY revenue DESC) AS rank_in_category
FROM #sales;
```

## See Also
- [Standard Library — §13.2 Ranking Functions](../../../../../Docs/Reference/Standard_Library.md#132-ranking-functions)
- Related: [`DENSE_RANK`](DENSE_RANK.md), [`ROW_NUMBER`](ROW_NUMBER.md), [`NTILE`](NTILE.md)
