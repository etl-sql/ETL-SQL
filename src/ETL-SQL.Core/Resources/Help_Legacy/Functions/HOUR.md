# HOUR
Returns the hour component of a datetime as an integer (0–23).

**Category:** Date

## Syntax
```sql
HOUR(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATETIME` / `TIME` | The source datetime or time value |

## Returns
`INT` — Hour from `0` (midnight) to `23` (11 PM).

## Example
```sql
SELECT HOUR('2026-05-17 14:30:00');   -- → 14
SELECT HOUR(GETDATE()) AS current_hour;
SELECT HOUR(event_time) AS hour, COUNT(*) AS events
  FROM #log GROUP BY HOUR(event_time) ORDER BY hour;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`MINUTE`](MINUTE.md), [`SECOND`](SECOND.md), [`DATEPART`](DATEPART.md)
