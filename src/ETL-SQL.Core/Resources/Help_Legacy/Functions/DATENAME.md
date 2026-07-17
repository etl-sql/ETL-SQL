# DATENAME
Returns the name of a specific date part as a string.

**Category:** Date

## Syntax
```sql
DATENAME(datepart, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The part to name — see [§3.1 Keyword Parameter Enumerations](../../../../../docs/syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `date` | `DATE` / `DATETIME` | The source date |

## Returns
`STRING` — The name of the date part (e.g., `'April'` for month, `'Monday'` for weekday).

## Example
```sql
SELECT DATENAME(MONTH, '2026-05-17');    -- → 'May'
SELECT DATENAME(WEEKDAY, '2026-05-17'); -- → 'Sunday'
SELECT DATENAME(YEAR, GETDATE());        -- → '2026'
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- [Syntax Index §3.1 — datepart values](../../../../../docs/syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- Related: [`DATEPART`](DATEPART.md), [`FORMAT`](FORMAT.md)
