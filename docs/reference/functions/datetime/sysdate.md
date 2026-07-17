# SYSDATE

Returns the current date and time from the system hosting the ETL-SQL engine. `SYSDATE` is the Oracle-compatible alias.

## Syntax

```sql
SYSDATE()
```

## Parameters

None.

## Returns

Returns a `DATETIME`.

## Null Behavior

`SYSDATE()` takes no arguments and never returns `NULL`.

## Remarks

- Use `SYSDATE()` when porting Oracle-oriented scripts.
- Use [`GETDATE`](getdate.md) for T-SQL-style current local datetime.
- Use [`NOW`](now.md) for the engine's current timestamp alias.

## Examples

```sql
SELECT SYSDATE() AS captured_at;
```

```sql
SELECT *
FROM #events
WHERE event_time < SYSDATE();
```

## References

- [Functions](../README.md)
- [GETDATE](getdate.md)
- [NOW](now.md)
