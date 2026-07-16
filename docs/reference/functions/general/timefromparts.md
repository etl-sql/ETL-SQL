# TIMEFROMPARTS
Constructs a TIME value from individual hour, minute, second, fractions, and precision components.

**Category:** Date

## Syntax
```sql
TIMEFROMPARTS(hour, minute, second, fractions, precision)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `hour` | `INT` | Hour (0–23) |
| `minute` | `INT` | Minute (0–59) |
| `second` | `INT` | Second (0–59) |
| `fractions` | `INT` | Fractional seconds value |
| `precision` | `INT` | Decimal precision of fractions (0–7) |

## Returns
`TIME` — The constructed time value. Raises an error if any component is out of range.

## Example
```sql
SELECT TIMEFROMPARTS(14, 30, 0, 0, 0);      -- → 14:30:00
SELECT TIMEFROMPARTS(14, 30, 45, 500, 3);   -- → 14:30:45.500
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`DATETIMEFROMPARTS`](datetimefromparts.md)
