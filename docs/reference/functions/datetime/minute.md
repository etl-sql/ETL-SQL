# MINUTE
Returns the minute component of a datetime as an integer (0–59).

**Category:** Date

## Syntax
```sql
MINUTE(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATETIME` / `TIME` | The source datetime or time value |

## Returns
`INT` — Minute from `0` to `59`.

## Example
```sql
SELECT MINUTE('2026-05-17 14:30:00');   -- → 30
SELECT MINUTE(GETDATE()) AS current_minute;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`HOUR`](hour.md), [`SECOND`](second.md), [`DATEPART`](datepart.md)
