# ISDATE
Returns 1 if the expression can be parsed as a valid date, 0 otherwise.

**Category:** Date

## Syntax
```sql
ISDATE(string)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `string` | `STRING` | The value to test for date parseability |

## Returns
`BIT` — `1` if the value is a valid date/datetime; `0` otherwise.

## Example
```sql
SELECT ISDATE('2026-05-17');    -- → 1
SELECT ISDATE('not a date');    -- → 0
SELECT ISDATE('2026-13-01');    -- → 0  (invalid month)

-- Filter valid date strings before casting
SELECT TRY_CAST(date_str AS DATE) AS parsed
  FROM #raw WHERE ISDATE(date_str) = 1;
```

## See Also
- [Standard Library — §4. Date & Time Functions](../../../guides/getting-started.md#4-date--time-functions)
- Related: [`TRY_CAST`](../conversion/try_cast.md), [`CAST`](../conversion/cast.md)
