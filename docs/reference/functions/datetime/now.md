# NOW

Returns the current UTC date and time. `NOW()` is an alias for `GETDATE()`.

## Syntax

```sql
NOW()
```

## Returns

Returns a `DATETIME` containing the current UTC date and time.

## Null Behavior

This function does not take arguments and never returns `NULL`.

## Remarks

- `NOW()` is the preferred cross-dialect alias.
- `GETDATE()` matches T-SQL convention.

## Examples

```sql
SELECT NOW() AS captured_at;
```

```sql
SELECT job_id, DATEDIFF(SECOND, start_time, NOW()) AS elapsed_seconds
FROM #jobs;
```

## References

- [Functions](../README.md)
- [GETDATE](getdate.md)
- [CURRENT_TIMESTAMP](current_timestamp.md)
