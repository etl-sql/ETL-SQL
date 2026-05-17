# EOMONTH
Returns the last day of the month for a given date, optionally offset by a number of months.

**Category:** Date

## Syntax
```sql
EOMONTH(date)
EOMONTH(date, months)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATE` / `DATETIME` | The reference date |
| `months` | `INT` | Optional: number of months to offset before computing end-of-month (negative = prior months) |

## Returns
`DATE` — The last calendar day of the specified month.

## Example
```sql
SELECT EOMONTH('2026-02-01');      -- → 2026-02-28
SELECT EOMONTH(GETDATE());         -- last day of current month
SELECT EOMONTH(GETDATE(), -1);     -- last day of previous month
SELECT EOMONTH(GETDATE(), 2);      -- last day two months from now
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`DATEADD`](DATEADD.md), [`DATETRUNC`](DATETRUNC.md)
