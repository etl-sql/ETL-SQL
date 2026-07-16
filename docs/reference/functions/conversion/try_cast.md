# TRY_CAST
Safely converts a value to the specified type, returning NULL on failure instead of raising an error.

**Category:** System

## Syntax
```sql
TRY_CAST(expression AS type)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `expression` | `ANY` | The value to convert |
| `type` | `TYPE` | The target data type |

## Returns
The converted value in `type`, or `NULL` if conversion fails. Never raises an exception.

## Remarks
- Use `TRY_CAST` when source data may contain non-convertible values (dirty data).
- `CAST` is the strict form — use it when invalid data should raise an error.

## Example
```sql
SELECT TRY_CAST('42' AS INT);           -- → 42
SELECT TRY_CAST('N/A' AS INT);          -- → NULL  (no error)
SELECT TRY_CAST('2026-05-17' AS DATE);  -- → 2026-05-17

-- Validate before loading
SELECT * INTO #valid FROM #raw WHERE TRY_CAST(amount_str AS DECIMAL) IS NOT NULL;
```

## See Also
- [Standard Library — §2. Type Conversion](../../../guides/getting-started.md#2-type-conversion)
- Related: [`CAST`](cast.md), [`TRY_CONVERT`](../general/try_convert.md), [`TRY_PARSE`](../general/try_parse.md), [`ISDATE`](../general/isdate.md)
