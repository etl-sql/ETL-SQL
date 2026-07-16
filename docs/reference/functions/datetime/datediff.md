# DATEDIFF
Returns the count of date/time part boundaries crossed between two dates.

**Category:** Date

## Syntax
```sql
DATEDIFF(datepart, start_date, end_date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `datepart` | `KEYWORD` | The unit to measure — see [§3.1 Keyword Parameter Enumerations](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract) |
| `start_date` | `DATE` / `DATETIME` | The starting date |
| `end_date` | `DATE` / `DATETIME` | The ending date |

## Returns
`INT` — Number of `datepart` boundaries crossed. Positive when `end_date > start_date`; negative when reversed.

## Remarks
- Counts **boundaries crossed**, not elapsed time. `DATEDIFF(MONTH, '2026-01-31', '2026-02-01')` returns `1` even though only 1 day elapsed.

## Example
```sql
SELECT DATEDIFF(DAY, '2026-01-01', '2026-05-17');    -- → 136
SELECT DATEDIFF(MONTH, hire_date, GETDATE()) AS months_employed FROM #employees;
SELECT DATEDIFF(SECOND, start_time, end_time) AS duration_sec FROM #jobs;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- [Syntax Index §3.1 — datepart values](../../../Syntax_Index.md#datepart--dateadd-datediff-datename-datepart-datetrunc-extract)
- Related: [`DATEADD`](dateadd.md), [`DATEPART`](datepart.md)
