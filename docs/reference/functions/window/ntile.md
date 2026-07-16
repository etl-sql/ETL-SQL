# NTILE
Divides rows within a partition into N approximately equal buckets and assigns a bucket number.

**Category:** Window

## Syntax
```sql
NTILE(buckets) OVER (
    [PARTITION BY col1, ...]
    ORDER BY colA [ASC|DESC], ...
)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `buckets` | `INT` | Number of groups to divide rows into |

## Returns
`INT` — Bucket number from 1 to `buckets`. Rows are distributed as evenly as possible; earlier buckets get one extra row when the count isn't divisible.

## Example
```sql
-- Quartile analysis
SELECT customer_id, total_spend,
    NTILE(4) OVER (ORDER BY total_spend DESC) AS spend_quartile
FROM #customers;
-- quartile 1 = top 25% spenders

-- Decile assignment
SELECT *, NTILE(10) OVER (ORDER BY score) AS decile FROM #students;
```

## See Also
- [Standard Library — §13.2 Ranking Functions](../../../guides/getting-started.md#132-ranking-functions)
- Related: [`RANK`](rank.md), `PERCENT_RANK`, [`PERCENTILE_CONT`](../general/percentile_cont.md)
