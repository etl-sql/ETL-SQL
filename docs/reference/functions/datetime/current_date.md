# CURRENT_DATE
Returns the current date (no time component).

**Category:** Date

## Syntax
```sql
CURRENT_DATE()
```

## Returns
`DATE` — Today's date with no time component.

## Example
```sql
SELECT CURRENT_DATE();
SELECT * FROM #orders WHERE order_date = CURRENT_DATE();
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`GETDATE`](getdate.md), [`CURRENT_TIMESTAMP`](current_timestamp.md)
