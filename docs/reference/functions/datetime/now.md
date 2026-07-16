# NOW
Returns the current UTC date and time. Alias for GETDATE().

**Category:** Date

## Syntax
```sql
NOW()
```

## Returns
`DATETIME` — Current UTC date and time.

## Remarks
- `NOW()` is the preferred cross-dialect alias; `GETDATE()` matches T-SQL convention.
- Both return the same instant; use whichever matches your SQL dialect preference.

## Example
```sql
SELECT NOW();
SELECT DATEDIFF(SECOND, start_time, NOW()) AS elapsed FROM #jobs;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`GETDATE`](getdate.md), [`CURRENT_TIMESTAMP`](current_timestamp.md)
