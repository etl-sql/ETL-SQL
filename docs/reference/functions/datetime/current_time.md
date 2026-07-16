# CURRENT_TIME
Returns the current time of day.

**Category:** Date

## Syntax
```sql
CURRENT_TIME()
```

## Returns
`TIME` — The current time.

## Example
```sql
SELECT CURRENT_TIME();
SELECT HOUR(CURRENT_TIME()) AS current_hour;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`GETDATE`](getdate.md), [`CURRENT_TIMESTAMP`](current_timestamp.md)
