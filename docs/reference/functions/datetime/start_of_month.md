# START_OF_MONTH

Returns the first day of the month containing a given date.

## Syntax

```sql
START_OF_MONTH(date)
```

## Parameters

- **date** - Reference date.

## Returns

Returns midnight on the first day of that month as a `DATE`. Any time component in the input is discarded.

## Null Behavior

Returns `NULL` when `date` is `NULL` or cannot be interpreted as a date.

## Examples

```sql
SELECT START_OF_MONTH('2026-07-26') AS month_start;
-- 2026-07-01
```

```sql
-- Roll daily rows up to a month key
SELECT
  START_OF_MONTH(order_date) AS month,
  SUM(amount)                AS monthly_total
FROM #orders
GROUP BY START_OF_MONTH(order_date);
```

## References

- [Functions](../README.md)
- [END_OF_MONTH](end_of_month.md)
- [EOMONTH](eomonth.md)
- [DATETRUNC](datetrunc.md)
