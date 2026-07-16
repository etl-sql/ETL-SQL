# MONTH
Returns the month component of a date as an integer (1–12).

**Category:** Date

## Syntax
```sql
MONTH(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATE` / `DATETIME` | The source date |

## Returns
`INT` — Month number from `1` (January) to `12` (December).

## Example
```sql
SELECT MONTH('2026-05-17');    -- → 5
SELECT MONTH(GETDATE());       -- → current month number
SELECT DATENAME(MONTH, GETDATE()) AS month_name;   -- → 'May' (use DATENAME for name)
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`YEAR`](YEAR.md), [`DAY`](DAY.md), [`DATENAME`](DATENAME.md)
