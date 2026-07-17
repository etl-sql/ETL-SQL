# DATETRUNC
Truncates a date to the beginning of the specified date part boundary.

**Category:** Date

## Syntax
```sql
DATETRUNC(datepart, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The boundary to truncate to — see [§3.1 Keyword Parameter Enumerations](../../../../../docs/syntax-index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `date` | `DATE` / `DATETIME` | The date to truncate |

## Returns
`DATETIME` — The date with all parts below `datepart` zeroed out.

## Accepted Values for `datepart`
`YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`

## Example
```sql
SELECT DATETRUNC(MONTH, '2026-05-17');      -- → 2026-05-01 00:00:00
SELECT DATETRUNC(YEAR, '2026-05-17');       -- → 2026-01-01 00:00:00
SELECT DATETRUNC(HOUR, '2026-05-17 14:37') -- → 2026-05-17 14:00:00
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`TRUNC`](TRUNC.md), [`DATEPART`](DATEPART.md), [`DATEADD`](DATEADD.md)
