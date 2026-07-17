# SECOND

Returns the second component of a datetime or time value as an integer from `0` through `59`.

## Syntax

```sql
SECOND(date)
```

## Parameters

- **date** - Source `DATETIME` or `TIME` value.

## Returns

Returns an `INT` second value.

## Null Behavior

Returns `NULL` when `date` is `NULL`.

## Examples

```sql
SELECT SECOND('2026-05-17 14:30:45') AS order_second;
```

```sql
SELECT job_id, SECOND(finished_at) AS finished_second
FROM #jobs;
```

## References

- [Functions](../README.md)
- [HOUR](hour.md)
- [MINUTE](minute.md)
- [DATEPART](datepart.md)
