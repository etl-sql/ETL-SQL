# DATETIMEFROMPARTS
Constructs a DATETIME value from individual year, month, day, hour, minute, second, and millisecond components.

**Category:** Date

## Syntax
```sql
DATETIMEFROMPARTS(year, month, day, hour, minute, second, millisecond)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `year` | `INT` | Four-digit year |
| `month` | `INT` | Month (1–12) |
| `day` | `INT` | Day of month (1–31) |
| `hour` | `INT` | Hour (0–23) |
| `minute` | `INT` | Minute (0–59) |
| `second` | `INT` | Second (0–59) |
| `millisecond` | `INT` | Millisecond (0–999) |

## Returns
`DATETIME` — The constructed datetime. Raises an error if any component is out of range.

## Example
```sql
SELECT DATETIMEFROMPARTS(2026, 4, 1, 8, 0, 0, 0);   -- → 2026-04-01 08:00:00.000
SELECT DATETIMEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1, 0, 0, 0, 0) AS first_of_month;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`TIMEFROMPARTS`](timefromparts.md), [`DATEPART`](../datetime/datepart.md)
