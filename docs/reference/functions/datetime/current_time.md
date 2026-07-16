# CURRENT_TIME

Returns the current time of day.

## Syntax

```sql
CURRENT_TIME()
```

## Parameters

None.

## Returns

Returns a `TIME`.

## Null Behavior

`CURRENT_TIME()` takes no arguments and never returns `NULL`.

## Remarks

- Use `CURRENT_TIME()` when only the time-of-day component is needed.
- Use [`CURRENT_DATE`](current_date.md) for date only.
- Use [`CURRENT_TIMESTAMP`](current_timestamp.md) for date and time.

## Examples

```sql
SELECT CURRENT_TIME();

SELECT HOUR(CURRENT_TIME()) AS current_hour;
```

## References

- [Standard Library](../standard-library.md)
- [CURRENT_DATE](current_date.md)
- [CURRENT_TIMESTAMP](current_timestamp.md)
- [GETDATE](getdate.md)
