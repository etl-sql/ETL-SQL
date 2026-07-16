# DENSE_RANK
Assigns a rank to each row with no gaps for tied ranks.

**Category:** Window

## Syntax
```sql
DENSE_RANK() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Returns
`BIGINT` — Rank without gaps (1, 1, 2, 3, …). Requires `ORDER BY`.

## Remarks
- Unlike `RANK`, tied rows share a rank and the next rank does **not** skip.

## Example
```sql
SELECT name, score,
    DENSE_RANK() OVER (ORDER BY score DESC) AS rank
FROM #leaderboard;
-- → scores 100, 100, 95, 90 get ranks 1, 1, 2, 3
```

## See Also
- [Standard Library — §13.2 Ranking Functions](../../../../../Docs/Reference/Standard_Library.md#132-ranking-functions)
- Related: [`RANK`](RANK.md), [`ROW_NUMBER`](ROW_NUMBER.md)
