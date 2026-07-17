# CURRENT_DATE

Returns the current date (no time component).

## Syntax

```sql
CURRENT_DATE()
```

## Parameters

None.

## Returns

Returns a `DATE`.

## Null Behavior

`CURRENT_DATE()` takes no arguments and never returns `NULL`.

## Remarks

- Use `CURRENT_DATE()` when date-only comparison is needed.
- Use [`CURRENT_TIME`](current_time.md) for time only.
- Use [`CURRENT_TIMESTAMP`](current_timestamp.md) for date and time.

## Examples

```sql
SELECT CURRENT_DATE();

SELECT * FROM #orders WHERE order_date = CURRENT_DATE();
```

## References

- [Functions](../README.md)
- [CURRENT_TIME](current_time.md)
- [CURRENT_TIMESTAMP](current_timestamp.md)
- [GETDATE](getdate.md)
