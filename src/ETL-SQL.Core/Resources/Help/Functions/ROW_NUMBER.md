# ROW_NUMBER
Assigns a unique sequential integer to each row within a partition, starting at 1.

**Category:** Window

## Syntax
```sql
ROW_NUMBER() OVER (
    [PARTITION BY col1, col2, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Returns
`BIGINT` — A unique row number per partition. No ties; each row gets a distinct number.

## Remarks
- `ORDER BY` is required. Without `PARTITION BY`, numbering is across the entire result set.
- Use for pagination, deduplication, and top-N-per-group queries.

## Example
```sql
-- Top 1 per customer (deduplication)
SELECT * FROM (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY order_date DESC) AS rn
    FROM #orders
) t WHERE rn = 1;

-- Pagination: page 3, 10 rows each
SELECT * FROM (
    SELECT *, ROW_NUMBER() OVER (ORDER BY last_name) AS rn FROM #customers
) t WHERE rn BETWEEN 21 AND 30;
```

## See Also
- [Standard Library — §13.2 Ranking Functions](../../../../../Docs/Reference/Standard_Library.md#132-ranking-functions)
- Related: [`RANK`](RANK.md), [`DENSE_RANK`](DENSE_RANK.md)
