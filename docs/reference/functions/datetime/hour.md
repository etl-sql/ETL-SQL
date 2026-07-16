# HOUR

Returns the hour component of a datetime as an integer (0–23).

## Syntax

```sql
HOUR(date)
```

## Parameters

- **date** - Source datetime or time value.

## Returns

Returns an `INT` hour value from `0` through `23`.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT HOUR('2026-05-17 14:30:00') AS order_hour;
```

```sql
SELECT HOUR(GETDATE()) AS current_hour;
```

```sql
SELECT HOUR(event_time) AS event_hour, COUNT(*) AS event_count
FROM #log
GROUP BY HOUR(event_time)
ORDER BY event_hour;
```

## References

- [Standard Library](../standard-library.md)
- [MINUTE](minute.md)
- [SECOND](second.md)
- [DATEPART](datepart.md)
