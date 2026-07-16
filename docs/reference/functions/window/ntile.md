# NTILE

Divides rows within a partition into N approximately equal buckets and assigns a bucket number.

## Syntax

```sql
NTILE(buckets)
  OVER (
    [PARTITION BY column_name [, ...]]
    ORDER BY sort_expression [ASC|DESC] [, ...]
)
```

## Parameters

- **buckets** - Number of groups to divide rows into.

## Returns

Returns an `INT` bucket number from `1` through `buckets`.

## Null Behavior

`NTILE` does not return `NULL`.

## Remarks

- Rows are distributed as evenly as possible.
- Earlier buckets receive one extra row when the partition size is not evenly divisible by `buckets`.
- `ORDER BY` inside `OVER (...)` is required.

## Examples

```sql
SELECT
  customer_id,
  total_spend,
  NTILE(4) OVER (ORDER BY total_spend DESC) AS spend_quartile
FROM #customers;
```

```sql
SELECT
  *,
  NTILE(10) OVER (ORDER BY score) AS score_decile
FROM #students;
```

## References

- [Standard Library](../standard-library.md)
- [RANK](rank.md)
- [PERCENTILE_CONT](../general/percentile_cont.md)
