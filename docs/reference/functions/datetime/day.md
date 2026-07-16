# DAY
Returns the day-of-month component of a date as an integer (1–31).

**Category:** Date

## Syntax
```sql
DAY(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATE` / `DATETIME` | The source date |

## Returns
`INT` — Day of the month from `1` to `31`.

## Example
```sql
SELECT DAY('2026-05-17');    -- → 17
SELECT DAY(GETDATE());       -- → current day of month
SELECT * FROM #orders WHERE DAY(order_date) = 1;  -- first of each month
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`YEAR`](year.md), [`MONTH`](month.md), [`DATEPART`](datepart.md)
