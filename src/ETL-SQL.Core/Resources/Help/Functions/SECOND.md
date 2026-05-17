# SECOND
Returns the second component of a datetime as an integer (0–59).

**Category:** Date

## Syntax
```sql
SECOND(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATETIME` / `TIME` | The source datetime or time value |

## Returns
`INT` — Second from `0` to `59`.

## Example
```sql
SELECT SECOND('2026-05-17 14:30:45');   -- → 45
SELECT DATEDIFF(SECOND, start_time, end_time) AS duration_sec FROM #jobs;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`HOUR`](HOUR.md), [`MINUTE`](MINUTE.md), [`DATEPART`](DATEPART.md)
