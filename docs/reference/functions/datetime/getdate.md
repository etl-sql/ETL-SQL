# GETDATE

Returns the current local date and time of the host machine.

## Syntax

```sql
GETDATE()
```

## Returns

Returns a `DATETIME` containing the current local system date and time.

## Null Behavior

This function does not take arguments and never returns `NULL`.

## Remarks

- `GETDATE()` and `NOW()` are interchangeable. `NOW()` is preferred in cross-dialect contexts.
- For UTC time, use `CURRENT_TIMESTAMP()`.
- `SYSDATE` (no parentheses) is a bare identifier equivalent also supported.

## Examples

```sql
SELECT GETDATE() AS captured_at;
```

```sql
SELECT DATEADD(DAY, -30, GETDATE()) AS one_month_ago;
```

```sql
INSERT INTO #log (created_at) VALUES (GETDATE());
```

## References

- [Functions](../README.md)
- [NOW](now.md)
- [CURRENT_TIMESTAMP](current_timestamp.md)
