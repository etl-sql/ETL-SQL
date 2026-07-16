# CURRENT_TIMESTAMP

Returns the current UTC date and time. `CURRENT_TIMESTAMP` is equivalent to `GETDATE()` and `NOW()`.

## Syntax

```sql
CURRENT_TIMESTAMP()
```

```sql
CURRENT_TIMESTAMP
```

## Returns

Returns a `DATETIME` containing the current UTC date and time.

## Null Behavior

This function does not take arguments and never returns `NULL`.

## Remarks

- Use the bare `CURRENT_TIMESTAMP` form when you want SQL-standard style.
- Use `CURRENT_TIMESTAMP()` when you prefer function-call style in expression lists.

## Examples

```sql
SELECT CURRENT_TIMESTAMP() AS captured_at;
```

```sql
INSERT INTO #audit (event_name, captured_at)
VALUES ('load-started', CURRENT_TIMESTAMP);
```

## References

- [Standard Library](../standard-library.md)
- [GETDATE](getdate.md)
- [NOW](now.md)
