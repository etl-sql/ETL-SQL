# TRUNC
Truncates the time portion of a datetime, returning the date at midnight.

**Category:** Date

## Syntax
```sql
TRUNC(date)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `date` | `DATETIME` | The datetime value to truncate |

## Returns
`DATE` — The date portion only, with time set to 00:00:00.

## Remarks
- Equivalent to `CAST(date AS DATE)`.
- For truncation to other date parts (month, hour, etc.), use [`DATETRUNC`](DATETRUNC.md).

## Example
```sql
SELECT TRUNC('2026-05-17 14:30:00');   -- → 2026-05-17
SELECT TRUNC(GETDATE()) AS today;
SELECT * FROM #orders WHERE TRUNC(order_time) = TRUNC(GETDATE());  -- today's orders
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../../../Docs/Reference/Standard_Library.md#4-date--time-functions)
- Related: [`DATETRUNC`](DATETRUNC.md), [`CAST`](CAST.md)
