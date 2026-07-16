# DATEPART
Returns the integer value of a specific date part from a date or datetime.

**Category:** Date

## Syntax
```sql
DATEPART(datepart, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The part to extract — see [§3.1 Keyword Parameter Enumerations](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `date` | `DATE` / `DATETIME` | The source date |

## Returns
`INT` — The integer value of the specified date part.

## Example
```sql
SELECT DATEPART(YEAR, GETDATE());             -- → 2026
SELECT DATEPART(QUARTER, '2026-05-17');       -- → 2
SELECT DATEPART(WEEKDAY, '2026-05-17');       -- → 1 (Sunday=1 default)
SELECT DATEPART(HOUR, order_time) AS hour FROM #orders GROUP BY DATEPART(HOUR, order_time);
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- [Syntax Index §3.1 — datepart values](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- Related: [`DATENAME`](datename.md), [`DATEADD`](dateadd.md), [`YEAR`](year.md), [`MONTH`](month.md), [`DAY`](day.md)
