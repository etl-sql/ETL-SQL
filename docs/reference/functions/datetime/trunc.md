# TRUNC

Truncates the time portion of a datetime, returning the date at midnight.

## Syntax

```sql
TRUNC(date)
```

## Parameters

- **date** - Datetime value to truncate.

## Returns

Returns the date portion with time set to midnight.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Remarks

- `TRUNC(date)` is equivalent to casting a datetime to `DATE`.
- Use [`DATETRUNC`](datetrunc.md) when truncating to month, hour, or another date part boundary.

## Examples

```sql
SELECT TRUNC('2026-05-17 14:30:00') AS order_day;
```

```sql
SELECT *
FROM #orders
WHERE TRUNC(order_time) = TRUNC(GETDATE());
```

## References

- [Functions](../README.md)
- [DATETRUNC](datetrunc.md)
- [CAST](../conversion/cast.md)
