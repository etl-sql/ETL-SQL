# DATE_PART
Extracts a specified date part component from a date as an integer value.

**Category:** Date

## Syntax
```sql
DATE_PART(datepart, date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The part to extract — see [§3.1 Keyword Parameter Enumerations](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `date` | `DATE` / `DATETIME` | The date to extract from |

## Returns
`INTEGER` — The value representing the specified date part.

## Accepted Values for `datepart`
`YEAR`, `QUARTER`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `MILLISECOND`, `DOW`, `DOY`

## Example
```sql
SELECT DATE_PART(MONTH, '2026-05-17');        -- → 5
SELECT DATE_PART(QUARTER, '2026-05-17');      -- → 2
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`DATEPART`](../datetime/datepart.md), [`EXTRACT`](../../../guides/getting-started.md#extract)
