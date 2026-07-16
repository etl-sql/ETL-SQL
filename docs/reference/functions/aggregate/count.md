# COUNT

Returns the number of rows or non-NULL values in a group or window.

## Syntax

```sql
COUNT(*)
COUNT(expression)
COUNT(DISTINCT expression)
COUNT(*) OVER (...)
```

## Parameters

- **expression** - Optional column or expression to count.

## Returns

Returns a `BIGINT` row count or non-NULL value count.

## Null Behavior

- `COUNT(*)` counts all rows, including rows where selected columns contain `NULL`.
- `COUNT(expression)` counts only non-NULL expression values.
- `COUNT(DISTINCT expression)` counts unique non-NULL expression values.

## Remarks

Use `COUNT(*) OVER (...)` to add partition counts without collapsing rows.

## Examples

```sql
SELECT COUNT(*) AS total_rows
FROM #orders;
```

```sql
SELECT COUNT(email) AS users_with_email
FROM #users;
```

```sql
SELECT region, COUNT(*) OVER (PARTITION BY region) AS region_count
FROM #sales;
```

## References

- [Standard Library](../standard-library.md)
- [SUM](sum.md)
- [AVG](avg.md)
