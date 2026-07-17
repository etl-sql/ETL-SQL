# DATE_TRUNC
Truncates a datetime to the beginning of the specified date part boundary.

**Category:** Date

## Syntax
```sql
DATE_TRUNC(datepart, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The boundary to truncate to — see [§3.1 Keyword Parameter Enumerations](../../../../../docs/syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `date` | `DATE` / `DATETIME` | The date to truncate |

## Returns
`DATETIME` — The date with all components below `datepart` zeroed out.

## Accepted Values for `datepart`
`YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`

## Example
```sql
SELECT DATE_TRUNC(MONTH, '2026-05-17 12:30:00');    -- → 2026-05-01 00:00:00
SELECT DATE_TRUNC(HOUR, '2026-05-17 14:37:15');     -- → 2026-05-17 14:00:00
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`DATETRUNC`](DATETRUNC.md) (T-SQL style variant), [`TRUNC`](TRUNC.md)
