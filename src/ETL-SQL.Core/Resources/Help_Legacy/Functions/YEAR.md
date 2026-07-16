# YEAR
Returns the year component of a date as an integer.

**Category:** Date

## Syntax
```sql
YEAR(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATE` / `DATETIME` | The source date |

## Returns
`INT` — The four-digit year (e.g., `2026`).

## Example
```sql
SELECT YEAR('2026-05-17');       -- → 2026
SELECT YEAR(GETDATE());          -- → current year
SELECT YEAR(order_date) AS year, SUM(amount) AS total
  FROM #orders GROUP BY YEAR(order_date);
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`MONTH`](MONTH.md), [`DAY`](DAY.md), [`DATEPART`](DATEPART.md)
