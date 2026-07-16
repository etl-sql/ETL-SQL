# ROW_NUMBER

Assigns a unique sequential integer to each row within a partition, starting at 1.

## Syntax

```sql
ROW_NUMBER()
  OVER (
    [PARTITION BY column_name [, ...]]
    ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Returns

Returns a `BIGINT` row number. Tied sort values still receive distinct row numbers.

## Null Behavior

`ROW_NUMBER` does not return `NULL`.

## Remarks

- `ORDER BY` is required. Without `PARTITION BY`, numbering is across the entire result set.
- Use for pagination, deduplication, and top-N-per-group queries.

## Examples

```sql
SELECT *
FROM (
  SELECT
    *,
    ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY order_date DESC) AS rn
  FROM #orders
) AS ranked
WHERE rn = 1;
```

```sql
SELECT *
FROM (
  SELECT *, ROW_NUMBER() OVER (ORDER BY last_name) AS rn
  FROM #customers
) AS page_rows
WHERE rn BETWEEN 21 AND 30;
```

## References

- [Standard Library](../standard-library.md)
- [RANK](rank.md)
- [DENSE_RANK](dense_rank.md)
