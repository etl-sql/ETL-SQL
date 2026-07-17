# DAY

Returns the day-of-month component of a date as an integer (1–31).

## Syntax

```sql
DAY(date)
```

## Parameters

- **date** - Source date or datetime value.

## Returns

Returns an `INT` day-of-month value from `1` through `31`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT DAY('2026-05-17') AS day_of_month;
```

```sql
SELECT *
FROM #orders
WHERE DAY(order_date) = 1;
```

## References

- [Functions](../README.md)
- [YEAR](year.md)
- [MONTH](month.md)
- [DATEPART](datepart.md)
