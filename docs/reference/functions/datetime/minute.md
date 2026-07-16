# MINUTE

Returns the minute component of a datetime or time value as an integer from `0` through `59`.

## Syntax

```sql
MINUTE(date)
```

## Parameters

- **date** - Source `DATETIME` or `TIME` value.

## Returns

Returns an `INT` minute value.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT MINUTE('2026-05-17 14:30:00') AS order_minute;
```

```sql
SELECT MINUTE(GETDATE()) AS current_minute;
```

## References

- [Standard Library](../standard-library.md)
- [HOUR](hour.md)
- [SECOND](second.md)
- [DATEPART](datepart.md)
