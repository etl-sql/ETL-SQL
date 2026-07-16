# DATEADD
Adds a specified number of date/time units to a date or datetime value.

**Category:** Date

## Syntax
```sql
DATEADD(datepart, number, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The unit to add — see [§3.1 Keyword Parameter Enumerations](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `number` | `INT` | Number of units to add. Use a negative value to subtract |
| `date` | `DATE` / `DATETIME` | The base date or datetime value |

## Returns
`DATETIME` — The resulting date with the interval added.

## Example
```sql
SELECT DATEADD(MONTH, 3, '2025-01-15');    -- → 2025-04-15
SELECT DATEADD(DAY, -7, GETDATE());         -- → 7 days ago
SELECT DATEADD(YEAR, 1, order_date) AS warranty_expires FROM #orders;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- [Syntax Index §3.1 — datepart values](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- Related: [`DATEDIFF`](datediff.md), [`DATEPART`](datepart.md), [`DATENAME`](datename.md)
