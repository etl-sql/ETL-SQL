# GETDATE
Returns the current local date and time of the host machine.

**Category:** Date

## Syntax
```sql
GETDATE()
```

## Returns
`DATETIME` — The current local system date and time.

## Remarks
- `GETDATE()` and `NOW()` are interchangeable. `NOW()` is preferred in cross-dialect contexts.
- For UTC time, use `CURRENT_TIMESTAMP()`.
- `SYSDATE` (no parentheses) is a bare identifier equivalent also supported.

## Example
```sql
SELECT GETDATE();                                    -- → 2026-05-17 14:59:37
SELECT DATEADD(DAY, -30, GETDATE()) AS one_month_ago;
INSERT INTO #log (created_at) VALUES (GETDATE());
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`NOW`](now.md), [`CURRENT_TIMESTAMP`](current_timestamp.md)
